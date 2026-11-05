using System.Text.Json;
using Careeroc.Application.Services;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

// Scores the production pipeline (full API dependency injection) against eval/pipeline-labels.json.
// Set CAREEROC_EVAL_RESULTS_PATH to a repo-relative path to write the scores there.
// scripts/eval-model.sh does this with a model behind the optional pass; CI runs the rules alone.
public sealed class PipelineEvaluationTests
{
    private const string VisitorEmail = "visitor-eval@demo.careeroc.example";

    [Fact]
    public async Task DemoInboxPipelineMeetsQualityThresholds()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var labels = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "eval", "pipeline-labels.json")));
        await using var factory = new ApiRoutingSecurityTests.CareerocApiFactory(
            new Dictionary<string, string?> { ["Demo:Enabled"] = "true" });
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoWorkspaceService>().EnsureWorkspaceAsync(VisitorEmail);
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        var emails = await dbContext.InboxEmails
            .AsNoTracking()
            .Include(email => email.JobApplication)
            .ToDictionaryAsync(email => email.ExternalMessageId);
        var applications = await dbContext.JobApplications.AsNoTracking().ToListAsync();
        var modelCalls = await dbContext.ModelCallEvents.AsNoTracking().ToListAsync();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OllamaEmailExtractionOptions>>().Value;

        var labeledEmails = labels.RootElement.GetProperty("emails").EnumerateObject().ToList();
        var categoryHits = labeledEmails.Count(label =>
            emails[label.Name].Category?.ToString() == label.Value.GetProperty("category").GetString());
        var companyHits = labeledEmails.Count(label =>
            (emails[label.Name].JobApplication?.Company ?? emails[label.Name].ExtractedCompany) ==
            label.Value.GetProperty("company").GetString());
        var ignored = labels.RootElement.GetProperty("ignored").EnumerateArray().Select(value => value.GetString()!).ToList();
        var triageHits = ignored.Count(id => emails[id].Category is null) +
            labeledEmails.Count(label => emails[label.Name].Category is not null);
        var expectedReview = labels.RootElement.GetProperty("needsReview").EnumerateArray().Select(value => value.GetString()!).ToHashSet();
        var flagged = emails.Values.Where(email => email.RequiresManualReview).Select(email => email.ExternalMessageId).ToHashSet();
        var expectedApplications = labels.RootElement.GetProperty("applications").EnumerateObject().ToList();
        var statusHits = expectedApplications.Count(expected =>
            applications.Any(application => application.Company == expected.Name && application.Status.ToString() == expected.Value.GetString()));
        var unexpectedApplications = applications
            .Select(application => application.Company)
            .Where(company => expectedApplications.All(expected => expected.Name != company))
            .Order()
            .ToList();

        var results = new
        {
            dataset = "src/Careeroc.Infrastructure/Demo/demo-inbox.json",
            pipeline = options.Enabled ? $"heuristic extraction with {options.Model}" : "heuristic extraction, no model",
            modelCalls = modelCalls.Count,
            modelCallFailures = modelCalls.Count(call => call.Outcome != "success"),
            modelSeconds = Math.Round(modelCalls.Sum(call => call.LatencyMs) / 1000d),
            triageAccuracy = Ratio(triageHits, ignored.Count + labeledEmails.Count),
            categoryAccuracy = Ratio(categoryHits, labeledEmails.Count),
            companyAccuracy = Ratio(companyHits, labeledEmails.Count),
            applicationStatusAccuracy = Ratio(statusHits, expectedApplications.Count),
            reviewRecall = Ratio(expectedReview.Count(flagged.Contains), expectedReview.Count),
            reviewPrecision = Ratio(flagged.Count(expectedReview.Contains), flagged.Count),
            unexpectedApplications,
            categoryMisses = labeledEmails
                .Where(label => emails[label.Name].Category?.ToString() != label.Value.GetProperty("category").GetString())
                .Select(label => $"{label.Name}: expected {label.Value.GetProperty("category").GetString()}, got {emails[label.Name].Category?.ToString() ?? "ignored"}")
                .ToList(),
            flaggedForReview = flagged.Order().ToList()
        };

        if (Environment.GetEnvironmentVariable("CAREEROC_EVAL_RESULTS_PATH") is { Length: > 0 } resultsPath)
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, resultsPath),
                JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
        }

        // Thresholds sit just under the current scores so a regression fails CI.
        Assert.True(results.triageAccuracy >= 0.95, $"triage {results.triageAccuracy}");
        Assert.True(results.categoryAccuracy >= 0.95, $"category {results.categoryAccuracy}: {string.Join("; ", results.categoryMisses)}");
        Assert.True(results.companyAccuracy >= 0.95, $"company {results.companyAccuracy}");
        Assert.True(results.applicationStatusAccuracy >= 0.95, $"status {results.applicationStatusAccuracy}");
        Assert.Empty(results.unexpectedApplications);
        Assert.True(results.reviewRecall >= 1.0, $"review recall {results.reviewRecall}: {string.Join(", ", results.flaggedForReview)}");
        Assert.True(results.reviewPrecision >= 0.8, $"review precision {results.reviewPrecision}: {string.Join(", ", results.flaggedForReview)}");
    }

    private static double Ratio(int hits, int total)
    {
        return total == 0 ? 1 : Math.Round((double)hits / total, 3);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Careeroc.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
