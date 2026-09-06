using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Phonebook.Api.Tests;

[Collection("api")]
public sealed class ContactTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task ProtectedContactEndpoints_RequireAuthentication()
    {
        var payload = TestData.Contact();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/contacts/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("/contacts/", payload, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/contacts/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PutAsJsonAsync("/contacts/1", payload, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.DeleteAsync("/contacts/1")).StatusCode);
    }

    [Fact]
    public async Task CreateGetAndListContacts()
    {
        using var client = Register(TestData.UniqueUser());
        var payload = TestData.Contact();
        var created = await client.PostAsJsonAsync("/contacts/", payload, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contact = await created.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        var single = await client.GetAsync($"/contacts/{contact.GetProperty("id").GetInt32()}");
        var listing = await client.GetAsync("/contacts/?page=1&limit=10");
        var listingBody = await listing.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var singleBody = await single.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Equal(payload["phone_number"], singleBody.GetProperty("phone_number").GetString());
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal(1, listingBody.GetProperty("total").GetInt32());
        Assert.Equal(contact.GetProperty("id").GetInt32(), listingBody.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Create_InvalidContactData_ReturnsUnprocessable()
    {
        using var client = Register(TestData.UniqueUser());
        var invalid = TestData.Contact(phone: "not-a-phone");
        var response = await client.PostAsJsonAsync("/contacts/", invalid, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task DuplicatePhoneAndEmail_AreRejected()
    {
        using var client = Register(TestData.UniqueUser());
        var payload = TestData.Contact();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/contacts/", payload, ApiFactory.Json)).StatusCode);

        var duplicatePhone = TestData.Contact(email: "other@example.com");
        var duplicateEmail = TestData.Contact(phone: "+14155550102");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/contacts/", duplicatePhone, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/contacts/", duplicateEmail, ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task UpdateContact_ChangesProvidedFields()
    {
        using var client = Register(TestData.UniqueUser());
        var created = await (await client.PostAsJsonAsync("/contacts/", TestData.Contact(), ApiFactory.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        var response = await client.PutAsJsonAsync($"/contacts/{created.GetProperty("id").GetInt32()}", new
        {
            name = "Updated Contact",
            address = "456 Updated Avenue"
        }, ApiFactory.Json);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated Contact", body.GetProperty("name").GetString());
        Assert.Equal("456 Updated Avenue", body.GetProperty("address").GetString());
    }

    [Fact]
    public async Task DeleteContact_AndMissingContact()
    {
        using var client = Register(TestData.UniqueUser());
        var created = await (await client.PostAsJsonAsync("/contacts/", TestData.Contact(), ApiFactory.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var contactId = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/contacts/{contactId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/contacts/{contactId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/contacts/999999")).StatusCode);
    }

    [Fact]
    public async Task SearchAndPagination()
    {
        using var client = Register(TestData.UniqueUser());
        for (var index = 0; index < 12; index++)
        {
            var response = await client.PostAsJsonAsync("/contacts/", TestData.Contact(
                name: $"Search Person {(char)('A' + index)}",
                phone: $"+1415555{1000 + index:0000}",
                email: $"search{index}@example.com",
                address: "123 Search Street"
            ), ApiFactory.Json);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var page = await (await client.GetAsync("/contacts/?page=2&limit=10")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var search = await (await client.GetAsync("/contacts/?page=1&limit=10&search=Search Person A")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(10, page.GetProperty("limit").GetInt32());
        Assert.Equal(12, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        Assert.Equal("Search Person A", search.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task UsersCannotAccessEachOthersContacts()
    {
        var firstUser = TestData.UniqueUser();
        using var firstClient = Register(firstUser);
        var created = await (await firstClient.PostAsJsonAsync("/contacts/", TestData.Contact(), ApiFactory.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        await firstClient.PostAsync("/auth/logout", null);

        using var secondClient = Register(new Dictionary<string, string>
        {
            ["username"] = "second-user",
            ["email"] = "second-user@example.com",
            ["password"] = "StrongPass123!"
        });
        var listing = await (await secondClient.GetAsync("/contacts/?page=1&limit=10")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.GetAsync($"/contacts/{created.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.PutAsJsonAsync(
            $"/contacts/{created.GetProperty("id").GetInt32()}",
            new { name = "Hijacked Contact" },
            ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secondClient.DeleteAsync($"/contacts/{created.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(0, listing.GetProperty("total").GetInt32());
    }
}
