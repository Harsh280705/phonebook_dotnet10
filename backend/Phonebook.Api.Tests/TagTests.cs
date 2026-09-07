using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Phonebook.Api.Tests;

[Collection("api")]
public sealed class TagTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task ProtectedTagEndpoints_RequireAuthentication()
    {
        var payload = new { name = "Work" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/tags/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("/tags/", payload, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PutAsJsonAsync("/tags/1", payload, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.DeleteAsync("/tags/1")).StatusCode);
    }

    [Fact]
    public async Task CreateListUpdateAndDeleteTags()
    {
        using var client = Register(TestData.UniqueUser());
        var created = await client.PostAsJsonAsync("/tags/", new { name = " Family " }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = await created.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal("Family", tag.GetProperty("name").GetString());

        var listing = await (await client.GetAsync("/tags/")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(1, listing.GetArrayLength());
        Assert.Equal(tag.GetProperty("id").GetInt32(), listing[0].GetProperty("id").GetInt32());

        var updated = await client.PutAsJsonAsync($"/tags/{tag.GetProperty("id").GetInt32()}", new { name = "Personal" }, ApiFactory.Json);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Personal", updatedBody.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/tags/{tag.GetProperty("id").GetInt32()}")).StatusCode);
        var empty = await (await client.GetAsync("/tags/")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(0, empty.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/tags/999999")).StatusCode);
    }

    [Fact]
    public async Task DuplicateAndInvalidTagNames_AreRejected()
    {
        using var client = Register(TestData.UniqueUser());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/tags/", new { name = "Work" }, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/tags/", new { name = "work" }, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/tags/", new { name = "" }, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/tags/", new { name = "bad,name" }, ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task AssignMultipleTagsToContacts_AndClearThem()
    {
        using var client = Register(TestData.UniqueUser());
        var work = await CreateTag(client, "Work");
        var family = await CreateTag(client, "Family");

        var created = await client.PostAsJsonAsync("/contacts/", new
        {
            name = "Tagged Contact",
            phone_number = "+14155550101",
            email = "tagged@example.com",
            address = "123 Test Street",
            tag_ids = new[] { work, family }
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contact = await created.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(2, contact.GetProperty("tags").GetArrayLength());

        var contactId = contact.GetProperty("id").GetInt32();
        var updated = await client.PutAsJsonAsync($"/contacts/{contactId}", new { tag_ids = new[] { work } }, ApiFactory.Json);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1, updatedBody.GetProperty("tags").GetArrayLength());
        Assert.Equal("Work", updatedBody.GetProperty("tags")[0].GetProperty("name").GetString());

        var cleared = await client.PutAsJsonAsync($"/contacts/{contactId}", new { tag_ids = Array.Empty<int>() }, ApiFactory.Json);
        Assert.Equal(0, (await cleared.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json)).GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task ExistingContactUpdateWithoutTagIds_LeavesTagsUnchanged()
    {
        using var client = Register(TestData.UniqueUser());
        var work = await CreateTag(client, "Work");
        var created = await (await client.PostAsJsonAsync("/contacts/", new
        {
            name = "Tagged Contact",
            phone_number = "+14155550101",
            email = "tagged@example.com",
            address = "123 Test Street",
            tag_ids = new[] { work }
        }, ApiFactory.Json)).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        var updated = await client.PutAsJsonAsync($"/contacts/{created.GetProperty("id").GetInt32()}", new
        {
            name = "Tagged Contact Updated"
        }, ApiFactory.Json);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Tagged Contact Updated", body.GetProperty("name").GetString());
        Assert.Equal(1, body.GetProperty("tags").GetArrayLength());
        Assert.Equal(work, body.GetProperty("tags")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task FilterContactsByTags_AndCombineWithTextSearch()
    {
        using var client = Register(TestData.UniqueUser());
        var work = await CreateTag(client, "Work");
        var family = await CreateTag(client, "Family");

        await CreateTaggedContact(client, "Alice Work", "+14155551001", "alice-work@example.com", [work]);
        await CreateTaggedContact(client, "Alice Family", "+14155551002", "alice-family@example.com", [family]);
        await CreateTaggedContact(client, "Bob Both", "+14155551003", "bob-both@example.com", [work, family]);

        var byWork = await (await client.GetAsync($"/contacts/?page=1&limit=10&tag_ids={work}")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var byBoth = await (await client.GetAsync($"/contacts/?page=1&limit=10&tag_ids={work}&tag_ids={family}")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var combined = await (await client.GetAsync($"/contacts/?page=1&limit=10&search=Alice&tag_ids={work}")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var paged = await (await client.GetAsync($"/contacts/?page=1&limit=1&tag_ids={work}")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(2, byWork.GetProperty("total").GetInt32());
        Assert.Equal(1, byBoth.GetProperty("total").GetInt32());
        Assert.Equal("Bob Both", byBoth.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(1, combined.GetProperty("total").GetInt32());
        Assert.Equal("Alice Work", combined.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(2, paged.GetProperty("total").GetInt32());
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        Assert.Equal(2, paged.GetProperty("total_pages").GetInt32());
    }

    [Fact]
    public async Task UsersCannotAccessEachOthersTagsOrAssignThem()
    {
        using var firstClient = Register(TestData.UniqueUser("first"));
        var firstTag = await CreateTag(firstClient, "Secret");
        var firstContact = await CreateTaggedContact(firstClient, "First Person", "+14155552001", "first-person@example.com", [firstTag]);
        await firstClient.PostAsync("/auth/logout", null);

        using var secondClient = Register(TestData.UniqueUser("second"));
        var listing = await (await secondClient.GetAsync("/tags/")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(0, listing.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.PutAsJsonAsync($"/tags/{firstTag}", new { name = "Stolen" }, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.DeleteAsync($"/tags/{firstTag}")).StatusCode);

        var ownTag = await CreateTag(secondClient, "Mine");
        var created = await secondClient.PostAsJsonAsync("/contacts/", new
        {
            name = "Second Person",
            phone_number = "+14155552002",
            email = "second-person@example.com",
            address = "123 Test Street",
            tag_ids = new[] { firstTag }
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);

        var ownContact = await CreateTaggedContact(secondClient, "Second Person", "+14155552002", "second-person@example.com", [ownTag]);
        Assert.Equal(HttpStatusCode.BadRequest, (await secondClient.PutAsJsonAsync(
            $"/contacts/{ownContact}",
            new { tag_ids = new[] { firstTag } },
            ApiFactory.Json)).StatusCode);

        var filtered = await (await secondClient.GetAsync($"/contacts/?page=1&limit=10&tag_ids={firstTag}")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal(0, filtered.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.GetAsync($"/contacts/{firstContact}")).StatusCode);
    }

    private static async Task<int> CreateTag(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/tags/", new { name }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateTaggedContact(HttpClient client, string name, string phone, string email, int[] tagIds)
    {
        var response = await client.PostAsJsonAsync("/contacts/", new
        {
            name,
            phone_number = phone,
            email,
            address = "123 Test Street",
            tag_ids = tagIds
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        return body.GetProperty("id").GetInt32();
    }
}
