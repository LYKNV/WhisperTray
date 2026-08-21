using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperTray.Core.Transcription;

/// <summary>
/// Transcription client that speaks OpenAI's `/audio/transcriptions` multipart protocol.
/// Works against OpenAI itself and OpenAI-compatible endpoints (e.g., Lemonfox / whisper-api.com).
/// </summary>
public sealed class OpenAiCompatibleClient : ITranscriptionClient
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _apiKey;

    public OpenAiCompatibleClient(HttpClient http, string baseUrl, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _endpoint = BuildEndpoint(baseUrl);
        _apiKey = apiKey;
    }

    public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var form = BuildForm(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);

        httpRequest.Content = form;
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TranscriptionServerException("Transport error while contacting transcription API.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranscriptionServerException("Transcription API request timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ParseResponse(body) : throw BuildException(response.StatusCode, body);
        }
    }

    private static Uri BuildEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return new Uri($"{trimmed}/audio/transcriptions", UriKind.Absolute);
    }

    private static MultipartFormDataContent BuildForm(TranscriptionRequest request)
    {
        var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(request.AudioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        form.Add(fileContent, "file", request.FileName);

        form.Add(new StringContent(request.Model), "model");

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            form.Add(new StringContent(request.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            form.Add(new StringContent(request.Prompt), "prompt");
        }

        return form;
    }

    private static TranscriptionResult ParseResponse(string body)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<SuccessPayload>(body, ResponseOptions);
            if (payload is null || string.IsNullOrEmpty(payload.Text))
            {
                throw new TranscriptionServerException(null, "Transcription API returned an empty response.");
            }

            return new TranscriptionResult(payload.Text, payload.Language, ParseUsage(payload.Usage));
        }
        catch (JsonException ex)
        {
            throw new TranscriptionServerException("Could not parse transcription API response: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the optional <c>usage</c> block, which OpenAI shapes either as token
    /// counts or as billed seconds. Driven by which fields are present rather than by
    /// <c>type</c>, so a provider that renames or omits <c>type</c> still yields usable
    /// numbers. Anything unparseable yields null — usage is telemetry, and the
    /// transcription has already succeeded by this point.
    /// </summary>
    private static TranscriptionUsage? ParseUsage(JsonElement? usage)
    {
        if (usage is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        var input = ReadInt(element, "input_tokens");
        var output = ReadInt(element, "output_tokens");
        var total = ReadInt(element, "total_tokens");
        var seconds = ReadDouble(element, "seconds");

        if (input is null && output is null && total is null && seconds is null)
        {
            return null;
        }

        return new TranscriptionUsage(input, output, total, seconds);
    }

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static double? ReadDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var parsed)
            ? parsed
            : null;

    private static TranscriptionException BuildException(HttpStatusCode status, string body)
    {
        var message = ExtractErrorMessage(body) ?? $"HTTP {(int)status} {status}";

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new TranscriptionAuthException(message),
            HttpStatusCode.TooManyRequests =>
                new TranscriptionRateLimitException(message),
            >= HttpStatusCode.InternalServerError =>
                new TranscriptionServerException(status, message),
            _ =>
                new TranscriptionRequestException(status, message),
        };
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    return errorElement.GetString();
                }

                if (errorElement.ValueKind == JsonValueKind.Object
                    && errorElement.TryGetProperty("message", out var msgElement)
                    && msgElement.ValueKind == JsonValueKind.String)
                {
                    return msgElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // non-JSON error body — fall through
        }

        return body.Length > 256 ? body[..256] : body;
    }

    private sealed class SuccessPayload
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("usage")]
        public JsonElement? Usage { get; init; }
    }
}
