using CsvHelper;
using CsvHelper.Configuration;
using BpcAdoUpdater.Models;
using System.Globalization;

namespace BpcAdoUpdater.Csv;

public sealed class CsvLoader
{
    public IReadOnlyList<CatalogRow> Load(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV file not found", csvPath);
        }

        var rows = new List<CatalogRow>();
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
        csv.Context.RegisterClassMap<CatalogRowMap>();

        int rowNumber = 1;
        foreach (CatalogRowRaw raw in csv.GetRecords<CatalogRowRaw>())
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(raw.MicrosoftId) || string.IsNullOrWhiteSpace(raw.WorkItemType))
            {
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

        return rows;
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
        public CatalogRowMap()
        {
            Map(x => x.Id).Name("ID");
            Map(x => x.ProcessSequenceId).Name("Process sequence ID");
            Map(x => x.AlternateProcessSequenceId).Name("Alternate process sequence ID");
            Map(x => x.MicrosoftId).Name("Microsoft ID");
            Map(x => x.WorkItemType).Name("Work item type");
            Map(x => x.Title1).Name("Title 1");
            Map(x => x.Title2).Name("Title 2");
            Map(x => x.Title3).Name("Title 3");
            Map(x => x.Title4).Name("Title 4");
            Map(x => x.Title5).Name("Title 5");
            Map(x => x.State).Name("State");
            Map(x => x.AreaPath).Name("Area Path");
            Map(x => x.IterationPath).Name("Iteration path");
            Map(x => x.Description).Name("Description");
            Map(x => x.CatalogStatus).Name("Catalog status");
            Map(x => x.ArticleStatus).Name("Article status");
            Map(x => x.Author).Name("Author");
            Map(x => x.BusinessProcessFlowStatus).Name("Business process flow status");
            Map(x => x.MicrosoftReferences).Name("Microsoft references");
            Map(x => x.PartnerReferences).Name("Partner references");
            Map(x => x.UpdateComments).Name("Update comments");
            Map(x => x.ApplicationFamily).Name("Application family");
            Map(x => x.Products).Name("Products");
            Map(x => x.SoftwareDevelopmentCompany).Name("Software development company");
            Map(x => x.SoftwareDevelopmentSolutions).Name("Software development solutions");
            Map(x => x.Module).Name("Module");
            Map(x => x.MenuPath).Name("Menu path");
            Map(x => x.MenuItemName).Name("Menu item name");
            Map(x => x.ApqcId).Name("APQC ID");
            Map(x => x.ApqcDescription).Name("APQC description");
            Map(x => x.Scope).Name("Scope");
            Map(x => x.FitGapStatus).Name("Fit Gap Status");
            Map(x => x.GapSolutionApproach).Name("Gap solution approach");
        }
    }
}
