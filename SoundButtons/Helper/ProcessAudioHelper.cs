﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SoundButtons.Models;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SoundButtons.Helper;

internal static class ProcessAudioHelper
{
    private static ILogger Logger => Log.Logger;

    internal static Task UpdateFFmpegAsync(string tempDir)
    {
        FFmpeg.SetExecutablesPath(tempDir);
        return FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official,
                                                 FFmpeg.ExecutablesPath);
    }

    internal static async Task<string> UpdateYtdlpAsync(string tempDir)
    {
        var useBuiltInYtdlp = bool.Parse(Environment.GetEnvironmentVariable("UseBuiltInYtdlp") ?? "false");
        string youtubeDLPath = Path.Combine(tempDir, "yt-dlp.exe");

        if (useBuiltInYtdlp) return UseBuiltInYtdlp();

        if (File.Exists(youtubeDLPath)) return youtubeDLPath;

        try
        {
            // 同步下載youtube-dl.exe (yt-dlp.exe)
            HttpClient httpClient = new();
            using HttpResponseMessage response =
                await httpClient.GetAsync(new Uri(@"https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe").ToString());

            response.EnsureSuccessStatusCode();
            await using Stream ms = await response.Content.ReadAsStreamAsync();
            await using FileStream fs = File.Create(youtubeDLPath);
            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(fs);
            await fs.FlushAsync();
            Logger.Information("Download yt-dlp.exe at {ytdlPath}", youtubeDLPath);
            return youtubeDLPath;
        }
        catch (Exception e)
        {
            Logger.Warning("Cannot download yt-dlp. {exception}: {exception}", nameof(e), e.Message);
            return UseBuiltInYtdlp();
        }

        string UseBuiltInYtdlp()
        {
            File.Copy(@"C:\home\site\wwwroot\yt-dlp.exe", youtubeDLPath, true);
            Logger.Information("Use built-in yt-dlp.exe");
            return youtubeDLPath;
        }
    }

    internal static Task<int> DownloadAudioAsync(string youtubeDLPath, string tempPath, Source source)
    {
        if (string.IsNullOrEmpty(source.VideoId)) throw new ArgumentNullException(nameof(source));

        OptionSet optionSet = new()
        {
            // 最佳音質
            Format = "251",
            NoCheckCertificates = true,
            Output = tempPath,
            ExtractorArgs = "youtube:skip=dash",
            DownloadSections = $"*{source.Start}-{source.End}"
        };

        // 下載音訊來源
        Logger.Information("Start to download audio source from youtube {videoId}", source.VideoId);

        YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        youtubeDLProcess.ErrorReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        Logger.Debug("yt-dlp arguments: {arguments}", optionSet.ToString());

        return youtubeDLProcess.RunAsync(
            new[] { $"https://youtu.be/{source.VideoId}" },
            optionSet,
            new CancellationToken());
    }

    internal static Task<int> DownloadAudioAsync(string youtubeDLPath, string tempPath, string url)
    {
        if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));

        OptionSet optionSet = new()
        {
            NoCheckCertificates = true,
            Output = tempPath
        };

        // 下載音訊來源
        Logger.Information("Start to download audio source from url {url}", url);

        YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        youtubeDLProcess.ErrorReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        Logger.Debug("yt-dlp arguments: {arguments}", optionSet.ToString());

        return youtubeDLProcess.RunAsync(
            new[] { url },
            optionSet,
            new CancellationToken());
    }

    internal static async Task CutAudioAsync(string tempPath, Source source)
    {
        // 剪切音檔
        Logger.Information("Start to cut audio");

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
            => Logger.Verbose("Progress: {progress}%", e.Percent);

        conversion.OnDataReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        Logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();
        File.Move(outputPath, tempPath, true);

        Logger.Information("Cut audio Finish: {path}", tempPath);
        Logger.Information("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
    }

    internal static async Task<string> TranscodeAudioAsync(string tempPath)
    {
        await UpdateFFmpegAsync(Path.GetDirectoryName(tempPath)!);

        Logger.Information("Start to transcode audio");

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        string outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                       .AddStream(mediaInfo.Streams)
                                       .AddParameter("-map -0:v")
                                       .SetOutput(outputPath)
                                       .SetOverwriteOutput(true);

        conversion.OnProgress += (_, e)
            => Logger.Verbose("Progress: {progress}%", e.Percent);

        conversion.OnDataReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");

        Logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();

        string newPath = Path.ChangeExtension(tempPath, ".webm");
        File.Move(outputPath, newPath, true);

        Logger.Information("Transcode audio Finish: {path}", newPath);
        Logger.Information("Transcode audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
        return newPath;
    }
}