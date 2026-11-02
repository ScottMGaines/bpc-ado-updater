using CsvHelper;
using CsvHelper.Configuration;
using BpcAdoUpdater.Models;
using System.Globalization;

namespace BpcAdoUpdater.Csv;

public sealed class CsvLoader
{
    public IReadOnlyList<CatalogRow> Load(string csvPath) => LoadWithDiagnostics(csvPath).Rows;

    public CsvLoadResult LoadWithDiagnostics(
        string csvPath,
        IReadOnlyDictionary<string, string>? csvColumnMap = null)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV file not found", csvPath);
        }

        var rows = new List<CatalogRow>();
    int sourceRecordCount = 0;
    int skippedRowCount = 0;
    int missingMicrosoftIdCount = 0;
    int missingWorkItemTypeCount = 0;
        using var reader = new StreamReader(csvPath);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            IgnoreBlankLines = false,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, csvConfig);
        csv.Context.RegisterClassMap(new CatalogRowMap(csvColumnMap ?? new Dictionary<string, string>()));

        int rowNumber = 1;
        foreach (CatalogRowRaw raw in csv.GetRecords<CatalogRowRaw>())
        {
            sourceRecordCount++;
            rowNumber++;
            bool missingMicrosoftId = string.IsNullOrWhiteSpace(raw.MicrosoftId);
            bool missingWorkItemType = string.IsNullOrWhiteSpace(raw.WorkItemType);
            if (missingMicrosoftId || missingWorkItemType)
            {
                skippedRowCount++;
                if (missingMicrosoftId)
                {
                    missingMicrosoftIdCount++;
                }

                if (missingWorkItemType)
                {
                    missingWorkItemTypeCount++;
                }

                continue;
            }

            rows.Add(new CatalogRow
            {
                RowNumber = rowNumber,
                Id = Normalize(raw.Id),
                ProcessSequenceId = Normalize(raw.ProcessSequenceId),
                AlternateProcessSequenceId = Normalize(raw.AlternateProcessSequenceId),
                MicrosoftId = Normalize(raw.MicrosoftId),
                WorkItemType = Normalize(raw.WorkItemType),
                Title1 = Normalize(raw.Title1),
                Title2 = Normalize(raw.Title2),
                Title3 = Normalize(raw.Title3),
                Title4 = Normalize(raw.Title4),
                Title5 = Normalize(raw.Title5),
                Title6 = Normalize(raw.Title6),
                Title7 = Normalize(raw.Title7),
                Title8 = Normalize(raw.Title8),
                State = Normalize(raw.State),
                AreaPath = Normalize(raw.AreaPath),
                IterationPath = Normalize(raw.IterationPath),
                Description = Normalize(raw.Description),
                CatalogStatus = Normalize(raw.CatalogStatus),
                ArticleStatus = Normalize(raw.ArticleStatus),
                Author = Normalize(raw.Author),
                BusinessProcessFlowStatus = Normalize(raw.BusinessProcessFlowStatus),
                MicrosoftReferences = Normalize(raw.MicrosoftReferences),
                PartnerReferences = Normalize(raw.PartnerReferences),
                UpdateComments = Normalize(raw.UpdateComments),
                ApplicationFamily = Normalize(raw.ApplicationFamily),
                Products = Normalize(raw.Products),
                SoftwareDevelopmentCompany = Normalize(raw.SoftwareDevelopmentCompany),
                SoftwareDevelopmentSolutions = Normalize(raw.SoftwareDevelopmentSolutions),
                Module = Normalize(raw.Module),
                MenuPath = Normalize(raw.MenuPath),
                MenuItemName = Normalize(raw.MenuItemName),
                ApqcId = Normalize(raw.ApqcId),
                ApqcDescription = Normalize(raw.ApqcDescription),
                Scope = Normalize(raw.Scope),
                FitGapStatus = Normalize(raw.FitGapStatus),
                GapSolutionApproach = Normalize(raw.GapSolutionApproach),
            });
        }

        return new CsvLoadResult(
            rows,
            sourceRecordCount,
            skippedRowCount,
            missingMicrosoftIdCount,
            missingWorkItemTypeCount);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private sealed class CatalogRowRaw
    {
        public string? Id { get; set; }
        public string? ProcessSequenceId { get; set; }
        public string? AlternateProcessSequenceId { get; set; }
        public string? MicrosoftId { get; set; }
        public string? WorkItemType { get; set; }
        public string? Title1 { get; set; }
        public string? Title2 { get; set; }
        public string? Title3 { get; set; }
        public string? Title4 { get; set; }
        public string? Title5 { get; set; }
        public string? Title6 { get; set; }
        public string? Title7 { get; set; }
        public string? Title8 { get; set; }
        public string? State { get; set; }
        public string? AreaPath { get; set; }
        public string? IterationPath { get; set; }
        public string? Description { get; set; }
        public string? CatalogStatus { get; set; }
        public string? ArticleStatus { get; set; }
        public string? Author { get; set; }
        public string? BusinessProcessFlowStatus { get; set; }
        public string? MicrosoftReferences { get; set; }
        public string? PartnerReferences { get; set; }
        public string? UpdateComments { get; set; }
        public string? ApplicationFamily { get; set; }
        public string? Products { get; set; }
        public string? SoftwareDevelopmentCompany { get; set; }
        public string? SoftwareDevelopmentSolutions { get; set; }
        public string? Module { get; set; }
        public string? MenuPath { get; set; }
        public string? MenuItemName { get; set; }
        public string? ApqcId { get; set; }
        public string? ApqcDescription { get; set; }
        public string? Scope { get; set; }
        public string? FitGapStatus { get; set; }
        public string? GapSolutionApproach { get; set; }
    }

    private sealed class CatalogRowMap : ClassMap<CatalogRowRaw>
    {
        public CatalogRowMap(IReadOnlyDictionary<string, string> csvColumnMap)
        {
            Map(x => x.Id).Name(GetHeaderNames("ID", csvColumnMap));
            Map(x => x.ProcessSequenceId).Name(GetHeaderNames("Process sequence ID", csvColumnMap));
            Map(x => x.AlternateProcessSequenceId).Name(GetHeaderNames("Alternate process sequence ID", csvColumnMap));
            Map(x => x.MicrosoftId).Name(GetHeaderNames("Microsoft ID", csvColumnMap));
            Map(x => x.WorkItemType).Name(GetHeaderNames("Work item type", csvColumnMap));
            Map(x => x.Title1).Name(GetHeaderNames("Title 1", csvColumnMap));
            Map(x => x.Title2).Name(GetHeaderNames("Title 2", csvColumnMap));
            Map(x => x.Title3).Name(GetHeaderNames("Title 3", csvColumnMap));
            Map(x => x.Title4).Name(GetHeaderNames("Title 4", csvColumnMap));
            Map(x => x.Title5).Name(GetHeaderNames("Title 5", csvColumnMap));
            Map(x => x.Title6).Name(GetHeaderNames("Title 6", csvColumnMap));
            Map(x => x.Title7).Name(GetHeaderNames("Title 7", csvColumnMap));
            Map(x => x.Title8).Name(GetHeaderNames("Title 8", csvColumnMap));
            Map(x => x.State).Name(GetHeaderNames("State", csvColumnMap));
            Map(x => x.AreaPath).Name(GetHeaderNames("Area Path", csvColumnMap));
            Map(x => x.IterationPath).Name(GetHeaderNames("Iteration path", csvColumnMap));
            Map(x => x.Description).Name(GetHeaderNames("Description", csvColumnMap));
            Map(x => x.CatalogStatus).Name(GetHeaderNames("Catalog status", csvColumnMap));
            Map(x => x.ArticleStatus).Name(GetHeaderNames("Article status", csvColumnMap));
            Map(x => x.Author).Name(GetHeaderNames("Author", csvColumnMap));
            Map(x => x.BusinessProcessFlowStatus).Name(GetHeaderNames("Business process flow status", csvColumnMap));
            Map(x => x.MicrosoftReferences).Name(GetHeaderNames("Microsoft references", csvColumnMap));
            Map(x => x.PartnerReferences).Name(GetHeaderNames("Partner references", csvColumnMap));
            Map(x => x.UpdateComments).Name(GetHeaderNames("Update comments", csvColumnMap));
            Map(x => x.ApplicationFamily).Name(GetHeaderNames("Application family", csvColumnMap));
            Map(x => x.Products).Name(GetHeaderNames("Products", csvColumnMap));
            Map(x => x.SoftwareDevelopmentCompany).Name(GetHeaderNames("Software development company", csvColumnMap));
            Map(x => x.SoftwareDevelopmentSolutions).Name(GetHeaderNames("Software development solutions", csvColumnMap));
            Map(x => x.Module).Name(GetHeaderNames("Module", csvColumnMap));
            Map(x => x.MenuPath).Name(GetHeaderNames("Menu path", csvColumnMap));
            Map(x => x.MenuItemName).Name(GetHeaderNames("Menu item name", csvColumnMap));
            Map(x => x.ApqcId).Name(GetHeaderNames("APQC ID", csvColumnMap));
            Map(x => x.ApqcDescription).Name(GetHeaderNames("APQC description", csvColumnMap));
            Map(x => x.Scope).Name(GetHeaderNames("Scope", csvColumnMap));
            Map(x => x.FitGapStatus).Name(GetHeaderNames("Fit Gap Status", csvColumnMap));
            Map(x => x.GapSolutionApproach).Name(GetHeaderNames("Gap solution approach", csvColumnMap));
        }

        private static string[] GetHeaderNames(string canonicalName, IReadOnlyDictionary<string, string> csvColumnMap)
        {
            if (csvColumnMap.TryGetValue(canonicalName, out string? configuredName)
                && !string.IsNullOrWhiteSpace(configuredName)
                && !configuredName.Equals(canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { configuredName, canonicalName };
            }

            return new[] { canonicalName };
        }
    }
}
