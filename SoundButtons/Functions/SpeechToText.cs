using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using SoundButtons.Models;
using SoundButtons.Services;
using System.Threading.Tasks;

namespace SoundButtons.Functions
{
    public partial class SoundButtons
    {
        [FunctionName(nameof(SpeechToTextAsync))]
        public static async Task<Request> SpeechToTextAsync(
            [ActivityTrigger] Request request)
        {
            var openAIService = new OpenAIService();
            if (string.IsNullOrEmpty(request.nameJP))
            {
                var speechToTextJP = await openAIService.SpeechToTextAsync(request.tempPath, "ja");

                request.nameJP = speechToTextJP.Text;
            }

            if (string.IsNullOrEmpty(request.nameZH))
            {
                var speechToTextZH = await openAIService.SpeechToTextAsync(request.tempPath, "zh");

                request.nameZH = speechToTextZH.Text;
            }

            return request;
        }
    }
}