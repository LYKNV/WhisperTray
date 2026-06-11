using System.Net.Http;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Orchestration;
using WhisperTray.Core.Transcription;

namespace WhisperTray.App.Adapters;

/// <summary>
/// Builds an OpenAI-compatible client per Settings snapshot, reusing a single
/// shared HttpClient across all invocations to keep sockets pooled.
/// </summary>
public sealed class HttpTranscriptionClientFactory : ITranscriptionClientFactory
{
    private readonly HttpClient _http;

    public HttpTranscriptionClientFactory(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    public ITranscriptionClient Create(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("API key is not configured.");
        }

        return settings.Provider switch
        {
            TranscriptionProvider.WhisperApi =>
                new WhisperApiClient(
                    _http,
                    settings.BaseUrl,
                    settings.ApiKey,
                    pollBudget: settings.TimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(settings.TimeoutSeconds)
                        : null),
            _ =>
                new OpenAiCompatibleClient(_http, settings.BaseUrl, settings.ApiKey),
        };
    }
}
