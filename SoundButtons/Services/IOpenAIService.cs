using System.Threading.Tasks;
using static SoundButtons.Models.OpenAI;

namespace SoundButtons.Services;

/// <summary>
///     Abstraction over the OpenAI speech-to-text call so consuming functions can be
///     unit tested with a mocked implementation.
/// </summary>
public interface IOpenAiService
{
    Task<TranscriptionsResponse?> SpeechToTextAsync(string path, string language = "");
}
