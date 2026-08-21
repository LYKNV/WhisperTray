using System.Net;
using System.Text;
using FluentAssertions;
using WhisperTray.Core.Tests.TestInfrastructure;
using WhisperTray.Core.Transcription;

namespace WhisperTray.Core.Tests.Transcription;

public class OpenAiCompatibleClientTests
{
    private static readonly byte[] SampleAudio = Encoding.ASCII.GetBytes("FAKE-AUDIO-BYTES");

    private static TranscriptionRequest SampleRequest(string? language = null, string? prompt = null, string model = "whisper-1") =>
        new()
        {
            AudioBytes = SampleAudio,
            ContentType = "audio/ogg",
            FileName = "audio.ogg",
            Model = model,
            Language = language,
            Prompt = prompt,
        };

    private static (OpenAiCompatibleClient client, StubHttpMessageHandler handler) BuildClient(StubHttpMessageHandler handler, string baseUrl = "https://api.openai.com/v1")
    {
        var http = new HttpClient(handler);
        return (new OpenAiCompatibleClient(http, baseUrl, "sk-test-key"), handler);
    }

    [Fact]
    public async Task TranscribeAsync_SuccessfulResponse_ReturnsParsedText()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"hello world"}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Text.Should().Be("hello world");
    }

    [Fact]
    public async Task TranscribeAsync_ResponseWithLanguage_PopulatesDetectedLanguage()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"привет","language":"russian"}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.DetectedLanguage.Should().Be("russian");
    }

    [Fact]
    public async Task TranscribeAsync_PostsToAudioTranscriptionsEndpoint()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""),
            baseUrl: "https://api.lemonfox.ai/v1");

        await client.TranscribeAsync(SampleRequest());

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.ToString().Should().Be("https://api.lemonfox.ai/v1/audio/transcriptions");
    }

    [Fact]
    public async Task TranscribeAsync_BaseUrlWithTrailingSlash_StillBuildsValidEndpoint()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""),
            baseUrl: "https://api.openai.com/v1/");

        await client.TranscribeAsync(SampleRequest());

        handler.Requests[0].Uri.ToString().Should().Be("https://api.openai.com/v1/audio/transcriptions");
    }

    [Fact]
    public async Task TranscribeAsync_SendsBearerAuthorization()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest());

        handler.Requests[0].Headers["Authorization"].Should().Be("Bearer sk-test-key");
    }

    [Fact]
    public async Task TranscribeAsync_SendsMultipartFormData()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest());

        handler.Requests[0].ContentTypeMediaType.Should().Be("multipart/form-data");
    }

    [Fact]
    public async Task TranscribeAsync_IncludesFileModelAudioInBody()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest(model: "gpt-4o-transcribe"));

        var body = handler.Requests[0].BodyText!;
        body.Should().MatchRegex(@"name=""?file""?");
        body.Should().MatchRegex(@"filename=""?audio\.ogg""?");
        body.Should().MatchRegex(@"name=""?model""?");
        body.Should().Contain("gpt-4o-transcribe");
        body.Should().Contain("FAKE-AUDIO-BYTES");
    }

    [Fact]
    public async Task TranscribeAsync_LanguageSet_IncludesLanguagePart()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest(language: "ru"));

        handler.Requests[0].BodyText.Should().MatchRegex(@"name=""?language""?").And.Contain("ru");
    }

    [Fact]
    public async Task TranscribeAsync_LanguageNull_OmitsLanguagePart()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest(language: null));

        handler.Requests[0].BodyText.Should().NotMatchRegex(@"name=""?language""?");
    }

    [Fact]
    public async Task TranscribeAsync_PromptSet_IncludesPromptPart()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest(prompt: "Tech terms: Anthropic, Opus"));

        handler.Requests[0].BodyText.Should().MatchRegex(@"name=""?prompt""?").And.Contain("Tech terms: Anthropic, Opus");
    }

    [Fact]
    public async Task TranscribeAsync_PromptEmpty_OmitsPromptPart()
    {
        var (client, handler) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"ok"}"""));

        await client.TranscribeAsync(SampleRequest(prompt: ""));

        handler.Requests[0].BodyText.Should().NotMatchRegex(@"name=""?prompt""?");
    }

    [Fact]
    public async Task TranscribeAsync_Status401_ThrowsAuthException()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid api key"}}"""));

        var act = async () => await client.TranscribeAsync(SampleRequest());

        (await act.Should().ThrowAsync<TranscriptionAuthException>()).Which.Message.Should().Contain("invalid api key");
    }

    [Fact]
    public async Task TranscribeAsync_Status429_ThrowsRateLimitException()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}"""));

        var act = async () => await client.TranscribeAsync(SampleRequest());

        await act.Should().ThrowAsync<TranscriptionRateLimitException>();
    }

    [Fact]
    public async Task TranscribeAsync_Status500_ThrowsServerException()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.InternalServerError, "server is sad"));

        var act = async () => await client.TranscribeAsync(SampleRequest());

        (await act.Should().ThrowAsync<TranscriptionServerException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TranscribeAsync_Status400_ThrowsRequestException()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.BadRequest, """{"error":{"message":"bad model name"}}"""));

        var act = async () => await client.TranscribeAsync(SampleRequest());

        var thrown = (await act.Should().ThrowAsync<TranscriptionRequestException>()).Which;
        thrown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        thrown.Message.Should().Contain("bad model name");
    }

    [Fact]
    public async Task TranscribeAsync_TransportFailure_ThrowsServerException()
    {
        var handler = new StubHttpMessageHandler
        {
            Responder = (_, _) => throw new HttpRequestException("network down"),
        };
        var (client, _) = BuildClient(handler);

        var act = async () => await client.TranscribeAsync(SampleRequest());

        await act.Should().ThrowAsync<TranscriptionServerException>();
    }

    [Fact]
    public async Task TranscribeAsync_Cancelled_PropagatesCancellation()
    {
        var handler = new StubHttpMessageHandler
        {
            Responder = async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        var (client, _) = BuildClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.TranscribeAsync(SampleRequest(), cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task TranscribeAsync_MalformedSuccessJson_ThrowsServerException()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, "not json at all"));

        var act = async () => await client.TranscribeAsync(SampleRequest());

        await act.Should().ThrowAsync<TranscriptionServerException>();
    }

    [Fact]
    public void Constructor_EmptyApiKey_Throws()
    {
        var handler = new StubHttpMessageHandler();
        var http = new HttpClient(handler);
        var act = () => new OpenAiCompatibleClient(http, "https://api.openai.com/v1", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyBaseUrl_Throws()
    {
        var handler = new StubHttpMessageHandler();
        var http = new HttpClient(handler);
        var act = () => new OpenAiCompatibleClient(http, "", "sk-test");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task TranscribeAsync_TokenShapedUsage_PopulatesTokenCounts()
    {
        var (client, _) = BuildClient(StubHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"text":"hello","usage":{"type":"tokens","input_tokens":412,"output_tokens":48,"total_tokens":460}}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Usage.Should().NotBeNull();
        result.Usage!.InputTokens.Should().Be(412);
        result.Usage.OutputTokens.Should().Be(48);
        result.Usage.TotalTokens.Should().Be(460);
        result.Usage.BilledSeconds.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_DurationShapedUsage_PopulatesBilledSeconds()
    {
        var (client, _) = BuildClient(StubHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"text":"hello","usage":{"type":"duration","seconds":8.72}}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Usage.Should().NotBeNull();
        result.Usage!.BilledSeconds.Should().Be(8.72);
        result.Usage.InputTokens.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_UsageWithoutTypeField_StillParsesPresentNumbers()
    {
        var (client, _) = BuildClient(StubHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"text":"hello","usage":{"input_tokens":7,"total_tokens":7}}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Usage.Should().NotBeNull();
        result.Usage!.InputTokens.Should().Be(7);
        result.Usage.TotalTokens.Should().Be(7);
        result.Usage.OutputTokens.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_NoUsageBlock_LeavesUsageNull()
    {
        var (client, _) = BuildClient(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"text":"hello"}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Usage.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_MalformedUsageBlock_LeavesUsageNullWithoutFailing()
    {
        var (client, _) = BuildClient(StubHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"text":"hello","usage":"not-an-object"}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Text.Should().Be("hello");
        result.Usage.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_EmptyUsageObject_LeavesUsageNull()
    {
        var (client, _) = BuildClient(StubHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"text":"hello","usage":{}}"""));

        var result = await client.TranscribeAsync(SampleRequest());

        result.Usage.Should().BeNull();
    }
}
