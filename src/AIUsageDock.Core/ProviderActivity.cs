namespace AIUsageDock.Core;

public readonly record struct ProcessDescriptor(
    int ProcessId,
    int ParentProcessId,
    string Name,
    TimeSpan TotalProcessorTime = default,
    ulong TotalIoOperations = 0,
    ulong TotalIoBytes = 0);

public static class ProviderActivityDetection
{
    public static bool IsActive(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId)
        => FindExternalProviderProcesses(provider, processes, monitoringProcessId).Count > 0;

    public static IReadOnlyList<ProcessDescriptor> FindExternalProviderProcesses(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId)
    {
        var ownedProcessIds = FindDescendants(processes, monitoringProcessId);
        var expectedName = provider == ProviderId.Codex ? "codex" : "claude";
        return processes.Where(process =>
                !ownedProcessIds.Contains(process.ProcessId) &&
                Path.GetFileNameWithoutExtension(process.Name).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<ProcessDescriptor> FindExternalProviderProcessTrees(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId)
    {
        var roots = FindExternalProviderProcesses(provider, processes, monitoringProcessId);
        var processIds = new HashSet<int>(roots.Select(process => process.ProcessId));
        var foundNewDescendant = true;
        while (foundNewDescendant)
        {
            foundNewDescendant = false;
            foreach (var process in processes)
            {
                if (processIds.Contains(process.ParentProcessId) && processIds.Add(process.ProcessId))
                {
                    foundNewDescendant = true;
                }
            }
        }

        return processes.Where(process => processIds.Contains(process.ProcessId)).ToArray();
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

public sealed class ProviderCpuActivityTracker
{
    private readonly Dictionary<int, (TimeSpan CpuTime, ulong IoOperations, ulong IoBytes)> _previousActivity = new();
    private readonly TimeSpan _activeHoldDuration;
    private readonly TimeSpan _minimumCpuDelta;
    private DateTimeOffset? _lastActivity;
    private bool _hasObserved;

    public ProviderCpuActivityTracker(TimeSpan activeHoldDuration, TimeSpan minimumCpuDelta)
    {
        if (activeHoldDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeHoldDuration), "The activity hold duration must be positive.");
        }

        if (minimumCpuDelta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCpuDelta), "The minimum CPU delta must be positive.");
        }

        _activeHoldDuration = activeHoldDuration;
        _minimumCpuDelta = minimumCpuDelta;
    }

    public bool Observe(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId,
        DateTimeOffset observedAt)
    {
        var externalProcesses = ProviderActivityDetection.FindExternalProviderProcessTrees(
            provider,
            processes,
            monitoringProcessId);
        var currentProcessIds = new HashSet<int>();
        var activityObserved = false;
        foreach (var process in externalProcesses)
        {
            currentProcessIds.Add(process.ProcessId);
            if (_previousActivity.TryGetValue(process.ProcessId, out var previous)
                ? process.TotalProcessorTime < previous.CpuTime ||
                    process.TotalProcessorTime - previous.CpuTime >= _minimumCpuDelta ||
                    process.TotalIoOperations != previous.IoOperations ||
                    process.TotalIoBytes != previous.IoBytes
                : _hasObserved)
            {
                activityObserved = true;
            }

            _previousActivity[process.ProcessId] = (
                process.TotalProcessorTime,
                process.TotalIoOperations,
                process.TotalIoBytes);
        }

        foreach (var processId in _previousActivity.Keys.Where(processId => !currentProcessIds.Contains(processId)).ToArray())
        {
            _previousActivity.Remove(processId);
        }

        if (activityObserved)
        {
            _lastActivity = observedAt;
        }

        _hasObserved = true;

        return _lastActivity is not null && observedAt - _lastActivity.Value < _activeHoldDuration;
    }
}
