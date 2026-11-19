using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Models.Data;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;

namespace TinaClass.Api.Features.Models;

/// <summary>Which adapter serves a provider.</summary>
public static class ProviderRouting
{
    /// <summary>
    /// The sample workspace's providers live under the reserved <c>.example</c> domain (RFC 2606): no such host exists, so the gateway answers
    /// for them with the Echo provider and the sample workspace works offline and without a key.
    /// </summary>
    public static bool IsDemoHost(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && (uri.Host.EndsWith(".example", StringComparison.OrdinalIgnoreCase) || uri.Scheme == "builtin");
}

/// <summary>Echo's pacing: <c>Models:Echo:DelayMs</c> is the pause between streamed pieces (the tests set it to 0), <c>ThinkMs</c> the pause before the first token.</summary>
public sealed class EchoOptions
{
    public int DelayMs { get; set; } = 14;

    public int ThinkMs { get; set; } = 350;
}

/// <summary>
/// The default model provider: no key, no network. It answers with the design's event stream (reasoning, plan, tool calls, sources, the
/// answer token by token, usage) so that the app's streaming UI, the tests and a first run work. A question about the export revamp gets the
/// sample answer of the design; anything else is echoed back with a note.
/// </summary>
public sealed partial class EchoProvider(Microsoft.Extensions.Options.IOptions<EchoOptions> options) : IModelProvider
{
    public string Protocol => ProviderProtocols.Echo;

    public int CountTokens(string text) => Math.Max(1, (text.Length + 3) / 4);

    [GeneratedRegex(@"[㐀-鿿豈-﫿]{1,2}|[^\s㐀-鿿豈-﫿]+\s*|\s+")]
    private static partial Regex Pieces();

    [GeneratedRegex(@"[㐀-鿿]")]
    private static partial Regex Cjk();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex Markers();

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var delay = options.Value.DelayMs;
        var clock = Stopwatch.StartNew();
        var question = request.Messages.LastOrDefault(m => m.Role == MessageRoles.User)?.Content ?? "";
        var zh = Cjk().IsMatch(question) || request.Messages.Any(m => Cjk().IsMatch(m.Content));
        var sample = zh && question.Contains("导出", StringComparison.Ordinal) && (question.Contains("痛点", StringComparison.Ordinal) || question.Contains("优先级", StringComparison.Ordinal));
        var research = request.Sources.Count > 0 || request.Tools.Count > 0 || sample;

        yield return new ChatStreamEvent(StreamEvents.ReasoningDelta, Text: "");
        if (options.Value.ThinkMs > 0)
        {
            await Task.Delay(options.Value.ThinkMs, cancellationToken);
        }
        var reasoning = sample ? Contracts.Sample.SampleChat.Reasoning
            : zh ? "先弄清问题要什么，再决定是否需要资料，最后组织成简短的回答。" : "Work out what is being asked, decide whether sources are needed, then answer briefly.";
        foreach (var piece in Pieces().Matches(reasoning).Select(m => m.Value))
        {
            yield return new ChatStreamEvent(StreamEvents.ReasoningDelta, Text: piece);
            if (delay > 0)
            {
                await Task.Delay(delay / 2, cancellationToken);
            }
        }
        yield return new ChatStreamEvent(StreamEvents.ReasoningEnd, Ms: (int)clock.ElapsedMilliseconds);

