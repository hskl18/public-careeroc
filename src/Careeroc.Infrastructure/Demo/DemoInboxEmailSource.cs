using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Infrastructure.Demo;

// Serves the fictional inbox in demo-inbox.json to the regular sync pipeline.
public sealed partial class DemoInboxEmailSource : IInboxEmailSource
{
    private const string FixtureResourceName = "Careeroc.Infrastructure.Demo.demo-inbox.json";

    private static readonly Lazy<IReadOnlyList<DemoFixtureEmail>> Fixture = new(LoadFixture);

    private readonly IClock _clock;

    public static int FixtureEmailCount => Fixture.Value.Count;

    public DemoInboxEmailSource(IClock clock)
    {
        _clock = clock;
    }

    public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
        InboxAccount inboxAccount,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        CancellationToken cancellationToken = default)
    {
        // Anchor to the workspace creation time so later syncs produce identical timestamps and dates.
        var anchor = inboxAccount.CreatedAtUtc == default ? _clock.UtcNow : inboxAccount.CreatedAtUtc;
        var ownAddress = $"Alex Rivera <{inboxAccount.EmailAddress}>";

        IReadOnlyList<ExternalInboxEmail> emails = Fixture.Value
            .Where(email => existingMessageIds?.Contains(email.Id) != true)
            .Select(
                email => new ExternalInboxEmail(
                    email.Id,
                    $"demo-thread-{email.Thread}",
                    email.Outbound ? ownAddress : email.From,
                    RenderDates(email.Subject, anchor),
                    RenderDates(email.Body, anchor),
                    anchor.AddDays(-email.DaysAgo),
                    email.Outbound))
            .ToList();

        return Task.FromResult(emails);
    }

    private static IReadOnlyList<DemoFixtureEmail> LoadFixture()
    {
        using var stream = typeof(DemoInboxEmailSource).Assembly.GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException($"Embedded demo fixture {FixtureResourceName} is missing.");
        var fixture = JsonSerializer.Deserialize<DemoFixture>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Demo fixture is empty.");
        return fixture.Emails;
    }

    private static string RenderDates(string text, DateTimeOffset anchor)
    {
        return DatePlaceholderRegex().Replace(
            text,
            match => anchor
                .AddDays(int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture))
                .ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US")));
    }

    [GeneratedRegex(@"\{\{date:(?<offset>[+-]\d+)\}\}")]
    private static partial Regex DatePlaceholderRegex();

    internal sealed record DemoFixture(IReadOnlyList<DemoFixtureEmail> Emails);

    internal sealed record DemoFixtureEmail(
        string Id,
        string Thread,
        double DaysAgo,
        string From,
        string Subject,
        string Body,
        bool Outbound = false);
}
