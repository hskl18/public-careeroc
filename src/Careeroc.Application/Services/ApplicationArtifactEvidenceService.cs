using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Review;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Careeroc.Application.Services;

public sealed partial class ApplicationArtifactEvidenceService
{
    private const int MaxArtifactBytes = 5 * 1024 * 1024;
    private const string ModelPath = "gemma4:31b";
    private const string Purpose = "artifact_evidence_extraction";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IResumeVisionTextExtractor _visionTextExtractor;

    public ApplicationArtifactEvidenceService(ICareerocDbContext dbContext, IClock clock)
        : this(dbContext, clock, NullResumeVisionTextExtractor.Instance)
    {
    }

    public ApplicationArtifactEvidenceService(
        ICareerocDbContext dbContext,
        IClock clock,
        IResumeVisionTextExtractor visionTextExtractor)
    {
        _dbContext = dbContext;
        _clock = clock;
        _visionTextExtractor = visionTextExtractor;
    }

    public async Task<ApplicationArtifactEvidenceDto?> CreateAsync(
        Guid jobApplicationId,
        ApplicationArtifactInputDto request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var application = await _dbContext.JobApplications
            .FirstOrDefaultAsync(
                candidate => candidate.Id == jobApplicationId && candidate.UserId == user.Id,
                cancellationToken);
        if (application is null)
        {
            return null;
        }

        var artifactKind = NormalizeArtifactKind(request.ArtifactKind);
        var fileName = NormalizeFileName(request.FileName);
        var contentType = NormalizeContentType(request.ContentType);
        var bytes = DecodeBase64Content(request.Base64Content);
        ValidateContent(bytes, contentType, fileName);

        var extraction = await ExtractArtifactTextAsync(
            bytes,
            contentType,
            fileName,
            artifactKind,
            resolvedUserEmail,
            cancellationToken);
        var sourceSnippet = BuildSourceSnippet(request.SourceLabel, extraction.Text, artifactKind, fileName);
        var normalizedDateUtc = TryExtractDate(sourceSnippet);
        var confidence = ScoreEvidence(sourceSnippet, normalizedDateUtc, extraction.Confidence);
        var requiresReview = confidence < 0.75m;
        var reviewReason = requiresReview
            ? "Artifact evidence is not confident enough to mutate application state or create reminders."
            : null;
        var reviewGateResult = requiresReview
            ? "manual_review_required"
            : "evidence_recorded_no_mutation";
        var fallbackPath = requiresReview
            ? "manual_review_no_mutation"
            : "stored_evidence_only_no_mutation";
        var evidence = new ApplicationArtifactEvidence
        {
            UserId = user.Id,
            JobApplicationId = application.Id,
            ArtifactKind = artifactKind,
            FileName = fileName,
            ContentType = contentType,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SourceLabel = SanitizeOptionalEvidenceText(request.SourceLabel, 500),
            SafeTitle = BuildSafeTitle(artifactKind, fileName),
            ExtractedFactType = ResolveFactType(artifactKind),
            ExtractedFactText = sourceSnippet,
            NormalizedDateUtc = normalizedDateUtc,
            Confidence = confidence,
            SourceSnippet = sourceSnippet,
            ModelPath = extraction.ModelPath ?? ModelPath,
            Purpose = Purpose,
            EvidenceSource = extraction.EvidenceSource ?? $"{artifactKind}:{fileName}",
            ReviewGateResult = reviewGateResult,
            FallbackPath = fallbackPath,
            RequiresReview = requiresReview,
            ReviewReason = reviewReason,
            CreatedAtUtc = _clock.UtcNow
        };

        _dbContext.ApplicationArtifactEvidence.Add(evidence);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(evidence);
    }

    public async Task<ApplicationArtifactEvidenceDto?> ReviewAsync(
        Guid jobApplicationId,
        Guid artifactEvidenceId,
        ReviewApplicationArtifactRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var evidence = await _dbContext.ApplicationArtifactEvidence
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == artifactEvidenceId &&
                    candidate.JobApplicationId == jobApplicationId &&
                    candidate.UserId == user.Id,
                cancellationToken);
        if (evidence is null)
        {
            return null;
        }

