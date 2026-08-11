namespace AIUsageDock.Core;

public readonly record struct ProcessDescriptor(
    int ProcessId,
    int ParentProcessId,
    string Name,
    TimeSpan TotalProcessorTime = default);

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
    private readonly Dictionary<int, TimeSpan> _previousCpuTimes = new();
    private readonly TimeSpan _activeHoldDuration;
    private DateTimeOffset? _lastActivity;
    private bool _hasObserved;

    public ProviderCpuActivityTracker(TimeSpan activeHoldDuration)
    {
        if (activeHoldDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeHoldDuration), "The activity hold duration must be positive.");
        }

        _activeHoldDuration = activeHoldDuration;
    }

    public bool Observe(
        ProviderId provider,
        IReadOnlyCollection<ProcessDescriptor> processes,
        int monitoringProcessId,
        DateTimeOffset observedAt)
    {
        var externalProcesses = ProviderActivityDetection.FindExternalProviderProcesses(
            provider,
            processes,
            monitoringProcessId);
        var currentProcessIds = new HashSet<int>();
        var activityObserved = false;
        foreach (var process in externalProcesses)
        {
            currentProcessIds.Add(process.ProcessId);
            if (_previousCpuTimes.TryGetValue(process.ProcessId, out var previousCpuTime)
                ? process.TotalProcessorTime != previousCpuTime
                : _hasObserved)
            {
                activityObserved = true;
            }

            _previousCpuTimes[process.ProcessId] = process.TotalProcessorTime;
        }

        foreach (var processId in _previousCpuTimes.Keys.Where(processId => !currentProcessIds.Contains(processId)).ToArray())
        {
            _previousCpuTimes.Remove(processId);
        }

        if (activityObserved)
        {
            _lastActivity = observedAt;
        }

        _hasObserved = true;

        return _lastActivity is not null && observedAt - _lastActivity.Value < _activeHoldDuration;
    }
}
