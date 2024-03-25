using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Models;
using SoundButtons.Services;
using Log = SoundButtons.Helper.Log;

namespace SoundButtons.Functions;

public class SpeechToText
{
    private static ILogger Logger => Log.Logger;

    [FunctionName(nameof(SpeechToTextAsync))]
    public async Task<Request> SpeechToTextAsync(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        Logger.Information("Start to do speech to text.");
        var openAIService = new OpenAIService();
        try
        {
            if (request.NameJP == "[useSTT]")
            {
                OpenAI.TranscriptionsResponse? speechToTextJP = await openAIService.SpeechToTextAsync(request.TempPath, "ja");

                request.NameJP = speechToTextJP?.Text ?? "";
            }
        }
        catch (HttpRequestException e)
        {
            Logger.Error(e, "Failed to get STT ja response.");
        }

        return request;
    }
}