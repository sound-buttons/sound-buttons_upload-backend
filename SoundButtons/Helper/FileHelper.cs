using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SoundButtons.Helper;

internal static class FileHelper
{
    public static string PrepareTempDir()
    {
        string tempPath = Path.GetTempPath();

#if !DEBUG
        // For running on Azure
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) tempPath = @"C:\home\data\";
#endif

        return tempPath;
    }
}