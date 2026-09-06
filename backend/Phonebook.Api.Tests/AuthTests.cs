using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Phonebook.Api.Services;
using Xunit;

namespace Phonebook.Api.Tests;

[Collection("api")]
public sealed class AuthTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task RootEndpoint_ReturnsRunningMessage()
    {
        var response = await Client.GetAsync("/");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Phonebook API is running", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Register_ValidUser_CreatesAccountAndSessionCookie()
    {
        var user = TestData.UniqueUser();
        var response = await Client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(user["email"], body.GetProperty("email").GetString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("phonebook_session="));
    }

    [Fact]
    public async Task Register_DuplicateUsernameOrEmail_ReturnsConflict()
    {
        var user = TestData.UniqueUser();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json)).StatusCode);

        var duplicateUsername = new Dictionary<string, string>(user) { ["email"] = "different@example.com" };
        var duplicateEmail = new Dictionary<string, string>(user) { ["username"] = "different_username" };

        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/auth/register", duplicateUsername, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/auth/register", duplicateEmail, ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Register_InvalidData_ReturnsUnprocessable()
    {
        var response = await Client.PostAsJsonAsync("/auth/register", new
        {
            username = "ab",
            email = "not-an-email",
            password = "short"
        }, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Login_ByUsernameAndEmail_Succeeds()
    {
        var user = TestData.UniqueUser();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json)).StatusCode);
        await Client.PostAsync("/auth/logout", null);

        foreach (var identifier in new[] { user["username"], user["email"] })
        {
            var response = await Client.PostAsJsonAsync("/auth/login", new
            {
                identifier,
                password = user["password"]
            }, ApiFactory.Json);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(user["username"], body.GetProperty("username").GetString());
            await Client.PostAsync("/auth/logout", null);
        }
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/auth/login", new
        {
            identifier = "missing-user",
            password = "wrong-password"
        }, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CurrentUser_RequiresAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/auth/me")).StatusCode);

        var user = TestData.UniqueUser();
        await Client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json);
        var response = await Client.GetAsync("/auth/me");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user["email"], body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Logout_InvalidatesSession()
    {
        var user = TestData.UniqueUser();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public void PasswordHash_IsCompatibleWithPythonScrypt()
    {
        var security = new SecurityService();
        const string pythonHash = "scrypt$AAECAwQFBgcICQoLDA0ODw==$-TWpMCXcNA8tK2qNm9ZnzhACHcOUIsMX2JcTblKjZr-E0YSC3CSTf0Nt0CQ-ITS3ZVYTuhmr2CFnAN_6yGWWNA==";

        Assert.True(security.VerifyPassword("StrongPass123!", pythonHash));
        Assert.False(security.VerifyPassword("wrong-password", pythonHash));
        Assert.True(security.VerifyPassword("round-trip-password", security.HashPassword("round-trip-password")));
    }
}
