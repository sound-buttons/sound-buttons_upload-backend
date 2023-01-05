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
    public async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Source source,
        [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
    {
        string tempPath = Path.Combine(_tempDir, DateTime.Now.Ticks.ToString() + ".webm");

        _logger.Information("TempDir: {tempDir}", _tempDir);

        var task1 = UpdateFFmpegAsync(_tempDir);
        var task2 = UpdateYtdlpAsync(_tempDir);

        await Task.WhenAll(task1, task2);
        string youtubeDLPath = task2.Result;

        await DownloadAudioAsync(youtubeDLPath, tempPath, source);
        await CutAudioAsync(tempPath, source);
        return tempPath;
    }

    private Task UpdateFFmpegAsync(string tempDir)
    {
        FFmpeg.SetExecutablesPath(tempDir);
        return FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official,
                                                 FFmpeg.ExecutablesPath,
                                                 new Progress<ProgressInfo>(p => _logger.Verbose($"{p.DownloadedBytes}/{p.TotalBytes}")));
    }

    private async Task<string> UpdateYtdlpAsync(string tempDir)
    {
        bool useBuiltInYtdlp = bool.Parse(Environment.GetEnvironmentVariable("UseBuiltInYtdlp"));
        string youtubeDLPath = Path.Combine(tempDir, DateTime.Today.Ticks + "yt-dlp.exe");

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
            _logger.Information("Download yt-dlp.exe at {ytdlPath}", youtubeDLPath);
            return youtubeDLPath;
        }
        catch (Exception e)
        {
            _logger.Warning("Cannot download yt-dlp. {exception}: {exception}", nameof(e), e.Message);
            return UseBuiltInYtdlp();
        }

        string UseBuiltInYtdlp()
        {
            File.Copy(@"C:\home\site\wwwroot\yt-dlp.exe", youtubeDLPath, true);
            _logger.Information("Use built-in yt-dlp.exe");
            return youtubeDLPath;
        }
    }

    private Task<int> DownloadAudioAsync(string youtubeDLPath, string tempPath, Source source)
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
        _logger.Information("Start to download audio source from youtube {videoId}", source.videoId);

        YoutubeDLProcess youtubeDLProcess = new(youtubeDLPath);

        youtubeDLProcess.OutputReceived += (_, e)
            => _logger.Information(e.Data);
        youtubeDLProcess.ErrorReceived += (_, e)
            => _logger.Error(e.Data);

        return youtubeDLProcess.RunAsync(
            new string[] { @$"https://youtu.be/{source.videoId}" },
            optionSet,
            new System.Threading.CancellationToken());
    }

    private async Task CutAudioAsync(string tempPath, Source source)
    {
        // 剪切音檔
        _logger.Information("Start to cut audio");

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
            => _logger.Information("Progress: {progress}%", e.Percent);
        conversion.OnDataReceived += (_, e)
            => _logger.Warning(e.Data);
        _logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();
        File.Move(outputPath, tempPath, true);

        _logger.Information("Cut audio Finish: {path}", tempPath);
        _logger.Information("Cut audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
    }

    private async Task<string> TranscodeAudioAsync(string tempPath)
    {
        await UpdateFFmpegAsync(Path.GetDirectoryName(tempPath));

        _logger.Information("Start to transcode audio");

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempPath);
        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".webm");

        IConversion conversion = FFmpeg.Conversions.New()
                                   .AddStream(mediaInfo.Streams)
                                   .AddParameter("-map 0:a", ParameterPosition.PostInput)
                                   .SetOutput(outputPath)
                                   .SetOverwriteOutput(true);
        conversion.OnProgress += (_, e)
            => _logger.Information("Progress: {progress}%", e.Percent);
        conversion.OnDataReceived += (_, e)
            => _logger.Warning(e.Data);
        _logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());

        IConversionResult convRes = await conversion.Start();

        var newPath = Path.ChangeExtension(tempPath, ".webm");
        File.Move(outputPath, newPath, true);

        _logger.Information("Transcode audio Finish: {path}", newPath);
        _logger.Information("Transcode audio Finish in {duration} seconds.", convRes.Duration.TotalSeconds);
        return newPath;
    }

}
