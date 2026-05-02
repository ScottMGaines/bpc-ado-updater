namespace BpcAdoUpdater.Models;

public sealed class DecisionPlan
{
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string CsvPath { get; init; }
    public required string Project { get; init; }
    public required string AreaPathRoot { get; init; }
    public required List<DecisionPlanItem> Decisions { get; init; }
}

public sealed class DecisionPlanItem
{
    public required string MicrosoftId { get; init; }
    public required ChangeKind Kind { get; init; }
    public required bool Approved { get; init; }
    public string? Title { get; init; }
    public string? WorkItemType { get; init; }
}
