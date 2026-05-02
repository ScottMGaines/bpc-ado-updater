namespace BpcAdoUpdater.Models;

public sealed class RunLog
{
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; set; }
    public required string CsvPath { get; init; }
    public required bool DryRun { get; init; }
    public required List<RunLogOperation> Operations { get; init; }
}

public sealed class RunLogOperation
{
    public required string MicrosoftId { get; init; }
    public required ChangeKind Kind { get; init; }
    public int? AdoId { get; init; }
    public bool Approved { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public List<FieldDelta>? Fields { get; init; }
}
