# BPC Azure DevOps Updater

C# console application for reconciling the Microsoft quarterly Business Process Catalog CSV with an existing Azure DevOps project.

The tool:
- matches records by Microsoft ID (strict)
- updates existing work items when mapped fields differ
- adds missing work items
- supports interactive review before any write
- supports an interactive main menu (review, apply, quit)
- supports decision plan files for safe two-step review/apply
- supports dry-run mode and optional backup export
- writes a JSON run log for traceability

## Quick Start (5 Minutes)

1. Build the tool:

```powershell
dotnet restore BpcAdoUpdater.sln
dotnet build BpcAdoUpdater.sln -c Debug
```

2. Create local config (first time only):

```powershell
Copy-Item appsettings.example.json appsettings.json
```

3. Provide PAT (recommended: environment variable, optional: appsettings fallback):

```powershell
$env:ADO_PAT = "<your-pat>"
```

Optional fallback for testing: set Ado:Pat in appsettings.json.

4. Edit appsettings.json values:
- Ado:OrganizationUrl
- Ado:Project
- Ado:AreaPathRoot
- Ado:Pat (optional testing fallback)
- FieldMap entries for your ADO process

5. Run interactive flow with backup (review + apply from same run):

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --backup
```

6. In the main menu:
- Review Adds
- Review Updates
- Acknowledge Duplicates (if any)
- Apply Approved Updates (enabled only when required review steps are complete)
- Quit (exit with no changes)

7. Optional two-step controlled release: build decision plan (review + save approvals/skips, no writes):

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --build-plan --backup
```

8. Apply using the saved decision plan file:

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --apply-plan "artifacts/decision-plans/decision-plan-YYYYMMDD-HHMMSS.json" --backup
```

Or let the app automatically use the latest decision plan:

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --apply-plan --backup
```

9. The app applies only approved items from the plan and skips planned skips.

## What This Tool Is For

Use this when Microsoft releases a new catalog file (for example, Business Process Catalog MAR 2026.csv) and you need your Azure DevOps work items aligned to that release while still reviewing what changes are about to be applied.

## Prerequisites

1. Windows with .NET SDK 8 installed.
2. Existing Azure DevOps project that already contains the Business Process Catalog structure.
3. A Personal Access Token (PAT) with work item read/write permissions.
4. Correct field reference names in your process template (configured in appsettings.json).

## Project Structure

- src/BpcAdoUpdater: console application
- tests/BpcAdoUpdater.Tests: unit tests
- appsettings.json: runtime configuration (local)
- appsettings.example.json: template for configuration

## Setup Steps

1. Build the solution:

```powershell
dotnet restore BpcAdoUpdater.sln
dotnet build BpcAdoUpdater.sln -c Debug
```

2. Configure Azure DevOps connection and mappings in appsettings.json.

If needed, start from the template:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

3. Set your PAT.

Preferred: environment variable (default name ADO_PAT, configurable in Ado:PatEnvironmentVariableName).

```powershell
$env:ADO_PAT = "<your-pat>"
```

Fallback: set Ado:Pat in appsettings.json (the app warns when this is used).

4. Validate mappings before first real run.

Confirm all configured field names exist in your Azure DevOps process and match exact reference names.

5. Run unit tests:

```powershell
dotnet test BpcAdoUpdater.sln -c Debug
```

## Configuration Reference (appsettings.json)

### Ado section

