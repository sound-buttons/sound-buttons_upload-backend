using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;

namespace SoundButtons.Helper;

internal static class YoutubeDLHelper
{
    private static ILogger Logger => Log.Logger;

    /// <summary>
    /// 尋找程式路徑
    /// </summary>
    /// <returns>Full path of yt-dlp and FFmpeg</returns>
    /// <exception cref="BadImageFormatException" >The function is only works in windows.</exception>
    public static (string? YtdlPath, string? FFmpegPath) WhereIs()
    {
        char splitChar = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';

        DirectoryInfo tempDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), nameof(SoundButtons)));

        // https://stackoverflow.com/a/63021455
        string[] paths = Environment.GetEnvironmentVariable("PATH")?.Split(splitChar) ?? [];
        string[] extensions = Environment.GetEnvironmentVariable("PATHEXT")?.Split(splitChar) ?? [""];

        string? ytdlpPath = (from p in new[] { Environment.CurrentDirectory, tempDirectory.FullName }.Concat(paths)
                             from e in extensions
                             let path = Path.Combine(p.Trim(), "yt-dlp" + e.ToLower())
                             where File.Exists(path)
                             select path)?.FirstOrDefault();

        string? ffmpegPath = (from p in new[] { Environment.CurrentDirectory, tempDirectory.FullName }.Concat(paths)
                              from e in extensions
                              let path = Path.Combine(p.Trim(), "ffmpeg" + e.ToLower())
                              where File.Exists(path)
                              select path)?.FirstOrDefault();

        Logger.Debug("Found yt-dlp at {YtdlpPath}", ytdlpPath);
        Logger.Debug("Found ffmpeg at {FFmpegPath}", ffmpegPath);

        return (ytdlpPath, ffmpegPath);
    }
}
