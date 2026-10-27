namespace Careeroc.Application;

public static class DateTimeOffsetExtensions
{
    public static DateTimeOffset NormalizeUtc(this DateTimeOffset value)
    {
        return value.Offset == TimeSpan.Zero
            ? value
            : value.ToUniversalTime();
    }

    public static DateTimeOffset? NormalizeUtc(this DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.NormalizeUtc()
            : null;
    }
}
