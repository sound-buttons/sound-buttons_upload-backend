using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Helper;
using SoundButtons.Models;
using Log = SoundButtons.Helper.Log;

namespace SoundButtons.Functions;

public class ProcessAudio
{
    private static ILogger Logger => Log.Logger;

    [FunctionName("ProcessAudioAsync")]
    public static async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Request request,
        [Blob("sound-buttons")] [StorageAccount("AzureStorage")]
        BlobContainerClient blobContainerClient)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        string tempDir = FileHelper.PrepareTempDir();
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks + ".webm");

        Logger.Information("TempDir: {tempDir}", tempDir);

        Task task1 = ProcessAudioHelper.UpdateFFmpegAsync(tempDir);
        Task<string> task2 = ProcessAudioHelper.UpdateYtdlpAsync(tempDir);

        await Task.WhenAll(task1, task2);
        string youtubeDLPath = task2.Result;

        if (!string.IsNullOrEmpty(request.Source.VideoId))
        {
            await ProcessAudioHelper.DownloadAudioAsync(youtubeDLPath, tempPath, request.Source);
            await ProcessAudioHelper.CutAudioAsync(tempPath, request.Source);
        }
        else
        {
            await ProcessAudioHelper.DownloadAudioAsync(youtubeDLPath, tempPath, request.Clip);
        }

        return tempPath;
    }
}