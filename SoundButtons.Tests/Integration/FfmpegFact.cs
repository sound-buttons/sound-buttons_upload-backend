using System;
using System.IO;
using Xunit;

namespace SoundButtons.Tests.Integration;

/// <summary>
///     A <see cref="FactAttribute" /> that skips the test when ffmpeg/ffprobe are not
///     available on the host. Integration tests that drive the real encoder run inside the
///     Docker test stage (which provides the static ffmpeg binaries) and are skipped on
///     developer machines that lack them, keeping the unit-test run hermetic.
/// </summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!FfmpegProbe.IsAvailable)
        {
            Skip = "ffmpeg/ffprobe not found on PATH; integration test skipped.";
        }
    }
}

internal static class FfmpegProbe
{
    public static readonly bool IsAvailable = Locate("ffmpeg") is not null && Locate("ffprobe") is not null;

    public static string? Locate(string name)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
