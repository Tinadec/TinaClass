using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;

namespace TinaClass.Api.Tests.Infrastructure;

public static class Expect
{
    public static async Task StatusAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        if (response.StatusCode != status)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"Expected {(int)status} {status}, got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }

    public static async Task<T> JsonAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        await StatusAsync(response, status);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json, TestContext.Current.CancellationToken))!;
    }

    /// <summary>A problem details response with this status and <c>code</c>; returns its <c>errors</c> field names.</summary>
    public static async Task<IReadOnlyList<string>> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await StatusAsync(response, status);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.MediaType + "", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        return document.RootElement.TryGetProperty("errors", out var errors) ? [.. errors.EnumerateObject().Select(p => p.Name)] : [];
    }
}

public static class HttpExtensions
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) => client.PostAsJsonAsync(url, body, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) => client.PutAsJsonAsync(url, body, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsync(url, JsonContent.Create(body, options: ApiFactory.Json), Ct);

    public static Task<HttpResponseMessage> SendDeleteAsync(this HttpClient client, string url) => client.DeleteAsync(url, Ct);

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url) => await Expect.JsonAsync<T>(await client.GetAsync(url, Ct));
}

/// <summary>Reads a server-sent event stream (what <c>POST /conversations/{id}/messages</c> answers with) into its events.</summary>
public static class Sse
{
    public static async Task<IReadOnlyList<(int Id, ChatStreamEvent Event)>> ReadChatAsync(HttpResponseMessage response)
    {
        await Expect.StatusAsync(response, HttpStatusCode.OK);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = new List<(int, ChatStreamEvent)>();
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var id = -1;
            string? data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = int.Parse(line[4..]);
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line[6..];
                }
            }
            if (data is not null)
            {
                events.Add((id, JsonSerializer.Deserialize(data, ContractsJsonContext.Default.ChatStreamEvent)!));
            }
        }
        return events;
    }
}

/// <summary>Reads an event stream while it is still open, one event at a time (stop and reconnect tests need to act in the middle of an answer).</summary>
public sealed class LiveSse(HttpResponseMessage response, Stream body) : IDisposable
{
    private readonly StreamReader _reader = new(body);

    public HttpResponseMessage Response { get; } = response;

    public static async Task<LiveSse> OpenAsync(HttpClient client, HttpRequestMessage request)
    {
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        await Expect.StatusAsync(response, HttpStatusCode.OK);
        return new LiveSse(response, await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The next event, or null when the stream has ended.</summary>
    public async Task<(int Id, ChatStreamEvent Event)?> NextAsync()
    {
        var id = -1;
        string? data = null;
        while (await _reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data is not null)
                {
                    return (id, JsonSerializer.Deserialize(data, ContractsJsonContext.Default.ChatStreamEvent)!);
                }
                continue;
            }
            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = int.Parse(line[4..]);
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
        }
        return null;
    }

    public void Dispose()
    {
        _reader.Dispose();
        Response.Dispose();
    }
}
