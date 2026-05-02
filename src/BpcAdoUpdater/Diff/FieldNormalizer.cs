namespace BpcAdoUpdater.Diff;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public static class FieldNormalizer
{
    private static readonly Regex BreakTagRegex = new("<\\s*br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockTagRegex = new("</?(div|p|li|ul|ol|h[1-6])[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static string? Normalize(string? value, string csvFieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (csvFieldName.Equals("Products", StringComparison.OrdinalIgnoreCase) ||
            csvFieldName.Equals("Application family", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = normalized
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            normalized = string.Join("; ", parts);
        }

        if (csvFieldName.Equals("Description", StringComparison.OrdinalIgnoreCase))
        {
            normalized = NormalizeDescription(normalized);
            normalized = CollapseWhitespace(normalized);
        }

        if (csvFieldName.Equals("Microsoft references", StringComparison.OrdinalIgnoreCase) ||
            csvFieldName.Equals("Partner references", StringComparison.OrdinalIgnoreCase))
        {
            normalized = NormalizeReferenceText(normalized);
            normalized = CollapseWhitespace(normalized);
        }

        return normalized;
    }

    private static string NormalizeDescription(string value)
    {
        string decoded = DecodeHtmlEntities(value);
        decoded = BreakTagRegex.Replace(decoded, "\n");
        decoded = BlockTagRegex.Replace(decoded, "\n");
        decoded = HtmlTagRegex.Replace(decoded, " ");
        return decoded;
    }

    private static string NormalizeReferenceText(string value)
    {
        string decoded = DecodeHtmlEntities(value);
        decoded = BreakTagRegex.Replace(decoded, "\n");
        decoded = BlockTagRegex.Replace(decoded, "\n");
        decoded = HtmlTagRegex.Replace(decoded, " ");
        return decoded;
    }

    private static string DecodeHtmlEntities(string value)
    {
        string decoded = value;

        // Some stored descriptions are encoded more than once (for example: &amp;nbsp;).
        for (int i = 0; i < 3; i++)
        {
            string next = WebUtility.HtmlDecode(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
            {
                break;
            }

            decoded = next;
        }

        return decoded;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasWhitespace = false;

        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i] == '\u00A0' ? ' ' : value[i];
            bool isWhitespace = char.IsWhiteSpace(ch);
            if (isWhitespace)
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
