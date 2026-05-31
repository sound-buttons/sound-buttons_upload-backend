using System;
using System.Net.Http.Headers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using SoundButtons.Services;

namespace SoundButtons;

/// <summary>
///     Service registrations for the application, extracted from the composition root
///     so the wiring (named HTTP client identity, service lifetimes, blob client) can be
///     asserted in tests while keeping <c>Program.cs</c> a thin entry point.
/// </summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoundButtonsServices(this IServiceCollection services)
    {
        services.AddHttpClient("client",
                               config =>
                               {
                                   config.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(".NET", "10.0"));
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

        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IProcessAudioService, ProcessAudioService>();

        return services;
    }
}
