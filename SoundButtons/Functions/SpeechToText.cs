using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Models;
using SoundButtons.Services;

namespace SoundButtons.Functions;

public class SpeechToText(ILogger<SpeechToText> logger,
                          OpenAIService openAIService)
{
    private readonly ILogger _logger = logger;

    [Function(nameof(SpeechToTextAsync))]
    public async Task<Request> SpeechToTextAsync(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        _logger.LogInformation("Start to do speech to text.");
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
            _logger.LogError(e, "Failed to get STT ja response.");
        }

        return request;
    }
}