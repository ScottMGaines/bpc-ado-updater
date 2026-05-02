namespace BpcAdoUpdater.Models;

public sealed class CatalogRow
{
    public required int RowNumber { get; init; }
    public string? Id { get; init; }
    public string? ProcessSequenceId { get; init; }
    public string? AlternateProcessSequenceId { get; init; }
    public string? MicrosoftId { get; init; }
    public string? WorkItemType { get; init; }
    public string? Title1 { get; init; }
    public string? Title2 { get; init; }
    public string? Title3 { get; init; }
    public string? Title4 { get; init; }
    public string? Title5 { get; init; }
    public string? State { get; init; }
    public string? AreaPath { get; init; }
    public string? IterationPath { get; init; }
    public string? Description { get; init; }
    public string? CatalogStatus { get; init; }
    public string? ArticleStatus { get; init; }
    public string? Author { get; init; }
    public string? BusinessProcessFlowStatus { get; init; }
    public string? MicrosoftReferences { get; init; }
    public string? PartnerReferences { get; init; }
    public string? UpdateComments { get; init; }
    public string? ApplicationFamily { get; init; }
    public string? Products { get; init; }
    public string? SoftwareDevelopmentCompany { get; init; }
    public string? SoftwareDevelopmentSolutions { get; init; }
    public string? Module { get; init; }
    public string? MenuPath { get; init; }
    public string? MenuItemName { get; init; }
    public string? ApqcId { get; init; }
    public string? ApqcDescription { get; init; }
    public string? Scope { get; init; }
    public string? FitGapStatus { get; init; }
    public string? GapSolutionApproach { get; init; }

    public string? EffectiveTitle =>
        FirstNonEmpty(Title5, Title4, Title3, Title2, Title1) ??
        FirstNonEmpty(Title1, Title2, Title3, Title4, Title5);

    public int TitleDepth
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title1)) return 1;
            if (!string.IsNullOrWhiteSpace(Title2)) return 2;
            if (!string.IsNullOrWhiteSpace(Title3)) return 3;
            if (!string.IsNullOrWhiteSpace(Title4)) return 4;
            if (!string.IsNullOrWhiteSpace(Title5)) return 5;
            return 0;
        }
    }

    public string? ParentProcessSequenceId => ProcessSequenceHierarchy.GetParent(ProcessSequenceId);

    public IReadOnlyDictionary<string, string?> ToCsvFieldMap() => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ID"] = Id,
        ["Process sequence ID"] = ProcessSequenceId,
        ["Alternate process sequence ID"] = AlternateProcessSequenceId,
        ["Microsoft ID"] = MicrosoftId,
        ["Work item type"] = WorkItemType,
        ["Title 1"] = Title1,
        ["Title 2"] = Title2,
        ["Title 3"] = Title3,
        ["Title 4"] = Title4,
        ["Title 5"] = Title5,
        ["State"] = State,
        ["Area Path"] = AreaPath,
        ["Iteration path"] = IterationPath,
        ["Description"] = Description,
        ["Catalog status"] = CatalogStatus,
        ["Article status"] = ArticleStatus,
        ["Author"] = Author,
        ["Business process flow status"] = BusinessProcessFlowStatus,
        ["Microsoft references"] = MicrosoftReferences,
        ["Partner references"] = PartnerReferences,
        ["Update comments"] = UpdateComments,
        ["Application family"] = ApplicationFamily,
        ["Products"] = Products,
        ["Software development company"] = SoftwareDevelopmentCompany,
        ["Software development solutions"] = SoftwareDevelopmentSolutions,
        ["Module"] = Module,
        ["Menu path"] = MenuPath,
        ["Menu item name"] = MenuItemName,
        ["APQC ID"] = ApqcId,
        ["APQC description"] = ApqcDescription,
        ["Scope"] = Scope,
        ["Fit Gap Status"] = FitGapStatus,
        ["Gap solution approach"] = GapSolutionApproach,
        ["Title"] = EffectiveTitle,
    };

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
