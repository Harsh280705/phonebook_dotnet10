using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Phonebook.Api.Data;
using Xunit;

namespace Phonebook.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public HttpClient CreateAuthenticatedClient(object user)
    {
        var client = CreateClient();
        var response = client.PostAsJsonAsync("/auth/register", user, Json).GetAwaiter().GetResult();
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return client;
    }

    public Task InitializeAsync()
    {
        TestDatabase.Recreate();
        Environment.SetEnvironmentVariable("DATABASE_URL", TestDatabase.Url);
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        TestDatabase.Drop();
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE auth_sessions, contacts, users RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

public static class TestDatabase
{
    public const string Name = "phonebook_test";

    private static readonly bool InDocker = File.Exists("/.dockerenv");
    private static readonly string Host = Environment.GetEnvironmentVariable("TEST_POSTGRES_HOST") ?? (InDocker ? "db" : "localhost");
    private static readonly string Port = Environment.GetEnvironmentVariable("TEST_POSTGRES_PORT") ?? (InDocker ? "5432" : "5434");
    private static readonly string User = Environment.GetEnvironmentVariable("TEST_POSTGRES_USER") ?? "postgres";
    private static readonly string Password = Environment.GetEnvironmentVariable("TEST_POSTGRES_PASSWORD") ?? "RoKo@2024";

    public static string Url =>
        $"postgresql://{Uri.EscapeDataString(User)}:{Uri.EscapeDataString(Password)}@{Host}:{Port}/{Name}";

    public static void Recreate()
    {
        ExecuteOnPostgres($"""
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{Name}' AND pid <> pg_backend_pid();
            """);
        ExecuteOnPostgres($"DROP DATABASE IF EXISTS {Name};");
        ExecuteOnPostgres($"CREATE DATABASE {Name};");
    }

    public static void Drop()
    {
        ExecuteOnPostgres($"""
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{Name}' AND pid <> pg_backend_pid();
            """);
        ExecuteOnPostgres($"DROP DATABASE IF EXISTS {Name};");
    }

    private static void ExecuteOnPostgres(string sql)
    {
        var admin = $"Host={Host};Port={Port};Database=postgres;Username={User};Password={Password}";
        using var connection = new NpgsqlConnection(admin);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

public static class TestData
{
    public static Dictionary<string, string> UniqueUser(string prefix = "testuser")
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new Dictionary<string, string>
        {
            ["username"] = $"{prefix}_{suffix}",
            ["email"] = $"{prefix}_{suffix}@example.com",
            ["password"] = "StrongPass123!"
        };
    }

    public static Dictionary<string, string?> Contact(
        string name = "Test Contact",
        string phone = "+14155550101",
        string? email = "test-contact@example.com",
        string? address = "123 Test Street") => new()
    {
        ["name"] = name,
        ["phone_number"] = phone,
        ["email"] = email,
        ["address"] = address
    };
}

[Collection("api")]
public abstract class ApiTestBase : IDisposable
{
    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected ApiFactory Factory { get; }
    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        Factory.ResetDatabase();
        GC.SuppressFinalize(this);
    }

    protected HttpClient Register(Dictionary<string, string> user)
    {
        var client = Factory.CreateClient();
        var response = client.PostAsJsonAsync("/auth/register", user, ApiFactory.Json).GetAwaiter().GetResult();
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return client;
    }
}
