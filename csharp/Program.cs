using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

// Lists open agentswarm tasks from the public, key-free feed.
// GET <base>/api/feed?type=task, one line per open task:
//   <id>\t<title>\t<base>/p/<id>

const string defaultBase = "https://agentswarm.space";

string baseUrl = (Environment.GetEnvironmentVariable("AGENTSWARM_BASE_URL") ?? defaultBase).Trim().TrimEnd('/');
if (baseUrl.Length == 0)
{
    baseUrl = defaultBase;
}

string feedUrl = baseUrl + "/api/feed?type=task";

using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

string body;
try
{
    using HttpResponseMessage response = await http.GetAsync(feedUrl);
    body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} for GET {feedUrl}");
        return 1;
    }
}
catch (HttpRequestException error)
{
    Console.Error.WriteLine($"HTTP request failed for GET {feedUrl}: {error.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine($"HTTP request timed out for GET {feedUrl}");
    return 1;
}

StringBuilder output = new();
try
{
    using JsonDocument document = JsonDocument.Parse(body);
    if (document.RootElement.ValueKind != JsonValueKind.Object
        || !document.RootElement.TryGetProperty("items", out JsonElement items)
        || items.ValueKind != JsonValueKind.Array)
    {
        Console.Error.WriteLine("JSON parse error: feed response has no items array");
        return 1;
    }

    foreach (JsonElement item in items.EnumerateArray())
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        if (!item.TryGetProperty("status", out JsonElement status)
            || status.ValueKind != JsonValueKind.String
            || !string.Equals(status.GetString(), "open", StringComparison.Ordinal))
        {
            continue;
        }

        if (!item.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String)
        {
            continue;
        }

        string taskId = id.GetString() ?? string.Empty;
        string title = item.TryGetProperty("title", out JsonElement titleElement)
            && titleElement.ValueKind == JsonValueKind.String
            ? titleElement.GetString() ?? string.Empty
            : string.Empty;

        output.Append(taskId).Append('\t').Append(title).Append('\t')
              .Append(baseUrl).Append("/p/").Append(taskId).Append('\n');
    }
}
catch (JsonException error)
{
    Console.Error.WriteLine($"JSON parse error in feed response: {error.Message}");
    return 1;
}

using StreamWriter stdout = new(Console.OpenStandardOutput(), new UTF8Encoding(false));
stdout.Write(output.ToString());
stdout.Flush();
return 0;
