using System.Runtime.InteropServices;
using System.Diagnostics;
using AIUsageDock.Core;
using Microsoft.Win32.SafeHandles;

namespace AIUsageDock.Extension;

public sealed class ProviderActivityMonitor : IClaudeSessionDetector, IDisposable
{
    public static TimeSpan DefaultBlinkPeriod { get; } = TimeSpan.FromSeconds(1);

    private const uint SnapshotProcesses = 0x00000002;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<ProviderId, ProviderActivityState> _states = new();
    private readonly object _gate = new();
    private readonly TimeSpan _halfPeriod;
    private readonly Task _monitorTask;
    private readonly Dictionary<ProviderId, ProviderCpuActivityTracker> _activityTrackers = new();
    private readonly ProviderUsageActivityTracker _usageActivityTracker = new(TimeSpan.FromSeconds(5));
    private bool _claudeSessionRunning;
    private bool _disposed;

    public ProviderActivityMonitor(TimeSpan? blinkPeriod = null)
    {
        var period = blinkPeriod ?? DefaultBlinkPeriod;
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(blinkPeriod), "The blink period must be positive.");
        }

        _halfPeriod = TimeSpan.FromTicks(Math.Max(1, period.Ticks / 2));
        foreach (var provider in Enum.GetValues<ProviderId>())
        {
            _states[provider] = new ProviderActivityState(provider, IsActive: false, IsDimmed: false);
            _activityTrackers[provider] = new ProviderCpuActivityTracker(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(50));
        }

        _monitorTask = MonitorAsync(_shutdown.Token);
    }

    public event EventHandler<ProviderActivityState>? ActivityChanged;

    public ProviderActivityState GetState(ProviderId provider)
    {
        lock (_gate)
        {
            return _states[provider];
        }
    }

    public bool IsSessionRunning()
    {
        lock (_gate)
        {
            return _claudeSessionRunning ||
                _usageActivityTracker.IsActive(ProviderId.Claude, DateTimeOffset.UtcNow);
        }
    }

    public void ObserveSnapshot(object? sender, ProviderSnapshot snapshot)
    {
        lock (_gate)
        {
            _usageActivityTracker.Observe(snapshot, DateTimeOffset.UtcNow);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        try
        {
            _monitorTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        var dimmed = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyCollection<ProcessDescriptor> processes;
            try
            {
                processes = CaptureProcesses();
            }
            catch
            {
                processes = Array.Empty<ProcessDescriptor>();
            }

            lock (_gate)
            {
                _claudeSessionRunning = ProviderActivityDetection.IsActive(
                    ProviderId.Claude,
                    processes,
                    Environment.ProcessId);
            }

            var observedAt = DateTimeOffset.UtcNow;
            foreach (var provider in Enum.GetValues<ProviderId>())
            {
                var active = _activityTrackers[provider].Observe(
                    provider,
                    processes,
                    Environment.ProcessId,
                    observedAt);
                lock (_gate)
                {
                    active |= _usageActivityTracker.IsActive(provider, observedAt);
                }
                Publish(new ProviderActivityState(provider, active, active && dimmed));
            }

            await Task.Delay(_halfPeriod, cancellationToken);
            dimmed = !dimmed;
        }
    }

    private void Publish(ProviderActivityState next)
    {
        ProviderActivityState previous;
        lock (_gate)
        {
            previous = _states[next.Provider];
            if (previous == next)
            {
                return;
            }
            _states[next.Provider] = next;
        }

        if (previous.IsActive != next.IsActive)
        {
            ActivityDiagnosticLog.Write(next.Provider, next.IsActive);
        }
        ActivityChanged?.Invoke(this, next);
    }

    private static IReadOnlyCollection<ProcessDescriptor> CaptureProcesses()
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
        {
            return Array.Empty<ProcessDescriptor>();
        }

        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry))
        {
            return Array.Empty<ProcessDescriptor>();
        }

        var processes = new List<ProcessDescriptor>();
        do
        {
            var processId = unchecked((int)entry.ProcessId);
            var executableFile = entry.ExecutableFile ?? string.Empty;
            processes.Add(new ProcessDescriptor(
                processId,
                unchecked((int)entry.ParentProcessId),
                executableFile));
            entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (Process32Next(snapshot, ref entry));

        var activityProcessIds = Enum.GetValues<ProviderId>()
            .SelectMany(provider => ProviderActivityDetection.FindExternalProviderProcessTrees(
                provider,
                processes,
                Environment.ProcessId))
            .Select(process => process.ProcessId)
            .ToHashSet();
        return processes.Select(process => activityProcessIds.Contains(process.ProcessId)
                ? WithProcessActivity(process)
                : process)
            .ToArray();
    }

    private static ProcessDescriptor WithProcessActivity(ProcessDescriptor process)
    {
        var io = ReadIoCounters(process.ProcessId);
        return process with
        {
            TotalProcessorTime = ReadTotalProcessorTime(process.ProcessId),
            TotalIoOperations = io.Operations,
            TotalIoBytes = io.Bytes,
        };
    }

    private static TimeSpan ReadTotalProcessorTime(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.TotalProcessorTime;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SystemException)
        {
            return TimeSpan.Zero;
        }
    }

    private static (ulong Operations, ulong Bytes) ReadIoCounters(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return GetProcessIoCounters(process.SafeHandle, out var counters)
                ? (counters.ReadOperationCount + counters.WriteOperationCount + counters.OtherOperationCount,
                    counters.ReadTransferCount + counters.WriteTransferCount + counters.OtherTransferCount)
                : default;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SystemException)
        {
            return default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(SafeProcessHandle processHandle, out IoCounters counters);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);
}

internal static class ActivityDiagnosticLog
{
    private static readonly object Gate = new();

    public static void Write(ProviderId provider, bool isActive)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIUsage",
                    "activity.log");
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024)
                {
                    File.WriteAllText(path, string.Empty);
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.UtcNow:O} provider={provider} active={isActive}{Environment.NewLine}");
            }
        }
        catch
        {
            // Activity diagnostics must not affect the monitor loop.
        }
    }
}

public sealed record ProviderActivityState(ProviderId Provider, bool IsActive, bool IsDimmed);
