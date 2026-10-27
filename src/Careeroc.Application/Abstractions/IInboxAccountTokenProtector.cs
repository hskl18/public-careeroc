namespace Careeroc.Application.Abstractions;

public interface IInboxAccountTokenProtector
{
    string? Protect(string? token);
    string? Unprotect(string? token);
}

public sealed class NoopInboxAccountTokenProtector : IInboxAccountTokenProtector
{
    public static readonly NoopInboxAccountTokenProtector Instance = new();

    private NoopInboxAccountTokenProtector()
    {
    }

    public string? Protect(string? token)
    {
        return token;
    }

    public string? Unprotect(string? token)
    {
        return token;
    }
}
