namespace BpcAdoUpdater.Models;

public sealed class DuplicateWorkItemInfo
{
    public required string MicrosoftId { get; init; }
    public required int AdoId { get; init; }
    public string? Title { get; init; }
}