using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void AddSoundButtonsServices_ConfiguresWorkerSerializerToCamelCase()
    {
        // The Durable check-status payload (CreateCheckStatusResponseAsync) is serialized with
        // this worker serializer; the frontend reads `statusQueryGetUri` (camelCase), so a
        // PascalCase serializer would break the upload polling flow.
        ServiceProvider provider = new ServiceCollection().AddSoundButtonsServices().BuildServiceProvider();

        WorkerOptions options = provider.GetRequiredService<IOptions<WorkerOptions>>().Value;
        var serializer = Assert.IsType<JsonObjectSerializer>(options.Serializer);

        using var stream = new MemoryStream();
        serializer.Serialize(stream, new SamplePayload { StatusQueryGetUri = "https://example/poll" }, typeof(SamplePayload),
                             CancellationToken.None);
        string json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("\"statusQueryGetUri\"", json);
        Assert.DoesNotContain("\"StatusQueryGetUri\"", json);
    }

    private sealed class SamplePayload
    {
        public string StatusQueryGetUri { get; set; } = "";
    }
}
