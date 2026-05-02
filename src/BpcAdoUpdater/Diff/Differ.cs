using BpcAdoUpdater.Models;
using BpcAdoUpdater.Apply;

namespace BpcAdoUpdater.Diff;

public sealed class Differ
{
    public ChangeSet Compare(
        IReadOnlyList<CatalogRow> rows,
        IReadOnlyDictionary<string, AdoWorkItemRecord> adoByMicrosoftId,
        IReadOnlyDictionary<string, string> fieldMap,
        IReadOnlyDictionary<string, string>? defaultsWhenCsvNull = null)
    {
        var changes = new List<Change>();
        var info = new List<string>();
        var csvIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CatalogRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.MicrosoftId))
            {
                continue;
            }

            csvIds.Add(row.MicrosoftId);

            if (!adoByMicrosoftId.TryGetValue(row.MicrosoftId, out AdoWorkItemRecord? existing))
            {
                changes.Add(new Change
                {
                    Kind = ChangeKind.Add,
                    Row = row,
                    Deltas = BuildAddDeltas(row, fieldMap, defaultsWhenCsvNull),
                    Approved = false,
                    IsCustomerModified = false,
                });

                continue;
            }

            List<FieldDelta> deltas = BuildUpdateDeltas(row, existing, fieldMap, defaultsWhenCsvNull);
            bool deprecatedOnly = IsDeprecatedOrDeleted(row.CatalogStatus);
            if (deprecatedOnly)
            {
                deltas = deltas
                    .Where(x => x.CsvFieldName.Equals("Catalog status", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (deltas.Count == 0)
            {
                changes.Add(new Change
                {
                    Kind = ChangeKind.Unchanged,
                    Row = row,
                    AdoId = existing.Id,
                    AdoRev = existing.Rev,
                    Deltas = deltas,
                    Approved = false,
                    IsCustomerModified = false,
                });
                continue;
            }

            changes.Add(new Change
            {
                Kind = ChangeKind.Update,
                Row = row,
                AdoId = existing.Id,
                AdoRev = existing.Rev,
                Deltas = deltas,
                Approved = false,
                IsCustomerModified = !string.Equals(existing.Fields.GetValueOrDefault("Author"), "Microsoft", StringComparison.OrdinalIgnoreCase),
            });
        }

        foreach ((string microsoftId, AdoWorkItemRecord record) in adoByMicrosoftId)
        {
            if (!csvIds.Contains(microsoftId))
            {
                info.Add($"ADO item {record.Id} with Microsoft ID '{microsoftId}' was not found in CSV; no action taken.");
            }
        }

        return new ChangeSet
        {
            Changes = changes,
            InformationalMessages = info,
        };
    }

    private static List<FieldDelta> BuildAddDeltas(
        CatalogRow row,
        IReadOnlyDictionary<string, string> fieldMap,
        IReadOnlyDictionary<string, string>? defaultsWhenCsvNull)
    {
        var deltas = new List<FieldDelta>();
        IReadOnlyDictionary<string, string?> csvFields = row.ToCsvFieldMap();

        foreach ((string csvField, string adoField) in fieldMap)
        {
            csvFields.TryGetValue(csvField, out string? newValue);
            string? effectiveNewValue = ResolveConfiguredValue(newValue, csvField, defaultsWhenCsvNull);
            deltas.Add(new FieldDelta
            {
                CsvFieldName = csvField,
                AdoFieldName = adoField,
                OldValue = null,
                NewValue = effectiveNewValue,
            });
        }

        return deltas;
    }

    private static List<FieldDelta> BuildUpdateDeltas(
        CatalogRow row,
        AdoWorkItemRecord existing,
        IReadOnlyDictionary<string, string> fieldMap,
        IReadOnlyDictionary<string, string>? defaultsWhenCsvNull)
    {
        var deltas = new List<FieldDelta>();
        IReadOnlyDictionary<string, string?> csvFields = row.ToCsvFieldMap();

        foreach ((string csvField, string adoField) in fieldMap)
        {
            csvFields.TryGetValue(csvField, out string? csvValue);
            existing.Fields.TryGetValue(adoField, out string? rawOld);

            string? rawNew = csvValue;
            if (string.IsNullOrWhiteSpace(csvValue)
                && defaultsWhenCsvNull is not null
                && defaultsWhenCsvNull.TryGetValue(csvField, out string? configuredDefault)
                && !string.IsNullOrWhiteSpace(configuredDefault))
            {
                string? normalizedOldForDefault = FieldNormalizer.Normalize(rawOld, csvField);
                string? normalizedConfiguredDefault = FieldNormalizer.Normalize(configuredDefault, csvField);

                // If ADO already holds the configured default, don't surface a false-positive update.
                if (string.Equals(normalizedOldForDefault, normalizedConfiguredDefault, StringComparison.Ordinal))
                {
                    continue;
                }

                rawNew = configuredDefault;
            }

            string? normalizedNew = FieldNormalizer.Normalize(rawNew, csvField);
            string? normalizedOld = FieldNormalizer.Normalize(rawOld, csvField);

            bool hasMeaningfulDifference = !string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal);
            bool needsHtmlMigration = !hasMeaningfulDifference
                && AdoFieldValueFormatter.NeedsHtmlMigration(csvField, rawOld, rawNew);

            if (hasMeaningfulDifference || needsHtmlMigration)
            {
                deltas.Add(new FieldDelta
                {
                    CsvFieldName = csvField,
                    AdoFieldName = adoField,
                    OldValue = rawOld,
                    NewValue = rawNew,
                });
            }
        }

        return deltas;
    }

    private static string? ResolveConfiguredValue(
        string? rawValue,
        string csvField,
        IReadOnlyDictionary<string, string>? defaultsWhenCsvNull)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        if (defaultsWhenCsvNull is null)
        {
            return rawValue;
        }

        if (defaultsWhenCsvNull.TryGetValue(csvField, out string? configuredDefault)
            && !string.IsNullOrWhiteSpace(configuredDefault))
        {
            return configuredDefault;
        }

        return rawValue;
    }

    private static bool IsDeprecatedOrDeleted(string? catalogStatus)
    {
        return catalogStatus?.StartsWith("60 - Deprecated", StringComparison.OrdinalIgnoreCase) == true
            || catalogStatus?.StartsWith("70 - Deleted", StringComparison.OrdinalIgnoreCase) == true;
    }
}
