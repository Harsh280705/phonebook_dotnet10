namespace Phonebook.Api.Models;

public sealed class Contact
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Name { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<ContactTag> ContactTags { get; set; } = [];
}
