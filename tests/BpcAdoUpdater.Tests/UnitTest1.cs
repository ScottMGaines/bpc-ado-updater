using BpcAdoUpdater.Csv;
using BpcAdoUpdater.Config;
using BpcAdoUpdater.Diff;
using BpcAdoUpdater.Models;
using BpcAdoUpdater.Apply;
using FluentAssertions;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace BpcAdoUpdater.Tests;

public class UnitTests
{
    [Fact]
    public void AppConfig_Should_Resolve_Only_Configured_WorkItemTypes()
    {
        var config = new AppConfig
        {
            OrganizationUrl = "https://example.test",
            Project = "Project",
            AreaPathRoot = "BPC",
            PatEnvironmentVariableName = "ADO_PAT",
            IgnoredApplications = new List<string> { "Business Central" },
            CsvColumnMap = new Dictionary<string, string>(),
            FieldMap = new Dictionary<string, string> { ["Microsoft ID"] = "Custom.MicrosoftID" },
            WorkItemTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["End to end"] = "Feature",
            },
            DefaultsWhenCsvNull = new Dictionary<string, string>(),
        };

        config.TryResolveWorkItemType("End to end", out string adoType).Should().BeTrue();
        adoType.Should().Be("Feature");
        config.TryResolveWorkItemType("Test case", out _).Should().BeFalse();
    }

    [Fact]
    public void AppConfig_Should_Ignore_Only_Exact_ApplicationFamily_Matches()
    {
        var config = new AppConfig
        {
            OrganizationUrl = "https://example.test",
            Project = "Project",
            AreaPathRoot = "BPC",
            PatEnvironmentVariableName = "ADO_PAT",
            IgnoredApplications = new List<string> { "Business Central" },
            CsvColumnMap = new Dictionary<string, string>(),
            FieldMap = new Dictionary<string, string> { ["Microsoft ID"] = "Custom.MicrosoftID" },
            WorkItemTypeMap = new Dictionary<string, string>(),
            DefaultsWhenCsvNull = new Dictionary<string, string>(),
        };

        config.ShouldIgnoreApplication("Business Central").Should().BeTrue();
        config.ShouldIgnoreApplication(" Business Central ").Should().BeTrue();
        config.ShouldIgnoreApplication("Business Central, Supply Chain").Should().BeFalse();
        config.ShouldIgnoreApplication("business central").Should().BeFalse();
    }

    [Fact]
    public void ProcessSequenceHierarchy_Should_Preserve_ZeroPadding_When_Finding_Parents()
    {
        ProcessSequenceHierarchy.GetParent("10.05.000.000").Should().Be("10.00.000.000");
        ProcessSequenceHierarchy.GetParent("10.05.010.000").Should().Be("10.05.000.000");
        ProcessSequenceHierarchy.GetParent("10.05.010.100").Should().Be("10.05.010.000");
    }

    [Fact]
    public void Applier_OrderAdds_Should_Order_New_Hierarchy_Parents_Before_Children()
    {
        var config = new AppConfig
        {
            OrganizationUrl = "https://example.test",
            Project = "Project",
            AreaPathRoot = "BPC",
            PatEnvironmentVariableName = "ADO_PAT",
            IgnoredApplications = new List<string>(),
            CsvColumnMap = new Dictionary<string, string>(),
            FieldMap = new Dictionary<string, string> { ["Microsoft ID"] = "Custom.MicrosoftID" },
            WorkItemTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["End to end"] = "End to end",
                ["Process"] = "Process",
                ["Process area"] = "Process area",
                ["Scenario"] = "Scenario",
            },
            DefaultsWhenCsvNull = new Dictionary<string, string>(),
        };

        static Change CreateAdd(string microsoftId, string workItemType, string sequenceId, int rowNumber) => new()
        {
            Kind = ChangeKind.Add,
            Row = new CatalogRow
            {
                RowNumber = rowNumber,
                MicrosoftId = microsoftId,
                WorkItemType = workItemType,
                ProcessSequenceId = sequenceId,
            },
            Deltas = new List<FieldDelta>(),
        };

        Change[] additions =
        {
            CreateAdd("scenario", "Scenario", "10.05.010.100", 1),
            CreateAdd("process", "Process", "10.05.010.000", 2),
            CreateAdd("area", "Process area", "10.05.000.000", 3),
            CreateAdd("end-to-end", "End to end", "10.00.000.000", 4),
        };

        Applier.OrderAdds(additions, config)
            .Select(change => change.MicrosoftId)
            .Should()
            .Equal("end-to-end", "area", "process", "scenario");
    }

    [Fact]
    public void CsvLoader_Should_Load_Valid_Rows_And_Skip_Invalid_Ones()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "ID,Process sequence ID,Alternate process sequence ID,Microsoft ID,Work item type,Title 1,Title 2,Title 3,Title 4,Title 5,State,Area Path,Iteration path,Description,Catalog status,Article status,Author,Business process flow status,Microsoft references,Partner references,Update comments,Application family,Products,Software development company,Software development solutions,Module,Menu path,Menu item name,APQC ID,APQC description,Scope,Fit Gap Status,Gap solution approach\n" +
                ",10.00.000.000,,ms-1,End to end,Acquire to dispose,,,,,New,BPC\\,BPC,Desc,30 - Updated,10 - Not started,Microsoft,,,,,Finance and Operations,Finance,,,,,,,,,,\n" +
                ",10.05.000.000,,,Process area,Missing Microsoft ID,,,,,New,BPC\\,BPC,Desc,30 - Updated,10 - Not started,Microsoft,,,,,Finance and Operations,Finance,,,,,,,,,,\n");

            var loader = new CsvLoader();

            CsvLoadResult result = loader.LoadWithDiagnostics(path);
            IReadOnlyList<CatalogRow> rows = result.Rows;

            rows.Should().HaveCount(1);
            result.SourceRecordCount.Should().Be(2);
            result.SkippedRowCount.Should().Be(1);
            result.MissingMicrosoftIdCount.Should().Be(1);
            result.MissingWorkItemTypeCount.Should().Be(0);
            rows[0].MicrosoftId.Should().Be("ms-1");
            rows[0].EffectiveTitle.Should().Be("Acquire to dispose");
            rows[0].ParentProcessSequenceId.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CsvLoader_Should_Use_Configured_Headers_And_Load_Title_Levels_Through_Eight()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "Microsoft ID MS BPC,Work item type,Title 1,Title 2,Title 3,Title 4,Title 5,Title 6,Title 7,Title 8,Process sequence ID MS BPC\n" +
                "ms-8,Process,,,,,,,,Level 8,10.20.100.000\n");

            var loader = new CsvLoader();
            var csvColumnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft ID"] = "Microsoft ID MS BPC",
                ["Process sequence ID"] = "Process sequence ID MS BPC",
            };

            CsvLoadResult result = loader.LoadWithDiagnostics(path, csvColumnMap);

            result.Rows.Should().ContainSingle();
            result.Rows[0].MicrosoftId.Should().Be("ms-8");
            result.Rows[0].EffectiveTitle.Should().Be("Level 8");
            result.Rows[0].TitleDepth.Should().Be(8);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Differ_Should_Detect_Updates_Adds_And_Deprecated_Rule()
    {
        var rowUpdate = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-2",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Plan fixed assets",
            CatalogStatus = "30 - Updated",
            Products = "Finance; Supply Chain Management",
        };

        var rowDeprecated = new CatalogRow
        {
            RowNumber = 3,
            MicrosoftId = "ms-3",
            ProcessSequenceId = "10.20.200.000",
            WorkItemType = "Process",
            Title1 = "Budget fixed assets",
            CatalogStatus = "60 - Deprecated",
            Description = "New description that must be ignored for deprecated rows",
        };

        var rowAdd = new CatalogRow
        {
            RowNumber = 4,
            MicrosoftId = "ms-4",
            ProcessSequenceId = "10.20.300.000",
            WorkItemType = "Process",
            Title1 = "Source assets",
            CatalogStatus = "30 - Updated",
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-2"] = new AdoWorkItemRecord
            {
                Id = 101,
                Rev = 3,
                MicrosoftId = "ms-2",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Custom.MicrosoftID"] = "ms-2",
                    ["System.Title"] = "Plan fixed assets",
                    ["Custom.CatalogStatus"] = "30 - Updated",
                    ["Custom.Products"] = "Supply Chain Management; Finance",
                    ["Author"] = "Microsoft",
                },
            },
            ["ms-3"] = new AdoWorkItemRecord
            {
                Id = 102,
                Rev = 9,
                MicrosoftId = "ms-3",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Custom.MicrosoftID"] = "ms-3",
                    ["System.Title"] = "Budget fixed assets",
                    ["Custom.CatalogStatus"] = "30 - Updated",
                    ["System.Description"] = "Old description",
                    ["Author"] = "Customer",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "Custom.MicrosoftID",
            ["Title"] = "System.Title",
            ["Catalog status"] = "Custom.CatalogStatus",
            ["Products"] = "Custom.Products",
            ["Description"] = "System.Description",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { rowUpdate, rowDeprecated, rowAdd }, ado, fieldMap);

        changes.Changes.Count(x => x.Kind == ChangeKind.Add).Should().Be(1);
        changes.Changes.Count(x => x.Kind == ChangeKind.Update).Should().Be(1);
        changes.Changes.Count(x => x.Kind == ChangeKind.Unchanged).Should().Be(1);

        Change deprecated = changes.Changes.Single(x => x.Row.MicrosoftId == "ms-3");
        deprecated.Kind.Should().Be(ChangeKind.Update);
        deprecated.Deltas.Should().ContainSingle(x => x.CsvFieldName == "Catalog status");
    }

    [Fact]
    public void Differ_Should_Not_Include_Area_Or_Iteration_Path_Deltas_For_Existing_Items()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-area-1",
            WorkItemType = "Process",
            AreaPath = "CSV\\Area",
            IterationPath = null,
        };
        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-area-1"] = new AdoWorkItemRecord
            {
                Id = 401,
                Rev = 1,
                MicrosoftId = "ms-area-1",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Custom.MicrosoftID"] = "ms-area-1",
                    ["System.AreaPath"] = "Existing\\Area",
                    ["System.IterationPath"] = "Existing\\Iteration",
                },
            },
        };
        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "Custom.MicrosoftID",
            ["Area Path"] = "System.AreaPath",
            ["Iteration path"] = "System.IterationPath",
        };

        Change change = new Differ().Compare(new[] { row }, ado, fieldMap).Changes.Single();

        change.Kind.Should().Be(ChangeKind.Unchanged);
        change.Deltas.Should().NotContain(delta => delta.CsvFieldName == "Area Path");
        change.Deltas.Should().NotContain(delta => delta.CsvFieldName == "Iteration path");
    }

    [Fact]
    public void Applier_BuildPatch_Should_Not_Update_Area_Or_Iteration_Paths()
    {
        var config = new AppConfig
        {
            OrganizationUrl = "https://example.test",
            Project = "Project",
            AreaPathRoot = "Configured\\Area",
            DefaultIterationPath = "Configured\\Iteration",
            PatEnvironmentVariableName = "ADO_PAT",
            IgnoredApplications = new List<string>(),
            CsvColumnMap = new Dictionary<string, string>(),
            FieldMap = new Dictionary<string, string> { ["Microsoft ID"] = "Custom.MicrosoftID" },
            WorkItemTypeMap = new Dictionary<string, string>(),
            DefaultsWhenCsvNull = new Dictionary<string, string>(),
        };
        var update = new Change
        {
            Kind = ChangeKind.Update,
            Row = new CatalogRow { RowNumber = 2, MicrosoftId = "ms-path-2", WorkItemType = "Process" },
            AdoId = 402,
            AdoRev = 3,
            Deltas = new List<FieldDelta>
            {
                new() { CsvFieldName = "Area Path", AdoFieldName = "System.AreaPath", OldValue = "Existing\\Area", NewValue = null },
                new() { CsvFieldName = "Iteration path", AdoFieldName = "System.IterationPath", OldValue = "Existing\\Iteration", NewValue = null },
            },
        };

        JsonPatchDocument patch = Applier.BuildPatch(
            update,
            config,
            new Dictionary<string, AdoWorkItemRecord>(),
            new Dictionary<string, string>(),
            update.AdoRev);

        patch.Should().NotContain(operation => operation.Path == "/fields/System.AreaPath");
        patch.Should().NotContain(operation => operation.Path == "/fields/System.IterationPath");
    }

    [Fact]
    public void Applier_BuildPatch_Should_Use_Configured_Paths_For_Adds()
    {
        var config = new AppConfig
        {
            OrganizationUrl = "https://example.test",
            Project = "Project",
            AreaPathRoot = "Configured\\Area",
            DefaultIterationPath = "Configured\\Iteration",
            PatEnvironmentVariableName = "ADO_PAT",
            IgnoredApplications = new List<string>(),
            CsvColumnMap = new Dictionary<string, string>(),
            FieldMap = new Dictionary<string, string> { ["Microsoft ID"] = "Custom.MicrosoftID" },
            WorkItemTypeMap = new Dictionary<string, string>(),
            DefaultsWhenCsvNull = new Dictionary<string, string>(),
        };
        var add = new Change
        {
            Kind = ChangeKind.Add,
            Row = new CatalogRow
            {
                RowNumber = 2,
                MicrosoftId = "ms-path-1",
                WorkItemType = "Process",
                AreaPath = "CSV\\Area",
                IterationPath = "CSV\\Iteration",
            },
            Deltas = new List<FieldDelta>
            {
                new() { CsvFieldName = "Area Path", AdoFieldName = "System.AreaPath", NewValue = "CSV\\Area" },
                new() { CsvFieldName = "Iteration path", AdoFieldName = "System.IterationPath", NewValue = "CSV\\Iteration" },
            },
        };

        JsonPatchDocument patch = Applier.BuildPatch(
            add,
            config,
            new Dictionary<string, AdoWorkItemRecord>(),
            new Dictionary<string, string>(),
            null);

        patch.Should().Contain(operation => operation.Path == "/fields/System.AreaPath" && Equals(operation.Value, "Configured\\Area"));
        patch.Should().Contain(operation => operation.Path == "/fields/System.IterationPath" && Equals(operation.Value, "Configured\\Iteration"));
        patch.Should().NotContain(operation => Equals(operation.Value, "CSV\\Area") || Equals(operation.Value, "CSV\\Iteration"));
    }

    [Fact]
    public void Differ_Should_Ignore_Null_When_Old_Equals_Configured_Default()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-default-1",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Test",
            BusinessProcessFlowStatus = null,
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-default-1"] = new AdoWorkItemRecord
            {
                Id = 200,
                Rev = 1,
                MicrosoftId = "ms-default-1",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-default-1",
                    ["MSBPC.businessprocessflowstatus"] = "10 - Not started",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Business process flow status"] = "MSBPC.businessprocessflowstatus",
        };

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Business process flow status"] = "10 - Not started",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap, defaults);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Unchanged);
        change.Deltas.Should().BeEmpty();
    }

    [Fact]
    public void Differ_Should_Set_Default_When_Null_And_Old_Differs()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-default-2",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Test",
            BusinessProcessFlowStatus = null,
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-default-2"] = new AdoWorkItemRecord
            {
                Id = 201,
                Rev = 1,
                MicrosoftId = "ms-default-2",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-default-2",
                    ["MSBPC.businessprocessflowstatus"] = "20 - In progress",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Business process flow status"] = "MSBPC.businessprocessflowstatus",
        };

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Business process flow status"] = "10 - Not started",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap, defaults);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Update);
        change.Deltas.Should().ContainSingle(x =>
            x.CsvFieldName == "Business process flow status"
            && x.OldValue == "20 - In progress"
            && x.NewValue == "10 - Not started");
    }

    [Fact]
    public void Differ_Should_Ignore_Description_Differences_From_Whitespace_And_Html_Entities()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-desc-1",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Classify Assets",
            Description = "Classifying assets means organizing and categorizing assets based on specific\ncriteria such as type, usage, value, location, or lifecycle stage. This helps\norganizations manage, track, and report on their assets more effectively.\u00A0\nCommon asset classifications include: * Tangible vs. Intangible (e.g., machinery\nvs. patents) * Current vs. Non-current (e.g., inventory vs. buildings)",
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-desc-1"] = new AdoWorkItemRecord
            {
                Id = 300,
                Rev = 1,
                MicrosoftId = "ms-desc-1",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-desc-1",
                    ["System.Description"] = "Classifying assets means organizing and categorizing assets based on specific<br/>criteria such as type, usage, value, location, or lifecycle stage. This helps<br/>organizations manage, track, and report on their assets more effectively.&amp;nbsp;<br/>Common asset classifications include: * Tangible vs. Intangible (e.g., machinery vs. patents) * Current vs. Non-current (e.g., inventory vs. buildings)",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Description"] = "System.Description",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Unchanged);
        change.Deltas.Should().BeEmpty();
    }

    [Fact]
    public void Differ_Should_Ignore_MicrosoftReferences_Differences_From_LineBreaks_And_Spacing()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-ref-1",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Case to resolution",
            MicrosoftReferences = "https://learn.microsoft.com/en-us/dynamics365/guidance/business-processes/case-to-resolution-introduction\n\n\ncase-to-resolution-graphics.pptx\n[https://bdo4.sharepoint.com/:p:/r/sites/bdodigital-erp/Shared%20Documents/Compliance/FastTrack/Business%20Process%20Catalog/Microsoft%20BPC%20-%202024.05/Business%20Process%20Flows/case-to-resolution-graphics.pptx?d=wf2809a298f41448a8a39bc973122fb79&csf=1&web=1&e=1J87Oy]",
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-ref-1"] = new AdoWorkItemRecord
            {
                Id = 301,
                Rev = 1,
                MicrosoftId = "ms-ref-1",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-ref-1",
                    ["MSBPC.microsoftreferences"] = "https://learn.microsoft.com/en-us/dynamics365/guidance/business-processes/case-to-resolution-introduction<br/><br/><br/>case-to-resolution-graphics.pptx<br/>[https://bdo4.sharepoint.com/:p:/r/sites/bdodigital-erp/Shared%20Documents/Compliance/FastTrack/Business%20Process%20Catalog/Microsoft%20BPC%20-%202024.05/Business%20Process%20Flows/case-to-resolution-graphics.pptx?d=wf2809a298f41448a8a39bc973122fb79&csf=1&web=1&e=1J87Oy]",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Microsoft references"] = "MSBPC.microsoftreferences",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Unchanged);
        change.Deltas.Should().BeEmpty();
    }

    [Fact]
    public void Differ_Should_Flag_MicrosoftReferences_For_Html_Migration_When_Ado_Is_PlainText()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-ref-2",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Case to resolution",
            MicrosoftReferences = "https://learn.microsoft.com/en-us/dynamics365/guidance/business-processes/case-to-resolution-introduction\n\ncase-to-resolution-graphics.pptx\n[https://example.com]",
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-ref-2"] = new AdoWorkItemRecord
            {
                Id = 302,
                Rev = 1,
                MicrosoftId = "ms-ref-2",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-ref-2",
                    ["MSBPC.microsoftreferences"] = "https://learn.microsoft.com/en-us/dynamics365/guidance/business-processes/case-to-resolution-introduction case-to-resolution-graphics.pptx [https://example.com]",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Microsoft references"] = "MSBPC.microsoftreferences",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Update);
        change.Deltas.Should().ContainSingle(x => x.CsvFieldName == "Microsoft references");
    }

    [Fact]
    public void AdoFieldValueFormatter_Should_Convert_RichText_Fields_To_Html()
    {
        string input = "Line one\n\nLine two\n[https://example.com]";

        string? output = AdoFieldValueFormatter.FormatForAdo("Microsoft references", input);

        output.Should().Be("Line one<br/><br/>Line two<br/>[https://example.com]");
    }

    [Fact]
    public void FieldNormalizer_Should_Treat_Html_And_PlainText_References_As_Equal()
    {
        string html = "https://learn.microsoft.com/<br/><br/>case-to-resolution-graphics.pptx<br/>[https://example.com]";
        string plain = "https://learn.microsoft.com/\n\ncase-to-resolution-graphics.pptx\n[https://example.com]";

        string? normalizedHtml = FieldNormalizer.Normalize(html, "Microsoft references");
        string? normalizedPlain = FieldNormalizer.Normalize(plain, "Microsoft references");

        normalizedHtml.Should().Be(normalizedPlain);
    }

    [Fact]
    public void Differ_Should_Not_Flag_Description_For_Html_Migration_When_No_LineBreaks_Exist()
    {
        var row = new CatalogRow
        {
            RowNumber = 2,
            MicrosoftId = "ms-desc-plain-1",
            ProcessSequenceId = "10.20.100.000",
            WorkItemType = "Process",
            Title1 = "Trade agreements",
            Description = "Using a single Excel or CSV file import lines for trade agreement journals for many legal entities at the same time using Avantiico's AMCS Solution.  This file can have data for multiple legal entities and will result in local trade agreement journals being created.",
        };

        var ado = new Dictionary<string, AdoWorkItemRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ms-desc-plain-1"] = new AdoWorkItemRecord
            {
                Id = 303,
                Rev = 1,
                MicrosoftId = "ms-desc-plain-1",
                WorkItemType = "Process",
                Fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBPC.microsoftid"] = "ms-desc-plain-1",
                    ["System.Description"] = "Using a single Excel or CSV file import lines for trade agreement journals for many legal entities at the same time using Avantiico's AMCS Solution. This file can have data for multiple legal entities and will result in local trade agreement journals being created.",
                    ["Author"] = "Microsoft",
                },
            },
        };

        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft ID"] = "MSBPC.microsoftid",
            ["Description"] = "System.Description",
        };

        var differ = new Differ();
        ChangeSet changes = differ.Compare(new[] { row }, ado, fieldMap);

        Change change = changes.Changes.Single();
        change.Kind.Should().Be(ChangeKind.Unchanged);
        change.Deltas.Should().BeEmpty();
    }
}
