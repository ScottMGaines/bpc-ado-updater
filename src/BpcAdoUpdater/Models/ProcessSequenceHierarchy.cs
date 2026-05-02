namespace BpcAdoUpdater.Models;

public static class ProcessSequenceHierarchy
{
    public static string? GetParent(string? sequenceId)
    {
        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            return null;
        }

        string[] parts = sequenceId.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        int nonZeroIndex = -1;
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!IsZero(parts[i]))
            {
                nonZeroIndex = i;
                break;
            }
        }

        if (nonZeroIndex <= 0)
        {
            return null;
        }

        string[] parentParts = (string[])parts.Clone();
        parentParts[nonZeroIndex] = "000";
        for (int i = nonZeroIndex + 1; i < parentParts.Length; i++)
        {
            parentParts[i] = "000";
        }

        string parent = string.Join('.', parentParts);
        return parent.Equals(sequenceId, StringComparison.OrdinalIgnoreCase) ? null : parent;
    }

    public static int GetDepth(string? sequenceId)
    {
        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            return 0;
        }

        string[] parts = sequenceId.Split('.');
        int depth = 0;
        foreach (string part in parts)
        {
            if (!IsZero(part))
            {
                depth++;
            }
        }

        return depth;
    }

    private static bool IsZero(string value)
    {
        return int.TryParse(value, out int number) && number == 0;
    }
}
