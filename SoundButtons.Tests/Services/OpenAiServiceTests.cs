using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SoundButtons.Services;
using SoundButtons.Tests.Fakes;
using Xunit;
using static SoundButtons.Models.OpenAI;

namespace SoundButtons.Tests.Services;

[Trait("spec", "speech-to-text-transcription")]
[Collection("OpenAiService")]
public class OpenAiServiceTests
{
    private static string CreateTempAudioFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "sb-stt-" + Guid.NewGuid().ToString("n") + ".webm");
        File.WriteAllBytes(path, [0x1A, 0x45, 0xDF, 0xA3]);
        return path;
    }

    [Fact]
    public async Task SpeechToTextAsync_SendsAuthorizedMultipartRequest()
    {
        Environment.SetEnvironmentVariable("OpenAI_ApiKey", "test-key");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{\"text\":\"hello world\"}");
        var service = new OpenAiService(NullLogger<OpenAiService>.Instance, new FakeHttpClientFactory(handler));
        string path = CreateTempAudioFile();
        try
        {
            TranscriptionsResponse? result = await service.SpeechToTextAsync(path, "ja");

            Assert.NotNull(result);
            Assert.Equal("hello world", result!.Text);

            HttpRequestMessage request = Assert.Single(handler.Requests);
            Assert.EndsWith("audio/transcriptions", request.RequestUri!.ToString());
            Assert.Equal("Bearer test-key", request.Headers.GetValues("Authorization").First());
            Assert.IsType<MultipartFormDataContent>(request.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeechToTextAsync_NoApiKey_ReturnsEmptyWithoutCall()
    {
        Environment.SetEnvironmentVariable("OpenAI_ApiKey", "");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{\"text\":\"should not be called\"}");
        var service = new OpenAiService(NullLogger<OpenAiService>.Instance, new FakeHttpClientFactory(handler));

        TranscriptionsResponse? result = await service.SpeechToTextAsync("/nonexistent.webm");

        Assert.NotNull(result);
        Assert.Null(result!.Text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SpeechToTextAsync_HttpFailure_Throws()
    {
        Environment.SetEnvironmentVariable("OpenAI_ApiKey", "test-key");
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var service = new OpenAiService(NullLogger<OpenAiService>.Instance, new FakeHttpClientFactory(handler));
        string path = CreateTempAudioFile();
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => service.SpeechToTextAsync(path, "ja"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[CollectionDefinition("OpenAiService", DisableParallelization = true)]
public class OpenAiServiceCollection;
