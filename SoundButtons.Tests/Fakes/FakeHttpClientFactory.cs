using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SoundButtons.Tests.Fakes;

/// <summary>Simple <see cref="IHttpClientFactory" /> returning a client over the supplied handler.</summary>
public sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
