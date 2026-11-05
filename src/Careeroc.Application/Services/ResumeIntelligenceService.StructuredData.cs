using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
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
                    CleanList(experience.Bullets).Select(bullet => CleanStructuredText(bullet, 500)).Where(IsUsefulStructuredText).Take(10).ToList()))
                .ToList(),
            NonNullItems(structuredData?.Education)
                .Take(12)
                .Select(education => new ResumeEducationDto(
                    CleanStructuredText(education.School, 180),
                    CleanStructuredText(education.Degree, 100),
                    CleanStructuredText(education.Field, 160),
                    CleanStructuredText(education.DateRange, 100),
                    CleanStructuredText(education.Gpa, 60),
                    CleanList(education.Notes).Select(note => CleanStructuredText(note, 300)).Where(IsUsefulStructuredText).Take(8).ToList()))
                .ToList(),
            new ResumeSkillGroupsDto(
                CleanList(skills.Technical).Take(80).ToList(),
                CleanList(skills.Languages).Take(20).ToList(),
                CleanList(skills.Other).Take(40).ToList()),
            NonNullItems(structuredData?.Projects)
                .Take(20)
                .Select(project => new ResumeProjectDto(
                    CleanStructuredText(project.Name, 180),
                    CleanStructuredText(project.Description, 800),
                    CleanList(project.Tech).Take(30).ToList(),
                    CleanList(project.Links).Take(20).ToList(),
                    CleanList(project.Bullets).Select(bullet => CleanStructuredText(bullet, 500)).Where(IsUsefulStructuredText).Take(10).ToList()))
                .ToList());
    }

    private static List<string> CleanList(IEnumerable<string?>? values)
    {
        return (values ?? [])
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
    }

    private static IEnumerable<T> NonNullItems<T>(IEnumerable<T?>? items)
        where T : class
    {
        return (items ?? []).Where(item => item is not null).Select(item => item!);
    }

    private static IReadOnlyList<string> FlattenSkills(ResumeSkillGroupsDto? skills)
    {
        return CleanList((skills?.Technical ?? [])
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
        string? promptVersion)
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
            promptVersion);
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
