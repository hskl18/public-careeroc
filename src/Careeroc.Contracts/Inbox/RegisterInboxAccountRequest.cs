namespace Careeroc.Contracts.Inbox;

public sealed class RegisterInboxAccountRequest
{
    public string EmailAddress { get; set; } = string.Empty;
    public int? BackfillMonths { get; set; }
}