- OrganizationUrl: Azure DevOps org URL (for example https://dev.azure.com/your-org)
- Project: target project name
- AreaPathRoot: root area path to query/update under
- DefaultIterationPath: fallback iteration path for newly created items
- PatEnvironmentVariableName: env var name used to read PAT
- Pat: optional fallback PAT in config (used only when env var is empty)

### FieldMap section

Maps CSV column names to Azure DevOps field reference names.

Examples:
- Microsoft ID -> Custom.MicrosoftID
- Title -> System.Title
- Description -> System.Description
- Catalog status -> Custom.CatalogStatus

Important: if these names do not match your process template, updates will fail.

### WorkItemTypeMap section

Maps CSV Work item type values to actual ADO work item types.

Example:
- End to end -> Feature
- Process area -> Feature
- Process -> Feature
- Scenario -> User Story

Note: add operations are ordered by mapped work item type order and hierarchy depth so parents are created before children.

### DefaultsWhenCsvNull section

Optional per-field defaults used when CSV value is null/blank.

Example:
- Business process flow status -> 10 - Not started

Behavior:
- if CSV is null and ADO already has the configured default, no diff is shown
- if CSV is null and ADO has a different value, proposed value becomes the configured default
- if no default is configured, normal null/clear behavior applies

## User Manual

### Standard operating flow

1. Prepare:
- get latest Microsoft CSV
- verify appsettings.json
- set PAT env var

2. Run interactive review with backup:

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --backup
```

3. Review console output:
- summary counts (adds, updates, unchanged)
- warnings (duplicates, customer-modified rows)
- informational rows present in ADO but not in CSV

4. Use the main menu:
- Review Adds
- Review Updates
- Acknowledge Duplicates
- Apply Approved Updates
- Quit

Duplicate review behavior:
- selecting Acknowledge Duplicates opens a dedicated review screen
- the screen lists duplicate rows in a table with Microsoft ID, ADO ID, and Title
- acknowledge the duplicates after reviewing which ADO items need cleanup

5. Inside review screens, use controls:
- a = approve current change
- s = skip current change
- A = approve all remaining
- S = skip all remaining
- q = quit review section

6. Optional two-step plan/apply mode:

Build plan:

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --build-plan --backup
```

Apply plan:

```powershell
dotnet run --project src/BpcAdoUpdater -- --csv "Business Process Catalog MAR 2026.csv" --config "appsettings.json" --apply-plan "artifacts/decision-plans/decision-plan-YYYYMMDD-HHMMSS.json" --backup
```

7. Validate outcome:
- check artifacts/run-logs/run-log-YYYYMMDD-HHMMSS.json
- spot check updated work items in Azure DevOps

8. Keep run logs/backups (and decision plan file if used) for release traceability.

### Command options

- --csv <path>: input catalog CSV path
- --config <path>: config file path
- --dry-run: review and logging only, no API writes
- --build-plan: interactive review that writes a decision plan file (implies no API writes)
- --apply-plan [path]: applies approvals/skips from a saved decision plan file; if omitted, latest plan in artifacts/decision-plans is used
- --backup: export indexed ADO records before apply
- --help, -h, /?: show command help and examples

Interactive mode note:
- In default interactive mode (without --build-plan/--apply-plan), apply can be executed directly from the main menu after required review steps are complete.

## Behavior Rules Implemented

- Strict matching key: Microsoft ID only
- Existing row changed: update only mapped fields that differ
- Missing row in ADO: create new work item
- Adds are ordered parent-first by WorkItemTypeMap order and process sequence depth
- Parent links for adds are resolved from process sequence hierarchy and CSV Microsoft ID mapping
- Deprecated/Deleted catalog rows: diff flow limits to Catalog status update handling
- Rows in ADO but not in CSV: informational only, no delete
- Description comparison normalizes whitespace and HTML/entity formatting to reduce false positives

## Outputs

### Run log

A run log JSON file is generated in the working directory:
- artifacts/run-logs/run-log-YYYYMMDD-HHMMSS.json

It records:
- operation type (add/update)
- Microsoft ID
- approval status
- success/failure
- message/error
- field deltas

Note: artifacts/run-logs and artifacts/backups are cleared at the start of each run.

### Decision plan

When --build-plan is used, a file is generated:
- artifacts/decision-plans/decision-plan-YYYYMMDD-HHMMSS.json

It records:
- Microsoft ID
- change kind (Add/Update)
- approved or skipped decision

Note: when running --build-plan, artifacts/decision-plans is reset so the newly reviewed plan is the active one for the next apply step.

### Duplicate report

When duplicate Microsoft IDs are found, duplicate artifacts are generated:
- artifacts/duplicates/duplicate-report-YYYYMMDD-HHMMSS.json
- artifacts/duplicates/duplicate-report-YYYYMMDD-HHMMSS.csv

The duplicate CSV contains one row per duplicate ADO item with:
- MicrosoftId
- AdoId
- Title

### Backup file (optional)

When --backup is used, files are generated:
- artifacts/backups/ado-backup-YYYYMMDD-HHMMSS.json
- artifacts/backups/ado-backup-YYYYMMDD-HHMMSS.csv

## Troubleshooting

### PAT not found

Symptom: error that PAT was not found.

Fix:
- set the env var specified by Ado:PatEnvironmentVariableName, or
- set Ado:Pat in appsettings.json for temporary testing
- confirm the variable exists in the same shell session used to run dotnet when using env var mode

### Field mapping errors

Symptom: update/create fails for one or more items.

Fix:
- verify FieldMap values are valid field reference names in your ADO process
- verify mapped fields are present on target work item types

### Permission errors

Symptom: unauthorized or forbidden responses.

Fix:
- verify PAT scope includes work item read and write
- verify PAT belongs to a user with access to the project and area path

### Unexpected large update count

Symptom: dry-run proposes too many updates.

Fix:
- verify semicolon-list fields and mapping names
- verify CSV version and target project are correct
- inspect a sample delta set before applying all

## Recommended Release Process

1. Always run dry-run first.
2. Always run against a sandbox/copy project first.
3. Review sample deltas with a process owner.
4. Apply to production only after sign-off.
5. Archive CSV, backup file, and run log together for audit.

## Security Notes

- Do not commit appsettings.json with real organization details or secrets.
- PAT is resolved in this order: environment variable, then Ado:Pat fallback.
- Keep PAT lifetime short and rotate regularly.

## Developer Notes

Build and test:

```powershell
dotnet build BpcAdoUpdater.sln -c Debug
dotnet test BpcAdoUpdater.sln -c Debug
```

Main entry point:
- src/BpcAdoUpdater/Program.cs
