using CloudConfigAgent.Core.Agent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

public class OpenAiChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public OpenAiChatModel(HttpClient http, LlmOptions options)
    {
        _http = http;
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<JsonObject> ChatAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = 0,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl!.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["choices"]?[0]?["message"]?.AsObject().DeepClone().AsObject()
               ?? throw new InvalidOperationException("Unerwartete Antwort des Sprachmodells.");
    }
}
