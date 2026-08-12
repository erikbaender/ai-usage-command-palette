using System.Net;
using AIUsageDock.Providers;

namespace AIUsageDock.Tests;

public sealed class ClaudeWebUsageClientTests
{
    [Fact]
    public void RequiresSessionTokenAndOrganizationId()
    {
        using var client = new ClaudeWebUsageClient("not-a-session-token", Guid.NewGuid().ToString());

        Assert.False(client.IsConfigured);
    }

    [Fact]
    public async Task SendsSessionCookieOnlyToExpectedOrganizationEndpoint()
    {
        var organizationId = Guid.NewGuid().ToString();
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new ClaudeWebUsageClient(
            "sk-ant-sid-test-value",
            organizationId,
            httpClient);

        var result = await client.ReadUsageAsync(CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal($"https://claude.ai/api/organizations/{organizationId}/usage", handler.RequestUri?.ToString());
        Assert.Equal("sessionKey=sk-ant-sid-test-value", handler.Cookie);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Cookie { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Cookie = request.Headers.GetValues("Cookie").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }
}
