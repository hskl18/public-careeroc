namespace Careeroc.Application;

internal static class RequestEnum
{
    // Accepts a defined name in any case, with spaces, dashes, or underscores ("Oa Pending", "oa_pending").
    // Enum.TryParse alone also accepts "0" or "Applied,Rejected", which would store a value no name defines.
    public static TEnum Parse<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        var normalized = (value ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (normalized.Length > 0 &&
            !normalized.Contains(',') &&
            !char.IsAsciiDigit(normalized[0]) &&
            Enum.TryParse<TEnum>(normalized, true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new UserFacingException($"Unsupported value '{value}' for {typeof(TEnum).Name}.");
    }
}
