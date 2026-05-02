using System.Net;
using System.Text;

namespace BpcAdoUpdater.Apply;

public static class AdoFieldValueFormatter
{
    public static bool IsRichTextField(string csvFieldName)
    {
        return csvFieldName.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || csvFieldName.Equals("Microsoft references", StringComparison.OrdinalIgnoreCase)
            || csvFieldName.Equals("Partner references", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FormatForAdo(string csvFieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!IsRichTextField(csvFieldName))
        {
            return value;
        }

        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        string[] lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length + 32);

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("<br/>");
            }

            builder.Append(WebUtility.HtmlEncode(lines[i]));
        }

        return builder.ToString();
    }

    public static bool NeedsHtmlMigration(string csvFieldName, string? existingValue, string? newValue)
    {
        if (!IsRichTextField(csvFieldName) || string.IsNullOrWhiteSpace(existingValue) || string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        // Only trigger one-time migration when the source text actually needs rich-text structure.
        bool sourceHasLineBreaks = newValue.Contains('\n') || newValue.Contains('\r');
        if (!sourceHasLineBreaks)
        {
            return false;
        }

        if (ContainsHtmlMarkup(existingValue))
        {
            return false;
        }

        return !string.Equals(existingValue.Trim(), FormatForAdo(csvFieldName, newValue), StringComparison.Ordinal);
    }

    private static bool ContainsHtmlMarkup(string value)
    {
        return value.Contains("<br", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<div", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<p", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<span", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<ul", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<ol", StringComparison.OrdinalIgnoreCase)
            || value.Contains("<li", StringComparison.OrdinalIgnoreCase);
    }
}