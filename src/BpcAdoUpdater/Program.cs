using BpcAdoUpdater.Ado;
using BpcAdoUpdater.Apply;
using BpcAdoUpdater.Config;
using BpcAdoUpdater.Csv;
using BpcAdoUpdater.Diff;
using BpcAdoUpdater.Models;
using BpcAdoUpdater.Review;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.Json;

internal static class Program
{
    private const string DecisionPlansFolder = "decision-plans";
    private const string ArtifactsFolder = "artifacts";
    private const string BackupsFolder = "backups";
    private const string RunLogsFolder = "run-logs";
    private const string DuplicatesFolder = "duplicates";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);

            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            PrintBanner();
            PrintRunMode(options);
            CleanupArtifacts(options);

            IConfiguration configRoot = BuildConfiguration(options.ConfigPath);
            AppConfig config = AppConfig.Load(configRoot);
            string pat = config.GetPatOrThrow(out bool usedConfigFallback);

            if (usedConfigFallback)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] Using PAT from Ado:Pat in config. Prefer environment variable for better security.");
            }

            var csvLoader = new CsvLoader();
            IReadOnlyList<CatalogRow> csvRows = Array.Empty<CatalogRow>();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[blue]Loading CSV catalog...[/]", _ =>
                {
                    csvRows = csvLoader.Load(options.CsvPath);
                    return Task.CompletedTask;
                });

            AnsiConsole.Write(new Panel(
                $"[bold]CSV Rows[/]: [green]{csvRows.Count}[/]\n" +
                $"[bold]CSV Path[/]: [grey]{options.CsvPath.EscapeMarkup()}[/]")
                .Border(BoxBorder.Rounded)
                .Header("[bold]Input[/]"));

            var adoClient = new AdoClient(config.OrganizationUrl, pat);
            WorkItemIndex index = new()
            {
                ByMicrosoftId = new Dictionary<string, AdoWorkItemRecord>(),
                Warnings = new List<string>(),
                DuplicateMicrosoftIds = new Dictionary<string, List<DuplicateWorkItemInfo>>(),
                QueriedWorkItemCount = 0,
                RetrievedWorkItemCount = 0,
                MissingMicrosoftIdCount = 0,
            };

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Star)
                .StartAsync("[blue]Reading Azure DevOps work items...[/]", async _ =>
                {
                    index = await WorkItemIndex.LoadAsync(
                        adoClient,
                        config.Project,
                        config.AreaPathRoot,
                        Array.Empty<string>(),
                        config.FieldMap,
                        CancellationToken.None);
                });

            AnsiConsole.Write(new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Metric")
                .AddColumn("Value")
                .AddRow("Queried", $"[green]{index.QueriedWorkItemCount}[/]")
                .AddRow("Retrieved", $"[green]{index.RetrievedWorkItemCount}[/]")
                .AddRow("Missing Microsoft ID", $"[yellow]{index.MissingMicrosoftIdCount}[/]")
                .AddRow("Indexed", $"[green]{index.ByMicrosoftId.Count}[/]"));

            if (index.ByMicrosoftId.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No existing rows were indexed by Microsoft ID. Check Ado:AreaPathRoot and FieldMap for 'Microsoft ID'.");
            }

            foreach (string warning in index.Warnings)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {warning.EscapeMarkup()}");
            }

            string? duplicateReportSummary = null;
            if (index.DuplicateMicrosoftIds.Count > 0)
            {
                duplicateReportSummary = await WriteDuplicateReportArtifactsAsync(index.DuplicateMicrosoftIds);
                AnsiConsole.MarkupLine($"[yellow]Duplicate report generated:[/] {duplicateReportSummary.EscapeMarkup()}");
            }

            if (options.Backup)
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string backupDirectory = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder, BackupsFolder);
                Directory.CreateDirectory(backupDirectory);

                var backupRecords = index.ByMicrosoftId.Values
                    .OrderBy(x => x.Id)
                    .ToList();

                string jsonBackupPath = Path.Combine(backupDirectory, $"ado-backup-{stamp}.json");
                string csvBackupPath = Path.Combine(backupDirectory, $"ado-backup-{stamp}.csv");

                await File.WriteAllTextAsync(
                    jsonBackupPath,
                    JsonSerializer.Serialize(backupRecords, new JsonSerializerOptions { WriteIndented = true }));
                await WriteBackupCsvAsync(backupRecords, csvBackupPath);

                AnsiConsole.MarkupLine($"Backup written to [blue]{jsonBackupPath.EscapeMarkup()}[/] and [blue]{csvBackupPath.EscapeMarkup()}[/]");
            }

            var differ = new Differ();
            ChangeSet changes = differ.Compare(csvRows, index.ByMicrosoftId, config.FieldMap, config.DefaultsWhenCsvNull);

            var reviewer = new ConsoleReviewer();
            reviewer.PrintSummary(changes);
            bool applyFromMenu = false;

            foreach (string info in changes.InformationalMessages.Take(10))
            {
                AnsiConsole.MarkupLine($"[grey]{info.EscapeMarkup()}[/]");
            }

            if (options.ApplyPlan)
            {
                string planPath = ResolveDecisionPlanPath(options.ApplyPlanPath);
                DecisionPlan loadedPlan = await LoadDecisionPlanAsync(planPath);
                ApplyDecisionsFromPlan(changes, loadedPlan);
                AnsiConsole.MarkupLine($"Loaded decision plan: [blue]{planPath.EscapeMarkup()}[/]");
            }
            else
            {
                MainMenuResult mainMenuResult = RunMainMenu(changes, index.DuplicateMicrosoftIds, duplicateReportSummary, reviewer);
                if (mainMenuResult == MainMenuResult.Quit)
                {
                    AnsiConsole.MarkupLine("[yellow]Exited from main menu. No updates were applied.[/]");
                    return 0;
                }

                if (mainMenuResult == MainMenuResult.Apply)
                {
                    applyFromMenu = true;
                }
            }

            if (options.BuildPlan)
            {
                string planPath = await WriteDecisionPlanAsync(changes, options, config);
                AnsiConsole.MarkupLine($"Decision plan written to [blue]{planPath.EscapeMarkup()}[/]");
            }

            int approved = changes.ApprovedChanges.Count();
            AnsiConsole.MarkupLine($"Approved changes: [green]{approved}[/]");

            bool executeWrites = !options.DryRun && !options.BuildPlan;
            bool confirmed = options.ApplyPlan || applyFromMenu || reviewer.ConfirmApply(approved);

            var runLog = new RunLog
            {
                StartedAtUtc = DateTimeOffset.UtcNow,
                CsvPath = options.CsvPath,
                DryRun = !executeWrites,
                Operations = new List<RunLogOperation>(),
            };

            if (approved > 0 && (!executeWrites || confirmed))
            {
                var applier = new Applier();
                if (executeWrites)
                {
                    await AnsiConsole.Progress()
                        .AutoClear(false)
                        .StartAsync(async ctx =>
                        {
                            ProgressTask applyTask = ctx.AddTask("[green]Applying approved changes...[/]", maxValue: approved);

                            await applier.ApplyApprovedChangesAsync(
                                adoClient,
                                config,
                                changes,
                                index.ByMicrosoftId,
                                dryRun: false,
                                runLog.Operations,
                                CancellationToken.None,
                                maxUpdateConcurrency: 8,
                                onProgress: (done, total, label) =>
                                {
                                    applyTask.MaxValue = total;
                                    applyTask.Value = done;
                                    string safeLabel = Markup.Escape(TruncateProgressLabel(label, 48));
                                    applyTask.Description = $"[green]Applying[/] {done}/{total} [grey]({safeLabel})[/]";
                                });
                        });
                }
                else
                {
                    await applier.ApplyApprovedChangesAsync(
                        adoClient,
                        config,
                        changes,
                        index.ByMicrosoftId,
                        dryRun: true,
                        runLog.Operations,
                        CancellationToken.None,
                        maxUpdateConcurrency: 8);
                }
            }
            else
            {
                foreach (Change change in changes.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update))
                {
                    runLog.Operations.Add(new RunLogOperation
                    {
                        MicrosoftId = change.MicrosoftId,
                        Kind = change.Kind,
                        AdoId = change.AdoId,
                        Approved = change.Approved,
                        Success = !change.Approved,
                        Message = change.Approved ? "Approval pending final confirmation" : "Not approved",
                        Fields = change.Deltas,
                    });
                }
            }

            runLog.FinishedAtUtc = DateTimeOffset.UtcNow;
            string runLogFile = $"run-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            string runLogDirectory = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder, RunLogsFolder);
            Directory.CreateDirectory(runLogDirectory);
            string runLogPath = Path.Combine(runLogDirectory, runLogFile);
            await File.WriteAllTextAsync(runLogPath, JsonSerializer.Serialize(runLog, new JsonSerializerOptions { WriteIndented = true }));

            int succeeded = runLog.Operations.Count(x => x.Success);
            int failed = runLog.Operations.Count(x => !x.Success && x.Approved);
            AnsiConsole.Write(new Panel(
                $"[bold]Run log[/]: [blue]{runLogPath.EscapeMarkup()}[/]\n" +
                $"[bold]Succeeded[/]: [green]{succeeded}[/]\n" +
                $"[bold]Failed[/]: [red]{failed}[/]")
                .Border(BoxBorder.Rounded)
                .Header("[bold]Run Complete[/]"));

            return 0;
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            PrintHelp();
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }
    }

    private static void PrintBanner()
    {
        var figlet = new FigletText("BPC Updater")
            .LeftJustified()
            .Color(Color.DeepSkyBlue2);

        AnsiConsole.Write(figlet);
        AnsiConsole.Write(new Rule("[grey]Azure DevOps Catalog Reconciliation[/]").RuleStyle("grey"));
    }

    private static void PrintRunMode(Options options)
    {
        string mode = options.ApplyPlanPath is not null
            ? "Apply Plan"
            : options.BuildPlan
                ? "Build Plan"
                : options.DryRun
                    ? "Dry Run"
                    : "Interactive Apply";

        AnsiConsole.Write(new Panel(
            $"[bold]Mode[/]: [deepskyblue2]{mode}[/]\n" +
            $"[bold]Config[/]: [grey]{options.ConfigPath.EscapeMarkup()}[/]")
            .Border(BoxBorder.Rounded)
            .Header("[bold]Session[/]"));
    }

    private static MainMenuResult RunMainMenu(
        ChangeSet changes,
        IReadOnlyDictionary<string, List<DuplicateWorkItemInfo>> duplicateItems,
        string? duplicateReportSummary,
        ConsoleReviewer reviewer)
    {
        int addCount = changes.Changes.Count(x => x.Kind == ChangeKind.Add);
        int updateCount = changes.Changes.Count(x => x.Kind == ChangeKind.Update);
        int duplicateCount = duplicateItems.Count;

        bool addsComplete = addCount == 0;
        bool updatesComplete = updateCount == 0;
        bool duplicatesComplete = duplicateCount == 0;

        while (true)
        {
            AnsiConsole.Clear();

            RenderMainMenuHeader();

            var statusTable = new Table().Border(TableBorder.Rounded).Title("[bold]Review Status[/]");
            statusTable.AddColumn("Section");
            statusTable.AddColumn("Count");
            statusTable.AddColumn("Status");
            statusTable.AddRow("Adds", addCount.ToString(), addsComplete ? "[green]Complete[/]" : "[yellow]Not Complete[/]");
            statusTable.AddRow("Updates", updateCount.ToString(), updatesComplete ? "[green]Complete[/]" : "[yellow]Not Complete[/]");
            statusTable.AddRow("Duplicates", duplicateCount.ToString(), duplicateCount == 0 ? "[green]None[/]" : duplicatesComplete ? "[green]Acknowledged[/]" : "[yellow]Pending[/]");
            AnsiConsole.Write(statusTable);

            bool canApply = addsComplete && updatesComplete && duplicatesComplete;
            const string reviewAddsAction = "review-adds";
            const string reviewUpdatesAction = "review-updates";
            const string acknowledgeDuplicatesAction = "acknowledge-duplicates";
            const string applyAction = "apply";
            const string quitAction = "quit";

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Main Menu[/]")
                    .AddChoices(
                        reviewAddsAction,
                        reviewUpdatesAction,
                        acknowledgeDuplicatesAction,
                        applyAction,
                        quitAction)
                    .UseConverter(action => action switch
                    {
                        reviewAddsAction => $"Review Adds ({addCount}) - {FormatCompletion(addsComplete)}",
                        reviewUpdatesAction => $"Review Updates ({updateCount}) - {FormatCompletion(updatesComplete)}",
                        acknowledgeDuplicatesAction => $"Acknowledge Duplicates ({duplicateCount}) - {FormatCompletion(duplicatesComplete)}",
                        applyAction => canApply
                            ? "[green]Apply Approved Updates - Ready[/]"
                            : "[red]Apply Approved Updates - Blocked[/]",
                        quitAction => "[grey]Quit[/]",
                        _ => action,
                    }));

            if (choice == reviewAddsAction)
            {
                reviewer.Review(changes, x => x.Kind == ChangeKind.Add, "Adds");
                addsComplete = true;
                continue;
            }

            if (choice == reviewUpdatesAction)
            {
                reviewer.Review(changes, x => x.Kind == ChangeKind.Update, "Updates");
                updatesComplete = true;
                continue;
            }

            if (choice == acknowledgeDuplicatesAction)
            {
                duplicatesComplete = ReviewDuplicates(duplicateItems, duplicateReportSummary);

                continue;
            }

            if (choice == applyAction)
            {
                if (!canApply)
                {
                    AnsiConsole.MarkupLine("[yellow]Apply is blocked.[/] Complete Adds, Updates, and duplicate acknowledgment first.");
                    continue;
                }

                return MainMenuResult.Apply;
            }

            return MainMenuResult.Quit;
        }
    }

    private static void RenderMainMenuHeader()
    {
        var figlet = new FigletText("BPC Updater")
            .LeftJustified()
            .Color(Color.DeepSkyBlue2);

        AnsiConsole.Write(figlet);
        AnsiConsole.Write(new Rule("[grey]Main Menu[/]").RuleStyle("grey"));

        AnsiConsole.Write(new Panel(
            "[bold deepskyblue2]Application Menu[/]\n" +
            "Review changes, acknowledge duplicates, then apply updates or quit.")
            .Border(BoxBorder.Double)
            .Header("[bold]Application[/]"));
    }

    private static bool ReviewDuplicates(
        IReadOnlyDictionary<string, List<DuplicateWorkItemInfo>> duplicateItems,
        string? duplicateReportSummary)
    {
        AnsiConsole.Clear();
        RenderMainMenuHeader();

        if (duplicateItems.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No duplicates found.[/]");
            return true;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Duplicate Review[/]");
        table.AddColumn("Microsoft ID");
        table.AddColumn("ADO ID");
        table.AddColumn("Title");

        foreach ((string microsoftId, List<DuplicateWorkItemInfo> items) in duplicateItems
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (DuplicateWorkItemInfo item in items.OrderBy(x => x.AdoId))
            {
                table.AddRow(
                    Markup.Escape(microsoftId),
                    item.AdoId.ToString(),
                    Markup.Escape(item.Title ?? string.Empty));
            }
        }

        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(duplicateReportSummary))
        {
            AnsiConsole.MarkupLine($"[grey]Artifact report:[/] {duplicateReportSummary.EscapeMarkup()}");
        }

        return AnsiConsole.Confirm("Acknowledge these duplicates and return to the main menu?", defaultValue: true);
    }

    private static string FormatCompletion(bool complete)
    {
        return complete ? "Complete" : "Not Complete";
    }

    private static async Task<string> WriteDuplicateReportArtifactsAsync(Dictionary<string, List<DuplicateWorkItemInfo>> duplicates)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder, DuplicatesFolder);
        Directory.CreateDirectory(directory);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string jsonPath = Path.Combine(directory, $"duplicate-report-{stamp}.json");
        string csvPath = Path.Combine(directory, $"duplicate-report-{stamp}.csv");

        var normalized = duplicates
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                MicrosoftId = x.Key,
                Items = x.Value
                    .OrderBy(item => item.AdoId)
                    .Select(item => new
                    {
                        item.AdoId,
                        item.Title,
                    })
                    .ToList(),
            })
            .ToList();

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));

        var csvLines = new List<string> { "MicrosoftId,AdoId,Title" };
        foreach (var row in normalized)
        {
            foreach (var item in row.Items)
            {
                csvLines.Add($"\"{EscapeCsv(row.MicrosoftId)}\",\"{item.AdoId}\",\"{EscapeCsv(item.Title ?? string.Empty)}\"");
            }
        }

        await File.WriteAllLinesAsync(csvPath, csvLines);
        return $"{jsonPath} | {csvPath}";
    }

    private static async Task WriteBackupCsvAsync(IReadOnlyList<AdoWorkItemRecord> records, string csvPath)
    {
        var fieldColumns = records
            .SelectMany(x => x.Fields.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            string.Join(",", new[]
            {
                "Id",
                "Rev",
                "MicrosoftId",
                "WorkItemType",
            }.Concat(fieldColumns).Select(x => $"\"{EscapeCsv(x)}\"")),
        };

        foreach (AdoWorkItemRecord record in records)
        {
            var row = new List<string>
            {
                record.Id.ToString(),
                record.Rev.ToString(),
                record.MicrosoftId,
                record.WorkItemType,
            };

            foreach (string field in fieldColumns)
            {
                row.Add(record.Fields.TryGetValue(field, out string? value) ? value ?? string.Empty : string.Empty);
            }

            lines.Add(string.Join(",", row.Select(x => $"\"{EscapeCsv(x)}\"")));
        }

        await File.WriteAllLinesAsync(csvPath, lines);
    }

    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static IConfiguration BuildConfiguration(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Config file was not found", configPath);
        }

        return new ConfigurationBuilder()
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "BPC_")
            .Build();
    }

    private sealed class Options
    {
        public required string CsvPath { get; init; }
        public required string ConfigPath { get; init; }
        public required bool DryRun { get; init; }
        public required bool Backup { get; init; }
        public required bool BuildPlan { get; init; }
        public required bool ApplyPlan { get; init; }
        public required string? ApplyPlanPath { get; init; }
        public required bool ShowHelp { get; init; }

        public static Options Parse(string[] args)
        {
            string csvPath = Path.Combine(Environment.CurrentDirectory, "Business Process Catalog MAR 2026.csv");
            string configPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            bool dryRun = false;
            bool backup = false;
            bool buildPlan = false;
            bool applyPlan = false;
            string? applyPlanPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--csv":
                        csvPath = NextValue(args, ref i, "--csv");
                        break;
                    case "--config":
                        configPath = NextValue(args, ref i, "--config");
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--build-plan":
                        buildPlan = true;
                        dryRun = true;
                        break;
                    case "--apply-plan":
                        applyPlan = true;
                        if (TryReadOptionalValue(args, i, out string? optionalPath))
                        {
                            i++;
                            applyPlanPath = optionalPath;
                        }
                        break;
                    case "--backup":
                        backup = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        return new Options
                        {
                            CsvPath = Path.GetFullPath(csvPath),
                            ConfigPath = Path.GetFullPath(configPath),
                            DryRun = dryRun,
                            Backup = backup,
                            BuildPlan = buildPlan,
                            ApplyPlan = applyPlan,
                            ApplyPlanPath = applyPlanPath is null ? null : Path.GetFullPath(applyPlanPath),
                            ShowHelp = true,
                        };
                    default:
                        throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }

            if (buildPlan && applyPlanPath is not null)
            {
                throw new ArgumentException("--build-plan and --apply-plan cannot be used together.");
            }

            if (buildPlan && applyPlan)
            {
                throw new ArgumentException("--build-plan and --apply-plan cannot be used together.");
            }

            return new Options
            {
                CsvPath = Path.GetFullPath(csvPath),
                ConfigPath = Path.GetFullPath(configPath),
                DryRun = dryRun,
                Backup = backup,
                BuildPlan = buildPlan,
                ApplyPlan = applyPlan,
                ApplyPlanPath = applyPlanPath is null ? null : Path.GetFullPath(applyPlanPath),
                ShowHelp = false,
            };
        }

        private static string NextValue(string[] args, ref int index, string key)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {key}");
            }

            index++;
            return args[index];
        }

        private static bool TryReadOptionalValue(string[] args, int index, out string? value)
        {
            if (index + 1 >= args.Length)
            {
                value = null;
                return false;
            }

            string candidate = args[index + 1];
            if (candidate.StartsWith("--", StringComparison.Ordinal))
            {
                value = null;
                return false;
            }

            value = candidate;
            return true;
        }
    }

    private static async Task<string> WriteDecisionPlanAsync(ChangeSet changes, Options options, AppConfig config)
    {
        var reviewable = changes.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update).ToList();
        var decisionPlan = new DecisionPlan
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CsvPath = options.CsvPath,
            Project = config.Project,
            AreaPathRoot = config.AreaPathRoot,
            Decisions = reviewable
                .Select(x => new DecisionPlanItem
                {
                    MicrosoftId = x.MicrosoftId,
                    Kind = x.Kind,
                    Approved = x.Approved,
                })
                .ToList(),
        };

        string planDirectory = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder, DecisionPlansFolder);
        Directory.CreateDirectory(planDirectory);
        string planPath = Path.Combine(planDirectory, $"decision-plan-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(decisionPlan, new JsonSerializerOptions { WriteIndented = true }));
        return planPath;
    }

    private static async Task<DecisionPlan> LoadDecisionPlanAsync(string planPath)
    {
        if (!File.Exists(planPath))
        {
            throw new FileNotFoundException("Decision plan file not found", planPath);
        }

        string json = await File.ReadAllTextAsync(planPath);
        DecisionPlan? plan = JsonSerializer.Deserialize<DecisionPlan>(json);
        return plan ?? throw new InvalidOperationException("Decision plan file is invalid or empty.");
    }

    private static void ApplyDecisionsFromPlan(ChangeSet changes, DecisionPlan plan)
    {
        var decisionsByKey = plan.Decisions.ToDictionary(
            x => $"{x.MicrosoftId}::{x.Kind}",
            x => x,
            StringComparer.OrdinalIgnoreCase);

        int matched = 0;
        int unmatched = 0;

        foreach (Change change in changes.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update))
        {
            string key = $"{change.MicrosoftId}::{change.Kind}";
            if (decisionsByKey.TryGetValue(key, out DecisionPlanItem? decision))
            {
                change.Approved = decision.Approved;
                matched++;
            }
            else
            {
                change.Approved = false;
                unmatched++;
            }
        }

        AnsiConsole.MarkupLine($"Applied decisions from plan: matched [green]{matched}[/], default-skipped [yellow]{unmatched}[/].");
    }

    private static string ResolveDecisionPlanPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        string planDirectory = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder, DecisionPlansFolder);
        if (!Directory.Exists(planDirectory))
        {
            throw new DirectoryNotFoundException($"Decision plan folder not found: {planDirectory}");
        }

        string? latestPlan = Directory
            .GetFiles(planDirectory, "decision-plan-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestPlan is null)
        {
            throw new FileNotFoundException("No decision plan files found in artifacts/decision-plans.");
        }

        return latestPlan;
    }

    private static void CleanupArtifacts(Options options)
    {
        string artifactsRoot = Path.Combine(Environment.CurrentDirectory, ArtifactsFolder);
        Directory.CreateDirectory(artifactsRoot);

        ResetDirectory(Path.Combine(artifactsRoot, BackupsFolder));
        ResetDirectory(Path.Combine(artifactsRoot, RunLogsFolder));
        ResetDirectory(Path.Combine(artifactsRoot, DuplicatesFolder));

        if (options.BuildPlan)
        {
            // Build-plan writes a fresh review artifact set for the next apply step.
            ResetDirectory(Path.Combine(artifactsRoot, DecisionPlansFolder));
        }
    }

    private static void ResetDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            ClearDirectoryContents(directoryPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not clean artifact folder '{directoryPath.EscapeMarkup()}': {ex.Message.EscapeMarkup()}");
        }
    }

    private static void ClearDirectoryContents(string directoryPath)
    {
        foreach (string file in Directory.GetFiles(directoryPath))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not delete file '{file.EscapeMarkup()}': {ex.Message.EscapeMarkup()}");
            }
        }

        foreach (string directory in Directory.GetDirectories(directoryPath))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not delete directory '{directory.EscapeMarkup()}': {ex.Message.EscapeMarkup()}");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BPC Azure DevOps Updater");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/BpcAdoUpdater -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --csv <path>         Input catalog CSV path.");
        Console.WriteLine("  --config <path>      Configuration file path (default: appsettings.json).");
        Console.WriteLine("  --dry-run            Review/log only; no writes.");
        Console.WriteLine("  --backup             Export indexed ADO records to artifacts/backups.");
        Console.WriteLine("  --build-plan         Interactive review that writes a decision plan file (implies dry-run).");
        Console.WriteLine("  --apply-plan [path]  Applies approvals/skips from a decision plan file.");
        Console.WriteLine("                        If path is omitted, latest artifacts/decision-plans file is used.");
        Console.WriteLine("                        Updates are batched with bounded parallelism for speed.");
        Console.WriteLine("  --help, -h, /?       Show this help and exit.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project src/BpcAdoUpdater -- --csv \"Business Process Catalog MAR 2026.csv\" --config \"appsettings.json\" --build-plan --backup");
        Console.WriteLine("  dotnet run --project src/BpcAdoUpdater -- --csv \"Business Process Catalog MAR 2026.csv\" --config \"appsettings.json\" --apply-plan \"artifacts/decision-plans/decision-plan-YYYYMMDD-HHMMSS.json\" --backup");
    }

    private static string TruncateProgressLabel(string? label, int max)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        string value = label.Replace("\r", " ").Replace("\n", " ");
        return value.Length <= max ? value : value[..(max - 3)] + "...";
    }

    private enum MainMenuResult
    {
        Apply,
        Quit,
    }
}
