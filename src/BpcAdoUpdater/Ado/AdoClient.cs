using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace BpcAdoUpdater.Ado;

public sealed class AdoClient
{
    private readonly WorkItemTrackingHttpClient _witClient;

    public AdoClient(string organizationUrl, string pat)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(organizationUrl), credentials);
        _witClient = connection.GetClient<WorkItemTrackingHttpClient>();
    }

    public async Task<List<int>> QueryWorkItemIdsByAreaPathAsync(string project, string areaPath, IEnumerable<string> workItemTypes, CancellationToken cancellationToken)
    {
        string escapedAreaPath = areaPath.Replace("'", "''", StringComparison.Ordinal);
        string[] typeList = workItemTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string typeClause = typeList.Length == 0
            ? string.Empty
            : $" AND [System.WorkItemType] IN ({string.Join(",", typeList.Select(x => $"'{x.Replace("'", "''", StringComparison.Ordinal)}'"))})";

        string query = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project.Replace("'", "''", StringComparison.Ordinal)}' AND [System.AreaPath] UNDER '{escapedAreaPath}'{typeClause}";
        var wiql = new Wiql { Query = query };

        WorkItemQueryResult result = await _witClient.QueryByWiqlAsync(wiql, cancellationToken: cancellationToken);
        return result.WorkItems.Select(x => x.Id).ToList();
    }

    public async Task<List<WorkItem>> GetWorkItemsBatchAsync(IEnumerable<int> ids, IEnumerable<string> fields, CancellationToken cancellationToken)
    {
        int[] itemIds = ids.Distinct().ToArray();
        if (itemIds.Length == 0)
        {
            return new List<WorkItem>();
        }

        var request = new WorkItemBatchGetRequest
        {
            Ids = itemIds,
            Fields = fields.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ErrorPolicy = WorkItemErrorPolicy.Omit,
        };

        return await _witClient.GetWorkItemsBatchAsync(request, cancellationToken: cancellationToken);
    }

    public Task<WorkItem> CreateWorkItemAsync(string project, string workItemType, JsonPatchDocument patch, CancellationToken cancellationToken)
    {
        return _witClient.CreateWorkItemAsync(patch, project, workItemType, cancellationToken: cancellationToken);
    }

    public Task<WorkItem> UpdateWorkItemAsync(int id, JsonPatchDocument patch, CancellationToken cancellationToken)
    {
        return _witClient.UpdateWorkItemAsync(patch, id, cancellationToken: cancellationToken);
    }
}
