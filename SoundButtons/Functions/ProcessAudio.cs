using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Helper;
using SoundButtons.Models;

namespace SoundButtons.Functions;

public class ProcessAudio(ILogger<ProcessAudio> logger)
{
    [Function("ProcessAudioAsync")]
    public async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        string tempDir = FileHelper.PrepareTempDir();
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks + ".webm");

        logger.LogInformation("TempDir: {tempDir}", tempDir);

        Task task1 = ProcessAudioHelper.UpdateFFmpegAsync(tempDir);
        Task<string> task2 = ProcessAudioHelper.UpdateYtdlpAsync(tempDir);

        await Task.WhenAll(task1, task2);
        string youtubeDLPath = task2.Result;

        if (!string.IsNullOrEmpty(request.Source.VideoId))
        {
            await ProcessAudioHelper.DownloadAudioAsync(youtubeDLPath, tempPath, request.Source);
            await ProcessAudioHelper.CutAudioAsync(tempPath, request.Source);
        }
        else if (!string.IsNullOrEmpty(request.Clip))
        {
            await ProcessAudioHelper.DownloadAudioAsync(youtubeDLPath, tempPath, request.Clip);
        }

        return tempPath;
    }
}