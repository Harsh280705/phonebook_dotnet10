using System.Text.Json.Serialization;

namespace Phonebook.Api;

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string Identifier, string Password);

public sealed record UserResponse(
    int Id,
    string Username,
    string Email,
    DateTime CreatedAt);

public sealed record TagRequest(string Name);
public sealed record TagResponse(int Id, string Name);

public sealed record ContactRequest(
    string? Name,
    [property: JsonPropertyName("phone_number")] string? PhoneNumber,
    string? Email,
    string? Address,
    [property: JsonPropertyName("tag_ids")] IReadOnlyList<int>? TagIds = null);

public sealed record ContactResponse(
    int Id,
    string Name,
    [property: JsonPropertyName("phone_number")] string PhoneNumber,
    string? Email,
    string? Address,
    DateTime CreatedAt,
    IReadOnlyList<TagResponse> Tags);

public sealed record ContactPage(
    IReadOnlyList<ContactResponse> Items,
    int Page,
    int Limit,
    int Total,
    [property: JsonPropertyName("total_pages")] int TotalPages);

public sealed record ErrorResponse(string Detail);
