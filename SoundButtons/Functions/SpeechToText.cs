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
            if (request.NameJP == "[useSTT]")
            {
                var speechToTextJP = await openAIService.SpeechToTextAsync(request.TempPath, "ja");

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