using System.Runtime.InteropServices;
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

    public bool IsSessionRunning() => GetState(ProviderId.Claude).IsActive;

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

            foreach (var provider in Enum.GetValues<ProviderId>())
            {
                var active = ProviderActivityDetection.IsActive(provider, processes, Environment.ProcessId);
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
            processes.Add(new ProcessDescriptor(
                unchecked((int)entry.ProcessId),
                unchecked((int)entry.ParentProcessId),
                entry.ExecutableFile ?? string.Empty));
            entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (Process32Next(snapshot, ref entry));

        return processes;
    }

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

public sealed record ProviderActivityState(ProviderId Provider, bool IsActive, bool IsDimmed);
