using BpcAdoUpdater.Models;
using Spectre.Console;

namespace BpcAdoUpdater.Review;

public sealed class ConsoleReviewer
{
    public void PrintSummary(ChangeSet changeSet)
    {
        int adds = changeSet.Changes.Count(x => x.Kind == ChangeKind.Add);
        int updates = changeSet.Changes.Count(x => x.Kind == ChangeKind.Update);
        int unchanged = changeSet.Changes.Count(x => x.Kind == ChangeKind.Unchanged);
        int custom = changeSet.Changes.Count(x => x.Kind == ChangeKind.Update && x.IsCustomerModified);

        var workItemTypeTable = new Table().Border(TableBorder.Rounded).Title("[bold]By Work Item Type[/]");
        workItemTypeTable.AddColumn("Type");
        workItemTypeTable.AddColumn("Adds");
        workItemTypeTable.AddColumn("Updates");

        foreach (IGrouping<string, Change> group in changeSet.Changes
                     .Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update)
                     .GroupBy(x => x.Row.WorkItemType ?? "(unknown)")
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            workItemTypeTable.AddRow(
                group.Key,
                group.Count(x => x.Kind == ChangeKind.Add).ToString(),
                group.Count(x => x.Kind == ChangeKind.Update).ToString());
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Metric");
        table.AddColumn("Count");
        table.AddRow("Adds", $"[green]{adds}[/]");
        table.AddRow("Updates", $"[yellow]{updates}[/]");
        table.AddRow("Unchanged", $"[grey]{unchanged}[/]");
        table.AddRow("Customer-modified updates", $"[red]{custom}[/]");

        AnsiConsole.Write(new Rule("[bold]Diff Summary[/]").RuleStyle("deepskyblue2"));
        AnsiConsole.Write(table);
        if (workItemTypeTable.Rows.Count > 0)
        {
            AnsiConsole.Write(workItemTypeTable);
        }
    }

    public void Review(ChangeSet changeSet)
    {
        Review(changeSet, _ => true, "All Changes");
    }

    public void Review(ChangeSet changeSet, Func<Change, bool> filter, string sectionName)
    {
        var reviewable = changeSet.Changes
            .Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update)
            .Where(filter)
            .ToList();
        bool compactAddView = sectionName.Equals("Adds", StringComparison.OrdinalIgnoreCase);

        if (reviewable.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No items to review in section '{Markup.Escape(sectionName)}'.[/]");
            AnsiConsole.MarkupLine("Press Enter to return to the menu.");
            Console.ReadLine();
            return;
        }

        bool approveAll = false;
        bool skipAll = false;

        for (int i = 0; i < reviewable.Count; i++)
        {
            Change change = reviewable[i];

            if (approveAll)
            {
                change.Approved = true;
                continue;
            }

            if (skipAll)
            {
                change.Approved = false;
                continue;
            }

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(sectionName)}[/]").RuleStyle("deepskyblue2"));
            RenderChange(change, i + 1, reviewable.Count, compactAddView);
            string decision = PromptDecisionWithShortcuts();

