using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SoundButtons.Helper;
using SoundButtons.Models;
using Xabe.FFmpeg;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SoundButtons.Services;

public class ProcessAudioService
{
    private readonly ILogger _logger;
    private readonly string? _youtubeDLPath;

    public ProcessAudioService(ILogger<ProcessAudioService> logger)
    {
        _logger = logger;

        (_youtubeDLPath, string? ffmpegPath) = YoutubeDLHelper.WhereIs();
        FFmpeg.SetExecutablesPath(Path.GetDirectoryName(ffmpegPath));
    }

    public Task<int> DownloadAudioAsync(string tempPath, Source source)
    {
        if (string.IsNullOrEmpty(source.VideoId)) throw new ArgumentNullException(nameof(source));

        OptionSet optionSet = new()
        {
            // 最佳音質
            Format = "251/140",
            NoCheckCertificates = true,
            Output = tempPath,
            ExtractorArgs = "youtube:skip=dash",
            DownloadSections = $"*{source.Start}-{source.End}"
        };

        // 下載音訊來源
        _logger.LogInformation("Start to download audio source from youtube {videoId}", source.VideoId);

        YoutubeDLProcess youtubeDLProcess = new(_youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        youtubeDLProcess.ErrorReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        _logger.LogDebug("yt-dlp arguments: {arguments}", optionSet.ToString());

        return youtubeDLProcess.RunAsync(
            [$"https://youtu.be/{source.VideoId}"],
            optionSet,
            new CancellationToken());
    }

    public Task<int> DownloadAudioAsync(string tempPath, string url)
    {
        if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));

        OptionSet optionSet = new()
        {
            NoCheckCertificates = true,
            Output = tempPath
        };

        // 下載音訊來源
        _logger.LogInformation("Start to download audio source from url {url}", url);

        YoutubeDLProcess youtubeDLProcess = new(_youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        youtubeDLProcess.ErrorReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        _logger.LogDebug("yt-dlp arguments: {arguments}", optionSet.ToString());

        return youtubeDLProcess.RunAsync(
            [url],
            optionSet,
            new CancellationToken());
    }

    public async Task CutAudioAsync(string tempPath, Source source)
    {
        // 剪切音檔
        _logger.LogInformation("Start to cut audio");

        double duration = source.End - source.Start;

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        string outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                       .AddParameter($"-sseof -{duration}", ParameterPosition.PreInput)
                                       .AddStream(mediaInfo.Streams)
                                       .SetOutput(outputPath)
                                       .SetOverwriteOutput(true);

        conversion.OnProgress += (_, e)
            => _logger.LogTrace("Progress: {progress}%", e.Percent);

        conversion.OnDataReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        _logger.LogDebug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();
        File.Move(outputPath, tempPath, true);

        _logger.LogInformation("Cut audio Finish: {path}", tempPath);
        _logger.LogInformation("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
    }

    public async Task<string> TranscodeAudioAsync(string tempPath)
    {
        _logger.LogInformation("Start to transcode audio");

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        string outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                       .AddStream(mediaInfo.Streams)
                                       .AddParameter("-map -0:v")
                                       .SetOutput(outputPath)
                                       .SetOverwriteOutput(true);

        conversion.OnProgress += (_, e)
            => _logger.LogTrace("Progress: {progress}%", e.Percent);

        conversion.OnDataReceived += (_, e)
            => _logger.LogTrace(e.Data ?? "");

        _logger.LogDebug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();

        string newPath = Path.ChangeExtension(tempPath, ".webm");
        File.Move(outputPath, newPath, true);

        _logger.LogInformation("Transcode audio Finish: {path}", newPath);
        _logger.LogInformation("Transcode audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
        return newPath;
    }
}