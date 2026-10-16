using System.Net;
using System.Text;
using System.Text.Json;
using Agents.Llm;
using Xunit;

namespace Simulation.Tests;

public class OllamaAdapterTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task BoundedNonStreamingSchemaRequestAndMeasuredTokensUseOneLocalEndpoint()
    {
        var r = ModelTestFixtures.Request(); string? body = null; Uri? url = null;
        using var adapter = new OllamaAdapter(new(Model: "test-model"), new Handler(async (request, token) =>
        {
            url = request.RequestUri; body = await request.Content!.ReadAsStringAsync(token);
            return Response(JsonSerializer.Serialize(new { done = true, message = new { role = "assistant", content = ModelTestFixtures.Intent(r) }, prompt_eval_count = 120, eval_count = 20 }));
        }));
        var reply = await adapter.CompleteAsync(r, CancellationToken.None);
        Assert.Equal("Completed", reply.Outcome); Assert.Equal(120, reply.PromptTokens); Assert.Equal(20, reply.CompletionTokens);
        Assert.Equal("test-model", reply.Model); Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), url);
        using var doc = JsonDocument.Parse(body!); var root = doc.RootElement;
        Assert.False(root.GetProperty("stream").GetBoolean()); Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal(4096, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(512, root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.False(root.GetProperty("format").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(r.Prompt, root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.True(StrictIntentParser.TryParse(reply.Text, r, out _, out _));
    }
    [Theory]
    [InlineData("http://example.com/")] [InlineData("file:///tmp/model")]
    [InlineData("http://user:password@localhost/")] [InlineData("http://localhost/api/chat")]
    [InlineData("http://localhost/?secret=1")]
    public void ConfigurationRejectsRemoteDestinationsCredentialsAndUnexpectedPaths(string endpoint)
        => Assert.Throws<ArgumentException>(() => new OllamaAdapter(new(Endpoint: endpoint)));
    [Theory]
    [InlineData("not json")] [InlineData("{}")] [InlineData("{\"done\":false,\"message\":{\"role\":\"assistant\",\"content\":\"{}\"}}")]
    public async Task BadRuntimeEnvelopeDoesNotBecomeAnIntent(string text)
    {
        using var adapter = new OllamaAdapter(new(), new Handler((_, _) => Task.FromResult(Response(text))));
        Assert.NotEqual("Completed", (await adapter.CompleteAsync(ModelTestFixtures.Request(), CancellationToken.None)).Outcome);
    }
    [Theory]
    [InlineData(302)] [InlineData(404)] [InlineData(500)]
    public async Task RedirectUnavailableModelAndServerErrorsFailWithoutRetryOrRawDebug(int status)
    {
        int count = 0;
        using var adapter = new OllamaAdapter(new(), new Handler((_, _) => { count++; return Task.FromResult(Response("private server debug", (HttpStatusCode)status)); }));
        var reply = await adapter.CompleteAsync(ModelTestFixtures.Request(), CancellationToken.None);
        Assert.Equal(1, count); Assert.Null(reply.Text); Assert.Equal("ModelUnavailable", reply.Outcome);
    }
    [Fact]
    public async Task ResponseBodyLimitAndCancellationAreEnforcedWithoutNetwork()
    {
        using var oversized = new OllamaAdapter(new(), new Handler((_, _) => Task.FromResult(Response(new string('x', 65537)))));
        Assert.Equal("ResponseTooLarge", (await oversized.CompleteAsync(ModelTestFixtures.Request(), CancellationToken.None)).Outcome);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        using var blocked = new OllamaAdapter(new(), new Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Response("{}"); }));
        var reply = await blocked.CompleteAsync(ModelTestFixtures.Request(), cancel.Token).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal("Cancelled", reply.Outcome); Assert.Null(reply.Text);
    }
}
