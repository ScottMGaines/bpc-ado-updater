using Microsoft.Extensions.Configuration;

namespace BpcAdoUpdater.Config;

public sealed class AppConfig
{
    public required string OrganizationUrl { get; init; }
    public required string Project { get; init; }
    public required string AreaPathRoot { get; init; }
    public string? DefaultIterationPath { get; init; }
    public required string PatEnvironmentVariableName { get; init; }
    public string? Pat { get; init; }
    public required Dictionary<string, string> FieldMap { get; init; }
    public required Dictionary<string, string> WorkItemTypeMap { get; init; }
    public required Dictionary<string, string> DefaultsWhenCsvNull { get; init; }

    public static AppConfig Load(IConfiguration configuration)
    {
        var appConfig = new AppConfig
        {
            OrganizationUrl = configuration["Ado:OrganizationUrl"] ?? string.Empty,
            Project = configuration["Ado:Project"] ?? string.Empty,
            AreaPathRoot = configuration["Ado:AreaPathRoot"] ?? string.Empty,
            DefaultIterationPath = configuration["Ado:DefaultIterationPath"],
            PatEnvironmentVariableName = configuration["Ado:PatEnvironmentVariableName"] ?? "ADO_PAT",
            Pat = configuration["Ado:Pat"],
            FieldMap = ReadMap(configuration.GetSection("FieldMap")),
            WorkItemTypeMap = ReadMap(configuration.GetSection("WorkItemTypeMap")),
            DefaultsWhenCsvNull = ReadMap(configuration.GetSection("DefaultsWhenCsvNull")),
        };

        appConfig.Validate();
        return appConfig;
    }

    public string GetPatOrThrow(out bool usedConfigFallback)
    {
        usedConfigFallback = false;

        string? pat = Environment.GetEnvironmentVariable(PatEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(pat))
        {
            return pat;
        }

        if (!string.IsNullOrWhiteSpace(Pat))
        {
            usedConfigFallback = true;
            return Pat;
        }

        throw new InvalidOperationException(
            $"PAT was not found in environment variable '{PatEnvironmentVariableName}' and Ado:Pat is empty.");
    }

    public string ResolveWorkItemType(string? csvWorkItemType)
    {
        if (!string.IsNullOrWhiteSpace(csvWorkItemType) && WorkItemTypeMap.TryGetValue(csvWorkItemType, out string? mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(csvWorkItemType)
            ? throw new InvalidOperationException("Row has no work item type.")
            : csvWorkItemType;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(OrganizationUrl)) throw new InvalidOperationException("Ado:OrganizationUrl is required.");
        if (string.IsNullOrWhiteSpace(Project)) throw new InvalidOperationException("Ado:Project is required.");
        if (string.IsNullOrWhiteSpace(AreaPathRoot)) throw new InvalidOperationException("Ado:AreaPathRoot is required.");
        if (FieldMap.Count == 0) throw new InvalidOperationException("FieldMap must contain at least one mapping.");

        if (!FieldMap.ContainsKey("Microsoft ID"))
        {
            throw new InvalidOperationException("FieldMap must include mapping for 'Microsoft ID'.");
        }
    }

    private static Dictionary<string, string> ReadMap(IConfigurationSection section)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IConfigurationSection child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            {
                map[child.Key] = child.Value;
            }
        }

        return map;
    }
}
