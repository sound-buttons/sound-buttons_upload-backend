using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Helper;
using SoundButtons.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SoundButtons.Functions;

public class ProcessAudio
{
    private static ILogger Logger => Helper.Log.Logger;

    [FunctionName("ProcessAudioAsync")]
    public static async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Request request,
        [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient blobContainerClient)
    {
        using var _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        var tempDir = Helper.FileHelper.PrepareTempDir();
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks.ToString() + ".webm");

        Logger.Information("TempDir: {tempDir}", tempDir);

        var task1 = ProcessAudioHelper.UpdateFFmpegAsync(tempDir);
        var task2 = ProcessAudioHelper.UpdateYtdlpAsync(tempDir);

        await Task.WhenAll(task1, task2);
        string youtubeDLPath = task2.Result;

        await ProcessAudioHelper.DownloadAudioAsync(youtubeDLPath, tempPath, request.Source);
        await ProcessAudioHelper.CutAudioAsync(tempPath, request.Source);
        return tempPath;
    }
}
