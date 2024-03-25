using System;
using System.IO;

namespace SoundButtons.Helper;

internal static class FileHelper
{
    public static string PrepareTempDir()
    {
#if DEBUG
        string _tempDir = Path.GetTempPath();
#else
        var _tempDir = @"C:\home\data\";
#endif
        string tempDir = Path.Combine(_tempDir, "SoundButtons", new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString());
        Directory.CreateDirectory(tempDir); // For safety
        return tempDir;
    }
}