using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Functions;
using SoundButtons.Models;
using SoundButtons.Services;
using Xunit;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "audio-acquisition-encoding")]
public class ProcessAudioFunctionTests
{
    private static Request VideoRequest()
        => new() { Source = new Source("Gs7QYATahy4", 0, 10), Clip = "", InstanceId = "i" };

    [Fact]
    public async Task ProcessAudioAsync_VideoSource_DownloadsAndCuts()
    {
        var mock = new Mock<IProcessAudioService>();
        mock.Setup(s => s.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<Source>()))
            .Returns((string path, Source _) =>
            {
                File.WriteAllText(path, "audio");
                return Task.FromResult(0);
            });
        mock.Setup(s => s.CutAudioAsync(It.IsAny<string>(), It.IsAny<Source>())).Returns(Task.CompletedTask);

        var function = new ProcessAudio(NullLogger<ProcessAudio>.Instance, mock.Object);

        string result = await function.ProcessAudioAsync(VideoRequest());

        try
        {
            mock.Verify(s => s.CutAudioAsync(result, It.IsAny<Source>()), Times.Once);
        }
        finally
        {
            if (File.Exists(result)) File.Delete(result);
        }
    }

    [Fact]
    public async Task ProcessAudioAsync_VideoDownloadFails_SkipsCut()
    {
        var mock = new Mock<IProcessAudioService>();
        mock.Setup(s => s.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<Source>())).ReturnsAsync(0);

        var function = new ProcessAudio(NullLogger<ProcessAudio>.Instance, mock.Object);

        await function.ProcessAudioAsync(VideoRequest());

        mock.Verify(s => s.CutAudioAsync(It.IsAny<string>(), It.IsAny<Source>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAudioAsync_ClipSource_DownloadsAndTranscodes()
    {
        var mock = new Mock<IProcessAudioService>();
        mock.Setup(s => s.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string path, string _) =>
            {
                File.WriteAllText(path, "audio");
                return Task.FromResult(0);
            });
        mock.Setup(s => s.TranscodeAudioAsync(It.IsAny<string>())).ReturnsAsync("/tmp/out.webm");

        var function = new ProcessAudio(NullLogger<ProcessAudio>.Instance, mock.Object);
        var request = new Request { Source = new Source { VideoId = "" }, Clip = "https://clips.twitch.tv/x", InstanceId = "i" };

        string result = await function.ProcessAudioAsync(request);

        Assert.Equal("/tmp/out.webm", result);
        mock.Verify(s => s.TranscodeAudioAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAudioAsync_ClipDownloadFails_SkipsTranscode()
    {
        var mock = new Mock<IProcessAudioService>();
        mock.Setup(s => s.DownloadAudioAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(0);

        var function = new ProcessAudio(NullLogger<ProcessAudio>.Instance, mock.Object);
        var request = new Request { Source = new Source { VideoId = "" }, Clip = "https://clips.twitch.tv/x", InstanceId = "i" };

        await function.ProcessAudioAsync(request);

        mock.Verify(s => s.TranscodeAudioAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAudioAsync_NoSource_ReturnsTempPathWithoutCalls()
    {
        var mock = new Mock<IProcessAudioService>(MockBehavior.Strict);
        var function = new ProcessAudio(NullLogger<ProcessAudio>.Instance, mock.Object);
        var request = new Request { Source = new Source { VideoId = "" }, Clip = "", InstanceId = "i" };

        string result = await function.ProcessAudioAsync(request);

        Assert.EndsWith(".webm", result);
    }
}
