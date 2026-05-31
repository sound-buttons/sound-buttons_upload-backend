using System.Threading.Tasks;
using SoundButtons.Models;

namespace SoundButtons.Services;

/// <summary>
///     Abstraction over the yt-dlp / FFmpeg audio acquisition and encoding pipeline
///     so consuming functions can be unit tested with a mocked implementation.
/// </summary>
public interface IProcessAudioService
{
    Task<int> DownloadAudioAsync(string tempPath, Source source);

    Task<int> DownloadAudioAsync(string tempPath, string url);

    Task CutAudioAsync(string tempPath, Source source);

    Task<string> TranscodeAudioAsync(string tempPath);
}
