namespace BpcAdoUpdater.Models;

public sealed class AdoWorkItemRecord
{
    public required int Id { get; init; }
    public required int Rev { get; init; }
    public required string MicrosoftId { get; init; }
    public required string WorkItemType { get; init; }
    public required Dictionary<string, string?> Fields { get; init; }
}
