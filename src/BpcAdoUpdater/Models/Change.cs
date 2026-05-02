namespace BpcAdoUpdater.Models;

public sealed class Change
{
    public required ChangeKind Kind { get; init; }
    public required CatalogRow Row { get; init; }
    public int? AdoId { get; set; }
    public int? AdoRev { get; set; }
    public required List<FieldDelta> Deltas { get; init; }
    public bool Approved { get; set; }
    public bool IsCustomerModified { get; set; }

    public string MicrosoftId => Row.MicrosoftId ?? string.Empty;
}
