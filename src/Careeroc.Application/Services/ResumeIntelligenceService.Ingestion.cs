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
    private async Task<ResumeSourceImportResult> ExtractPdfTextOrVisionAsync(
        byte[] bytes,
        string fileName,
        string userEmail,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            return new ResumeSourceImportResult(
                ExtractPdfText(bytes),
                new ResumeOcrImportMetadata("not_required"));
        }
        catch (InvalidOperationException exception)
            when (string.Equals(exception.Message, "Paste at least 80 characters of resume text.", StringComparison.Ordinal))
        {
            var pageImage = TryExtractLargestPdfPageImage(bytes);
            if (pageImage is null)
            {
                throw new InvalidOperationException(
                    "PDF did not contain enough selectable text and no readable page image was found. Try an image export or paste the resume text.",
                    exception);
            }

            return await ExtractVisionTextAsync(
                pageImage.Bytes,
                pageImage.ContentType,
                fileName,
                "scanned_pdf",
                userEmail,
                runId,
                cancellationToken);
        }
    }

    private async Task<ResumeSourceImportResult> ExtractVisionTextAsync(
        byte[] imageBytes,
        string contentType,
        string fileName,
        string sourceKind,
        string userEmail,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var decision = await _resumeVisionTextExtractor.ExtractAsync(
            new ResumeVisionTextExtractionRequest(
                fileName,
                contentType,
                Convert.ToBase64String(imageBytes),
                sourceKind),
            cancellationToken,
            new ModelCallContext(userEmail, null, null, runId.ToString("N")));

        if (decision is null || decision.Confidence < 0.55m)
        {
            throw new InvalidOperationException("Resume image OCR/vision extraction is not configured or returned too little confidence.");
        }

        try
        {
            return new ResumeSourceImportResult(
                NormalizeSourceText(decision.ExtractedText),
                new ResumeOcrImportMetadata("completed", decimal.Clamp(decision.Confidence, 0m, 1m)));
        }
        catch (InvalidOperationException exception)
            when (string.Equals(exception.Message, "Paste at least 80 characters of resume text.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resume image OCR/vision extraction returned too little text.", exception);
        }
    }

    private static string NormalizeSourceText(string value)
    {
        var normalized = CleanSourceText(value);
        if (normalized.Length < 80)
        {
            throw new InvalidOperationException("Paste at least 80 characters of resume text.");
        }

        if (normalized.Length > 100_000)
        {
            throw new InvalidOperationException("Resume text must be 100,000 characters or fewer.");
        }

        return normalized;
    }

    private static string CleanSourceText(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("�", "", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", " ");
        normalized = Regex.Replace(normalized, @"(?<=\p{L})-\s+(?=\p{Ll})", "");
        normalized = Regex.Replace(normalized, @"(?:^|\s)(?:§|ï|#)\s*(?=(?:Portfolio|GitHub|LinkedIn)\b)", " ");
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n\s+", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string NormalizeTitle(string? value, string sourceText)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed.Length <= 240 ? trimmed : trimmed[..240];
        }

        var firstLine = sourceText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine)
            ? "Resume"
            : firstLine.Length <= 80
                ? firstLine
                : "Resume";
    }

    private static string NormalizeSourceKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "text" or "pdf" or "image"
            ? normalized
            : "text";
    }

    private static string NormalizeFileName(string value)
    {
        var fileName = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Resume file name is required.");
        }

        return fileName.Length <= 240 ? fileName : fileName[^240..];
    }

    private static byte[] DecodeBase64Content(string value)
    {
        var normalized = value.Trim();
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
            throw new InvalidOperationException("Resume file content must be base64 encoded.", exception);
        }

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Resume file is empty.");
        }

        if (bytes.Length > 5 * 1024 * 1024)
        {
            throw new InvalidOperationException("Resume file must be 5 MB or smaller.");
        }

        return bytes;
    }

    private static string ResolveSourceKind(string fileName, string contentType)
    {
        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (normalizedContentType == "application/pdf" || extension == ".pdf")
        {
            return "pdf";
        }

        if (normalizedContentType.StartsWith("text/", StringComparison.Ordinal) ||
            extension is ".txt" or ".md")
        {
            return "text";
        }

        if (normalizedContentType.StartsWith("image/", StringComparison.Ordinal) ||
            extension is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            return "image";
        }

        throw new InvalidOperationException("Resume file must be a PDF, image, or plain text file.");
    }

    private static string DecodeText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return NormalizeSourceText(text);
    }

    private static string ExtractPdfText(byte[] bytes)
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

            foreach (var link in ExtractPdfEmbeddedLinks(bytes))
            {
                builder.AppendLine(link);
            }

            return NormalizeSourceText(builder.ToString());
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("PDF text extraction failed. Try a selectable-text PDF or paste the resume text.", exception);
        }
    }

    private static IReadOnlyList<string> ExtractPdfEmbeddedLinks(byte[] bytes)
    {
        var raw = Encoding.Latin1.GetString(bytes);
        return LinkRegex()
            .Matches(raw)
            .Select(match => Uri.UnescapeDataString(match.Value.TrimEnd('.', ',', ')', ']', '>', '\\')))
            .Where(link => Uri.TryCreate(link, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
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

    private static string NormalizeImageContentType(string contentType, string fileName)
    {
        var normalized = contentType.Trim().ToLowerInvariant();
        if (normalized is "image/png" or "image/jpeg" or "image/jpg" or "image/webp")
        {
            return normalized == "image/jpg" ? "image/jpeg" : normalized;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Resume image must be PNG, JPEG, or WebP.")
        };
    }
}
