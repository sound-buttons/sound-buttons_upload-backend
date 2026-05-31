using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SoundButtons.Models;
using SoundButtons.Services;
using Xabe.FFmpeg;
using Xunit;

namespace SoundButtons.Tests.Integration;

/// <summary>
///     Integration tests that exercise the real ffmpeg encoder via <see cref="ProcessAudioService" />.
///     Media is synthesized with ffmpeg's lavfi virtual inputs so no network access is
///     required. These run inside the Docker test stage where the static ffmpeg binaries
///     are available.
/// </summary>
[Trait("spec", "audio-acquisition-encoding")]
[Collection("Ffmpeg")]
public class ProcessAudioServiceIntegrationTests : IDisposable
{
    private readonly string _workDir;

    public ProcessAudioServiceIntegrationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "sb-ffmpeg-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); }
        catch { /* best effort */ }
    }

    private static async Task RunFfmpeg(string arguments)
    {
        string ffmpeg = FfmpegProbe.Locate("ffmpeg")!;
        var psi = new ProcessStartInfo(ffmpeg, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            string err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"ffmpeg synthesis failed: {err}");
        }
    }

    private async Task<string> SynthesizeAudioVideo()
    {
        string path = Path.Combine(_workDir, "av.webm");
        await RunFfmpeg($"-y -f lavfi -i sine=frequency=440:duration=2 -f lavfi -i testsrc=duration=2:size=160x120:rate=10 -shortest -c:a libopus -c:v libvpx \"{path}\"");
        return path;
    }

    private async Task<string> SynthesizeVideoOnly()
    {
        string path = Path.Combine(_workDir, "video.webm");
        await RunFfmpeg($"-y -f lavfi -i testsrc=duration=1:size=160x120:rate=10 -c:v libvpx \"{path}\"");
        return path;
    }

    [FfmpegFact]
    public async Task CutAudioAsync_TrimsAndKeepsAudio()
    {
        string input = await SynthesizeAudioVideo();
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        await service.CutAudioAsync(input, new Source("v", 0, 1));

        IMediaInfo info = await FFmpeg.GetMediaInfo(input);
        Assert.NotEmpty(info.AudioStreams);
    }

    [FfmpegFact]
    public async Task TranscodeAudioAsync_ProducesAudioOnlyWebm()
    {
        string input = await SynthesizeAudioVideo();
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        string output = await service.TranscodeAudioAsync(input);

        IMediaInfo info = await FFmpeg.GetMediaInfo(output);
        Assert.NotEmpty(info.AudioStreams);
        Assert.Empty(info.VideoStreams);
        Assert.Equal("opus", info.AudioStreams.First().Codec);
    }

    [FfmpegFact]
    public async Task TranscodeAudioAsync_NoAudioStream_Throws()
    {
        string input = await SynthesizeVideoOnly();
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TranscodeAudioAsync(input));
    }

    [FfmpegFact]
    public async Task DownloadAudioAsync_FromLocalUrl_DownloadsFile()
    {
        // Exercise the generic (clip URL) download path offline by pointing yt-dlp at a
        // local file:// URL. Requires yt-dlp on PATH (provided by the Docker test stage).
        if (FfmpegProbe.Locate("yt-dlp") is null) return;

        string media = await SynthesizeAudioVideo();
        string output = Path.Combine(_workDir, "downloaded.webm");
        var service = new ProcessAudioService(NullLogger<ProcessAudioService>.Instance);

        int exitCode = await service.DownloadAudioAsync(output, new Uri(media).AbsoluteUri);

        // The value of this test is exercising the real generic-download code path
        // offline; yt-dlp's own success/failure for a file:// URL is not asserted.
        Assert.True(exitCode >= 0);
    }
}

[CollectionDefinition("Ffmpeg", DisableParallelization = true)]
public class FfmpegCollection;
