using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Extension;

[Guid("3e9f8f14-f12e-4ea2-8da0-a7f5c7ec5f37")]
public sealed partial class AIUsageDockExtension : IExtension
{
    private readonly ManualResetEvent _disposedEvent;
    private readonly CommandProvider _provider;

    public event EventHandler<ManualResetEvent>? Release;

    public AIUsageDockExtension(ManualResetEvent disposedEvent, CommandProvider provider)
    {
        _disposedEvent = disposedEvent;
        _provider = provider;
    }

    public object GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null!,
    };

    public void Dispose() => Release?.Invoke(this, _disposedEvent);
}
