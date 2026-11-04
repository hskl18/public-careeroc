using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
    private static ResumeExtraction BuildExtractionFromVersion(
        ResumeVersion version,
        ResumeStructuredDataDto structuredData)
    {
        var skills = DeserializeList(version.SkillsJson);
        var projectHighlights = DeserializeList(version.ProjectHighlightsJson);
        return new ResumeExtraction(
            version.Summary,
            skills,
            projectHighlights,
            Math.Max(version.ExperienceCount, structuredData.Experience.Count),
            Math.Max(version.ProjectCount, structuredData.Projects.Count),
            structuredData.Education.Count > 0,
            !string.IsNullOrWhiteSpace(structuredData.Contact.Email) ||
            !string.IsNullOrWhiteSpace(structuredData.Contact.Phone),
            structuredData.Contact.Links.Count > 0,
            structuredData);
    }

    private static void ApplyStructuredSnapshotToVersion(
        ResumeVersion version,
        ResumeStructuredDataDto structuredData,
        DateTimeOffset nowUtc,
        string extractionMethod,
        string? promptVersion = null)
    {
        var skills = FlattenSkills(structuredData.Skills);
        var projectHighlights = structuredData.Projects
            .Select(project => string.IsNullOrWhiteSpace(project.Description)
                ? project.Name
                : $"{project.Name}: {project.Description}")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(12)
            .ToList();
        version.SkillsJson = SerializeList(skills);
        version.ProjectHighlightsJson = SerializeList(projectHighlights);
        version.StructuredDataJson = SerializeStructuredData(structuredData);
        version.ExtractionMetadataJson = JsonSerializer.Serialize(
            BuildExtractionMetadata(
                version.ResumeDocument.SourceKind,
                version.ResumeDocument.SourceFileName,
                version.ResumeDocument.SourceText,
                structuredData,
                extractionMethod,
                nowUtc,
                promptVersion),
            JsonOptions);
        version.ExperienceCount = structuredData.Experience.Count;
        version.ProjectCount = structuredData.Projects.Count;
    }

    private static ResumeStructuredDataDto MergeStructuredSnapshot(
        ResumeStructuredDataDto preferred,
        ResumeStructuredDataDto extracted)
    {
        return new ResumeStructuredDataDto(
            preferred.Contact,
            preferred.Experience.Count > 0 ? preferred.Experience : extracted.Experience,
            preferred.Education.Count > 0 ? preferred.Education : extracted.Education,
            FlattenSkills(preferred.Skills).Count > 0 ? preferred.Skills : extracted.Skills,
            preferred.Projects.Count > 0 ? preferred.Projects : extracted.Projects);
    }

    private static ResumeStructuredDataDto ApplyStructuredCorrections(
        ResumeStructuredDataDto structuredData,
        IEnumerable<ResumeCorrection> corrections)
    {
        var current = structuredData;
        foreach (var correction in corrections.OrderBy(correction => correction.CreatedAtUtc))
        {
            if (TryBuildPatchOperation(correction, out var operation))
            {
                current = ApplyStructuredDataPatch(current, operation);
            }
        }

        return current;
    }

    private static bool TryBuildPatchOperation(
        ResumeCorrection correction,
        out NormalizedPatchOperation operation)
    {
        operation = default!;
        var path = correction.FieldPath.Trim();
        if (path.Length == 0 || !LooksLikeStructuredPatchPath(path))
        {
            return false;
        }

        var jsonPath = path.StartsWith('/') ? path : "/" + path.Replace(".", "/", StringComparison.Ordinal);
        var op = string.Equals(correction.CorrectedValue, "__removed__", StringComparison.Ordinal)
            ? "remove"
            : "replace";
        var value = JsonSerializer.SerializeToElement(correction.CorrectedValue, JsonOptions);
        operation = new NormalizedPatchOperation(op, jsonPath, value);
        return true;
    }

    private static bool LooksLikeStructuredPatchPath(string path)
    {
        return path.StartsWith("/", StringComparison.Ordinal) ||
               path.StartsWith("contact.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("experience.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("education.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("skills.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("projects.", StringComparison.OrdinalIgnoreCase);
    }

    private static NormalizedPatchRequest NormalizePatchRequest(PatchResumeStructuredDataRequest request)
    {
        if (request.Operations.Count is 0 or > 40)
        {
            throw new InvalidOperationException("Patch must contain between 1 and 40 operations.");
        }

        var operations = request.Operations.Select(NormalizePatchOperation).ToList();
        var note = NormalizeOptionalCorrectionValue(request.Note, 1000, "Patch note");
        return new NormalizedPatchRequest(operations, note);
    }

    private static NormalizedPatchOperation NormalizePatchOperation(ResumeStructuredDataPatchOperation operation)
    {
        var op = operation.Op.Trim().ToLowerInvariant();
        if (op is not ("add" or "replace" or "remove"))
        {
            throw new InvalidOperationException("Resume patch operation must be add, replace, or remove.");
        }

        var path = operation.Path.Trim();
        if (path.Length is 0 or > 240 || !path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resume patch path is invalid.");
        }

        if (op is not "remove" && operation.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new InvalidOperationException("Resume patch value is required for add and replace operations.");
        }

        return new NormalizedPatchOperation(op, path, operation.Value);
    }

    private static ResumeStructuredDataDto ApplyStructuredDataPatch(
        ResumeStructuredDataDto structuredData,
        NormalizedPatchOperation operation)
    {
        var json = JsonSerializer.SerializeToNode(structuredData, JsonOptions)
                   ?? throw new InvalidOperationException("Resume structured data could not be patched.");
        ApplyJsonPatchOperation(json, operation);
        return NormalizeStructuredData(
            json.Deserialize<ResumeStructuredDataDto>(JsonOptions)
            ?? throw new InvalidOperationException("Resume structured data patch produced invalid data."));
    }

    private static void ApplyJsonPatchOperation(JsonNode json, NormalizedPatchOperation operation)
    {
        var segments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
            .ToList();
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Resume patch path is invalid.");
        }

        var parent = json;
        foreach (var segment in segments.Take(segments.Count - 1))
        {
            parent = ResolvePatchChild(parent, segment);
        }

        var leaf = segments[^1];
        if (parent is JsonObject parentObject)
        {
            if (operation.Op == "remove")
            {
                parentObject.Remove(leaf);
                return;
            }

            parentObject[leaf] = JsonSerializer.SerializeToNode(operation.Value, JsonOptions);
            return;
        }

        if (parent is JsonArray parentArray)
        {
            var index = ResolveArrayPatchIndex(parentArray, leaf, operation.Op == "add");
            if (operation.Op == "remove")
            {
                parentArray.RemoveAt(index);
                return;
            }

            var value = JsonSerializer.SerializeToNode(operation.Value, JsonOptions);
            if (operation.Op == "add" && leaf == "-")
            {
                parentArray.Add(value);
            }
            else if (operation.Op == "add")
            {
                parentArray.Insert(index, value);
            }
            else
            {
                parentArray[index] = value;
            }

            return;
        }

        throw new InvalidOperationException("Resume patch path does not point to an editable field.");
    }

    private static JsonNode ResolvePatchChild(JsonNode parent, string segment)
    {
        if (parent is JsonObject parentObject &&
            parentObject.TryGetPropertyValue(segment, out var child) &&
            child is not null)
        {
            return child;
        }

        if (parent is JsonArray parentArray)
        {
            var index = ResolveArrayPatchIndex(parentArray, segment, allowAppend: false);
            return parentArray[index] ?? throw new InvalidOperationException("Resume patch path points to a null array item.");
        }

        throw new InvalidOperationException("Resume patch path does not exist.");
    }

    private static int ResolveArrayPatchIndex(JsonArray array, string segment, bool allowAppend)
    {
        if (allowAppend && segment == "-")
        {
            return array.Count;
        }

        if (!int.TryParse(segment, out var index) || index < 0 || index > array.Count || (!allowAppend && index == array.Count))
        {
            throw new InvalidOperationException("Resume patch array index is invalid.");
        }

        return index;
    }

    private static ResumeStructuredDataDto NormalizeStructuredData(ResumeStructuredDataDto? structuredData)
    {
        var contact = structuredData?.Contact ?? new ResumeContactDto(string.Empty, string.Empty, string.Empty, string.Empty, []);
        var skills = structuredData?.Skills ?? new ResumeSkillGroupsDto([], [], []);

        return new ResumeStructuredDataDto(
            new ResumeContactDto(
                CleanStructuredText(contact.Name, 120),
                CleanStructuredText(contact.Email, 160),
                CleanStructuredText(contact.Phone, 80),
                CleanStructuredText(contact.Location, 120),
                NonNullItems(contact.Links)
                    .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                    .Take(20)
                    .Select(link => new ResumeLinkDto(
                        CleanStructuredText(link.Label, 80),
                        CleanStructuredText(link.Url, 300)))
                    .ToList()),
            NonNullItems(structuredData?.Experience)
                .Take(20)
                .Select(experience => new ResumeExperienceDto(
                    CleanStructuredText(experience.Company, 140),
                    CleanStructuredText(experience.Title, 140),
                    CleanStructuredText(experience.Location, 100),
                    CleanStructuredText(experience.DateRange, 100),
                    CleanCorrectionList(experience.Bullets).Select(bullet => CleanStructuredText(bullet, 500)).Where(IsUsefulStructuredText).Take(10).ToList()))
                .ToList(),
            NonNullItems(structuredData?.Education)
                .Take(12)
                .Select(education => new ResumeEducationDto(
                    CleanStructuredText(education.School, 180),
                    CleanStructuredText(education.Degree, 100),
                    CleanStructuredText(education.Field, 160),
                    CleanStructuredText(education.DateRange, 100),
                    CleanStructuredText(education.Gpa, 60),
                    CleanCorrectionList(education.Notes).Select(note => CleanStructuredText(note, 300)).Where(IsUsefulStructuredText).Take(8).ToList()))
                .ToList(),
            new ResumeSkillGroupsDto(
                CleanCorrectionList(skills.Technical).Take(80).ToList(),
                CleanCorrectionList(skills.Languages).Take(20).ToList(),
                CleanCorrectionList(skills.Other).Take(40).ToList()),
            NonNullItems(structuredData?.Projects)
                .Take(20)
                .Select(project => new ResumeProjectDto(
                    CleanStructuredText(project.Name, 180),
                    CleanStructuredText(project.Description, 800),
                    CleanCorrectionList(project.Tech).Take(30).ToList(),
                    CleanCorrectionList(project.Links).Take(20).ToList(),
                    CleanCorrectionList(project.Bullets).Select(bullet => CleanStructuredText(bullet, 500)).Where(IsUsefulStructuredText).Take(10).ToList()))
                .ToList());
    }

    private static IEnumerable<T> NonNullItems<T>(IEnumerable<T?>? items)
        where T : class
    {
        return (items ?? []).Where(item => item is not null).Select(item => item!);
    }

    private static IReadOnlyList<string> FlattenSkills(ResumeSkillGroupsDto? skills)
    {
        return CleanCorrectionList((skills?.Technical ?? [])
            .Concat(skills?.Languages ?? [])
            .Concat(skills?.Other ?? []));
    }

    private static string SerializeStructuredData(ResumeStructuredDataDto structuredData)
    {
        return JsonSerializer.Serialize(NormalizeStructuredData(structuredData), JsonOptions);
    }

    private static ResumeStructuredDataDto DeserializeStructuredData(string json)
    {
        try
        {
            return NormalizeStructuredData(
                JsonSerializer.Deserialize<ResumeStructuredDataDto>(json, JsonOptions)
                ?? EmptyStructuredData());
        }
        catch (JsonException)
        {
            return EmptyStructuredData();
        }
    }

    private static ResumeStructuredDataDto EmptyStructuredData()
    {
        return new ResumeStructuredDataDto(
            new ResumeContactDto(string.Empty, string.Empty, string.Empty, string.Empty, []),
            [],
            [],
            new ResumeSkillGroupsDto([], [], []),
            []);
    }

    private static ResumeExtractionMetadataDto BuildExtractionMetadata(
        string sourceKind,
        string? sourceFileName,
        string sourceText,
        ResumeStructuredDataDto structuredData,
        string extractionMethod,
        DateTimeOffset extractedAtUtc,
        string? promptVersion,
        ResumeOcrImportMetadata? ocrMetadata = null)
    {
        var contactFieldCount = 0;
        if (!string.IsNullOrWhiteSpace(structuredData.Contact.Name)) contactFieldCount++;
        if (!string.IsNullOrWhiteSpace(structuredData.Contact.Email)) contactFieldCount++;
        if (!string.IsNullOrWhiteSpace(structuredData.Contact.Phone)) contactFieldCount++;
        if (!string.IsNullOrWhiteSpace(structuredData.Contact.Location)) contactFieldCount++;
        contactFieldCount += structuredData.Contact.Links.Count;

        return new ResumeExtractionMetadataDto(
            sourceKind,
            sourceFileName,
            sourceText.Length,
            extractionMethod,
            extractedAtUtc,
            contactFieldCount,
            structuredData.Experience.Count,
            structuredData.Education.Count,
            structuredData.Projects.Count,
            FlattenSkills(structuredData.Skills).Count,
            promptVersion,
            ocrMetadata?.Status ?? "not_required",
            ocrMetadata?.Confidence,
            ocrMetadata?.Error);
    }

    private static ResumeExtractionMetadataDto DeserializeExtractionMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ResumeExtractionMetadataDto>(json, JsonOptions)
                   ?? new ResumeExtractionMetadataDto("unknown", null, 0, "unknown", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, null);
        }
        catch (JsonException)
        {
            return new ResumeExtractionMetadataDto("unknown", null, 0, "unknown", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, null);
        }
    }

    private static IReadOnlyList<ResumeSectionReviewDto> DeserializeSectionReviews(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ResumeSectionReviewDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeOptionalSourceFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fileName = Path.GetFileName(value.Trim());
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : fileName.Length <= 240 ? fileName : fileName[^240..];
    }
}
