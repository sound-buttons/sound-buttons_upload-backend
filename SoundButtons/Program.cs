using System;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SoundButtons;
#if !RELEASE
using Serilog.Debugging;
#endif

#if !RELEASE
SelfLog.Enable(Console.WriteLine);
#endif

Log.Logger = new LoggerConfiguration()
             .MinimumLevel.Verbose()
             .MinimumLevel.Override("Microsoft", LogEventLevel.Fatal)
             .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Fatal)
             .MinimumLevel.Override("System", LogEventLevel.Fatal)
             .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj} <{SourceContext}>{NewLine}{Exception}",
                              restrictedToMinimumLevel: LogEventLevel.Verbose)
             .WriteTo.Seq(serverUrl: Environment.GetEnvironmentVariable("Seq_ServerUrl")!,
                          apiKey: Environment.GetEnvironmentVariable("Seq_ApiKey"),
                          restrictedToMinimumLevel: LogEventLevel.Verbose)
             .Enrich.FromLogContext()
             .CreateLogger();

Log.Information("Starting up...");

try
{
    IHost host = new HostBuilder()
                 .ConfigureFunctionsWebApplication()
                 .UseSerilog()
                 .ConfigureServices(services => services.AddSoundButtonsServices())
                 .Build();

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}