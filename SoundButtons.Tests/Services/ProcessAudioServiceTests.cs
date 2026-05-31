using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SoundButtons.Models;
using SoundButtons.Services;
using YoutubeDLSharp.Options;
using Xunit;

namespace SoundButtons.Tests.Services;

[Trait("spec", "audio-acquisition-encoding")]
public class ProcessAudioServiceTests
{
    [Fact]
    public void BuildVideoIdOptionSet_SelectsBestAudioAndSection()
    {
        var source = new Source("Gs7QYATahy4", 5, 12);

        OptionSet options = ProcessAudioService.BuildVideoIdOptionSet(source, "/tmp/out.webm");

        Assert.Equal("251/140", options.Format);
        Assert.Equal("/tmp/out.webm", options.Output);
        Assert.Equal("*5-12", options.DownloadSections.Values[0]);
        Assert.True(options.NoCheckCertificates);
        Assert.Equal("youtube:skip=dash", options.ExtractorArgs.Values[0]);
    }

    [Fact]
    public void BuildClipOptionSet_SetsOutputAndNoFormat()
    {
        OptionSet options = ProcessAudioService.BuildClipOptionSet("https://clip", "/tmp/clip.webm");

        Assert.Equal("/tmp/clip.webm", options.Output);
        Assert.True(options.NoCheckCertificates);
        Assert.Null(options.Format);
    }

    [Fact]
    public async Task DownloadAudioAsync_EmptyVideoId_Throws()
    {
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.DownloadAudioAsync("/tmp/x.webm", new Source { VideoId = "" }));
    }

    [Fact]
    public async Task DownloadAudioAsync_EmptyUrl_Throws()
    {
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.DownloadAudioAsync("/tmp/x.webm", ""));
    }
}
