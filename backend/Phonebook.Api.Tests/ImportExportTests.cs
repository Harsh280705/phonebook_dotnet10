using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Phonebook.Api.Tests;

[Collection("api")]
public sealed class ImportExportTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static MultipartFormDataContent CsvUpload(string content, string filename = "contacts.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", filename } };
        return form;
    }

    [Fact]
    public async Task ExportAndImport_RequireAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/contacts/export")).StatusCode);
        using var upload = CsvUpload("name,phone_number\nA,+14155550101\n");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/contacts/import", upload)).StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsOnlyAuthenticatedUsersContacts()
    {
        var firstUser = TestData.UniqueUser();
        using var firstClient = Register(firstUser);
        var created = await (await firstClient.PostAsJsonAsync("/contacts/", TestData.Contact(), ApiFactory.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        await firstClient.PostAsync("/auth/logout", null);

        using var secondClient = Register(new Dictionary<string, string>
        {
            ["username"] = "export_other_user",
            ["email"] = "export_other_user@example.com",
            ["password"] = "StrongPass123!"
        });
        var otherContact = TestData.Contact("Other User Contact", "+14155550102", "other@example.com");
        Assert.Equal(HttpStatusCode.Created, (await secondClient.PostAsJsonAsync("/contacts/", otherContact, ApiFactory.Json)).StatusCode);

        var response = await secondClient.GetAsync("/contacts/export");
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Name,Phone number,Email,Address", csv);
        Assert.Contains(otherContact["name"]!, csv);
        Assert.Contains(otherContact["phone_number"]!, csv);
        Assert.DoesNotContain(created.GetProperty("name").GetString() ?? "", csv);
    }

    [Fact]
    public async Task Import_ValidCsv_PersistsContacts()
    {
        using var client = Register(TestData.UniqueUser());
        const string content =
            "name,phone_number,email,address\n" +
            "Imported Alpha,+14155550121,alpha@example.com,123 Import Street\n" +
            "Imported Beta,+14155550122,beta@example.com,456 Import Avenue\n";
        using var upload = CsvUpload(content);
        var response = await client.PostAsync("/contacts/import", upload);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var contacts = await (await client.GetAsync("/contacts/?page=1&limit=10&search=Imported"))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, summary.GetProperty("total_rows").GetInt32());
        Assert.Equal(2, summary.GetProperty("imported").GetInt32());
        Assert.Equal(2, contacts.GetProperty("total").GetInt32());
        var emails = contacts.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "alpha@example.com", "beta@example.com" }, emails);
    }

    [Fact]
    public async Task Import_ExternalColumnsAndExcelPhoneText()
    {
        using var client = Register(TestData.UniqueUser());
        const string content =
            "First Name,Last Name,Email,Phone,City,Country\n" +
            "External,Contact,external@example.com,=\"+14155550131\",Mumbai,India\n";
        using var upload = CsvUpload(content);
        var response = await client.PostAsync("/contacts/import", upload);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var contacts = await (await client.GetAsync("/contacts/?page=1&limit=10&search=External"))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, summary.GetProperty("imported").GetInt32());
        Assert.Equal("+14155550131", contacts.GetProperty("items")[0].GetProperty("phone_number").GetString());
        Assert.Equal("Mumbai India", contacts.GetProperty("items")[0].GetProperty("address").GetString());
    }

    [Fact]
    public async Task Import_InvalidRowsAndScientificNotation()
    {
        using var client = Register(TestData.UniqueUser());
        const string content =
            "name,phone_number,email,address\n" +
            "Invalid Phone,9.11235E+11,valid@example.com,123 Invalid Street\n" +
            "Invalid Email,+14155550132,not-an-email,123 Invalid Street\n" +
            ",+14155550133,missing@example.com,123 Invalid Street\n";
        using var upload = CsvUpload(content);
        var response = await client.PostAsync("/contacts/import", upload);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, summary.GetProperty("total_rows").GetInt32());
        Assert.Equal(0, summary.GetProperty("imported").GetInt32());
        Assert.Equal(3, summary.GetProperty("invalid_rows").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid_phone_numbers").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid_emails").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid_names").GetInt32());
    }

    [Fact]
    public async Task Import_DuplicatesAreSkipped()
    {
        using var client = Register(TestData.UniqueUser());
        const string content =
            "name,phone_number,email,address\n" +
            "First Import,+14155550141,first@example.com,123 Duplicate Street\n" +
            "Duplicate Phone,+14155550141,second@example.com,456 Duplicate Street\n" +
            "Duplicate Email,+14155550142,first@example.com,789 Duplicate Street\n";
        using var firstUpload = CsvUpload(content);
        using var secondUpload = CsvUpload(content);
        var first = await (await client.PostAsync("/contacts/import", firstUpload)).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var second = await (await client.PostAsync("/contacts/import", secondUpload)).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(1, first.GetProperty("imported").GetInt32());
        Assert.Equal(2, first.GetProperty("skipped_duplicates").GetInt32());
        Assert.Equal(0, second.GetProperty("imported").GetInt32());
        Assert.Equal(3, second.GetProperty("skipped_duplicates").GetInt32());
    }

    [Fact]
    public async Task Import_RejectsMissingHeaders()
    {
        using var client = Register(TestData.UniqueUser());
        using var upload = CsvUpload("email,address\na@example.com,123 Street\n");
        var response = await client.PostAsync("/contacts/import", upload);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", body.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_RejectsNonCsvAndEmptyFiles()
    {
        using var client = Register(TestData.UniqueUser());
        using var nonCsv = CsvUpload("name,phone\nTest,+14155550151\n", "contacts.txt");
        using var empty = CsvUpload("", "empty.csv");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/contacts/import", nonCsv)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/contacts/import", empty)).StatusCode);
    }
}
