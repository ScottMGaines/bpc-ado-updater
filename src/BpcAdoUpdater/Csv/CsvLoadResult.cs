using BpcAdoUpdater.Models;

namespace BpcAdoUpdater.Csv;

public sealed record CsvLoadResult(
    IReadOnlyList<CatalogRow> Rows,
    int SourceRecordCount,
    int SkippedRowCount,
    int MissingMicrosoftIdCount,
    int MissingWorkItemTypeCount);