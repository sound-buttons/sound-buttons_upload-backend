using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace SoundButtons.Tests.Fakes;

/// <summary>Helpers to build multipart/form-data request doubles for the HTTP trigger.</summary>
public static class MultipartHelper
{
    public sealed record FilePart(string FieldName, string FileName, byte[] Content);

    /// <summary>
    ///     Builds a <see cref="FakeHttpRequestData" /> carrying a real multipart/form-data
    ///     payload generated with <see cref="MultipartFormDataContent" /> so the production
    ///     <c>MultipartReader</c> parses it exactly as in production.
    /// </summary>
    public static async Task<FakeHttpRequestData> BuildMultipartRequestAsync(
        FunctionContext context,
        (string Name, string Value)[]? fields = null,
        FilePart[]? files = null)
    {
        var content = new MultipartFormDataContent("----SoundButtonsBoundary");

        foreach ((string name, string value) in fields ?? [])
        {
            content.Add(new StringContent(value), name);
        }

        foreach (FilePart file in files ?? [])
        {
            content.Add(new ByteArrayContent(file.Content), file.FieldName, file.FileName);
        }

        var stream = new MemoryStream();
        await content.CopyToAsync(stream);
        stream.Position = 0;

        string contentType = content.Headers.ContentType!.ToString();
        return new FakeHttpRequestData(context, stream, contentType);
    }
}
