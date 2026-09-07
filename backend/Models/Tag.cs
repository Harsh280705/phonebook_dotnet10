namespace Phonebook.Api.Models;

public sealed class Tag
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public ICollection<ContactTag> ContactTags { get; set; } = [];
}

public sealed class ContactTag
{
    public int ContactId { get; set; }
    public int TagId { get; set; }
}
