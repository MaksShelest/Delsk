using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Agents.Llm;

public record OllamaSettings(string Endpoint = "http://127.0.0.1:11434/", string Model = "qwen3.5:4b",
    int ContextTokens = 4096, int MaximumOutputTokens = 512, int Seed = 42);

public sealed class OllamaAdapter : ILocalModelAdapter, IDisposable
{
    private readonly HttpClient _http;
    private readonly OllamaSettings _settings;
    private readonly Uri _endpoint;
    public OllamaAdapter(OllamaSettings settings, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || string.IsNullOrWhiteSpace(settings.Model) ||
            settings.Model.Length > 120 || settings.Model.Any(char.IsControl) || settings.ContextTokens is < 256 or > 4096 ||
            settings.MaximumOutputTokens is < 16 or > 512) throw new ArgumentException("Invalid local Ollama configuration");
        _settings = settings; _endpoint = new(uri, "api/chat");
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var schema = new
        {
            type = "object", additionalProperties = false,
            required = new[] { "decisionId", "actorId", "observationVersion", "sessionEpoch", "optionId", "reasonSummary" },
            properties = new
            {
                decisionId = new { type = "string", @enum = new[] { request.DecisionId } },
                actorId = new { type = "integer", @enum = new[] { request.ActorId } },
                observationVersion = new { type = "integer", @enum = new[] { request.ObservationVersion } },
                sessionEpoch = new { type = "integer", @enum = new[] { request.SessionEpoch } },
                optionId = new { type = "string", @enum = request.Options.Select(p => p.Id).ToArray() },
                reasonSummary = new { type = "string", maxLength = 256 }
            }
        };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = _settings.Model, stream = false, format = schema, keep_alive = "5m",
            options = new { num_ctx = _settings.ContextTokens, num_predict = _settings.MaximumOutputTokens, temperature = 0, seed = _settings.Seed },
            messages = new[] { new { role = "user", content = request.Prompt } }
        });
        if (body.Length > 64 * 1024) return new(null, "RequestTooLarge");
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new("application/json");
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return new(null, "ModelUnavailable");
            using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[4096];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (output.Length + read > 64 * 1024) return new(null, "ResponseTooLarge");
                output.Write(buffer, 0, read);
            }
            using var doc = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("message", out var content) || content.GetProperty("role").GetString() != "assistant")
                return new(null, "InvalidModelResponse");
            string? text = content.GetProperty("content").GetString();
            if (text is null || Encoding.UTF8.GetByteCount(text) > 4096) return new(null, "ResponseTooLarge");
            long? Tokens(string field) => root.TryGetProperty(field, out var p) && p.TryGetInt64(out long n) && n >= 0 ? n : null;
            if (Tokens("prompt_eval_count") > _settings.ContextTokens || Tokens("eval_count") > _settings.MaximumOutputTokens)
                return new(null, "TokenLimitExceeded", watch.ElapsedMilliseconds);
            return new(text, "Completed", watch.ElapsedMilliseconds, Tokens("prompt_eval_count"), Tokens("eval_count"), _settings.Model);
        }
        catch (OperationCanceledException) { return new(null, "Cancelled", watch.ElapsedMilliseconds); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(null, "ModelUnavailable", watch.ElapsedMilliseconds); }
    }
    public void Dispose() => _http.Dispose();
}
