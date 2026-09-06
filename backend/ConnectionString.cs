namespace Phonebook.Api;

public static class ConnectionString
{
    public static string ToNpgsql(string value)
    {
        value = value.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        var port = uri.IsDefaultPort ? 5432 : uri.Port;
        return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password}";
    }
}
