using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageDock.Core;
using AIUsageDock.Providers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AIUsageDock.Extension;

public sealed class ClaudeWebViewUsageClient : IClaudeWebUsageClient, IWebUsageSession
{
    private static readonly Uri UsagePage = new("https://claude.ai/new#settings/usage");

    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Thread _uiThread;
    private Form? _window;
    private WebView2? _webView;
    private System.Windows.Forms.Timer? _connectionProbeTimer;
    private Uri? _usageEndpoint;
    private bool _connectionRequested;
    private int _connectionProbeRunning;
    private bool _disposed;

    public ClaudeWebViewUsageClient()
    {
        _uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "AI Usage Dock Claude web session",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    public bool IsConfigured => true;

    public ProviderId Provider => ProviderId.Claude;

    public event EventHandler<WebUsageAuthenticatedEventArgs>? AuthenticationSucceeded;

    public async Task<string> ReadUsageAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        linkedCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        await _initialized.Task.WaitAsync(linkedCancellation.Token);
        await _requestGate.WaitAsync(linkedCancellation.Token);
        try
        {
            return await ExecuteOnUiThreadAsync(async webView =>
            {
                var source = webView.Source;
                if (source is null ||
                    !source.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase) ||
                    source.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ClaudeWebUsageException(HttpStatusCode.Unauthorized);
                }

                var requestId = Guid.NewGuid().ToString("N");
                var requestIdJson = JsonSerializer.Serialize(requestId);
                var usageEndpointJson = JsonSerializer.Serialize(_usageEndpoint?.AbsoluteUri);
                var responseCompletion = new TaskCompletionSource<WebUsageEnvelope>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<CoreWebView2WebMessageReceivedEventArgs>? messageHandler = null;
                messageHandler = (_, args) =>
                {
                    try
                    {
                        var response = JsonSerializer.Deserialize<WebUsageEnvelope>(args.WebMessageAsJson);
                        if (response?.RequestId == requestId)
                        {
                            responseCompletion.TrySetResult(response);
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore messages not produced by this usage request.
                    }
                };
                webView.CoreWebView2.WebMessageReceived += messageHandler;
                try
                {
                    var script =
                        """
                        (async () => {
                          const requestId = __REQUEST_ID__;
                          let usageUrl = __USAGE_ENDPOINT__ ?? performance.getEntriesByType('resource')
                            .map(entry => entry.name)
                            .find(url => /\/api\/organizations\/[^/]+\/usage$/.test(url));
                          if (!usageUrl) {
                            const organizationsResponse = await fetch('/api/organizations', {
                              cache: 'no-store',
                              credentials: 'include',
                              headers: {
                                'Accept': 'application/json',
                                'Cache-Control': 'no-cache, no-store, max-age=0',
                                'Pragma': 'no-cache'
                              }
                            });
                            if (!organizationsResponse.ok) {
                              window.chrome.webview.postMessage({
                                requestId,
                                status: organizationsResponse.status,
                                body: '',
                                usageUrl: null
                              });
                              return;
                            }
                            const organizations = await organizationsResponse.json();
                            const organization = organizations.find(candidate =>
                              Array.isArray(candidate.capabilities) && candidate.capabilities.includes('claude_pro')) ??
                              organizations.find(candidate => candidate.uuid);
                            if (!organization?.uuid) {
                              window.chrome.webview.postMessage({ requestId, status: 425, body: '', usageUrl: null });
                              return;
                            }
                            usageUrl = new URL(`/api/organizations/${organization.uuid}/usage`, location.origin).href;
                          }
                          try {
                            const requestUrl = new URL(usageUrl);
                            requestUrl.searchParams.set('_ai_usage_refresh', Date.now().toString());
                            const response = await fetch(requestUrl, {
                              cache: 'no-store',
                              credentials: 'include',
                              headers: {
                                'Accept': 'application/json',
                                'Cache-Control': 'no-cache, no-store, max-age=0',
                                'Pragma': 'no-cache'
                              }
                            });
                            window.chrome.webview.postMessage({
                              requestId,
                              status: response.status,
                              body: await response.text(),
                              usageUrl
                            });
                          } catch {
                            window.chrome.webview.postMessage({ requestId, status: 0, body: '', usageUrl });
                          }
                        })();
                        """
                        .Replace("__REQUEST_ID__", requestIdJson, StringComparison.Ordinal)
                        .Replace("__USAGE_ENDPOINT__", usageEndpointJson, StringComparison.Ordinal);
                    await webView.CoreWebView2.ExecuteScriptAsync(script);
                    var envelope = await responseCompletion.Task.WaitAsync(linkedCancellation.Token);
                    if (TryValidateUsageEndpoint(envelope.UsageUrl, out var usageEndpoint))
                    {
                        _usageEndpoint = usageEndpoint;
                    }
                    if (envelope.Status == 0)
                    {
                        throw new InvalidOperationException("Claude web usage request failed.");
                    }
                    if (envelope.Status == 425)
                    {
                        throw new InvalidOperationException("Claude usage page is still loading.");
                    }
                    if (envelope.Status is < 200 or >= 300)
                    {
                        if (envelope.Status is 401 or 403)
                        {
                            _usageEndpoint = null;
                        }
                        throw new ClaudeWebUsageException((HttpStatusCode)envelope.Status);
                    }

                    CompleteConnection();
                    return envelope.Body;
                }
                finally
                {
                    webView.CoreWebView2.WebMessageReceived -= messageHandler;
                }
            }, linkedCancellation.Token);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void ShowConnectionWindow()
    {
        _ = _initialized.Task.ContinueWith(
            _ =>
            {
                var window = _window;
                var webView = _webView;
                if (window is null || webView is null || window.IsDisposed)
                {
                    return;
                }

                window.BeginInvoke(() =>
                {
                    _connectionRequested = true;
                    window.ShowInTaskbar = true;
                    window.WindowState = FormWindowState.Normal;
                    window.Show();
                    window.Activate();
                    webView.CoreWebView2.Navigate(UsagePage.AbsoluteUri);
                    _connectionProbeTimer?.Start();
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        var window = _window;
        if (window is not null && !window.IsDisposed)
        {
            try
            {
                window.BeginInvoke(() =>
                {
                    window.FormClosing -= HideInsteadOfClose;
                    window.Close();
                    Application.ExitThread();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }
        if (_uiThread.IsAlive)
        {
            _uiThread.Join(TimeSpan.FromSeconds(5));
        }
        _requestGate.Dispose();
        _shutdown.Dispose();
    }

    private void RunUiThread()
    {
        try
        {
            _window = new Form
            {
                Text = "Connect Claude web usage",
                StartPosition = FormStartPosition.CenterScreen,
                Width = 1100,
                Height = 800,
                ShowInTaskbar = false,
            };
            _window.FormClosing += HideInsteadOfClose;
            _connectionProbeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _connectionProbeTimer.Tick += ProbeConnection;
            _webView = new WebView2 { Dock = DockStyle.Fill };
            _window.Controls.Add(_webView);
            _window.Load += InitializeWebView;
            _window.Opacity = 0;
            _window.Show();
            Application.Run();
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
        }
        finally
        {
            _connectionProbeTimer?.Dispose();
            _webView?.Dispose();
            _window?.Dispose();
        }
    }

    private async void InitializeWebView(object? sender, EventArgs args)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIUsage",
                "ClaudeWebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView!.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Navigate(UsagePage.AbsoluteUri);
            _window!.Hide();
            _window.Opacity = 1;
            _initialized.TrySetResult();
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess || _webView?.Source is not Uri source ||
            !source.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase) ||
            source.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ||
            source.Fragment.Equals("#settings/usage", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _webView.CoreWebView2.Navigate(UsagePage.AbsoluteUri);
    }

    private void HideInsteadOfClose(object? sender, FormClosingEventArgs args)
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        args.Cancel = true;
        _connectionRequested = false;
        _connectionProbeTimer?.Stop();
        _window?.Hide();
    }

    private async void ProbeConnection(object? sender, EventArgs args)
    {
        if (!_connectionRequested ||
            Interlocked.Exchange(ref _connectionProbeRunning, 1) != 0)
        {
            return;
        }

        try
        {
            await ReadUsageAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The window remains open while the user completes authentication.
        }
        finally
        {
            Interlocked.Exchange(ref _connectionProbeRunning, 0);
        }
    }

    private void CompleteConnection()
    {
        if (!_connectionRequested)
        {
            return;
        }

        _connectionRequested = false;
        _connectionProbeTimer?.Stop();
        _window?.Hide();
        try
        {
            AuthenticationSucceeded?.Invoke(this, new WebUsageAuthenticatedEventArgs(Provider));
        }
        catch
        {
            // A notification failure must not invalidate a successful login.
        }
    }

    private Task<T> ExecuteOnUiThreadAsync<T>(
        Func<WebView2, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var webView = _webView;
        var window = _window;
        if (webView is null || webView.IsDisposed || window is null || window.IsDisposed)
        {
            completion.TrySetException(new InvalidOperationException("Claude web session is unavailable."));
            return completion.Task;
        }

        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            window.BeginInvoke(async () =>
            {
                try
                {
                    completion.TrySetResult(await action(webView));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });
        }
        catch (Exception exception)
        {
            registration.Dispose();
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private static bool TryValidateUsageEndpoint(string? value, out Uri? endpoint)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            candidate.Host.Equals("claude.ai", StringComparison.OrdinalIgnoreCase) &&
            candidate.AbsolutePath.StartsWith("/api/organizations/", StringComparison.Ordinal) &&
            candidate.AbsolutePath.EndsWith("/usage", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(candidate.Query))
        {
            endpoint = candidate;
            return true;
        }

        endpoint = null;
        return false;
    }

    private sealed record WebUsageEnvelope(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("usageUrl")] string? UsageUrl);
}
