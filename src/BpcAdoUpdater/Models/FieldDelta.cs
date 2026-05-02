namespace BpcAdoUpdater.Models;

public sealed class FieldDelta
{
    public required string CsvFieldName { get; init; }
    public required string AdoFieldName { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
