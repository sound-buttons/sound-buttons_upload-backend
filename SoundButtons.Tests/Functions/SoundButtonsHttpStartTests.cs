using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Models;
using SoundButtons.Services;
using SoundButtons.Tests.Fakes;
using Xunit;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "audio-submission-api")]
public class SoundButtonsHttpStartTests
{
    private static SoundButtons.Functions.SoundButtons CreateSut(IProcessAudioService? audioService = null)
        => new(NullLogger<SoundButtons.Functions.SoundButtons>.Instance,
               audioService ?? Mock.Of<IProcessAudioService>(),
               new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK, "")));

    private static Mock<DurableTaskClient> CreateDurableClientMock()
    {
        var mock = new Mock<DurableTaskClient>("test");
        mock.Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                       It.IsAny<TaskName>(),
                       It.IsAny<object>(),
                       It.IsAny<StartOrchestrationOptions>(),
                       It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskName _, object _, StartOrchestrationOptions options, CancellationToken _) =>
                              options?.InstanceId ?? "instance-id");
        return mock;
    }

    [Fact]
    public async Task HttpStart_InvalidContentType_ReturnsBadRequest()
    {
        var sut = CreateSut();
        var req = FakeHttpRequestData.FromText(new TestFunctionContext(), "{}", "application/json");

        HttpResponseData response = await sut.HttpStart(req, CreateDurableClientMock().Object);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Invalid content type", ((FakeHttpResponseData)response).ReadBodyAsString());
    }

    [Fact]
    public async Task HttpStart_NoSource_ReturnsBadRequest()
    {
        var sut = CreateSut();
        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            new TestFunctionContext(),
            fields: [("directory", "test"), ("group", "雜談")]);

        HttpResponseData response = await sut.HttpStart(req, CreateDurableClientMock().Object);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No source found", ((FakeHttpResponseData)response).ReadBodyAsString());
    }

    [Fact]
    public async Task HttpStart_FileTooLarge_ReturnsBadRequest()
    {
        var sut = CreateSut();
        var big = new byte[30 * 1024 * 1024 + 1];
        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            new TestFunctionContext(),
            files: [new MultipartHelper.FilePart("file", "audio.mp3", big)]);

        HttpResponseData response = await sut.HttpStart(req, CreateDurableClientMock().Object);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("File size over 30MB", ((FakeHttpResponseData)response).ReadBodyAsString());
    }

    [Fact]
    public async Task HttpStart_ValidVideoSource_SchedulesOrchestration()
    {
        var sut = CreateSut();
        Mock<DurableTaskClient> clientMock = CreateDurableClientMock();
        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            new TestFunctionContext(),
            fields: [("videoId", "Gs7QYATahy4"), ("start", "0"), ("end", "10"), ("nameZH", "測試"), ("directory", "test")]);

        HttpResponseData response = await sut.HttpStart(req, clientMock.Object);

        Assert.NotNull(response);
        clientMock.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(
                              It.Is<TaskName>(n => n.Name == "main-sound-buttons"),
                              It.IsAny<object>(),
                              It.IsAny<StartOrchestrationOptions>(),
                              It.IsAny<CancellationToken>()),
                          Times.Once);
    }

    [Fact]
    public async Task HttpStart_AllOptionalFields_ParsesForwardedIpAndVolume()
    {
        var sut = CreateSut();
        Mock<DurableTaskClient> clientMock = CreateDurableClientMock();
        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            new TestFunctionContext(),
            fields:
            [
                ("videoId", "Gs7QYATahy4"), ("start", "0"), ("end", "10"),
                ("nameZH", "測試"), ("nameJP", "テスト"), ("directory", "aru"),
                ("group", "問候"), ("toastId", "99"), ("volume", "0.5")
            ]);
        req.Headers.Add("X-Forwarded-For", "5.6.7.8");

        Request? scheduled = null;
        clientMock.Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                             It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((TaskName _, object input, StartOrchestrationOptions options, CancellationToken _) =>
                  {
                      scheduled = input as Request;
                      return options?.InstanceId ?? "instance-id";
                  });

        await sut.HttpStart(req, clientMock.Object);

        Assert.NotNull(scheduled);
        Assert.Equal("5.6.7.8", scheduled!.Ip);
        Assert.Equal(0.5f, scheduled.Volume);
        Assert.Equal("テスト", scheduled.NameJP);
        Assert.Equal("99", scheduled.ToastId);
        Assert.Equal("aru", scheduled.Directory);
    }
}