        var outcome = NormalizeArtifactReviewOutcome(request.Outcome);
        var notes = NormalizeOptional(request.Notes, 280);
        evidence.RequiresReview = false;
        evidence.ReviewGateResult = outcome == "dismiss_evidence"
            ? "manual_review_dismissed_no_mutation"
            : "manual_review_kept_no_mutation";
        evidence.FallbackPath = outcome == "dismiss_evidence"
            ? "dismissed_evidence_no_mutation"
            : "stored_evidence_only_no_mutation";
        evidence.ReviewReason = notes ?? (outcome == "dismiss_evidence"
            ? "Dismissed during manual artifact review."
            : "Kept as reviewed artifact evidence. No application or reminder mutation was applied.");

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(evidence);
    }

    private static ApplicationArtifactEvidenceDto ToDto(ApplicationArtifactEvidence evidence)
    {
        return new ApplicationArtifactEvidenceDto(
            evidence.Id,
            evidence.JobApplicationId,
            evidence.ArtifactKind,
            evidence.FileName,
            evidence.ContentType,
            evidence.SafeTitle,
            evidence.ExtractedFactType,
            evidence.ExtractedFactText,
            evidence.NormalizedDateUtc,
            evidence.Confidence,
            evidence.SourceSnippet,
            evidence.RequiresReview,
            evidence.ReviewReason,
            evidence.CreatedAtUtc,
            AgentTraceSummaryBuilder.FromArtifactEvidence(evidence),
            BuildArtifactAgentTrace(evidence));
    }

    private static ReviewAgentTraceDto BuildArtifactAgentTrace(ApplicationArtifactEvidence evidence)
    {
        var summary = AgentTraceSummaryBuilder.FromArtifactEvidence(evidence);

        return new ReviewAgentTraceDto(
            evidence.CreatedAtUtc,
            [
                new ReviewAgentTraceStepDto(
                    "artifact_ingestion",
                    "Artifact evidence importer",
                    $"Accepted {evidence.ContentType} evidence as {ToLabel(evidence.ArtifactKind)}.",
                    "The artifact path stores evidence metadata and does not keep raw uploaded or downloaded bytes.",
                    [
                        new ReviewAgentTraceFactDto("file", evidence.FileName),
                        new ReviewAgentTraceFactDto("source", summary.EvidenceSource)
                    ]),
                new ReviewAgentTraceStepDto(
                    "gemma_extraction",
                    "Artifact extractor",
                    evidence.ExtractedFactText,
                    evidence.SourceSnippet,
                    [
                        new ReviewAgentTraceFactDto("model", summary.ModelPath ?? "not recorded"),
                        new ReviewAgentTraceFactDto("purpose", summary.Purpose),
                        new ReviewAgentTraceFactDto("confidence", $"{Math.Round(evidence.Confidence * 100)}%")
                    ]),
                new ReviewAgentTraceStepDto(
                    "review_gate",
                    "Artifact review gate",
                    evidence.RequiresReview
                        ? "Blocked this artifact from mutating application state."
                        : "Recorded evidence without mutating application state.",
                    evidence.ReviewReason ??
                    "Artifact evidence remains an auditable card; application status and reminders require a separate review path.",
                    [
                        new ReviewAgentTraceFactDto("outcome", summary.ReviewGateResult),
                        new ReviewAgentTraceFactDto("fallback", summary.FallbackPath)
                    ])
            ],
            summary);
    }

    private static string NormalizeArtifactKind(string value)
    {
        var normalized = NormalizeRequired(value, "artifact kind", 80)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_');
        return normalized is "interview_screenshot" or "offer_pdf" or "assessment_pdf"
            ? normalized
            : "recruiting_artifact";
    }

    private static string NormalizeArtifactReviewOutcome(string value)
    {
        var normalized = NormalizeRequired(value, "review outcome", 80)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_');
        return normalized is "keep_evidence" or "dismiss_evidence"
            ? normalized
            : throw new InvalidOperationException("Artifact review outcome must be keep_evidence or dismiss_evidence.");
    }

    private static string ToLabel(string value)
    {
        return string.Join(
            " ",
            value
                .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static string NormalizeFileName(string value)
    {
        var fileName = Path.GetFileName(NormalizeRequired(value, "file name", 240));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Artifact file name is required.");
        }

        return fileName.Length <= 240 ? fileName : fileName[^240..];
    }

    private static string NormalizeContentType(string value)
    {
        var normalized = NormalizeRequired(value, "content type", 120).ToLowerInvariant();
        if (!AllowedContentTypes.Contains(normalized))
        {
            throw new InvalidOperationException("Artifact must be a PDF, PNG, JPEG, or WebP file.");
        }

        return normalized;
    }

    private static byte[] DecodeBase64Content(string value)
    {
        var normalized = NormalizeRequired(value, "file content", int.MaxValue).Trim();
        var commaIndex = normalized.IndexOf(',');
        if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
        {
            normalized = normalized[(commaIndex + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(normalized);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Artifact file content must be base64 encoded.", exception);
        }

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Artifact file is empty.");
        }

        if (bytes.Length > MaxArtifactBytes)
        {
            throw new InvalidOperationException("Artifact file must be 5 MB or smaller.");
        }

        return bytes;
    }

    private static void ValidateContent(byte[] bytes, string contentType, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var signatureMatches = contentType switch
        {
            "application/pdf" => extension == ".pdf" && bytes.Length >= 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F',
            "image/png" => extension == ".png" && bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "image/jpeg" => extension is ".jpg" or ".jpeg" && bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/webp" => extension == ".webp" && bytes.Length >= 12 &&
                            Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
                            Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
            _ => false
        };

        if (!signatureMatches)
        {
            throw new InvalidOperationException("Artifact content does not match its declared file type.");
        }
    }

    private async Task<ArtifactExtractionResult> ExtractArtifactTextAsync(
        byte[] bytes,
        string contentType,
        string fileName,
        string artifactKind,
        string userEmail,
        CancellationToken cancellationToken)
    {
        if (contentType == "application/pdf")
        {
            var pdfText = TryExtractPdfText(bytes);
            if (!string.IsNullOrWhiteSpace(pdfText))
            {
                return new ArtifactExtractionResult(
                    pdfText,
                    0.80m,
                    null,
                    $"pdf_text:{artifactKind}:{fileName}");
            }

            var pageImage = TryExtractLargestPdfPageImage(bytes);
            if (pageImage is not null)
            {
                var visionResult = await TryExtractVisionTextAsync(
                    pageImage.Bytes,
                    pageImage.ContentType,
                    fileName,
                    $"scanned_pdf_{artifactKind}",
                    userEmail,
                    cancellationToken);
                if (visionResult is not null)
                {
                    return visionResult;
                }
            }

            return ArtifactExtractionResult.Empty;
        }

        return await TryExtractVisionTextAsync(
            bytes,
            contentType,
            fileName,
            artifactKind,
            userEmail,
            cancellationToken) ?? ArtifactExtractionResult.Empty;
    }

    private async Task<ArtifactExtractionResult?> TryExtractVisionTextAsync(
        byte[] bytes,
        string contentType,
        string fileName,
        string artifactKind,
        string userEmail,
        CancellationToken cancellationToken)
    {
        var decision = await _visionTextExtractor.ExtractAsync(
            new ResumeVisionTextExtractionRequest(
                fileName,
                contentType,
                Convert.ToBase64String(bytes),
                $"application_artifact:{artifactKind}"),
            cancellationToken,
            new ModelCallContext(userEmail, null, null, Guid.NewGuid().ToString("N")));

        if (decision is null || string.IsNullOrWhiteSpace(decision.ExtractedText))
        {
            return null;
        }

        return new ArtifactExtractionResult(
            CleanExtractedText(decision.ExtractedText),
            decimal.Clamp(decision.Confidence, 0m, 1m),
            ExtractModelPath(decision.AgentName),
            $"vision:{artifactKind}:{fileName}");
    }

    private static string? TryExtractPdfText(byte[] bytes)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var text = string.Join(
                    ' ',
                    page.GetWords()
                        .Select(word => word.Text.Trim())
                        .Where(word => word.Length > 0))
                    .Trim();
                if (text.Length > 0)
                {
                    builder.AppendLine(text);
                }
            }

            return CleanExtractedText(builder.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static PdfPageImage? TryExtractLargestPdfPageImage(byte[] bytes)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            return document
                .GetPages()
                .SelectMany(page => page.GetImages())
                .Select(
                    image =>
                    {
                        if (!image.TryGetPng(out var pngBytes) || pngBytes.Length == 0)
                        {
                            return null;
                        }

                        var pixels = Math.Max(0, image.WidthInSamples) * Math.Max(0, image.HeightInSamples);
                        return new PdfPageImage(pngBytes, "image/png", pixels);
                    })
                .Where(candidate => candidate is not null)
                .OrderByDescending(candidate => candidate!.PixelCount)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSourceSnippet(
        string? sourceLabel,
        string? extractedText,
        string artifactKind,
        string fileName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceLabel))
        {
            parts.Add(sourceLabel.Trim());
        }

        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            parts.Add(extractedText.Trim());
        }

        var source = parts.Count == 0
            ? $"Uploaded {artifactKind.Replace('_', ' ')} artifact {fileName} for evidence review."
            : string.Join(" ", parts);
        source = SanitizeEvidenceText(source);
        return source.Length <= 500 ? source : source[..500];
    }

    private static string CleanExtractedText(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("�", "", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", " ");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        return normalized.Length <= 1200 ? normalized : normalized[..1200];
    }

    private static decimal ScoreEvidence(
        string sourceSnippet,
        DateTimeOffset? normalizedDateUtc,
        decimal extractionConfidence)
    {
        var score = 0.45m;
        if (extractionConfidence > 0m)
        {
            score = Math.Max(score, extractionConfidence * 0.75m);
        }

        if (sourceSnippet.Length >= 40)
        {
            score += 0.15m;
        }

        if (normalizedDateUtc.HasValue)
        {
            score += 0.25m;
        }

        return decimal.Clamp(score, 0.10m, 0.85m);
    }

    private static DateTimeOffset? TryExtractDate(string value)
    {
        var match = IsoDateRegex().Match(value);
        if (match.Success &&
            DateOnly.TryParse(match.Value, out var date))
        {
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        return null;
    }

    private static string BuildSafeTitle(string artifactKind, string fileName)
    {
        return $"{artifactKind.Replace('_', ' ')} evidence: {fileName}";
    }

    private static string? ExtractModelPath(string agentName)
    {
        var match = ModelPathRegex().Match(agentName);
        return match.Success ? match.Groups["model"].Value.Trim() : null;
    }

    private static string ResolveFactType(string artifactKind)
    {
        return artifactKind switch
        {
            "interview_screenshot" => "schedule",
            "offer_pdf" => "offer_detail",
            "assessment_pdf" => "assessment_deadline",
            _ => "artifact_fact"
        };
    }

    private static string NormalizeRequired(string value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Artifact {fieldName} is required.");
        }

        var normalized = value.Trim();
        return maxLength == int.MaxValue || normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? SanitizeOptionalEvidenceText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = SanitizeEvidenceText(value);
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static string SanitizeEvidenceText(string value)
    {
        var sanitized = LongTokenRegex().Replace(value, "[redacted]");
        sanitized = UrlRegex().Replace(sanitized, "[link]");
        return WhitespaceRegex().Replace(sanitized, " ").Trim();
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? "local@careeroc.dev"
            : userEmail.Trim().ToLowerInvariant();
    }

    [GeneratedRegex("\\b\\d{4}-\\d{2}-\\d{2}\\b", RegexOptions.Compiled)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex("\\bhttps?://\\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("\\b[A-Za-z0-9_=.-]{48,}\\b", RegexOptions.Compiled)]
    private static partial Regex LongTokenRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\((?<model>[^)]+)\\)\\s*$", RegexOptions.Compiled)]
    private static partial Regex ModelPathRegex();

    private sealed record ArtifactExtractionResult(
        string? Text,
        decimal Confidence,
        string? ModelPath,
        string? EvidenceSource)
    {
        public static ArtifactExtractionResult Empty { get; } = new(null, 0m, null, null);
    }

    private sealed record PdfPageImage(byte[] Bytes, string ContentType, int PixelCount);
}
