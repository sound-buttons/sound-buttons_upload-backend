using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SoundButtons;
using SoundButtons.Services;
using Xunit;

namespace SoundButtons.Tests;

[Trait("spec", "audio-submission-api")]
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSoundButtonsServices_ConfiguresNamedClientWithUserAgent()
    {
        ServiceProvider provider = new ServiceCollection().AddSoundButtonsServices().BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient("client");

        string userAgent = client.DefaultRequestHeaders.UserAgent.ToString();
        Assert.Contains(".NET", userAgent);
        Assert.Contains("Sound-Buttons", userAgent);
        Assert.Contains("sound-buttons.click", userAgent);
    }

    [Fact]
    public void AddSoundButtonsServices_RegistersOpenApiOptions()
    {
        ServiceProvider provider = new ServiceCollection().AddSoundButtonsServices().BuildServiceProvider();

        var options = provider.GetRequiredService<IOpenApiConfigurationOptions>();

        Assert.NotNull(options);
        Assert.False(options.ForceHttps);
    }

    [Fact]
    public void AddSoundButtonsServices_RegistersScopedServices()
    {
        IServiceCollection services = new ServiceCollection().AddSoundButtonsServices();

        Assert.Contains(services, d => d.ServiceType == typeof(IProcessAudioService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IOpenAiService) && d.Lifetime == ServiceLifetime.Scoped);
    }
}
