using System;
using System.Net.Http.Headers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using SoundButtons.Services;

SelfLog.Enable(Console.WriteLine);

Logger logger = new LoggerConfiguration()
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

IHost host = new HostBuilder()
             .ConfigureFunctionsWebApplication()
             .ConfigureServices(services =>
             {
                 services.AddHttpClient("client",
                                        config =>
                                        {
                                            config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(".NET", "8.0"));
                                            config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sound-Buttons", "1.0"));
                                            config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://sound-buttons.click)"));
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

                 services.AddSingleton(logger);
                 services.AddScoped(typeof(OpenAIService));
             })
             .Build();

host.Run();