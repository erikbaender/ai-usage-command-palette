namespace AIUsageDock.Core;

public readonly record struct ProcessDescriptor(int ProcessId, int ParentProcessId, string Name);

public static class ProviderActivityDetection
{
    public static bool IsActive(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId)
    {
        var ownedProcessIds = FindDescendants(processes, monitoringProcessId);
        var expectedName = provider == ProviderId.Codex ? "codex" : "claude";
        return processes.Any(process =>
            !ownedProcessIds.Contains(process.ProcessId) &&
            Path.GetFileNameWithoutExtension(process.Name).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<int> FindDescendants(
        IReadOnlyCollection<ProcessDescriptor> processes,
        int parentProcessId)
    {
        var descendants = new HashSet<int> { parentProcessId };
        var foundNewDescendant = true;
        while (foundNewDescendant)
        {
            foundNewDescendant = false;
            foreach (var process in processes)
            {
                if (descendants.Contains(process.ParentProcessId) && descendants.Add(process.ProcessId))
                {
                    foundNewDescendant = true;
                }
            }
        }

        return descendants;
    }
}
