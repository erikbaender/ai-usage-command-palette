using System.Net;
using AIUsageDock.Core;

namespace AIUsageDock.Providers;

public enum UsageBackendPreference
{
    WebFirst,
    Cli,
}

public sealed class UsageBackendPreferences
{
    private int _codex = (int)UsageBackendPreference.WebFirst;
    private int _claude = (int)UsageBackendPreference.WebFirst;

    public UsageBackendPreference Get(ProviderId provider) => provider switch
    {
        ProviderId.Codex => (UsageBackendPreference)Volatile.Read(ref _codex),
        ProviderId.Claude => (UsageBackendPreference)Volatile.Read(ref _claude),
        _ => UsageBackendPreference.WebFirst,
    };

    public void Set(ProviderId provider, UsageBackendPreference preference)
    {
        switch (provider)
        {
            case ProviderId.Codex:
                Volatile.Write(ref _codex, (int)preference);
                break;
            case ProviderId.Claude:
                Volatile.Write(ref _claude, (int)preference);
                break;
        }
    }
}

public interface ICodexWebUsageClient : IDisposable
{
    bool IsConfigured { get; }

    Task<string> ReadUsageAsync(CancellationToken cancellationToken);
}

public sealed class CodexWebUsageException : InvalidOperationException
{
    public CodexWebUsageException(HttpStatusCode statusCode)
        : base($"Codex web usage returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
