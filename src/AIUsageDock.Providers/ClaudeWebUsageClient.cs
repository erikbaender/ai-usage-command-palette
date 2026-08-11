using System.Net;
using System.Net.Http.Headers;

namespace AIUsageDock.Providers;

public sealed class ClaudeWebUsageClient : IDisposable
{
    private readonly string? _sessionAccessToken;
    private readonly string? _organizationId;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _requestTimeout;

    public ClaudeWebUsageClient(
        string? sessionAccessToken = null,
        string? organizationId = null,
        HttpClient? httpClient = null,
        TimeSpan? requestTimeout = null)
    {
        _sessionAccessToken = sessionAccessToken ??
            Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ACCESS_TOKEN");
        _organizationId = organizationId ??
            Environment.GetEnvironmentVariable("CLAUDE_CODE_ORGANIZATION_UUID");
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        });
        _ownsHttpClient = httpClient is null;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
    }

    public bool IsConfigured =>
        IsValidSessionAccessToken(_sessionAccessToken) &&
        Guid.TryParse(_organizationId, out _);

    public async Task<string> ReadUsageAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Claude web usage requires CLAUDE_CODE_SESSION_ACCESS_TOKEN and CLAUDE_CODE_ORGANIZATION_UUID.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://claude.ai/api/organizations/{_organizationId}/usage");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("AIUsageDock/0.1");
        request.Headers.TryAddWithoutValidation("Cookie", $"sessionKey={_sessionAccessToken}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new ClaudeWebUsageException(response.StatusCode);
        }

        return body;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static bool IsValidSessionAccessToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.StartsWith("sk-ant-sid", StringComparison.Ordinal) &&
        token.IndexOfAny([';', '\r', '\n']) < 0;
}

public sealed class ClaudeWebUsageException : InvalidOperationException
{
    public ClaudeWebUsageException(HttpStatusCode statusCode)
        : base($"Claude web usage returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
