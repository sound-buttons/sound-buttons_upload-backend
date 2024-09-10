using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Helper;
using SoundButtons.Models;
using SoundButtons.Services;

namespace SoundButtons.Functions;

public class ProcessAudio(ILogger<ProcessAudio> logger,
                          ProcessAudioService processAudioService)
{
    [Function("ProcessAudioAsync")]
    public async Task<string> ProcessAudioAsync(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        string tempDir = FileHelper.PrepareTempDir();
        string tempPath = Path.Combine(tempDir, DateTime.Now.Ticks + ".webm");

        logger.LogInformation("TempDir: {tempDir}", tempDir);

        if (!string.IsNullOrEmpty(request.Source.VideoId))
        {
            await processAudioService.DownloadAudioAsync(tempPath, request.Source);
            await processAudioService.CutAudioAsync(tempPath, request.Source);
        }
        else if (!string.IsNullOrEmpty(request.Clip))
        {
            await processAudioService.DownloadAudioAsync(tempPath, request.Clip);
        }

        return tempPath;
    }
}