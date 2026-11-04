using System.Text.Json;

namespace Careeroc.Application.Models;

public sealed record AgentTraceFact(
    string Label,
    string Value);

public sealed record AgentTraceStep(
    string Stage,
    string AgentName,
    string Summary,
    string? Reason,
    IReadOnlyList<AgentTraceFact> Facts);

public sealed record InboxEmailAgentTrace(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<AgentTraceStep> Steps);

public static class InboxEmailAgentTraceJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(InboxEmailAgentTrace trace)
    {
        return JsonSerializer.Serialize(trace, SerializerOptions);
    }

    public static InboxEmailAgentTrace? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InboxEmailAgentTrace>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ExtractProcessingSource(InboxEmailAgentTrace? trace)
    {
        var extractionStep = trace?.Steps.FirstOrDefault(
            step => string.Equals(step.Stage, "workflow_extraction", StringComparison.Ordinal));
        if (extractionStep is null)
        {
            return null;
        }

        var parsingSource = FindFactValue(extractionStep, "Parsing source");
        if (string.IsNullOrWhiteSpace(parsingSource))
        {
            return extractionStep.AgentName;
        }

        var parsingModel = FindFactValue(extractionStep, "Parsing model");
        return string.IsNullOrWhiteSpace(parsingModel)
            ? $"{extractionStep.AgentName}: {parsingSource}"
            : $"{extractionStep.AgentName}: {parsingSource} ({parsingModel})";
    }

    private static string? FindFactValue(AgentTraceStep step, string label)
    {
        return step.Facts.FirstOrDefault(
            fact => string.Equals(fact.Label, label, StringComparison.Ordinal))?.Value;
    }
}
