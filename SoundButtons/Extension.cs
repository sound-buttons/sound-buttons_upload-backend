using Microsoft.AspNetCore.Http;
using System.Linq;

namespace SoundButtons;

static class Extension
{
    internal static string? GetFirstValue(this IFormCollection form, string name)
    {
        string? result = null;
        if (form.TryGetValue(name, out var sv))
        {
            result = sv.FirstOrDefault();
        }
        return result;
    }
}

