using System.Globalization;

namespace AIUsageDock.Core;

public static class UsageFormatting
{
    public static string FormatDock(ProviderSnapshot snapshot, DateTimeOffset now)
    {
        var name = snapshot.Provider.ToString();
        if (snapshot.Health is ProviderHealth.Unknown)
        {
            return $"{name}  waiting";
        }

        if (snapshot.Health is ProviderHealth.Unavailable)
        {
            return $"{name}  not found";
        }

        if (snapshot.Health is ProviderHealth.Unauthenticated)
        {
            return $"{name}  sign in";
        }

        if (snapshot.Health is ProviderHealth.Error)
        {
            return $"{name}  error";
        }

        var windows = new[]
        {
            FormatDockWindow("5h", snapshot.GetWindow(UsageWindow.Session), now),
            FormatDockWindow("7d", snapshot.GetWindow(UsageWindow.Weekly), now),
        }
        .Where(value => value is not null)
        .Select(value => value!)
        .ToArray();
        var stale = snapshot.Health == ProviderHealth.Stale || snapshot.IsStale(now, FreshnessPolicy.Default.ClaudeStaleAfter) ? " · stale" : string.Empty;
        return windows.Length == 0
            ? $"{name}  usage pending{stale}"
            : $"{name}  {string.Join(" · ", windows)}{stale}";
    }

    public static string FormatClaudeStatusLine(ProviderSnapshot snapshot) =>
        $"Claude 5h {FormatRemainingCompact(snapshot.GetWindow(UsageWindow.Session))} left · 7d {FormatRemainingCompact(snapshot.GetWindow(UsageWindow.Weekly))} left";

    public static string FormatDockWindow(ProviderSnapshot snapshot, UsageWindow window, DateTimeOffset now)
    {
        var usageWindow = snapshot.GetWindow(window);
        var remaining = FormatRemainingCompact(usageWindow);
        var countdown = usageWindow?.ResetsAt is DateTimeOffset resetAt
            ? FormatDockResetCountdown(resetAt, now)
            : string.Empty;

        return string.IsNullOrEmpty(countdown) ? remaining : $"{remaining} - {countdown}";
    }

    public static string FormatCompact(UsageWindowSnapshot? window) =>
        window?.UsedPercent is double percent && UsagePercent.IsValid(percent)
            ? $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%"
            : "—";

    public static string FormatRemainingCompact(UsageWindowSnapshot? window) =>
        window?.RemainingPercent is double percent && UsagePercent.IsValid(percent)
            ? $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%"
            : "—";

    private static string? FormatDockWindow(string label, UsageWindowSnapshot? window, DateTimeOffset now)
    {
        if (window is null)
        {
            return null;
        }

        if (window.RemainingPercent is double percent && UsagePercent.IsValid(percent))
        {
            return $"{label} {percent.ToString("0.#", CultureInfo.InvariantCulture)}% left";
        }

        return window.ResetsAt is not null
            ? $"{label} {FormatResetCompact(window.ResetsAt, now)}"
            : null;
    }

    private static string FormatResetCompact(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is null)
        {
            return "reset unknown";
        }

        var remaining = resetAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "reset due";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"resets {Math.Floor(remaining.TotalDays)}d {remaining.Hours}h";
        }

        return $"resets {remaining.Hours}h {remaining.Minutes}m";
    }

    private static string FormatDockResetCountdown(DateTimeOffset resetAt, DateTimeOffset now)
    {
        var remaining = resetAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var days = (int)Math.Floor(remaining.TotalDays);
        if (days > 0)
        {
            parts.Add($"{days}d");
        }

        if (remaining.Hours > 0)
        {
            parts.Add($"{remaining.Hours}h");
        }

        if (remaining.Minutes > 0)
        {
            parts.Add($"{remaining.Minutes}m");
        }

        return string.Join(" ", parts);
    }

    public static string FormatReset(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is null)
        {
            return "Reset: unknown";
        }

        var remaining = resetAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset: due";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"Resets in {Math.Floor(remaining.TotalDays)}d {remaining.Hours}h";
        }

        return $"Resets in {remaining.Hours}h {remaining.Minutes}m";
    }

    public static string FormatDetails(ProviderSnapshot snapshot, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            $"# {snapshot.Provider} usage",
            string.IsNullOrWhiteSpace(snapshot.PlanType) ? string.Empty : $"Plan: {snapshot.PlanType}",
            $"Status: {FormatHealth(snapshot)}",
        };

        foreach (var window in snapshot.Windows.OrderBy(item => item.Window))
        {
            var used = FormatCompact(window);
            var remaining = window.RemainingPercent is double value
                ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)}% remaining"
                : "remaining unknown";
            var label = window.Window switch
            {
                UsageWindow.Session => "5-hour",
                UsageWindow.Weekly => "7-day",
                UsageWindow.ModelWeekly => "Model weekly",
                _ => window.Window.ToString(),
            };
            lines.Add($"**{label}:** {used} used · {remaining} · {FormatReset(window.ResetsAt, now)}");
        }

        if (snapshot.Windows.Count == 0)
        {
            lines.Add(snapshot.Message ?? "Waiting for usage data.");
        }

        lines.Add(string.Empty);
        lines.Add($"Source: {snapshot.Source}");
        lines.Add($"Last updated: {(snapshot.LastUpdated is null ? "never" : snapshot.LastUpdated.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}");
        if (!string.IsNullOrWhiteSpace(snapshot.Message))
        {
            lines.Add($"\n{snapshot.Message}");
        }

        return string.Join(Environment.NewLine, lines.Where(line => line is not null));
    }

    private static string FormatHealth(ProviderSnapshot snapshot) => snapshot.Health switch
    {
        ProviderHealth.Available => "Available",
        ProviderHealth.Stale => "Stale (last known values retained)",
        ProviderHealth.Unauthenticated => "Not authenticated",
        ProviderHealth.Unavailable => "Provider not detected",
        ProviderHealth.Error => "Error",
        _ => "Waiting for data",
    };
}
