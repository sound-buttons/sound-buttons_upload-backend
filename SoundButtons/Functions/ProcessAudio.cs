using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SoundButtons.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName("ProcessAudioAsync")]
    public static async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Source source,
        ILogger log,
        [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
    {
#if DEBUG
        string tempDir = Path.GetTempPath();
#else
        string tempDir = @"C:\home\data";
#endif
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".webm");

        log.LogInformation("TempDir: {tempDir}", tempDir);

        var task1 = UpdateFFmpegAsync(tempDir, log);
        var task2 = UpdateYtdlpAsync(tempDir, log);

        await Task.WhenAll(task1, task2);
        string youtubeDLPath = task2.Result;

        await DownloadAudioAsync(youtubeDLPath, tempPath, source, log);
        await CutAudioAsync(tempPath, source, log);
        return tempPath;
    }

    private static Task UpdateFFmpegAsync(string tempDir, ILogger log)
    {
        FFmpeg.SetExecutablesPath(tempDir);
        return FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official,
                                                 FFmpeg.ExecutablesPath,
                                                 new Progress<ProgressInfo>(p => log.LogTrace($"{p.DownloadedBytes}/{p.TotalBytes}")));
    }

    private static async Task<string> UpdateYtdlpAsync(string tempDir, ILogger log)
    {
        string youtubeDLPath = Path.Combine(tempDir, DateTime.Now.DayOfYear + "yt-dlp.exe");
        if (!File.Exists(youtubeDLPath))
        {
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
            }
            catch (Exception)
            {
                // Download failed fallback
                if (File.Exists("yt-dlp.exe"))
                    File.Copy("yt-dlp.exe", youtubeDLPath, true);
            }
            log.LogInformation("Download yt-dlp.exe at {ytdlPath}", youtubeDLPath);
        }

        return youtubeDLPath;
    }

    private static Task<int> DownloadAudioAsync(string youtubeDLPath, string tempPath, Source source, ILogger log)
    {
        OptionSet optionSet = new()
        {
            // 最佳音質
            Format = "251",
            NoCheckCertificate = true,
            Output = tempPath
        };
        optionSet.AddCustomOption("--extractor-args", "youtube:skip=dash");
        optionSet.AddCustomOption("--download-sections", $"*{source.start}-{source.end}");
        //optionSet.ExternalDownloader = "ffmpeg";
        //optionSet.ExternalDownloaderArgs = $"ffmpeg_i:-ss {source.start} -to {source.end}";

        // 下載音訊來源
        log.LogInformation("Start to download audio source from youtube {videoId}", source.videoId);

        YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => log.LogInformation(e.Data);
        youtubeDLProcess.ErrorReceived += (_, e)
            => log.LogError(e.Data);

        return youtubeDLProcess.RunAsync(
            new string[] { @$"https://youtu.be/{source.videoId}" },
            optionSet,
            new System.Threading.CancellationToken());
    }

    private static async Task CutAudioAsync(string tempPath, Source source, ILogger log)
    {
        // 剪切音檔
        log.LogInformation("Start to cut audio");

        double duration = source.end - source.start;

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                   .AddParameter($"-sseof -{duration}", ParameterPosition.PreInput)
                                   .AddStream(mediaInfo.Streams)
                                   .SetOutput(outputPath)
                                   .SetOverwriteOutput(true);
        conversion.OnProgress += (_, e)
            => log.LogInformation("Progress: {progress}%", e.Percent);
        conversion.OnDataReceived += (_, e)
            => log.LogWarning(e.Data);
        log.LogDebug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();
        File.Move(outputPath, tempPath, true);

        log.LogInformation("Cut audio Finish: {path}", tempPath);
        log.LogInformation("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
    }

    private static async Task<string> TranscodeAudioAsync(string tempPath, ILogger log)
    {
        await UpdateFFmpegAsync(Path.GetDirectoryName(tempPath), log);

        log.LogInformation("Start to transcode audio");

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                   .AddStream(mediaInfo.Streams)
                                   .SetOutput(outputPath)
                                   .SetOverwriteOutput(true);
        conversion.OnProgress += (_, e)
            => log.LogInformation("Progress: {progress}%", e.Percent);
        conversion.OnDataReceived += (_, e)
            => log.LogWarning(e.Data);
        log.LogDebug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();

        var newPath = Path.ChangeExtension(tempPath, ".webm");
        File.Move(outputPath, newPath, true);

        log.LogInformation("Transcode audio Finish: {path}", newPath);
        log.LogInformation("Transcode audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
        return newPath;
    }

}
