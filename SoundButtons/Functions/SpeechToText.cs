using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog;
using Serilog.Context;
using SoundButtons.Models;
using SoundButtons.Services;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoundButtons.Functions;

public class SpeechToText
{
    private static ILogger Logger => Helper.Log.Logger;

    [FunctionName(nameof(SpeechToTextAsync))]
    public async Task<Request> SpeechToTextAsync(
        [ActivityTrigger] Request request)
    {
        using var _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        Logger.Information($"Start to do speech to text.");
        var openAIService = new OpenAIService();
        try
        {
            if (string.IsNullOrEmpty(request.NameJP))
            {
                var speechToTextJP = await openAIService.SpeechToTextAsync(request.TempPath, "ja");

                request.NameJP = speechToTextJP?.Text ?? "";
            }
        }
        catch (HttpRequestException e)
        {
            Logger.Error(e, "Failed to get STT ja response.");
        }

        try
        {
            if (string.IsNullOrEmpty(request.NameZH))
            {
                Logger.Information($"Start to process zh.");
                var speechToTextZH = await openAIService.SpeechToTextAsync(request.TempPath, "zh");

                request.NameZH = speechToTextZH?.Text ?? "";
            }
        }
        catch (HttpRequestException e)
        {
            Logger.Error(e, "Failed to get STT zh response.");
        }

        return request;
    }
}