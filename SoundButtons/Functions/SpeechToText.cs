using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Serilog.Context;
using SoundButtons.Models;
using SoundButtons.Services;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName(nameof(SpeechToTextAsync))]
    public async Task<Request> SpeechToTextAsync(
        [ActivityTrigger] Request request)
    {
        using var _ = LogContext.PushProperty("InstanceId", request.instanceId);
        _logger.Information($"Start to do speech to text.");
        var openAIService = new OpenAIService();
        try
        {
            if (string.IsNullOrEmpty(request.nameJP))
            {
                var speechToTextJP = await openAIService.SpeechToTextAsync(request.tempPath, "ja");

                request.nameJP = speechToTextJP.Text;
            }
        }
        catch (HttpRequestException e)
        {
            _logger.Error(e, "Failed to get STT ja response.");
        }

        try
        {
            if (string.IsNullOrEmpty(request.nameZH))
            {
                _logger.Information($"Start to process zh.");
                var speechToTextZH = await openAIService.SpeechToTextAsync(request.tempPath, "zh");

                request.nameZH = speechToTextZH.Text;
            }
        }
        catch (HttpRequestException e)
        {
            _logger.Error(e, "Failed to get STT zh response.");
        }

        return request;
    }
}