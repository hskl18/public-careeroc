namespace Careeroc.Infrastructure.Google;

public sealed class GmailOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int MessageFetchLimit { get; set; } = 20;
    public int SearchPageLimit { get; set; } = 3;
    public int SearchMessageLimit { get; set; } = 200;
    public int ThreadFetchLimit { get; set; } = 25;
    public int HistoryPageLimit { get; set; } = 5;
    public int HistoryMessageLimit { get; set; } = 200;
    public int BodyCharacterLimit { get; set; } = 12000;
}
