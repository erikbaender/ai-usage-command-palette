using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageDock.Core;
using AIUsageDock.Providers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AIUsageDock.Extension;

public sealed class CodexWebViewUsageClient : ICodexWebUsageClient, IWebUsageSession
{
    private static readonly Uri UsagePage = new("https://chatgpt.com/codex/settings/usage");
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Thread _uiThread;
    private Form? _window;
    private WebView2? _webView;
    private System.Windows.Forms.Timer? _connectionProbeTimer;
    private bool _connectionRequested;
    private int _connectionProbeRunning;
    private bool _disposed;

    public CodexWebViewUsageClient()
    {
        _uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "AI Usage Dock Codex web session",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    public bool IsConfigured => true;

    public ProviderId Provider => ProviderId.Codex;

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
                    !source.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                    source.AbsolutePath.StartsWith("/auth", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CodexWebUsageException(HttpStatusCode.Unauthorized);
                }

                var requestId = Guid.NewGuid().ToString("N");
                var requestIdJson = JsonSerializer.Serialize(requestId);
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
                          try {
                            const sessionResponse = await fetch('https://chatgpt.com/api/auth/session', {
                              cache: 'no-store',
                              credentials: 'include',
                              headers: {
                                'Accept': 'application/json',
                                'Cache-Control': 'no-cache, no-store, max-age=0',
                                'Pragma': 'no-cache'
                              }
                            });
                            const session = sessionResponse.ok ? await sessionResponse.json() : null;
                            if (!session?.accessToken) {
                              window.chrome.webview.postMessage({ requestId, status: 401, body: '' });
                              return;
                            }
                            const requestUrl = new URL('https://chatgpt.com/backend-api/wham/usage');
                            requestUrl.searchParams.set('_ai_usage_refresh', Date.now().toString());
                            const headers = {
                              'Accept': 'application/json',
                              'Authorization': `Bearer ${session.accessToken}`,
                              'Cache-Control': 'no-cache, no-store, max-age=0',
                              'Pragma': 'no-cache'
                            };
                            const accountId = session.account?.id ?? session.user?.account_id;
                            if (accountId) headers['ChatGPT-Account-Id'] = accountId;
                            const response = await fetch(requestUrl, {
                              cache: 'no-store',
                              credentials: 'include',
                              headers
                            });
                            window.chrome.webview.postMessage({
                              requestId,
                              status: response.status,
                              body: await response.text()
                            });
                          } catch {
                            window.chrome.webview.postMessage({ requestId, status: 0, body: '' });
                          }
                        })();
                        """
                        .Replace("__REQUEST_ID__", requestIdJson, StringComparison.Ordinal);
                    await webView.CoreWebView2.ExecuteScriptAsync(script);
                    var envelope = await responseCompletion.Task.WaitAsync(linkedCancellation.Token);
                    if (envelope.Status == 0)
                    {
                        throw new InvalidOperationException("Codex web usage request failed.");
                    }
                    if (envelope.Status is < 200 or >= 300)
                    {
                        throw new CodexWebUsageException((HttpStatusCode)envelope.Status);
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
                Text = "Connect Codex web usage",
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
                "CodexWebView2");
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
        if (!args.IsSuccess || !_connectionRequested)
        {
            return;
        }

        _connectionProbeTimer?.Start();
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
            completion.TrySetException(new InvalidOperationException("Codex web session is unavailable."));
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

    private sealed record WebUsageEnvelope(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("body")] string Body);
}
