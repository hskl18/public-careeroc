namespace Careeroc.Application.Models;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    // Reserved .example TLD: no real mailbox can own a demo identity, so Google sign-in can never reach a demo workspace.
    public string EmailDomain { get; set; } = "demo.careeroc.example";

    public int WorkspaceLifetimeHours { get; set; } = 24;

    public int MaxWorkspacesPerHour { get; set; } = 120;

    public bool IsDemoEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) &&
               email.Trim().EndsWith($"@{EmailDomain}", StringComparison.OrdinalIgnoreCase);
    }
}
