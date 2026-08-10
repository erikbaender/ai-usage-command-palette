using System.Globalization;

namespace AIUsageDock.Core;

public static class UsageFormatting
{
    public static string FormatDock(ProviderSnapshot snapshot, DateTimeOffset now)
    {
        var name = snapshot.Provider.ToString();
        if (snapshot.Health is ProviderHealth.Unknown)
        {
            return $"{name}  —";
        }

        if (snapshot.Health is ProviderHealth.Unavailable or ProviderHealth.Unauthenticated or ProviderHealth.Error)
        {
            return $"{name}  —";
        }

        var session = FormatCompact(snapshot.GetWindow(UsageWindow.Session));
        var weekly = FormatCompact(snapshot.GetWindow(UsageWindow.Weekly));
        var stale = snapshot.Health == ProviderHealth.Stale || snapshot.IsStale(now, FreshnessPolicy.Default.ClaudeStaleAfter) ? " · stale" : string.Empty;
        return $"{name}  5h {session} · 7d {weekly}{stale}";
    }

    public static string FormatCompact(UsageWindowSnapshot? window) =>
        window?.UsedPercent is double percent && UsagePercent.IsValid(percent)
            ? $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%"
            : "—";

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