        if (research)
        {
            var steps = new List<PlanStepDto>();
            if (request.Sources.Count > 0 || sample)
            {
                steps.Add(new("s1", zh ? "检索资料" : "Search the sources", "pending", null));
            }
            if (request.Tools.Count > 0)
            {
                steps.Add(new("s2", zh ? "调用工具" : "Call the tools", "pending", null));
            }
            steps.Add(new("s3", zh ? "组织回答" : "Write the answer", "pending", null));
            yield return new ChatStreamEvent(StreamEvents.Plan, Steps: Mark(steps, 0, null));
            for (var i = 0; i < steps.Count; i++)
            {
                var stepClock = Stopwatch.StartNew();
                if (steps[i].Id == "s1" && request.Sources.Count > 0)
                {
                    var args = $"query={Truncate(question, 60)}\ntop_k={request.Sources.Count}";
                    yield return new ChatStreamEvent(StreamEvents.ToolStart, ToolId: "kb", Name: zh ? "检索知识库 " + string.Join("、", request.Sources.Select(s => s.BaseName).Distinct()) : "Search knowledge " + string.Join(", ", request.Sources.Select(s => s.BaseName).Distinct()),
                        Icon: "book", Arguments: args);
                    if (delay > 0)
                    {
                        await Task.Delay(delay * 4, cancellationToken);
                    }
                    yield return new ChatStreamEvent(StreamEvents.ToolResult, ToolId: "kb", State: "done", Summary: $"{request.Sources.Count} · {stepClock.ElapsedMilliseconds / 1000.0:0.0}s",
                        Result: string.Join('\n', request.Sources.Select(s => $"{s.DocumentName} · {s.Where} · {s.Score:0.00}")), Ms: (int)stepClock.ElapsedMilliseconds);
                }
                else if (steps[i].Id == "s2")
                {
                    foreach (var tool in request.Tools)
                    {
                        var id = tool.Id.ToString("N");
                        if (tool.Risk == "read")
                        {
                            yield return new ChatStreamEvent(StreamEvents.ToolStart, ToolId: id, Name: tool.Name, Icon: "wrench", Arguments: $"query={Truncate(question, 60)}");
                            if (delay > 0)
                            {
                                await Task.Delay(delay * 3, cancellationToken);
                            }
                            yield return new ChatStreamEvent(StreamEvents.ToolResult, ToolId: id, State: "done", Summary: zh ? "示例结果" : "sample result", Result: zh ? "（Echo 示例：没有真正调用工具）" : "(Echo sample: the tool was not really called)", Ms: 3);
                        }
                        else
                        {
                            // A tool that writes or reaches outside needs a person's approval; Echo never runs it (the chat service asks for the approval).
                            yield return new ChatStreamEvent(StreamEvents.ToolApproval, ToolId: id, Name: tool.Name, Icon: "shield", Arguments: $"query={Truncate(question, 60)}", State: "pending", Title: tool.Risk);
                        }
                    }
                }
                else if (steps[i].Id == "s3")
                {
                    foreach (var c in request.Sources.Select((s, n) => (s, n)))
                    {
                        yield return new ChatStreamEvent(StreamEvents.Citation, Position: c.n + 1, DocumentId: c.s.DocumentId, ChunkId: c.s.ChunkId, Score: c.s.Score, Title: c.s.DocumentName, Quote: c.s.Text, Name: c.s.Where);
                    }
                }
                var ms = (int)stepClock.ElapsedMilliseconds;
                yield return new ChatStreamEvent(StreamEvents.Plan, Steps: Mark(steps, i + 1, ms));
            }
        }

        var answer = Compose(question, zh, sample, request.Sources);
        var first = -1;
        var outTokens = 0;
        foreach (var piece in Pieces().Matches(answer).Select(m => m.Value))
        {
            if (first < 0)
            {
                first = (int)clock.ElapsedMilliseconds;
            }
            outTokens++;
            yield return new ChatStreamEvent(StreamEvents.TextDelta, Text: piece);
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
        var tokensIn = request.Messages.Sum(m => CountTokens(m.Content)) + request.Sources.Sum(s => CountTokens(s.Text));
        yield return new ChatStreamEvent(StreamEvents.Usage, Usage: new MessageUsageDto(tokensIn, CountTokens(answer), 0, Math.Max(first, 0), (int)clock.ElapsedMilliseconds, 0, true));
    }

    private static string Compose(string question, bool zh, bool sample, IReadOnlyList<RetrievedChunk> sources)
    {
        if (sample)
        {
            return sources.Count == 0 ? Markers().Replace(Contracts.Sample.SampleChat.Answer, "") : Contracts.Sample.SampleChat.Answer;
        }
        if (sources.Count > 0)
        {
            var lines = sources.Select((s, i) => $"- {Truncate(s.Text, 80)}[{i + 1}]");
            return (zh ? "根据资料库里的内容：\n\n" : "From the knowledge base:\n\n") + string.Join('\n', lines)
                 + (zh ? "\n\n（这是 Echo 示例模型的回放；连接真实模型后会得到真正的回答。）" : "\n\n(This is the Echo sample model replaying; connect a real model to get a real answer.)");
        }
        return zh
            ? $"你说：{Truncate(question, 200)}\n\n这是 Echo 示例模型的回放，不需要密钥。在“模型”页添加服务商并设置密钥后，回答会来自真实的模型。"
            : $"You said: {Truncate(question, 200)}\n\nThis is the Echo sample model replaying; it needs no key. Add a provider and set its key on the Models page to get answers from a real model.";
    }

    private static List<PlanStepDto> Mark(List<PlanStepDto> steps, int done, int? ms) =>
        [.. steps.Select((s, i) => i < done ? s with { State = "done", Ms = i == done - 1 ? ms : s.Ms } : i == done ? s with { State = "running" } : s)];

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}

/// <summary>
/// The adapter for <c>/chat/completions</c> compatible services (most gateways and self-hosted runtimes, and the local runtime's
/// compatible endpoint). It streams the answer and reasoning; tools are not sent (an agent's tools run in this API, not at the provider).
/// Retrieved sources are put in the system message with their numbers so that the answer can cite them as [n].
/// </summary>
public sealed class OpenAiCompatibleProvider(IHttpClientFactory http, string protocol) : IModelProvider
{
    public const string ClientName = "models";

