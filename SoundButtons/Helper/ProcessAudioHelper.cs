using Serilog;
using SoundButtons.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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
        bool useBuiltInYtdlp = bool.Parse(Environment.GetEnvironmentVariable("UseBuiltInYtdlp") ?? "false");
        string youtubeDLPath = Path.Combine(tempDir, "yt-dlp.exe");

        if (useBuiltInYtdlp) return UseBuiltInYtdlp();

        if (File.Exists(youtubeDLPath)) return youtubeDLPath;

        try
        {
            // 同步下載youtube-dl.exe (yt-dlp.exe)
            HttpClient httpClient = new();
            using HttpResponseMessage response = await httpClient.GetAsync(new Uri(@"https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe").ToString());
            response.EnsureSuccessStatusCode();
            using var ms = await response.Content.ReadAsStreamAsync();
            using var fs = File.Create(youtubeDLPath);
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
        OptionSet optionSet = new()
        {
            // 最佳音質
            Format = "251",
            NoCheckCertificate = true,
            Output = tempPath
        };
        optionSet.AddCustomOption("--extractor-args", "youtube:skip=dash");
        optionSet.AddCustomOption("--download-sections", $"*{source.Start}-{source.End}");
        //optionSet.ExternalDownloader = "ffmpeg";
        //optionSet.ExternalDownloaderArgs = $"ffmpeg_i:-ss {source.start} -to {source.end}";

        // 下載音訊來源
        Logger.Information("Start to download audio source from youtube {videoId}", source.VideoId);

        YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");
        youtubeDLProcess.ErrorReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");
        Logger.Debug("yt-dlp arguments: {arguments}", optionSet.ToString());

        return youtubeDLProcess.RunAsync(
            new string[] { @$"https://youtu.be/{source.VideoId}" },
            optionSet,
            new System.Threading.CancellationToken());
    }

    internal static async Task CutAudioAsync(string tempPath, Source source)
    {
        // 剪切音檔
        Logger.Information("Start to cut audio");

        double duration = source.End - source.Start;

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        var outputPath = Path.GetTempFileName();
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
        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                   .AddStream(mediaInfo.Streams)
                                   .AddParameter("-map 0:a", ParameterPosition.PostInput)
                                   .SetOutput(outputPath)
                                   .SetOverwriteOutput(true);
        conversion.OnProgress += (_, e)
            => Logger.Verbose("Progress: {progress}%", e.Percent);
        conversion.OnDataReceived += (_, e)
            => Logger.Verbose(e.Data ?? "");
        Logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();

        var newPath = Path.ChangeExtension(tempPath, ".webm");
        File.Move(outputPath, newPath, true);

        Logger.Information("Transcode audio Finish: {path}", newPath);
        Logger.Information("Transcode audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
        return newPath;
    }
}
