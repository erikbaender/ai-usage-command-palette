using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageDock.Core;

public static class UsageJson
{
    public const int MaxPayloadBytes = 1_048_576;

    public static ProviderSnapshot ParseClaudeCliUsage(string json, DateTimeOffset observedAt)
    {
        using var document = ParseBounded(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("result", out var resultElement) ||
            resultElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Claude CLI usage response has no result text.");
        }

        var result = Regex.Replace(resultElement.GetString() ?? string.Empty, "\u001b\\[[0-9;]*[A-Za-z]", string.Empty)
            .Replace('\u00a0', ' ')
            // Some Claude CLI launches have emitted the UTF-8 bullet as mojibake ("Â·").
            // Normalize that presentation variant before matching the usage lines.
            .Replace("\u00c2\u00b7", "\u00b7", StringComparison.Ordinal);
        var windows = new List<UsageWindowSnapshot>();
        foreach (var line in result.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line,
                "^Current (?<window>session|week \\(all models\\)):\\s*(?<used>\\d+(?:\\.\\d+)?)%\\s+used(?:\\s+·\\s+resets\\s+(?<reset>.+))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !double.TryParse(match.Groups["used"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var used))
            {
                continue;
            }

            var window = match.Groups["window"].Value.StartsWith("session", StringComparison.OrdinalIgnoreCase)
                ? UsageWindow.Session
                : UsageWindow.Weekly;
            windows.Add(new UsageWindowSnapshot(
                window,
                UsagePercent.IsValid(used) ? used : null,
                match.Groups["reset"].Success ? ParseClaudeCliReset(match.Groups["reset"].Value, observedAt) : null,
                observedAt));
        }

        if (windows.Count == 0)
        {
            var preview = string.Join(" | ", result.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3));
            preview = preview.Length > 240 ? preview[..240] : preview;
            throw new JsonException($"Claude CLI usage response contained no recognized usage windows. Preview: {preview}");
        }

        return new ProviderSnapshot(
            ProviderId.Claude,
            ProviderHealth.Available,
            windows,
            observedAt,
            "Claude CLI · claude -p /usage");
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

    private static DateTimeOffset ParseClaudeCliReset(string value, DateTimeOffset observedAt)
    {
        var trimmed = value.Trim();
        var zoneStart = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        var hasZone = zoneStart >= 0 && trimmed.EndsWith(')');
        var dateText = hasZone ? trimmed[..zoneStart] : trimmed;
        var zone = hasZone ? FindTimeZone(trimmed[(zoneStart + 2)..^1]) : TimeZoneInfo.Local;
        var local = ParseClaudeCliLocalDate(dateText, observedAt.Year);
        var reset = ToDateTimeOffset(local, zone);
        if (reset <= observedAt)
        {
            local = ParseClaudeCliLocalDate(dateText, observedAt.Year + 1);
            reset = ToDateTimeOffset(local, zone);
        }

        return reset;
    }

    private static DateTime ParseClaudeCliLocalDate(string dateText, int year)
    {
        var parts = dateText.Replace(",", string.Empty, StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        var time = parts[2];
        if (time.Length < 3)
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        var period = time[^2..];
        var clock = time[..^2].Split(':');
        if (clock.Length is < 1 or > 2 ||
            !int.TryParse(clock[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            !period.Equals("am", StringComparison.OrdinalIgnoreCase) && !period.Equals("pm", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        var minute = 0;
        if (clock.Length == 2 && !int.TryParse(clock[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        var month = parts[0].ToLowerInvariant() switch
        {
            "jan" or "january" => 1,
            "feb" or "february" => 2,
            "mar" or "march" => 3,
            "apr" or "april" => 4,
            "may" => 5,
            "jun" or "june" => 6,
            "jul" or "july" => 7,
            "aug" or "august" => 8,
            "sep" or "september" => 9,
            "oct" or "october" => 10,
            "nov" or "november" => 11,
            "dec" or "december" => 12,
            _ => 0,
        };
        if (month == 0)
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        if (hour is < 1 or > 12 || minute is < 0 or > 59)
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }

        if (period.Equals("pm", StringComparison.OrdinalIgnoreCase) && hour < 12)
        {
            hour += 12;
        }
        else if (period.Equals("am", StringComparison.OrdinalIgnoreCase) && hour == 12)
        {
            hour = 0;
        }

        try
        {
            return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new JsonException("Claude CLI usage response contained an invalid reset time.");
        }
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime local, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(local, zone));

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
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
