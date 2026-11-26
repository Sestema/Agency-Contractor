using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Builds the request for the AI that writes a vacancy text, and reads its answer.
    /// The AI gets only public data: no firm name, no private notes.
    /// </summary>
    public static class VacancyAiWriter
    {
        public const string SystemPrompt = @"You are a recruiter at a Czech employment agency who writes job adverts for Facebook groups and Viber/Telegram channels.
The readers are people from Ukraine and other countries looking for work in the Czech Republic, often on a phone.

Write in the same language as the recruiter's draft. If the draft is empty or unclear, write in Ukrainian.

Rules:
- Use ONLY facts from the data and the draft. Never invent pay, housing, bonuses, dates, requirements or anything else.
- Never mention the employer's company name.
- Short and to the point. No long introductions, no clichés like ""a great opportunity"" or ""dynamic team"".
- Fix the spelling and grammar of the draft.

Return ONLY a JSON object, no markdown fences, with two fields:
{
  ""description"": ""2-3 plain sentences about the work itself for a printed poster: what people will do and who fits. No emoji, no pay, no phone, no list."",
  ""ad"": ""A Facebook post, 6-10 short lines: line 1 is a catchy headline with a fitting emoji (who is needed and where); then lines starting with ✔ for pay, housing, schedule, start date and who is needed; then 1-2 sentences about the work; the last line is a call to action with the phone if it is given (📞). Max 5 emoji in total. Plain text, line breaks as \n.""
}";

        /// <summary>The user message: the data of the position and the recruiter's own words.</summary>
        public static string BuildRequest(Vacancy vacancy, string draft, string phone, string phoneNote)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Position data:");
            Add(sb, "Work / position", vacancy.Position);
            Add(sb, "City", vacancy.City);
            var people = new[]
            {
                vacancy.Men > 0 ? $"{vacancy.Men} men" : null,
                vacancy.Women > 0 ? $"{vacancy.Women} women" : null,
                vacancy.Couples > 0 ? $"{vacancy.Couples} married couples" : null,
                vacancy.Anyone > 0 ? $"{vacancy.Anyone} people (men or women)" : null
            }.Where(part => part != null);
            Add(sb, "People needed", string.Join(", ", people));
            Add(sb, "Pay", vacancy.Pay);
            Add(sb, "Start date", FormatDate(vacancy.NeededFrom));
            Add(sb, "Conditions", vacancy.Conditions);
            Add(sb, "Contact phone", string.IsNullOrWhiteSpace(phoneNote) ? phone : $"{phone} ({phoneNote})");
            sb.AppendLine();
            sb.AppendLine("Recruiter's draft (rough notes, may have typos):");
            sb.AppendLine(string.IsNullOrWhiteSpace(draft) ? "(empty)" : draft.Trim());
            return sb.ToString();
        }

        /// <summary>
        /// Reads {"description","ad"}. Code fences are ignored. When the answer is not JSON,
        /// the whole text is taken as the advert and the description stays empty.
        /// </summary>
        public static (string Description, string Ad) ParseAnswer(string? answer)
        {
            var text = (answer ?? string.Empty).Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLine = text.IndexOf('\n');
                text = firstLine >= 0 ? text[(firstLine + 1)..] : string.Empty;
                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                    text = text[..fence];
                text = text.Trim();
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                    var root = doc.RootElement;
                    var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? string.Empty : string.Empty;
                    var ad = root.TryGetProperty("ad", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? string.Empty : string.Empty;
                    if (description.Length > 0 || ad.Length > 0)
                        return (description.Trim(), ad.Trim());
                }
                catch (JsonException)
                {
                }
            }

            return (string.Empty, text);
        }

        /// <summary>Text key that tells the user why the AI did not answer.</summary>
        public static string ErrorKey(string? answer)
        {
            var text = (answer ?? string.Empty).ToLowerInvariant();
            if (text.Contains("credits are depleted") || text.Contains("billing") || text.Contains("prepay"))
                return "VacAiErrorCredits";
            if (text.Contains("quota") || text.Contains("rate limit") || text.Contains("resource_exhausted") || text.Contains("429"))
                return "VacAiErrorQuota";
            if (text.Contains("api key") || text.Contains("api_key") || text.Contains("permission") || text.Contains("unauthenticated"))
                return "VacAiErrorKey";
            if (text.Contains("timed out") || text.Contains("network"))
                return "VacAiErrorNetwork";
            return "VacAiError";
        }

        private static void Add(StringBuilder sb, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"- {label}: {value.Trim()}");
        }

        private static string FormatDate(string value)
            => DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date)
                ? date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
    }
}
