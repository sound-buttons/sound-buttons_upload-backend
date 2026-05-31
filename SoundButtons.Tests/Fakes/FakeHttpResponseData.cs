using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace SoundButtons.Tests.Fakes;

/// <summary>
///     Minimal in-memory <see cref="HttpResponseData" /> double. The body is a seekable
///     <see cref="MemoryStream" /> so tests can read whatever the production code wrote.
/// </summary>
public sealed class FakeHttpResponseData(FunctionContext functionContext) : HttpResponseData(functionContext)
{
    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public override HttpHeadersCollection Headers { get; set; } = new();

    public override Stream Body { get; set; } = new MemoryStream();

    public override HttpCookies Cookies { get; } = new FakeHttpCookies();

    /// <summary>Reads the response body as a UTF-8 string.</summary>
    public string ReadBodyAsString()
    {
        Body.Position = 0;
        using var reader = new StreamReader(Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
