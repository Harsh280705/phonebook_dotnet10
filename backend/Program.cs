using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bogus;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Phonebook.Api;
using Phonebook.Api.Data;
using Phonebook.Api.Models;
using Phonebook.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("DATABASE_URL is required.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(ConnectionString.ToNpgsql(connectionString)));
builder.Services.AddSingleton<SecurityService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("ALTER TABLE contacts ADD COLUMN IF NOT EXISTS user_id INTEGER REFERENCES users(id)");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_contacts_user_id ON contacts (user_id)");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS tags (
            id SERIAL PRIMARY KEY,
            user_id INTEGER NOT NULL REFERENCES users(id),
            name VARCHAR(50) NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_tags_user_id ON tags (user_id)");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS ux_tags_user_id_lower_name ON tags (user_id, lower(name))");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS contact_tags (
            contact_id INTEGER NOT NULL REFERENCES contacts(id) ON DELETE CASCADE,
            tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
            PRIMARY KEY (contact_id, tag_id)
        )
        """);
}

if (args.Any(value => string.Equals(value, "populate", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await PopulateContactsAsync(db);
    return;
}

app.MapGet("/", () => Results.Ok(new { Message = "Phonebook API is running" }));

app.MapPost("/auth/register", async (RegisterRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var validation = ValidateRegistration(request);
    if (validation is not null) return validation;
    if (await db.Users.AnyAsync(x => x.Username == request.Username.Trim())) return Conflict("Username already exists.");
    if (await db.Users.AnyAsync(x => x.Email == request.Email.Trim())) return Conflict("Email already exists.");

    var user = new User
    {
        Username = request.Username.Trim(),
        Email = request.Email.Trim(),
        PasswordHash = security.HashPassword(request.Password),
        CreatedAt = DateTime.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    if (await db.Users.CountAsync() == 1)
    {
        await db.Contacts.Where(x => x.UserId == null).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UserId, user.Id));
    }

    await IssueSession(http, db, security, user.Id);
    return Results.Json(ToUserResponse(user), statusCode: StatusCodes.Status201Created);
});

app.MapPost("/auth/login", async (LoginRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == request.Identifier || x.Email == request.Identifier);
    if (user is null || !security.VerifyPassword(request.Password, user.PasswordHash)) return Unauthorized("Invalid username/email or password.");
    await IssueSession(http, db, security, user.Id);
    return Results.Ok(ToUserResponse(user));
});

app.MapGet("/auth/me", async (HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    return user is null ? Unauthorized("Authentication required.") : Results.Ok(ToUserResponse(user));
});

app.MapPost("/auth/logout", async (HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    if (http.Request.Cookies.TryGetValue("phonebook_session", out var token))
    {
        await db.AuthSessions.Where(x => x.TokenHash == security.HashSessionToken(token)).ExecuteDeleteAsync();
    }
    http.Response.Cookies.Delete("phonebook_session", SessionCookieOptions(TimeSpan.Zero));
    return Results.Ok(new { Message = "Logged out successfully." });
});

app.MapGet("/tags/", async (HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var tags = await db.Tags.Where(x => x.UserId == user.Id).OrderBy(x => x.Name).Select(x => new TagResponse(x.Id, x.Name)).ToListAsync();
    return Results.Ok(tags);
});

app.MapPost("/tags/", async (TagRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var validation = ValidateTagName(request.Name, out var name);
    if (validation is not null) return validation;
    if (await db.Tags.AnyAsync(x => x.UserId == user.Id && x.Name.ToLower() == name.ToLower()))
    {
        return Conflict("A tag with this name already exists.");
    }

    var tag = new Tag { UserId = user.Id, Name = name };
    db.Tags.Add(tag);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Conflict("A tag with this name already exists."); }
    return Results.Json(new TagResponse(tag.Id, tag.Name), statusCode: StatusCodes.Status201Created);
});

app.MapPut("/tags/{id:int}", async (int id, TagRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var tag = await db.Tags.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
    if (tag is null) return Results.NotFound(new ErrorResponse("Tag not found."));
    var validation = ValidateTagName(request.Name, out var name);
    if (validation is not null) return validation;
    if (await db.Tags.AnyAsync(x => x.UserId == user.Id && x.Id != id && x.Name.ToLower() == name.ToLower()))
    {
        return Conflict("A tag with this name already exists.");
    }

    tag.Name = name;
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Conflict("A tag with this name already exists."); }
    return Results.Ok(new TagResponse(tag.Id, tag.Name));
});

app.MapDelete("/tags/{id:int}", async (int id, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var tag = await db.Tags.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
    if (tag is null) return Results.NotFound(new ErrorResponse("Tag not found."));
    db.Tags.Remove(tag);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Tag deleted successfully." });
});

app.MapGet("/contacts/", async (HttpContext http, ApplicationDbContext db, SecurityService security, int page = 1, int limit = 10, string? search = null, int[]? tag_ids = null) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    if (page < 1 || limit < 1 || limit > 100) return Unprocessable("Page must be positive and limit must be between 1 and 100.");

    var query = db.Contacts.Where(x => x.UserId == user.Id);
    if (!string.IsNullOrWhiteSpace(search))
    {
        var pattern = $"%{search.Trim()}%";
        query = query.Where(x =>
            EF.Functions.ILike(x.Name, pattern) ||
            EF.Functions.ILike(x.PhoneNumber, pattern) ||
            (x.Email != null && EF.Functions.ILike(x.Email, pattern)));
    }
    if (tag_ids is { Length: > 0 })
    {
        foreach (var tagId in tag_ids.Distinct())
        {
            var requiredTagId = tagId;
            query = query.Where(x => x.ContactTags.Any(ct => ct.TagId == requiredTagId));
        }
    }
    var total = await query.CountAsync();
    var totalPages = Math.Max((total + limit - 1) / limit, 1);
    var contacts = await query.OrderBy(x => x.Name).Skip((page - 1) * limit).Take(limit).ToListAsync();
    return Results.Ok(new ContactPage(await ToContactResponsesAsync(db, contacts), page, limit, total, totalPages));
});

app.MapPost("/contacts/", async (ContactRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var validation = ValidateContact(request);
    if (validation is not null) return validation;
    var tagError = await EnsureOwnedTagsAsync(db, request.TagIds, user.Id);
    if (tagError is not null) return tagError;
    if (await db.Contacts.AnyAsync(x => x.PhoneNumber == request.PhoneNumber)) return Conflict("This phone number already exists.");
    if (!string.IsNullOrWhiteSpace(request.Email) && await db.Contacts.AnyAsync(x => x.Email == request.Email)) return Conflict("This email address already exists.");

    var contact = ToContact(request, user.Id);
    db.Contacts.Add(contact);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Conflict("A contact with this phone number or email already exists."); }
    if (request.TagIds is not null)
    {
        await ReplaceContactTagsAsync(db, contact.Id, request.TagIds);
        await db.SaveChangesAsync();
    }
    return Results.Json(await ToContactResponseAsync(db, contact), statusCode: StatusCodes.Status201Created);
});

app.MapGet("/contacts/export", async (HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var contacts = await db.Contacts.Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ToListAsync();
    using var writer = new StringWriter(CultureInfo.InvariantCulture);
    using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
    csv.WriteField("Name"); csv.WriteField("Phone number"); csv.WriteField("Email"); csv.WriteField("Address"); csv.NextRecord();
    foreach (var contact in contacts)
    {
        csv.WriteField(contact.Name);
        csv.WriteField($"=\"{contact.PhoneNumber}\"");
        csv.WriteField(contact.Email ?? "");
        csv.WriteField(contact.Address ?? "");
        csv.NextRecord();
    }
    return Results.File(Encoding.UTF8.GetBytes(writer.ToString()), "text/csv; charset=utf-8", "phonebook.csv");
});

app.MapPost("/contacts/import", async (HttpRequest request, HttpContext http, ApplicationDbContext db, SecurityService security) =>
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    if (!request.HasFormContentType) return BadRequest("Please upload a CSV file.");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || string.IsNullOrWhiteSpace(file.FileName) || !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
    {
        return BadRequest("Please upload a CSV file.");
    }
    if (file.Length == 0) return BadRequest("The CSV file is empty.");

    using var stream = file.OpenReadStream();
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    string text;
    try
    {
        text = await reader.ReadToEndAsync();
    }
    catch (DecoderFallbackException)
    {
        return BadRequest("The CSV file must use UTF-8 encoding.");
    }
    if (string.IsNullOrWhiteSpace(text)) return BadRequest("The CSV file is empty.");

    using var stringReader = new StringReader(text);
    var csvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
    {
        BadDataFound = null,
        MissingFieldFound = null,
        HeaderValidated = null,
        TrimOptions = CsvHelper.Configuration.TrimOptions.None
    };
    using var csv = new CsvReader(stringReader, csvConfig);
    if (!await csv.ReadAsync() || !csv.ReadHeader()) return BadRequest("The CSV file is empty.");
    var headers = csv.HeaderRecord ?? [];
    var normalized = headers.Select(NormalizeHeader).ToHashSet();
    var nameAliases = new HashSet<string>(["name", "full_name"]);
    var firstAliases = new HashSet<string>(["first_name", "firstname"]);
    var lastAliases = new HashSet<string>(["last_name", "lastname"]);
    var phoneAliases = new HashSet<string>(["phone", "phone_number", "mobile", "mobile_number"]);
    if ((!normalized.Overlaps(nameAliases) && !normalized.Overlaps(firstAliases) && !normalized.Overlaps(lastAliases)) || !normalized.Overlaps(phoneAliases))
    {
        return BadRequest("CSV must include a name or first/last name column and a phone column.");
    }

    var seenPhones = (await db.Contacts.Select(x => x.PhoneNumber).ToListAsync()).ToHashSet();
    var seenEmails = (await db.Contacts.Where(x => x.Email != null).Select(x => x.Email!).ToListAsync()).ToHashSet();
    var summary = new ImportSummary();
    var rowNumber = 1;
    while (await csv.ReadAsync())
    {
        rowNumber++;
        summary.TotalRows++;
        var name = CsvValue(csv, headers, nameAliases)
            ?? string.Join(' ', new[] { CsvValue(csv, headers, firstAliases), CsvValue(csv, headers, lastAliases) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var phone = CsvValue(csv, headers, phoneAliases) ?? "";
        var email = CsvValue(csv, headers, ["email", "email_address"]);
        var address = CsvValue(csv, headers, ["address", "street_address"])
            ?? string.Join(' ', new[] { CsvValue(csv, headers, ["city", "town"]), CsvValue(csv, headers, ["country", "country_name"]) }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var fields = new HashSet<string>();
        string? error;
        try
        {
            phone = NormalizePhone(phone);
            error = CollectContactErrors(name, phone, email, address, fields);
        }
        catch (InvalidOperationException exception)
        {
            fields.Add("phone_number");
            error = exception.Message;
        }

        if (error is not null)
        {
            summary.InvalidRows++;
            if (fields.Contains("name")) summary.InvalidNames++;
            if (fields.Contains("phone_number")) summary.InvalidPhoneNumbers++;
            if (fields.Contains("email")) summary.InvalidEmails++;
            if (fields.Contains("address")) summary.InvalidAddresses++;
            summary.RowErrors.Add(new ImportRowError { Row = rowNumber, Error = error });
            continue;
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (seenPhones.Contains(phone) || (normalizedEmail is not null && seenEmails.Contains(normalizedEmail)))
        {
            summary.SkippedDuplicates++;
            continue;
        }

        db.Contacts.Add(new Contact
        {
            UserId = user.Id,
            Name = name!.Trim(),
            PhoneNumber = phone,
            Email = normalizedEmail,
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        seenPhones.Add(phone);
        if (normalizedEmail is not null) seenEmails.Add(normalizedEmail);
        summary.Imported++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(summary);
});

app.MapGet("/contacts/{id:int}", ContactById);
app.MapPut("/contacts/{id:int}", UpdateContact);
app.MapDelete("/contacts/{id:int}", DeleteContact);

async Task<IResult> ContactById(int id, HttpContext http, ApplicationDbContext db, SecurityService security)
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var contact = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
    return contact is null ? Results.NotFound(new ErrorResponse("Contact not found.")) : Results.Ok(await ToContactResponseAsync(db, contact));
}

async Task<IResult> UpdateContact(int id, ContactRequest request, HttpContext http, ApplicationDbContext db, SecurityService security)
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var contact = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
    if (contact is null) return Results.NotFound(new ErrorResponse("Contact not found."));
    var validation = ValidateContactUpdate(request);
    if (validation is not null) return validation;
    var tagError = await EnsureOwnedTagsAsync(db, request.TagIds, user.Id);
    if (tagError is not null) return tagError;
    if (request.PhoneNumber is not null && await db.Contacts.AnyAsync(x => x.PhoneNumber == request.PhoneNumber && x.Id != id))
    {
        return Conflict("This phone number already exists.");
    }
    if (request.Email is not null && await db.Contacts.AnyAsync(x => x.Email == request.Email && x.Id != id))
    {
        return Conflict("This email address already exists.");
    }
    if (request.Name is not null) contact.Name = request.Name.Trim();
    if (request.PhoneNumber is not null) contact.PhoneNumber = request.PhoneNumber.Trim();
    if (request.Email is not null) contact.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
    if (request.Address is not null) contact.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
    await ReplaceContactTagsAsync(db, contact.Id, request.TagIds);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Conflict("A contact with this phone number or email already exists."); }
    return Results.Ok(await ToContactResponseAsync(db, contact));
}

async Task<IResult> DeleteContact(int id, HttpContext http, ApplicationDbContext db, SecurityService security)
{
    var user = await CurrentUser(http, db, security);
    if (user is null) return Unauthorized("Authentication required.");
    var contact = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id);
    if (contact is null) return Results.NotFound(new ErrorResponse("Contact not found."));
    db.Contacts.Remove(contact);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Contact deleted successfully." });
}

static async Task<User?> CurrentUser(HttpContext http, ApplicationDbContext db, SecurityService security)
{
    if (!http.Request.Cookies.TryGetValue("phonebook_session", out var token)) return null;
    var session = await db.AuthSessions.FirstOrDefaultAsync(x => x.TokenHash == security.HashSessionToken(token) && x.ExpiresAt > DateTime.UtcNow);
    return session is null ? null : await db.Users.FindAsync(session.UserId);
}

static async Task IssueSession(HttpContext http, ApplicationDbContext db, SecurityService security, int userId)
{
    var token = security.CreateSessionToken();
    db.AuthSessions.Add(new AuthSession
    {
        UserId = userId,
        TokenHash = security.HashSessionToken(token),
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    http.Response.Cookies.Append("phonebook_session", token, SessionCookieOptions(TimeSpan.FromDays(7)));
}

static CookieOptions SessionCookieOptions(TimeSpan maxAge) => new()
{
    HttpOnly = true,
    SameSite = SameSiteMode.Lax,
    Secure = false,
    Path = "/",
    MaxAge = maxAge
};

static UserResponse ToUserResponse(User user) => new(user.Id, user.Username, user.Email, user.CreatedAt);
static ContactResponse ToContactResponse(Contact contact, IReadOnlyList<TagResponse>? tags = null) =>
    new(contact.Id, contact.Name, contact.PhoneNumber, contact.Email, contact.Address, contact.CreatedAt, tags ?? []);

static async Task<ContactResponse> ToContactResponseAsync(ApplicationDbContext db, Contact contact)
{
    var tags = await LoadTagsByContactAsync(db, [contact.Id]);
    return ToContactResponse(contact, tags.GetValueOrDefault(contact.Id) ?? []);
}

static async Task<IReadOnlyList<ContactResponse>> ToContactResponsesAsync(ApplicationDbContext db, IReadOnlyList<Contact> contacts)
{
    var tags = await LoadTagsByContactAsync(db, contacts.Select(x => x.Id).ToList());
    return contacts.Select(contact => ToContactResponse(contact, tags.GetValueOrDefault(contact.Id) ?? [])).ToList();
}

static async Task<Dictionary<int, IReadOnlyList<TagResponse>>> LoadTagsByContactAsync(ApplicationDbContext db, IReadOnlyList<int> contactIds)
{
    if (contactIds.Count == 0) return [];
    var rows = await (
        from ct in db.ContactTags
        join tag in db.Tags on ct.TagId equals tag.Id
        where contactIds.Contains(ct.ContactId)
        orderby tag.Name
        select new { ct.ContactId, Tag = new TagResponse(tag.Id, tag.Name) }
    ).ToListAsync();
    return rows
        .GroupBy(x => x.ContactId)
        .ToDictionary(group => group.Key, group => (IReadOnlyList<TagResponse>)group.Select(x => x.Tag).ToList());
}

static async Task<IResult?> EnsureOwnedTagsAsync(ApplicationDbContext db, IReadOnlyList<int>? tagIds, int userId)
{
    if (tagIds is null) return null;
    var unique = tagIds.Distinct().ToList();
    if (unique.Any(id => id <= 0)) return BadRequest("One or more tags were not found.");
    if (unique.Count == 0) return null;
    var ownedCount = await db.Tags.CountAsync(x => x.UserId == userId && unique.Contains(x.Id));
    return ownedCount == unique.Count ? null : BadRequest("One or more tags were not found.");
}

static async Task ReplaceContactTagsAsync(ApplicationDbContext db, int contactId, IReadOnlyList<int>? tagIds)
{
    if (tagIds is null) return;
    var unique = tagIds.Distinct().ToList();
    await db.ContactTags.Where(x => x.ContactId == contactId).ExecuteDeleteAsync();
    foreach (var tagId in unique)
    {
        db.ContactTags.Add(new ContactTag { ContactId = contactId, TagId = tagId });
    }
}

static IResult? ValidateTagName(string? name, out string cleaned)
{
    cleaned = name?.Trim() ?? "";
    if (cleaned.Length is < 1 or > 50 || !Regex.IsMatch(cleaned, "^[A-Za-zÀ-ÿ0-9][A-Za-zÀ-ÿ0-9\\s'-]{0,49}$"))
    {
        return Unprocessable("Tag name must be 1 to 50 characters and contain only letters, numbers, spaces, apostrophes or hyphens.");
    }
    return null;
}
static Contact ToContact(ContactRequest request, int userId) => new()
{
    UserId = userId,
    Name = request.Name!.Trim(),
    PhoneNumber = request.PhoneNumber!.Trim(),
    Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
    Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
    CreatedAt = DateTime.UtcNow
};

static IResult? ValidateRegistration(RegisterRequest request)
{
    var errors = new List<object>();
    var username = request.Username?.Trim() ?? "";
    if (username.Length is < 3 or > 100)
    {
        errors.Add(new { loc = new[] { "body", "username" }, msg = "Username must be between 3 and 100 characters.", type = "value_error" });
    }
    try
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !new MailAddress(request.Email).Address.Equals(request.Email, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new { loc = new[] { "body", "email" }, msg = "Invalid email address.", type = "value_error" });
        }
    }
    catch (FormatException)
    {
        errors.Add(new { loc = new[] { "body", "email" }, msg = "Invalid email address.", type = "value_error" });
    }
    if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
    {
        errors.Add(new { loc = new[] { "body", "password" }, msg = "Password must contain at least 8 characters.", type = "value_error" });
    }
    return errors.Count == 0 ? null : Results.Json(new { detail = errors }, statusCode: StatusCodes.Status422UnprocessableEntity);
}

static IResult? ValidateContact(ContactRequest request)
{
    var fields = new HashSet<string>();
    var error = CollectContactErrors(request.Name, request.PhoneNumber, request.Email, request.Address, fields);
    return error is null ? null : Unprocessable(error);
}

static IResult? ValidateContactUpdate(ContactRequest request)
{
    if (request.Name is null && request.PhoneNumber is null && request.Email is null && request.Address is null) return null;
    var fields = new HashSet<string>();
    var error = CollectContactErrors(request.Name, request.PhoneNumber, request.Email, request.Address, fields, true);
    return error is null ? null : Unprocessable(error);
}

static string? CollectContactErrors(string? name, string? phone, string? email, string? address, HashSet<string> fields, bool partial = false)
{
    var messages = new List<string>();
    if (!partial || name is not null)
    {
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name.Trim(), "^[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ\\s'-]{1,99}$"))
        {
            fields.Add("name");
            messages.Add("Name must contain only letters, spaces, apostrophes or hyphens.");
        }
    }
    if (!partial || phone is not null)
    {
        if (string.IsNullOrWhiteSpace(phone) || !Regex.IsMatch(phone.Trim(), "^\\+?[0-9()\\s-]+$"))
        {
            fields.Add("phone_number");
            messages.Add("Phone number may contain digits, a leading +, spaces, hyphens, or parentheses.");
        }
        else if (Regex.Replace(phone, "\\D", "").Length is < 8 or > 15)
        {
            fields.Add("phone_number");
            messages.Add("Phone number must contain between 8 and 15 digits.");
        }
    }
    if (!string.IsNullOrWhiteSpace(email))
    {
        try { _ = new MailAddress(email); }
        catch (FormatException)
        {
            fields.Add("email");
            messages.Add("Invalid email address.");
        }
    }
    if (!string.IsNullOrWhiteSpace(address))
    {
        var cleaned = address.Trim();
        if (cleaned.Length < 5 || cleaned.Length > 255)
        {
            fields.Add("address");
            messages.Add("Address must be between 5 and 255 characters.");
        }
        else if (!Regex.IsMatch(cleaned, "[A-Za-zÀ-ÿ]"))
        {
            fields.Add("address");
            messages.Add("Address must contain at least one letter.");
        }
        else if (!Regex.IsMatch(cleaned, "^[A-Za-zÀ-ÿ0-9\\s,.'#/-]+$"))
        {
            fields.Add("address");
            messages.Add("Address contains invalid characters.");
        }
    }
    return messages.Count == 0 ? null : string.Join("; ", messages);
}

static IResult BadRequest(string detail) => Results.Json(new ErrorResponse(detail), statusCode: StatusCodes.Status400BadRequest);
static IResult Unprocessable(string detail) => Results.Json(new ErrorResponse(detail), statusCode: StatusCodes.Status422UnprocessableEntity);
static IResult Conflict(string detail) => Results.Conflict(new ErrorResponse(detail));
static IResult Unauthorized(string detail) => Results.Json(new ErrorResponse(detail), statusCode: StatusCodes.Status401Unauthorized);
static string NormalizeHeader(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
static string? CsvValue(CsvReader csv, string[] headers, HashSet<string> aliases)
{
    for (var i = 0; i < headers.Length; i++)
    {
        if (aliases.Contains(NormalizeHeader(headers[i])))
        {
            var value = csv.GetField(i)?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
    return null;
}

static string NormalizePhone(string value)
{
    var phone = value.Trim();
    var excelMatch = Regex.Match(phone, "^=\\\"([^\\\"]*)\\\"$");
    if (excelMatch.Success) phone = excelMatch.Groups[1].Value.Trim();
    else if (phone.StartsWith('\'')) phone = phone[1..].Trim();
    if (Regex.IsMatch(phone, "^[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)[eE][+-]?\\d+$"))
    {
        throw new InvalidOperationException("Phone number is in scientific notation and cannot be recovered safely.");
    }
    return phone;
}

static async Task PopulateContactsAsync(ApplicationDbContext db)
{
    const int targetCount = 1000;
    var existingCount = await db.Contacts.CountAsync();
    var recordsNeeded = Math.Max(targetCount - existingCount, 0);
    if (recordsNeeded == 0)
    {
        Console.WriteLine($"Database already contains {existingCount} contacts.");
        return;
    }

    var owner = await db.Users.OrderBy(x => x.Id).FirstOrDefaultAsync();
    var existingPhones = (await db.Contacts.Select(x => x.PhoneNumber).ToListAsync()).ToHashSet();
    var existingEmails = (await db.Contacts.Where(x => x.Email != null).Select(x => x.Email!).ToListAsync()).ToHashSet();
    var faker = new Faker();
    var contacts = new List<Contact>();
    while (contacts.Count < recordsNeeded)
    {
        var phoneNumber = $"+1{faker.Random.Long(2_000_000_000L, 9_999_999_999L)}";
        var email = faker.Internet.Email().ToLowerInvariant();
        if (existingPhones.Contains(phoneNumber) || existingEmails.Contains(email)) continue;
        existingPhones.Add(phoneNumber);
        existingEmails.Add(email);
        var address = faker.Address.FullAddress().Replace("\n", ", ");
        if (address.Length > 255) address = address[..255];
        contacts.Add(new Contact
        {
            Name = $"{faker.Name.FirstName()} {faker.Name.LastName()}",
            PhoneNumber = phoneNumber,
            Email = email,
            Address = address,
            UserId = owner?.Id,
            CreatedAt = DateTime.UtcNow
        });
    }

    db.Contacts.AddRange(contacts);
    await db.SaveChangesAsync();
    Console.WriteLine($"Added {recordsNeeded} contacts. Total contacts: {targetCount}.");
}

app.Run();

public partial class Program;
