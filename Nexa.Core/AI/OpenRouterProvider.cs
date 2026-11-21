using Microsoft.Extensions.Logging;
using Nexa.Core.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nexa.Core.AI;

public class OpenRouterProvider : OpenAICompatibleProvider
{
    public override string Name => "OpenRouter";
    protected override string DefaultBaseUrl => "https://openrouter.ai/api/v1";

    public OpenRouterProvider(
        ISettingsService settings,
        ISecretStore secretStore,
        HttpClient http,
        ILogger? logger)
        : base(settings, secretStore, http, logger) { }

    public override async Task<AIResponse> ProcessQueryAsync(
        string query,
        IEnumerable<AITool> availableTools,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        byte[]? audioWav = null)
    {
        if (audioWav is null)
            return await base.ProcessQueryAsync(query, availableTools, conversationHistory, cancellationToken);

        // Chat models such as Mercury accept text, while OpenRouter's dedicated STT
        // endpoint accepts audio. Transcribe first so voice works with the selected
        // text model and its tool calls.
        var transcript = await TranscribeAsync(audioWav, cancellationToken);
        if (string.IsNullOrWhiteSpace(transcript))
            throw new InvalidOperationException("OpenRouter transcription returned no speech. Try speaking again.");

        if (query.StartsWith("Transcribe the audio and respond with only the transcribed text.", StringComparison.Ordinal))
            return new AIResponse { TextResponse = transcript };

        var textQuery = string.IsNullOrWhiteSpace(query)
            ? transcript
            : $"{query}\n\nSpoken request: {transcript}";
        var answer = await base.ProcessQueryAsync(textQuery, availableTools, conversationHistory, cancellationToken);
        answer.RecognizedUserText = transcript;
        return answer;
    }

    private async Task<string> TranscribeAsync(byte[] audioWav, CancellationToken cancellationToken)
    {
        var config = await BuildConfigAsync();
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenRouter API key is missing. Add it in Nexa Settings → AI.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/audio/transcriptions");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        request.Content = JsonContent.Create(new
        {
            model = "openai/whisper-1",
            input_audio = new { data = Convert.ToBase64String(audioWav), format = "wav" }
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("OpenRouter transcription returned {Status}", response.StatusCode);
            throw new InvalidOperationException(FormatApiError(response.StatusCode, body));
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? ""
            : "";
    }

    protected override string FormatApiError(HttpStatusCode status, string errorBody) => status switch
    {
        HttpStatusCode.Unauthorized => "OpenRouter authentication failed (401). Replace the API key in Settings.",
        HttpStatusCode.PaymentRequired => "OpenRouter has insufficient credits (402). Add credits to the OpenRouter account.",
        HttpStatusCode.TooManyRequests => "OpenRouter rate limit reached (429). Wait and try again.",
        HttpStatusCode.BadRequest => "OpenRouter rejected the request (400). Check that the selected model supports this request.",
        HttpStatusCode.NotFound => "OpenRouter model or endpoint was not found (404). Check the model and Base URL.",
        _ => $"OpenRouter request failed ({(int)status}). Try again or check OpenRouter status."
    };
}
