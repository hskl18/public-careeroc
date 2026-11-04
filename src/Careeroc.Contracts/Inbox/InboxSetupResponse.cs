namespace Careeroc.Contracts.Inbox;

public sealed record InboxSetupResponse(IReadOnlyList<InboxAccountSetupDto> Accounts);