    public string Protocol => protocol;

    public int CountTokens(string text) => Math.Max(1, (text.Length + 3) / 4);

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = BuildBody(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Model.BaseUrl.TrimEnd('/') + "/chat/completions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (request.ApiKey is { Length: > 0 })
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        }
        var clock = Stopwatch.StartNew();
        using var response = await http.CreateClient(ClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The provider's body can echo request details; the person gets the status only.
            yield return new ChatStreamEvent(StreamEvents.MessageError, Error: $"The provider answered {(int)response.StatusCode}.");
            yield break;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var first = -1;
        var reasoningOpen = false;
        int tokensIn = 0, tokensOut = 0, cached = 0;
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }
            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                break;
            }
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                tokensIn = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                tokensOut = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.TryGetProperty("cached_tokens", out var cachedTokens))
                {
                    cached = cachedTokens.GetInt32();
                    tokensIn = Math.Max(0, tokensIn - cached);
                }
            }
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("delta", out var delta))
            {
                continue;
            }
            if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String && reasoning.GetString() is { Length: > 0 } r)
            {
                reasoningOpen = true;
                yield return new ChatStreamEvent(StreamEvents.ReasoningDelta, Text: r);
            }
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } c)
            {
                if (reasoningOpen)
                {
                    reasoningOpen = false;
                    yield return new ChatStreamEvent(StreamEvents.ReasoningEnd, Ms: (int)clock.ElapsedMilliseconds);
                }
                if (first < 0)
                {
                    first = (int)clock.ElapsedMilliseconds;
                }
                text.Append(c);
                yield return new ChatStreamEvent(StreamEvents.TextDelta, Text: c);
            }
        }
        var estimated = tokensIn == 0 && tokensOut == 0;
        if (estimated)
        {
            tokensIn = request.Messages.Sum(m => CountTokens(m.Content));
            tokensOut = CountTokens(text.ToString());
        }
        yield return new ChatStreamEvent(StreamEvents.Usage, Usage: new MessageUsageDto(tokensIn, tokensOut, cached, Math.Max(first, 0), (int)clock.ElapsedMilliseconds, 0, estimated));
    }

    private static string BuildBody(ModelRequest request)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model.Code);
            json.WriteBoolean("stream", true);
            json.WriteStartObject("stream_options");
            json.WriteBoolean("include_usage", true);
            json.WriteEndObject();
            json.WriteStartArray("messages");
            if (request.Sources.Count > 0)
            {
                json.WriteStartObject();
                json.WriteString("role", "system");
                json.WriteString("content", "Answer using these sources and cite them as [n]:\n" + string.Join('\n', request.Sources.Select((s, i) => $"[{i + 1}] {s.DocumentName}: {s.Text}")));
                json.WriteEndObject();
            }
            foreach (var m in request.Messages)
            {
                json.WriteStartObject();
                json.WriteString("role", m.Role);
                json.WriteString("content", m.Content);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>Tests a provider and lists its models (<c>GET {base}/models</c> for the compatible protocols, the base address itself otherwise).</summary>
public sealed class ProviderProbe(IHttpClientFactory http, TimeProvider time)
{
    public async Task<ProviderTestResult> CheckAsync(string protocol, string baseUrl, string? key, bool requiresKey, CancellationToken cancellationToken)
    {
        if (protocol == ProviderProtocols.Echo || ProviderRouting.IsDemoHost(baseUrl))
        {
            return new ProviderTestResult(true, 1, null);
        }
        if (requiresKey && string.IsNullOrEmpty(key))
        {
            return new ProviderTestResult(false, 0, "No key is set.");
        }
        var start = time.GetTimestamp();
        try
        {
            using var response = await SendAsync(protocol, baseUrl, key, cancellationToken);
            var ms = (int)time.GetElapsedTime(start).TotalMilliseconds;
            return response.IsSuccessStatusCode ? new ProviderTestResult(true, ms, null) : new ProviderTestResult(false, ms, $"The provider answered {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new ProviderTestResult(false, 0, "The provider could not be reached.");
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string protocol, string baseUrl, string? key, bool requiresKey, CancellationToken cancellationToken)
    {
        if (protocol == ProviderProtocols.Echo || ProviderRouting.IsDemoHost(baseUrl))
        {
            return [];
        }
        if (requiresKey && string.IsNullOrEmpty(key))
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.KeyRequired, "Set the provider's key first.");
        }
        try
        {
            using var response = await SendAsync(protocol, baseUrl, key, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(StatusCodes.Status502BadGateway, ErrorCodes.ProviderFailed, $"The provider answered {(int)response.StatusCode}.");
            }
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var list = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement.TryGetProperty("models", out var models) ? models : default;
            return list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Select(e => e.TryGetProperty("id", out var id) ? id.GetString() : e.TryGetProperty("name", out var name) ? name.GetString() : null).OfType<string>()]
                : [];
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, ErrorCodes.ProviderFailed, "The provider could not be reached.");
        }
    }

    private Task<HttpResponseMessage> SendAsync(string protocol, string baseUrl, string? key, CancellationToken cancellationToken)
    {
        var url = protocol == ProviderProtocols.Messages ? baseUrl : baseUrl.TrimEnd('/') + "/models";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        return http.CreateClient(OpenAiCompatibleProvider.ClientName).SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// The models as the other features see them (<see cref="IModelCatalog"/>) and the gateway to their adapters (<see cref="IModelGateway"/>).
/// A provider's key comes from the encrypted store; failing that from configuration (<c>Models:Keys:&lt;provider name&gt;</c>, for example the
/// environment variable <c>Models__Keys__Northlight Cloud</c>), so a deployment can hold keys without anyone typing them in.
/// </summary>
public sealed class ModelCatalog(ModelsDbContext db, KeyVault vault, IConfiguration configuration) : IModelCatalog
{
    public async Task<ResolvedModel?> ResolveAsync(string? code, CancellationToken cancellationToken)
    {
        var query = from m in db.ModelEntries.AsNoTracking() join p in db.Providers.AsNoTracking() on m.ProviderId equals p.Id
                    where m.Enabled && p.Enabled && m.Kind == "chat" select new { Model = m, Provider = p };
        var row = code is null
            ? await query.OrderByDescending(x => x.Model.IsDefault).ThenBy(x => x.Provider.SortOrder).ThenBy(x => x.Model.Id).FirstOrDefaultAsync(cancellationToken)
            : await query.Where(x => x.Model.Code == code).OrderBy(x => x.Provider.SortOrder).ThenBy(x => x.Model.Id).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResolved(row.Model, row.Provider);
    }

    public async Task<IReadOnlyList<ResolvedModel>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (from m in db.ModelEntries.AsNoTracking() join p in db.Providers.AsNoTracking() on m.ProviderId equals p.Id
                          where m.Enabled && p.Enabled && m.Kind == "chat" orderby p.SortOrder, m.DisplayName, m.Id select new { Model = m, Provider = p }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => ToResolved(r.Model, r.Provider))];
    }

    public async Task<string?> GetApiKeyAsync(Guid providerId, CancellationToken cancellationToken)
    {
        var provider = await db.Providers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == providerId, cancellationToken);
        if (provider is null)
        {
            return null;
        }
        return vault.Unprotect(provider.KeyCipher) ?? configuration["Models:Keys:" + provider.Name];
    }

    private static ResolvedModel ToResolved(AiModel m, AiProvider p) =>
        new(m.Id, m.Code, m.DisplayName, p.Id, p.Protocol, p.BaseUrl, m.ContextTokens, m.PriceIn, m.PriceOut, m.PriceCache, ModelService.Split(m.Efforts));
}