            switch (decision)
            {
                case "Approve":
                    change.Approved = true;
                    break;
                case "Skip":
                    change.Approved = false;
                    break;
                case "Approve all remaining":
                    change.Approved = true;
                    approveAll = true;
                    break;
                case "Skip all remaining":
                    change.Approved = false;
                    skipAll = true;
                    break;
                case "Quit review":
                    return;
                default:
                    change.Approved = false;
                    break;
            }
        }
    }

    public bool ConfirmApply(int approvedCount)
    {
        if (approvedCount == 0)
        {
            return false;
        }

        string value = AnsiConsole.Prompt(new TextPrompt<string>($"Type 'yes' to apply {approvedCount} approved changes")
            .AllowEmpty());
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void RenderChange(Change change, int index, int total, bool compactAddView)
    {
        string title = change.Row.EffectiveTitle ?? "(no title)";
        string kindColor = change.Kind == ChangeKind.Add ? "green" : "yellow";
        string warningLine = change.IsCustomerModified
            ? "\n[red]Customer-modified row: incoming values may overwrite customizations.[/]"
            : string.Empty;
        int completed = Math.Max(0, index - 1);

        AnsiConsole.Write(new Panel(BuildProgressMarkup(index, total, completed))
            .Border(BoxBorder.Rounded)
            .Header("[bold]Review Progress[/]"));
        AnsiConsole.Write(new Panel(
            $"[bold]Kind[/]: [{kindColor}]{change.Kind}[/]\n" +
            $"[bold]Microsoft ID[/]: [deepskyblue2]{change.MicrosoftId.EscapeMarkup()}[/]\n" +
            $"[bold]Title[/]: {title.EscapeMarkup()}" +
            warningLine)
            .Border(BoxBorder.Rounded)
            .Header("[bold]Candidate Change[/]"));

        if (compactAddView && change.Kind == ChangeKind.Add)
        {
            var addTable = new Table().Border(TableBorder.Rounded).Title("[bold]Add Preview[/]");
            addTable.AddColumn("Field");
            addTable.AddColumn("Value");
            addTable.AddRow("Microsoft ID", Markup.Escape(change.MicrosoftId));
            addTable.AddRow("Title", Markup.Escape(Truncate(title, 120)));
            addTable.AddRow("Description (first 200 chars)", Markup.Escape(Truncate(change.Row.Description, 200)));
            AnsiConsole.Write(addTable);
            return;
        }

        var table = new Table().Border(TableBorder.DoubleEdge).Title("[bold]Field Deltas[/]");
        table.ShowRowSeparators();
        table.AddColumn("Field");
        table.AddColumn("Current");
        table.AddColumn("Proposed");

        foreach (FieldDelta delta in change.Deltas)
        {
            table.AddRow(
                Truncate(delta.CsvFieldName, 35),
                Markup.Escape(Truncate(delta.OldValue, 80)),
                Markup.Escape(Truncate(delta.NewValue, 80)));
        }

        AnsiConsole.Write(table);
    }

    private static string PromptDecisionWithShortcuts()
    {
        AnsiConsole.MarkupLine("[grey]Shortcuts: [green]A[/]=Approve, [yellow]S[/]=Skip, [green]R[/]=Approve all remaining, [yellow]K[/]=Skip all remaining, [red]Q[/]=Quit, [deepskyblue2]Enter[/]=Arrow menu[/]");

        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        switch (key.Key)
        {
            case ConsoleKey.A:
                return "Approve";
            case ConsoleKey.S:
                return "Skip";
            case ConsoleKey.R:
                return "Approve all remaining";
            case ConsoleKey.K:
                return "Skip all remaining";
            case ConsoleKey.Q:
                return "Quit review";
            case ConsoleKey.Enter:
                return PromptDecisionWithArrows();
            default:
                AnsiConsole.MarkupLine("[grey]Unknown key. Opening arrow menu...[/]");
                return PromptDecisionWithArrows();
        }
    }

    private static string PromptDecisionWithArrows()
    {
        return AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[bold]Choose action[/]")
            .AddChoices(
                "Approve",
                "Skip",
                "Approve all remaining",
                "Skip all remaining",
                "Quit review"));
    }

    private static string Truncate(string? value, int max)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "<null>" : value.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= max ? text : text[..(max - 3)] + "...";
    }

    private static string BuildProgressMarkup(int index, int total, int completed)
    {
        double ratio = total == 0 ? 0 : (double)completed / total;
        int percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        const int barWidth = 32;
        int filled = (int)Math.Round(ratio * barWidth, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, barWidth);

        string bar = new string('=', filled) + new string('-', barWidth - filled);
        return $"[bold]Item[/]: [deepskyblue2]{index}[/] of [deepskyblue2]{total}[/]\n" +
               $"[bold]Completed[/]: [green]{completed}[/]  [bold]Remaining[/]: [yellow]{Math.Max(0, total - completed)}[/]\n" +
               $"[grey][[{bar}]][/] [bold]{percent}%[/]";
    }
}
