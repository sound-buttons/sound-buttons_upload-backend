using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Functions;
using SoundButtons.Models;
using SoundButtons.Services;
using Xunit;
using static SoundButtons.Models.OpenAI;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "speech-to-text-transcription")]
public class SpeechToTextFunctionTests
{
    [Fact]
    public async Task SpeechToTextAsync_WithSentinel_FillsNameJp()
    {
        var mock = new Mock<IOpenAiService>();
        mock.Setup(s => s.SpeechToTextAsync(It.IsAny<string>(), "ja"))
            .ReturnsAsync(new TranscriptionsResponse { Text = "認識結果" });

        var function = new SpeechToText(NullLogger<SpeechToText>.Instance, mock.Object);
        var request = new Request { NameJP = "[useSTT]", TempPath = "/tmp/a.webm", InstanceId = "i" };

        Request result = await function.SpeechToTextAsync(request);

        Assert.Equal("認識結果", result.NameJP);
        mock.Verify(s => s.SpeechToTextAsync("/tmp/a.webm", "ja"), Times.Once);
    }

    [Fact]
    public async Task SpeechToTextAsync_WithoutSentinel_DoesNothing()
    {
        var mock = new Mock<IOpenAiService>(MockBehavior.Strict);
        var function = new SpeechToText(NullLogger<SpeechToText>.Instance, mock.Object);
        var request = new Request { NameJP = "ありさか", TempPath = "/tmp/a.webm", InstanceId = "i" };

        Request result = await function.SpeechToTextAsync(request);

        Assert.Equal("ありさか", result.NameJP);
    }

    [Fact]
    public async Task SpeechToTextAsync_HttpException_IsSwallowed()
    {
        var mock = new Mock<IOpenAiService>();
        mock.Setup(s => s.SpeechToTextAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var function = new SpeechToText(NullLogger<SpeechToText>.Instance, mock.Object);
        var request = new Request { NameJP = "[useSTT]", TempPath = "/tmp/a.webm", InstanceId = "i" };

        Request result = await function.SpeechToTextAsync(request);

        // Sentinel remains because the assignment never completed.
        Assert.Equal("[useSTT]", result.NameJP);
    }

    [Fact]
    public async Task SpeechToTextAsync_NullText_SetsEmptyString()
    {
        var mock = new Mock<IOpenAiService>();
        mock.Setup(s => s.SpeechToTextAsync(It.IsAny<string>(), "ja"))
            .ReturnsAsync(new TranscriptionsResponse { Text = null });

        var function = new SpeechToText(NullLogger<SpeechToText>.Instance, mock.Object);
        var request = new Request { NameJP = "[useSTT]", TempPath = "/tmp/a.webm", InstanceId = "i" };

        Request result = await function.SpeechToTextAsync(request);

        Assert.Equal("", result.NameJP);
    }

    [Fact]
    public async Task SpeechToTextAsync_NullResponse_SetsEmptyString()
    {
        var mock = new Mock<IOpenAiService>();
        mock.Setup(s => s.SpeechToTextAsync(It.IsAny<string>(), "ja"))
            .ReturnsAsync((TranscriptionsResponse?)null);

        var function = new SpeechToText(NullLogger<SpeechToText>.Instance, mock.Object);
        var request = new Request { NameJP = "[useSTT]", TempPath = "/tmp/a.webm", InstanceId = "i" };

        Request result = await function.SpeechToTextAsync(request);

        Assert.Equal("", result.NameJP);
    }
}

[Trait("spec", "audio-submission-api")]
public class UtilityFunctionTests
{
    [Fact]
    public void Healthz_ReturnsOk()
    {
        var function = new Utility(NullLogger<Utility>.Instance);

        IActionResult result = function.Healthz(new DefaultHttpContext().Request);

        Assert.IsType<OkResult>(result);
    }
}
