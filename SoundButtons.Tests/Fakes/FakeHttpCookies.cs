using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker.Http;

namespace SoundButtons.Tests.Fakes;

/// <summary>No-op <see cref="HttpCookies" /> double; the production code under test does not use cookies.</summary>
public sealed class FakeHttpCookies : HttpCookies
{
    public List<IHttpCookie> Appended { get; } = [];

    public override void Append(string name, string value) => Appended.Add(new HttpCookie(name, value));

    public override void Append(IHttpCookie cookie) => Appended.Add(cookie);

    public override IHttpCookie CreateNew() => new HttpCookie(string.Empty, string.Empty);
}
