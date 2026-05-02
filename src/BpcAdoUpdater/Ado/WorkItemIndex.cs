using BpcAdoUpdater.Models;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace BpcAdoUpdater.Ado;

public sealed class WorkItemIndex
{
    public required Dictionary<string, AdoWorkItemRecord> ByMicrosoftId { get; init; }
    public required List<string> Warnings { get; init; }
    public required Dictionary<string, List<DuplicateWorkItemInfo>> DuplicateMicrosoftIds { get; init; }
    public required int QueriedWorkItemCount { get; init; }
    public required int RetrievedWorkItemCount { get; init; }
    public required int MissingMicrosoftIdCount { get; init; }

    public static async Task<WorkItemIndex> LoadAsync(
        AdoClient client,
        string project,
        string areaPath,
        IEnumerable<string> workItemTypes,
        IReadOnlyDictionary<string, string> fieldMap,
        CancellationToken cancellationToken)
    {
        string microsoftIdField = fieldMap["Microsoft ID"];

        var queryIds = await client.QueryWorkItemIdsByAreaPathAsync(project, areaPath, workItemTypes, cancellationToken);
        var requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Id",
            "System.Rev",
            "System.WorkItemType",
            microsoftIdField,
            "Author",
        };

        foreach (string adoField in fieldMap.Values)
        {
            requestedFields.Add(adoField);
        }

        var records = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var duplicateMap = new Dictionary<string, List<DuplicateWorkItemInfo>>(StringComparer.OrdinalIgnoreCase);
        int retrievedCount = 0;
        int missingMicrosoftIdCount = 0;

        foreach (IEnumerable<int> chunk in Chunk(queryIds, 200))
        {
            List<WorkItem> workItems = await client.GetWorkItemsBatchAsync(chunk, requestedFields, cancellationToken);
            foreach (WorkItem item in workItems)
            {
                retrievedCount++;
                if (!TryGetField(item, microsoftIdField, out string? microsoftId) || string.IsNullOrWhiteSpace(microsoftId))
                {
                    missingMicrosoftIdCount++;
                    continue;
                }

                string normalizedMicrosoftId = microsoftId.Trim();
                int id = item.Id ?? 0;
                int rev = item.Rev ?? 0;
                string workItemType = item.Fields.TryGetValue("System.WorkItemType", out object? witObj) ? Convert.ToString(witObj) ?? string.Empty : string.Empty;

                var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach ((string csvField, string adoField) in fieldMap)
                {
                    string? value = null;
                    if (TryGetField(item, adoField, out string? temp))
                    {
                        value = temp;
                    }

                    fieldValues[adoField] = value;
                    fieldValues[csvField] = value;
                }

                if (TryGetField(item, "Author", out string? author))
                {
                    fieldValues["Author"] = author;
                }

                var record = new AdoWorkItemRecord
                {
                    Id = id,
                    Rev = rev,
                    MicrosoftId = normalizedMicrosoftId,
                    WorkItemType = workItemType,
                    Fields = fieldValues,
                };

                if (records.ContainsKey(normalizedMicrosoftId))
                {
                    if (!duplicateMap.TryGetValue(normalizedMicrosoftId, out List<DuplicateWorkItemInfo>? duplicates))
                    {
                        duplicates = new List<DuplicateWorkItemInfo>();
                        string? firstTitle = records[normalizedMicrosoftId].Fields.GetValueOrDefault("System.Title")
                            ?? records[normalizedMicrosoftId].Fields.GetValueOrDefault("Title");
                        duplicates.Add(new DuplicateWorkItemInfo
                        {
                            MicrosoftId = normalizedMicrosoftId,
                            AdoId = records[normalizedMicrosoftId].Id,
                            Title = firstTitle,
                        });

                        duplicateMap[normalizedMicrosoftId] = duplicates;
                    }

                    string? duplicateTitle = item.Fields.TryGetValue("System.Title", out object? titleObj)
                        ? Convert.ToString(titleObj)
                        : null;

                    duplicates.Add(new DuplicateWorkItemInfo
                    {
                        MicrosoftId = normalizedMicrosoftId,
                        AdoId = id,
                        Title = duplicateTitle,
                    });

                    warnings.Add($"Duplicate Microsoft ID '{normalizedMicrosoftId}' found (work item id {id}). Keeping first occurrence.");
                    continue;
                }

                records[normalizedMicrosoftId] = record;
            }
        }

        return new WorkItemIndex
        {
            ByMicrosoftId = records,
            Warnings = warnings,
            DuplicateMicrosoftIds = duplicateMap,
            QueriedWorkItemCount = queryIds.Count,
            RetrievedWorkItemCount = retrievedCount,
            MissingMicrosoftIdCount = missingMicrosoftIdCount,
        };
    }

    private static bool TryGetField(WorkItem item, string fieldName, out string? value)
    {
        if (item.Fields.TryGetValue(fieldName, out object? obj) && obj is not null)
        {
            value = Convert.ToString(obj);
            return true;
        }

        foreach ((string key, object? candidate) in item.Fields)
        {
            if (!string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate is not null)
            {
                value = Convert.ToString(candidate);
                return true;
            }

            break;
        }

        value = null;
        return false;
    }

    private static IEnumerable<List<int>> Chunk(List<int> source, int chunkSize)
    {
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            int size = Math.Min(chunkSize, source.Count - i);
            yield return source.GetRange(i, size);
        }
    }
}
