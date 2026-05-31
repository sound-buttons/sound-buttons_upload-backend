using System;
using System.IO;
using SoundButtons.Helper;
using Xunit;

namespace SoundButtons.Tests.Helpers;

[Trait("spec", "audio-acquisition-encoding")]
public class HelperTests
{
    [Fact]
    public void PrepareTempDir_ReturnsExistingDirectory()
    {
        string dir = FileHelper.PrepareTempDir();

        Assert.False(string.IsNullOrEmpty(dir));
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void WhereIs_FindsBinariesOnPath()
    {
        string original = Environment.GetEnvironmentVariable("PATH") ?? "";
        string probeDir = Path.Combine(Path.GetTempPath(), "sb-where-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(probeDir);
        try
        {
            // Create dummy executables so the lookup resolves deterministically even
            // when real binaries are not installed (e.g. the SDK-only environment).
            File.WriteAllText(Path.Combine(probeDir, "yt-dlp"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(probeDir, "ffmpeg"), "#!/bin/sh\n");
            Environment.SetEnvironmentVariable("PATH", probeDir + Path.PathSeparator + original);

            (string? ytdlPath, string? ffmpegPath) = YoutubeDLHelper.WhereIs();

            Assert.NotNull(ytdlPath);
            Assert.NotNull(ffmpegPath);
            Assert.EndsWith("yt-dlp", ytdlPath);
            Assert.EndsWith("ffmpeg", ffmpegPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
            Directory.Delete(probeDir, true);
        }
    }

    [Fact]
    public void WhereIs_NoBinaries_ReturnsNulls()
    {
        string original = Environment.GetEnvironmentVariable("PATH") ?? "";
        string emptyDir = Path.Combine(Path.GetTempPath(), "sb-empty-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(emptyDir);
        string originalCwd = Directory.GetCurrentDirectory();
        try
        {
            Environment.SetEnvironmentVariable("PATH", emptyDir);
            Directory.SetCurrentDirectory(emptyDir);

            (string? ytdlPath, string? ffmpegPath) = YoutubeDLHelper.WhereIs();

            Assert.Null(ytdlPath);
            Assert.Null(ffmpegPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Environment.SetEnvironmentVariable("PATH", original);
            Directory.Delete(emptyDir, true);
        }
    }

    [Fact]
    public void WhereIs_FindsBinariesInTempDir()
    {
        // PrepareTempDir() resolves to Path.GetTempPath(); placing the binaries directly
        // there exercises the temp-directory search branch of WhereIs.
        string temp = Path.GetTempPath();
        string ytdl = Path.Combine(temp, "yt-dlp");
        string ffmpeg = Path.Combine(temp, "ffmpeg");
        string original = Environment.GetEnvironmentVariable("PATH") ?? "";
        string originalCwd = Directory.GetCurrentDirectory();
        string emptyCwd = Path.Combine(temp, "sb-cwd-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(emptyCwd);
        bool createdYt = !File.Exists(ytdl);
        bool createdFf = !File.Exists(ffmpeg);
        try
        {
            if (createdYt) File.WriteAllText(ytdl, "#!/bin/sh\n");
            if (createdFf) File.WriteAllText(ffmpeg, "#!/bin/sh\n");
            Environment.SetEnvironmentVariable("PATH", "");
            Directory.SetCurrentDirectory(emptyCwd);

            (string? ytdlPath, string? ffmpegPath) = YoutubeDLHelper.WhereIs();

            Assert.NotNull(ytdlPath);
            Assert.NotNull(ffmpegPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Environment.SetEnvironmentVariable("PATH", original);
            if (createdYt) File.Delete(ytdl);
            if (createdFf) File.Delete(ffmpeg);
            Directory.Delete(emptyCwd, true);
        }
    }

    [Fact]
    public void WhereIs_FindsBinariesInCurrentDirectory_WithPathExt()
    {
        // Exercises the current-directory search branch and the PATHEXT-present branch of
        // WhereIs (binaries live in the working directory, PATH is empty, PATHEXT set).
        string original = Environment.GetEnvironmentVariable("PATH") ?? "";
        string originalExt = Environment.GetEnvironmentVariable("PATHEXT") ?? "";
        string originalCwd = Directory.GetCurrentDirectory();
        string cwd = Path.Combine(Path.GetTempPath(), "sb-cwd2-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(cwd);
        try
        {
            File.WriteAllText(Path.Combine(cwd, "yt-dlp"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(cwd, "ffmpeg"), "#!/bin/sh\n");
            Environment.SetEnvironmentVariable("PATH", "");
            // PATHEXT set (non-null) with empty entries so the "yt-dlp"/"ffmpeg" lookup
            // still matches while covering the PATHEXT-present branch.
            Environment.SetEnvironmentVariable("PATHEXT", ":");
            Directory.SetCurrentDirectory(cwd);

            (string? ytdlPath, string? ffmpegPath) = YoutubeDLHelper.WhereIs();

            Assert.NotNull(ytdlPath);
            Assert.NotNull(ffmpegPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Environment.SetEnvironmentVariable("PATH", original);
            Environment.SetEnvironmentVariable("PATHEXT", originalExt);
            Directory.Delete(cwd, true);
        }
    }
}