internal sealed class ModelGateway(IEnumerable<IModelProvider> adapters, IModelCatalog catalog) : IModelGateway
{
    private readonly IModelProvider _echo = adapters.First(a => a.Protocol == ProviderProtocols.Echo);

    public int CountTokens(string text) => _echo.CountTokens(text);

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = request.Model;
        var adapter = model.Protocol == ProviderProtocols.Echo || ProviderRouting.IsDemoHost(model.BaseUrl)
            ? _echo
            : adapters.FirstOrDefault(a => a.Protocol == model.Protocol)
              ?? throw new ApiException(StatusCodes.Status502BadGateway, ErrorCodes.ProviderFailed, $"This build has no adapter for the '{model.Protocol}' protocol. Add an IModelProvider for it.");
        string? key = null;
        if (!ReferenceEquals(adapter, _echo))
        {
            key = await catalog.GetApiKeyAsync(model.ProviderId, cancellationToken);
            if (ModelService.RequiresKey(model.Protocol) && string.IsNullOrEmpty(key))
            {
                throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.KeyRequired, $"Set the key of the provider of {model.DisplayName} on the Models page.");
            }
        }
        await foreach (var e in adapter.StreamAsync(request with { ApiKey = key }, cancellationToken))
        {
            yield return e;
        }
    }
}
