using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIUsageDock.Core;

public static class UsageJson
{
    public const int ClaudeCacheSchemaVersion = 1;
    public const int MaxPayloadBytes = 1_048_576;

    public static ProviderSnapshot ParseClaudeStatusLine(string json, DateTimeOffset observedAt)
    {
        using var document = ParseBounded(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Claude status-line payload must be an object.");
        }

        var windows = new List<UsageWindowSnapshot>();
        if (root.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            AddClaudeWindow(limits, "five_hour", UsageWindow.Session, observedAt, windows);
            AddClaudeWindow(limits, "seven_day", UsageWindow.Weekly, observedAt, windows);
        }

        if (windows.Count == 0)
        {
            return ProviderSnapshot.Waiting(ProviderId.Claude, "Claude Code status line", "Waiting for first Claude Code response");
        }

        return new ProviderSnapshot(
            ProviderId.Claude,
            ProviderHealth.Available,
            windows,
            observedAt,
            "Claude Code status line");
    }

    public static ProviderSnapshot ParseClaudeCache(string json, DateTimeOffset now, TimeSpan staleAfter)
    {
        using var document = ParseBounded(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            schema.GetInt32() != ClaudeCacheSchemaVersion)
        {
            throw new JsonException("Unsupported Claude cache schema.");
        }

        if (!root.TryGetProperty("observedAt", out var observedElement) || !TryReadTimestamp(observedElement, out var observedAt))
        {
            throw new JsonException("Claude cache has no valid observation time.");
        }

        var windows = new List<UsageWindowSnapshot>();
        if (root.TryGetProperty("windows", out var windowsElement) && windowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in windowsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("window", out var windowElement) || !Enum.TryParse<UsageWindow>(windowElement.GetString(), true, out var window))
                {
                    continue;
                }

                var used = ReadPercentage(item, "usedPercent");
                DateTimeOffset? resetAt = item.TryGetProperty("resetsAt", out var resetElement) && TryReadTimestamp(resetElement, out var reset) ? reset : null;
                windows.Add(new UsageWindowSnapshot(window, used, resetAt, observedAt, item.TryGetProperty("limitId", out var limitId) ? limitId.GetString() : null));
            }
        }

        var health = windows.Count == 0 ? ProviderHealth.Unknown : now - observedAt > staleAfter ? ProviderHealth.Stale : ProviderHealth.Available;
        var message = health == ProviderHealth.Stale ? $"Cached data is older than {staleAfter.TotalMinutes:0} minutes." : null;
        return new ProviderSnapshot(
            ProviderId.Claude,
            health,
            windows,
            observedAt,
            "Claude Code status line cache",
            message,
            root.TryGetProperty("planType", out var plan) ? plan.GetString() : null);
    }

    public static string CreateClaudeCache(ProviderSnapshot snapshot)
    {
        var payload = new
        {
            schemaVersion = ClaudeCacheSchemaVersion,
            provider = "claude",
            observedAt = snapshot.LastUpdated ?? DateTimeOffset.UtcNow,
            planType = snapshot.PlanType,
            windows = snapshot.Windows.Select(window => new
            {
                window = window.Window.ToString(),
                usedPercent = window.UsedPercent,
                resetsAt = window.ResetsAt,
                observedAt = window.ObservedAt,
                limitId = window.LimitId,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
    }

    public static ProviderSnapshot ParseCodexRateLimits(JsonElement response, DateTimeOffset observedAt)
    {
        var limits = FindObject(response, "rateLimits") ?? response;
        var windows = new Dictionary<UsageWindow, UsageWindowSnapshot>();
        var planType = FindString(response, "planType") ?? FindString(limits, "planType");

        AddCodexWindow(limits, "primary", UsageWindow.Session, observedAt, windows);
        AddCodexWindow(limits, "secondary", UsageWindow.Weekly, observedAt, windows);
        AddCodexWindow(limits, "five_hour", UsageWindow.Session, observedAt, windows);
        AddCodexWindow(limits, "seven_day", UsageWindow.Weekly, observedAt, windows);

        if (limits.TryGetProperty("rateLimitsByLimitId", out var byLimit) && byLimit.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byLimit.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                AddCodexWindow(property.Value, "primary", UsageWindow.Session, observedAt, windows, property.Name);
                AddCodexWindow(property.Value, "secondary", UsageWindow.Weekly, observedAt, windows, property.Name);
                AddCodexWindow(property.Value, "five_hour", UsageWindow.Session, observedAt, windows, property.Name);
                AddCodexWindow(property.Value, "seven_day", UsageWindow.Weekly, observedAt, windows, property.Name);
            }
        }

        if (windows.Count == 0)
        {
            return new ProviderSnapshot(ProviderId.Codex, ProviderHealth.Error, Array.Empty<UsageWindowSnapshot>(), observedAt, "codex app-server", "The installed Codex protocol returned no recognizable rate-limit windows.", planType);
        }

        return new ProviderSnapshot(ProviderId.Codex, ProviderHealth.Available, windows.Values.OrderBy(window => window.Window).ToArray(), observedAt, "codex app-server · account/rateLimits/read", null, planType);
    }

    private static void AddClaudeWindow(JsonElement limits, string propertyName, UsageWindow window, DateTimeOffset observedAt, ICollection<UsageWindowSnapshot> target)
    {
        if (!limits.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        target.Add(new UsageWindowSnapshot(window, ReadPercentage(element, "used_percentage"), ReadTimestamp(element, "resets_at"), observedAt));
    }

    private static void AddCodexWindow(JsonElement limits, string propertyName, UsageWindow window, DateTimeOffset observedAt, IDictionary<UsageWindow, UsageWindowSnapshot> target, string? limitId = null)
    {
        if (!limits.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var duration = ReadNumber(element, "windowDurationMins") ?? ReadNumber(element, "window_duration_mins");
        var inferredWindow = duration switch
        {
            <= 360 when duration is not null => UsageWindow.Session,
            <= 11520 when duration is not null => UsageWindow.Weekly,
            _ => window,
        };
        var candidate = new UsageWindowSnapshot(inferredWindow, ReadPercentage(element, "usedPercent") ?? ReadPercentage(element, "used_percentage"), ReadTimestamp(element, "resetsAt") ?? ReadTimestamp(element, "resets_at"), observedAt, limitId);
        if (!target.ContainsKey(inferredWindow) || target[inferredWindow].UsedPercent is null)
        {
            target[inferredWindow] = candidate;
        }
    }

    private static JsonDocument ParseBounded(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
        {
            throw new JsonException("Provider payload exceeds the safety limit.");
        }

        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
    }

    private static JsonElement? FindObject(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.Object)
        {
            return direct;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object && result.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            return nested;
        }

        return null;
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("result", out var result) ? FindString(result, name) : null;
    }

    private static double? ReadPercentage(JsonElement element, string propertyName) =>
        ReadNumber(element, propertyName) is double value && UsagePercent.IsValid(value) ? value : null;

    private static double? ReadNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            return null;
        }

        return number;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && TryReadTimestamp(value, out var timestamp) ? timestamp : null;

    private static bool TryReadTimestamp(JsonElement value, out DateTimeOffset timestamp)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var unix))
        {
            try
            {
                timestamp = unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)unix) : DateTimeOffset.FromUnixTimeSeconds((long)unix);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
        {
            return true;
        }

        timestamp = default;
        return false;
    }
}
