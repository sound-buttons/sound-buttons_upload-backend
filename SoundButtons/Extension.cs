using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SoundButtons;

static class Extension
{
    internal static string? GetFirstValue(this IFormCollection form, string name)
    {
        string? result = null;
        if (form.TryGetValue(name, out StringValues sv))
        {
            result = sv.FirstOrDefault();
        }

        return result;
    }
}