namespace Careeroc.Application.Models;

public sealed record ExternalInboxEmail(
    string ExternalMessageId,
    string ThreadId,
    string Sender,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAtUtc,
    bool IsOutbound = false);

public sealed record DownloadedInboxAttachment(
    string SourceId,
    string FileName,
    string ContentType,
    int SizeBytes,
    byte[] ContentBytes);
