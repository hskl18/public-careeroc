namespace Careeroc.Domain;

public static class UserRoleNames
{
    public const string User = "user";
    public const string Admin = "admin";

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), Admin, StringComparison.OrdinalIgnoreCase)
            ? Admin
            : User;
    }

    public static bool IsAdmin(string? value)
    {
        return string.Equals(Normalize(value), Admin, StringComparison.Ordinal);
    }
}
