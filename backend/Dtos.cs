using System.Text.Json.Serialization;

namespace Phonebook.Api;

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string Identifier, string Password);

public sealed record UserResponse(
    int Id,
    string Username,
    string Email,
    DateTime CreatedAt);

public sealed record ContactRequest(
    string? Name,
    [property: JsonPropertyName("phone_number")] string? PhoneNumber,
    string? Email,
    string? Address);

public sealed record ContactResponse(
    int Id,
    string Name,
    [property: JsonPropertyName("phone_number")] string PhoneNumber,
    string? Email,
    string? Address,
    DateTime CreatedAt);

public sealed record ContactPage(
    IReadOnlyList<ContactResponse> Items,
    int Page,
    int Limit,
    int Total,
    [property: JsonPropertyName("total_pages")] int TotalPages);

public sealed record ErrorResponse(string Detail);
