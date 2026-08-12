using AIUsageDock.Core;

namespace AIUsageDock.Extension;

public sealed class WebUsageAuthenticatedEventArgs(ProviderId provider) : EventArgs
{
    public ProviderId Provider { get; } = provider;
}

public interface IWebUsageSession
{
    ProviderId Provider { get; }

    event EventHandler<WebUsageAuthenticatedEventArgs>? AuthenticationSucceeded;

    void ShowConnectionWindow();
}
