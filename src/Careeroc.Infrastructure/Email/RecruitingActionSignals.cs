using System.Text.RegularExpressions;

namespace Careeroc.Infrastructure.Email;

internal static class RecruitingActionSignals
{
    private static readonly string[] QuestionnaireOrFormPhrases =
    [
        "candidate questionnaire",
        "complete the questionnaire",
        "complete the candidate questionnaire",
        "fill out the questionnaire",
        "fill in the questionnaire",
        "complete this form",
        "complete the form",
        "form is submitted"
    ];

    public static bool IsQuestionnaireOrFormRequest(params string?[] textParts)
    {
        var normalized = Normalize(textParts);
        return QuestionnaireOrFormPhrases.Any(normalized.Contains);
    }

    private static string Normalize(IEnumerable<string?> textParts)
    {
        var content = string.Join(" ", textParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return Regex.Replace(content.ToLowerInvariant(), @"\s+", " ").Trim();
    }
}
