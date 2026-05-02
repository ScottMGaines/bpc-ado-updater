namespace BpcAdoUpdater.Models;

public sealed class ChangeSet
{
    public required List<Change> Changes { get; init; }
    public required List<string> InformationalMessages { get; init; }

    public IEnumerable<Change> Adds => Changes.Where(x => x.Kind == ChangeKind.Add);
    public IEnumerable<Change> Updates => Changes.Where(x => x.Kind == ChangeKind.Update);
    public IEnumerable<Change> ApprovedChanges => Changes.Where(x => x.Approved && x.Kind is ChangeKind.Add or ChangeKind.Update);
}
