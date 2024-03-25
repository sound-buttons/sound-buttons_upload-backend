using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Serilog;
using Log = SoundButtons.Helper.Log;

namespace SoundButtons.Functions;

public class Utility
{
    private static ILogger Logger => Log.Logger;

    [FunctionName(nameof(Wake))]
    public IActionResult Wake([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wake")] HttpRequest req)
    {
        Wake();
        return new OkResult();
    }

    private static void Wake()
    {
#if DEBUG
#pragma warning disable IDE0022 // 使用方法的運算式主體
        Logger.Verbose("Wake executed at: {time}", DateTime.Now);
#pragma warning restore IDE0022 // 使用方法的運算式主體
#endif
    }
}