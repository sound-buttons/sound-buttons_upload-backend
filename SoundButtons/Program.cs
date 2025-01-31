using System;
using System.Net.Http.Headers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SoundButtons.Services;
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
                 .ConfigureServices(services =>
                 {
                     services.AddHttpClient("client",
                                            config =>
                                            {
                                                config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(".NET", "8.0"));
                                                config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sound-Buttons", "1.0"));
                                                config.DefaultRequestHeaders.UserAgent.Add(
                                                    new ProductInfoHeaderValue("(+https://sound-buttons.click)"));
                                            });

                     services.AddSingleton<IOpenApiConfigurationOptions>(_ =>
                     {
                         var options = new OpenApiConfigurationOptions
                         {
                             Servers = DefaultOpenApiConfigurationOptions.GetHostNames(),
                             OpenApiVersion = OpenApiVersionType.V3,
                             IncludeRequestingHostName = true,
                             ForceHttps = false,
                             ForceHttp = false
                         };

                         return options;
                     });

                     services.AddAzureClients(clientBuilder =>
                     {
                         clientBuilder.AddBlobServiceClient(Environment.GetEnvironmentVariable("AzureStorage"))
                                      .WithName("sound-buttons");
                     });

                     services.AddScoped(typeof(OpenAiService));
                     services.AddScoped(typeof(ProcessAudioService));
                 })
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