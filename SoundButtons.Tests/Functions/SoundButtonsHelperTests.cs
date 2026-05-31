using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Functions;
using SoundButtons.Models;
using SoundButtons.Services;
using SoundButtons.Tests.Fakes;
using Xunit;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "audio-submission-api")]
public class SoundButtonsHelperTests
{
    private static SoundButtons.Functions.SoundButtons CreateSut(
        IProcessAudioService? audioService = null,
        IHttpClientFactory? httpClientFactory = null)
        => new(NullLogger<SoundButtons.Functions.SoundButtons>.Instance,
               audioService ?? Mock.Of<IProcessAudioService>(),
               httpClientFactory ?? new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK, "")));

    [Theory]
    [InlineData("multipart/form-data; boundary=\"abc123\"", "abc123")]
    [InlineData("multipart/form-data; boundary=xyz789", "xyz789")]
    public void GetBoundary_ExtractsBoundary(string contentType, string expected)
        => Assert.Equal(expected, SoundButtons.Functions.SoundButtons.GetBoundary(contentType));

    [Fact]
    public void GetSourceInfo_ParsesVideoIdAndTimes()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string>
        {
            ["videoId"] = "abcdef12345",
            ["start"] = "10",
            ["end"] = "20"
        };

        Source source = sut.GetSourceInfo(req);

        Assert.Equal("abcdef12345", source.VideoId);
        Assert.Equal(10, source.Start);
        Assert.Equal(20, source.End);
    }

    [Fact]
    public void GetSourceInfo_StripsVideoIdFromUrl()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string>
        {
            ["videoId"] = "https://www.youtube.com/watch?v=Gs7QYATahy4",
            ["start"] = "1",
            ["end"] = "2"
        };

        Source source = sut.GetSourceInfo(req);

        Assert.Equal("Gs7QYATahy4", source.VideoId);
    }

    [Fact]
    public void GetSourceInfo_DiscardsUnknownUrlSource()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string>
        {
            ["videoId"] = "https://example.com/not-a-video"
        };

        Source source = sut.GetSourceInfo(req);

        Assert.Equal("", source.VideoId);
    }

    [Fact]
    public void GetSourceInfo_MissingTimes_LeavesZero()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string> { ["videoId"] = "abcdef12345" };

        Source source = sut.GetSourceInfo(req);

        Assert.Equal(0, source.Start);
        Assert.Equal(0, source.End);
    }

    [Fact]
    public void SourceCheck_EmptyVideoId_ResetsTimes()
    {
        var sut = CreateSut();
        var source = new Source { VideoId = "", Start = 5, End = 10 };

        Source result = sut.SourceCheck(source);

        Assert.Equal(0, result.Start);
        Assert.Equal(0, result.End);
    }

    [Theory]
    [InlineData(0, 0)]   // zero-length
    [InlineData(10, 5)]  // negative
    [InlineData(0, 200)] // over 180s
    public void SourceCheck_InvalidDuration_Throws(double start, double end)
    {
        var sut = CreateSut();
        var source = new Source { VideoId = "abcdef12345", Start = start, End = end };

        Assert.Throws<System.Exception>(() => sut.SourceCheck(source));
    }

    [Fact]
    public void SourceCheck_ValidDuration_Passes()
    {
        var sut = CreateSut();
        var source = new Source { VideoId = "abcdef12345", Start = 0, End = 30 };

        Source result = sut.SourceCheck(source);

        Assert.Equal(30, result.End);
    }

    [Fact]
    public void GetFileName_KeepsAllowedCharacters()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string> { ["nameZH"] = "好聲音Voice123" };

        string filename = sut.GetFileName(req);

        Assert.Equal("好聲音Voice123", filename);
    }

    [Fact]
    public void GetFileName_StripsSymbols()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string> { ["nameZH"] = "a b!@#c" };

        string filename = sut.GetFileName(req);

        Assert.Equal("abc", filename);
    }

    [Fact]
    public void GetFileName_EmptyAfterStrip_FallsBackToGuid()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string> { ["nameZH"] = "!!!???" };

        string filename = sut.GetFileName(req);

        Assert.Equal(32, filename.Length); // GUID "n" format
    }

    [Fact]
    public async Task ParseMultipartFormDataAsync_SplitsFieldsAndFiles()
    {
        var context = new TestFunctionContext();
        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            context,
            fields: [("videoId", "abcdef12345"), ("group", "雜談")],
            files: [new MultipartHelper.FilePart("file", "audio.mp3", [1, 2, 3, 4])]);

        (Dictionary<string, string> formData, Dictionary<string, byte[]> fileData) =
            await SoundButtons.Functions.SoundButtons.ParseMultipartFormDataAsync(req);

        Assert.Equal("abcdef12345", formData["videoId"]);
        Assert.Equal("雜談", formData["group"]);
        Assert.True(fileData.ContainsKey("file"));
        Assert.Equal(4, fileData["file"].Length);
    }

    [Fact]
    public async Task ProcessClip_NullClip_ReturnsNull()
    {
        var sut = CreateSut();
        string? result = await sut.ProcessClip(new Dictionary<string, string>(), new Source());

        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessClip_TwitchClip_ResetsSourceAndReturnsClip()
    {
        var sut = CreateSut();
        var source = new Source { VideoId = "x", Start = 1, End = 2 };
        var req = new Dictionary<string, string> { ["clip"] = "https://clips.twitch.tv/AbcDef123" };

        string? result = await sut.ProcessClip(req, source);

        Assert.Equal("https://clips.twitch.tv/AbcDef123", result);
        Assert.Equal("", source.VideoId);
        Assert.Equal(0, source.Start);
    }

    [Fact]
    public async Task ProcessClip_UnknownClip_ReturnsNull()
    {
        var sut = CreateSut();
        var req = new Dictionary<string, string> { ["clip"] = "https://example.com/whatever" };

        string? result = await sut.ProcessClip(req, new Source());

        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessClip_YoutubeClip_ScrapesConfigFromBody()
    {
        const string body =
            "\"clipConfig\":{\"postId\":\"Ugkx123\",\"startTimeMs\":\"1891037\",\"endTimeMs\":\"1906037\"} " +
            "and {\"videoId\":\"Gs7QYATahy4\"}";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, body);
        var sut = CreateSut(httpClientFactory: new FakeHttpClientFactory(handler));

        var source = new Source();
        var req = new Dictionary<string, string> { ["clip"] = "https://www.youtube.com/clip/Ugkx123" };

        string? result = await sut.ProcessClip(req, source);

        Assert.Equal("https://www.youtube.com/clip/Ugkx123", result);
        Assert.Equal("Gs7QYATahy4", source.VideoId);
        Assert.Equal(1891.037, source.Start, 3);
        Assert.Equal(1906.037, source.End, 3);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProcessClip_YoutubeClip_NoMatchInBody_LeavesTimesUnchanged()
    {
        // Body without clipConfig/videoId so both TryParse calls fail.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "no useful data here");
        var sut = CreateSut(httpClientFactory: new FakeHttpClientFactory(handler));

        var source = new Source { Start = 7, End = 8 };
        var req = new Dictionary<string, string> { ["clip"] = "https://www.youtube.com/clip/Ugkx123" };

        string? result = await sut.ProcessClip(req, source);

        Assert.Equal("https://www.youtube.com/clip/Ugkx123", result);
        Assert.Equal(7, source.Start); // unchanged because parsing failed
        Assert.Equal(8, source.End);
        Assert.Equal("", source.VideoId);
    }

    [Fact]
    public void GetFileName_MissingName_FallsBackToGuid()
    {
        var sut = CreateSut();

        string filename = sut.GetFileName(new Dictionary<string, string>());

        Assert.Equal(32, filename.Length); // null name -> "" -> GUID "n" format
    }
}
