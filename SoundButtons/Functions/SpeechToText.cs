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
            if (string.IsNullOrEmpty(request.nameJP))
            {
                var speechToText = await new OpenAIService().SpeechToTextAsync(request.tempPath);

                if (speechToText.Language?.ToLower() == "japanese")
                {
                    request.nameJP = speechToText.Text;
                }
            }
            return request;
        }
    }
}