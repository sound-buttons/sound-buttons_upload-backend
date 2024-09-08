using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace SoundButtons.Functions;

public class Utility(ILogger<Utility> logger)
{
    private readonly ILogger _logger = logger;

    [Function(nameof(Wake))]
    public IActionResult Wake([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wake")] HttpRequest req)
    {
        Wake();
        return new OkResult();
    }

    private void Wake()
    {
#if DEBUG
#pragma warning disable IDE0022 // 使用方法的運算式主體
        _logger.LogTrace("Wake executed at: {time}", DateTime.Now);
#pragma warning restore IDE0022 // 使用方法的運算式主體
#endif
    }
}