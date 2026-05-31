using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace SoundButtons.Tests.Fakes;

/// <summary>
///     Minimal in-memory <see cref="HttpRequestData" /> double for exercising the HTTP
///     trigger without the Functions host. The body is supplied as a stream and headers
///     are mutable.
/// </summary>
public sealed class FakeHttpRequestData(FunctionContext functionContext, Stream body, string contentType)
    : HttpRequestData(functionContext)
{
    public override Stream Body { get; } = body;

    public override HttpHeadersCollection Headers { get; } = BuildHeaders(contentType);

    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();

    public override Uri Url { get; } = new("https://sound-buttons.click/api/sound-buttons");

    public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();

    public override string Method => "POST";

    public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);

    private static HttpHeadersCollection BuildHeaders(string contentType)
    {
        var headers = new HttpHeadersCollection();
        if (!string.IsNullOrEmpty(contentType))
        {
            headers.Add("Content-Type", contentType);
        }

        return headers;
    }

    public static FakeHttpRequestData FromText(FunctionContext context, string body, string contentType)
        => new(context, new MemoryStream(Encoding.UTF8.GetBytes(body)), contentType);
}
