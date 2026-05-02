using BpcAdoUpdater.Ado;
using BpcAdoUpdater.Config;
using BpcAdoUpdater.Models;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace BpcAdoUpdater.Apply;

public sealed class Applier
{
    public async Task ApplyApprovedChangesAsync(
        AdoClient client,
        AppConfig config,
        ChangeSet changeSet,
        Dictionary<string, AdoWorkItemRecord> index,
        bool dryRun,
        List<RunLogOperation> runLogOperations,
        CancellationToken cancellationToken,
        int maxUpdateConcurrency = 8,
        Action<int, int, string>? onProgress = null)
    {
        Dictionary<string, int> workItemTypeOrder = BuildWorkItemTypeOrder(config);
        Dictionary<string, string> microsoftIdBySequence = BuildMicrosoftIdBySequence(changeSet);

        List<Change> approvedAdds = changeSet.ApprovedChanges
            .Where(x => x.Kind == ChangeKind.Add)
            .OrderBy(x => GetWorkItemTypeRank(config, workItemTypeOrder, x.Row.WorkItemType))
            .ThenBy(x => BpcAdoUpdater.Models.ProcessSequenceHierarchy.GetDepth(x.Row.ProcessSequenceId))
            .ThenBy(x => x.Row.ProcessSequenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Row.RowNumber)
            .ToList();

        List<Change> approvedUpdates = changeSet.ApprovedChanges
            .Where(x => x.Kind == ChangeKind.Update)
            .ToList();

        int totalOperations = approvedAdds.Count + approvedUpdates.Count;
        int completedOperations = 0;
        object runLogLock = new();

        void AddRunLog(RunLogOperation operation)
        {
            lock (runLogLock)
            {
                runLogOperations.Add(operation);
            }
        }

        void ReportProgress(string label)
        {
            if (onProgress is null)
            {
                return;
            }

            int done = Interlocked.Increment(ref completedOperations);
            onProgress(done, totalOperations, label);
        }

        foreach (Change add in approvedAdds)
        {
            if (dryRun)
            {
                AddRunLog(new RunLogOperation
                {
                    MicrosoftId = add.MicrosoftId,
                    Kind = add.Kind,
                    AdoId = null,
                    Approved = true,
                    Success = true,
                    Message = "Dry run - create skipped",
                    Fields = add.Deltas,
                });
                ReportProgress($"Add {add.MicrosoftId}");
                continue;
            }

            try
            {
                JsonPatchDocument patch = BuildPatch(add, config, index, microsoftIdBySequence, null);
                string workItemType = config.ResolveWorkItemType(add.Row.WorkItemType);
                var created = await ExecuteWithRetryAsync(
                    () => client.CreateWorkItemAsync(config.Project, workItemType, patch, cancellationToken),
                    cancellationToken);
                int createdId = created.Id ?? 0;
                int rev = created.Rev ?? 0;

                var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (FieldDelta delta in add.Deltas)
                {
                    fields[delta.AdoFieldName] = delta.NewValue;
                    fields[delta.CsvFieldName] = delta.NewValue;
                }

                index[add.MicrosoftId] = new AdoWorkItemRecord
                {
                    Id = createdId,
                    Rev = rev,
                    MicrosoftId = add.MicrosoftId,
                    WorkItemType = workItemType,
                    Fields = fields,
                };

                AddRunLog(new RunLogOperation
                {
                    MicrosoftId = add.MicrosoftId,
                    Kind = add.Kind,
                    AdoId = createdId,
                    Approved = true,
                    Success = true,
                    Message = "Created",
                    Fields = add.Deltas,
                });
            }
            catch (Exception ex)
            {
                AddRunLog(new RunLogOperation
                {
                    MicrosoftId = add.MicrosoftId,
                    Kind = add.Kind,
                    AdoId = null,
                    Approved = true,
                    Success = false,
                    Message = ex.Message,
                    Fields = add.Deltas,
                });
            }

            ReportProgress($"Add {add.MicrosoftId}");
        }

        int concurrency = Math.Max(1, maxUpdateConcurrency);
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var updateTasks = approvedUpdates.Select(async update =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (dryRun)
                {
                    AddRunLog(new RunLogOperation
                    {
                        MicrosoftId = update.MicrosoftId,
                        Kind = update.Kind,
                        AdoId = update.AdoId,
                        Approved = true,
                        Success = true,
                        Message = "Dry run - update skipped",
                        Fields = update.Deltas,
                    });

                    return;
                }

                if (update.AdoId is null)
                {
                    throw new InvalidOperationException($"Cannot update Microsoft ID '{update.MicrosoftId}' without ADO ID.");
                }

                JsonPatchDocument patch = BuildPatch(update, config, index, microsoftIdBySequence, update.AdoRev);
                var updated = await ExecuteWithRetryAsync(
                    () => client.UpdateWorkItemAsync(update.AdoId.Value, patch, cancellationToken),
                    cancellationToken);

                AddRunLog(new RunLogOperation
                {
                    MicrosoftId = update.MicrosoftId,
                    Kind = update.Kind,
                    AdoId = updated.Id,
                    Approved = true,
                    Success = true,
                    Message = "Updated",
                    Fields = update.Deltas,
                });
            }
            catch (Exception ex)
            {
                AddRunLog(new RunLogOperation
                {
                    MicrosoftId = update.MicrosoftId,
                    Kind = update.Kind,
                    AdoId = update.AdoId,
                    Approved = true,
                    Success = false,
                    Message = ex.Message,
                    Fields = update.Deltas,
                });
            }
            finally
            {
                ReportProgress($"Update {update.MicrosoftId}");
                semaphore.Release();
            }
        });

        await Task.WhenAll(updateTasks);
    }

    private static JsonPatchDocument BuildPatch(
        Change change,
        AppConfig config,
        IReadOnlyDictionary<string, AdoWorkItemRecord> index,
        IReadOnlyDictionary<string, string> microsoftIdBySequence,
        int? expectedRev)
    {
        var patch = new JsonPatchDocument();
        var touchedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (expectedRev.HasValue)
        {
            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Test,
                Path = "/rev",
                Value = expectedRev.Value,
            });
        }

        foreach (FieldDelta delta in change.Deltas)
        {
            if (touchedFields.Contains(delta.AdoFieldName))
            {
                continue;
            }

            string? formattedValue = AdoFieldValueFormatter.FormatForAdo(delta.CsvFieldName, delta.NewValue);
            bool isBlank = string.IsNullOrWhiteSpace(formattedValue);

            if (change.Kind == ChangeKind.Update && isBlank)
            {
                patch.Add(new JsonPatchOperation
                {
                    Operation = Operation.Remove,
                    Path = "/fields/" + delta.AdoFieldName,
                });
                touchedFields.Add(delta.AdoFieldName);
                continue;
            }

            if (isBlank)
            {
                continue;
            }

            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/" + delta.AdoFieldName,
                Value = formattedValue,
            });
            touchedFields.Add(delta.AdoFieldName);
        }

        if (change.Kind == ChangeKind.Add)
        {
            if (!touchedFields.Contains("System.AreaPath"))
            {
                patch.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.AreaPath",
                    Value = change.Row.AreaPath ?? config.AreaPathRoot,
                });
            }

            if (!touchedFields.Contains("System.IterationPath"))
            {
                patch.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.IterationPath",
                    Value = change.Row.IterationPath ?? config.DefaultIterationPath ?? config.AreaPathRoot,
                });
            }

            TryAddParentLink(change, config.OrganizationUrl, index, microsoftIdBySequence, patch);
        }

        return patch;
    }

    private static void TryAddParentLink(
        Change change,
        string organizationUrl,
        IReadOnlyDictionary<string, AdoWorkItemRecord> index,
        IReadOnlyDictionary<string, string> microsoftIdBySequence,
        JsonPatchDocument patch)
    {
        string? parentProcessSequence = change.Row.ParentProcessSequenceId;
        if (string.IsNullOrWhiteSpace(parentProcessSequence))
        {
            return;
        }

        AdoWorkItemRecord? parent = null;

        if (microsoftIdBySequence.TryGetValue(parentProcessSequence, out string? parentMicrosoftId)
            && index.TryGetValue(parentMicrosoftId, out AdoWorkItemRecord? parentByMicrosoftId))
        {
            parent = parentByMicrosoftId;
        }

        if (parent is null)
        {
            parent = index.Values.FirstOrDefault(x =>
                string.Equals(x.Fields.GetValueOrDefault("Process sequence ID"), parentProcessSequence, StringComparison.OrdinalIgnoreCase));
        }

        if (parent is null)
        {
            return;
        }

        patch.Add(new JsonPatchOperation
        {
            Operation = Operation.Add,
            Path = "/relations/-",
            Value = new
            {
                rel = "System.LinkTypes.Hierarchy-Reverse",
                url = $"{organizationUrl.TrimEnd('/')}/_apis/wit/workItems/{parent.Id}",
            },
        });
    }

    private static Dictionary<string, int> BuildWorkItemTypeOrder(AppConfig config)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        foreach (string mappedType in config.WorkItemTypeMap.Values)
        {
            if (string.IsNullOrWhiteSpace(mappedType) || order.ContainsKey(mappedType))
            {
                continue;
            }

            order[mappedType] = index;
            index++;
        }

        return order;
    }

    private static int GetWorkItemTypeRank(
        AppConfig config,
        IReadOnlyDictionary<string, int> workItemTypeOrder,
        string? csvWorkItemType)
    {
        string resolved = config.ResolveWorkItemType(csvWorkItemType);
        return workItemTypeOrder.TryGetValue(resolved, out int rank)
            ? rank
            : int.MaxValue;
    }

    private static Dictionary<string, string> BuildMicrosoftIdBySequence(ChangeSet changeSet)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Change change in changeSet.Changes)
        {
            string? sequenceId = change.Row.ProcessSequenceId;
            string? microsoftId = change.Row.MicrosoftId;
            if (string.IsNullOrWhiteSpace(sequenceId) || string.IsNullOrWhiteSpace(microsoftId))
            {
                continue;
            }

            map[sequenceId] = microsoftId;
        }

        return map;
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        int delayMs = 500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        throw new InvalidOperationException("Retry operation unexpectedly failed to return or throw.");
    }
}
