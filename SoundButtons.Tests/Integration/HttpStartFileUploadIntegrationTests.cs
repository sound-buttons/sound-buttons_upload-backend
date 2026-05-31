using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Services;
using SoundButtons.Tests.Fakes;
using Xunit;

namespace SoundButtons.Tests.Integration;

/// <summary>
///     Integration test for the file-upload branch of the HTTP trigger. A real audio file
///     is synthesized and posted as multipart/form-data, driving the production transcode
///     path through the real <see cref="ProcessAudioService" />. Runs in the Docker test
///     stage where ffmpeg is available.
/// </summary>
[Trait("spec", "audio-submission-api")]
[Collection("Ffmpeg")]
public class HttpStartFileUploadIntegrationTests : IDisposable
{
    private readonly string _workDir;

    public HttpStartFileUploadIntegrationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "sb-upload-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); }
        catch { /* best effort */ }
    }

    private async Task<byte[]> SynthesizeAudio()
    {
        string path = Path.Combine(_workDir, "audio.webm");
        string ffmpeg = FfmpegProbe.Locate("ffmpeg")!;
        var psi = new ProcessStartInfo(ffmpeg, $"-y -f lavfi -i sine=frequency=440:duration=1 -c:a libopus \"{path}\"")
        {
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        return await File.ReadAllBytesAsync(path);
    }

    [FfmpegFact]
    public async Task HttpStart_FileUpload_TranscodesAndSchedules()
    {
        byte[] audio = await SynthesizeAudio();

        var sut = new SoundButtons.Functions.SoundButtons(
            NullLogger<SoundButtons.Functions.SoundButtons>.Instance,
            new ProcessAudioService(NullLogger<ProcessAudioService>.Instance),
            new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK, "")));

        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock.Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                             It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((TaskName _, object _, StartOrchestrationOptions options, CancellationToken _) => options?.InstanceId ?? "id");

        FakeHttpRequestData req = await MultipartHelper.BuildMultipartRequestAsync(
            new TestFunctionContext(),
            fields: [("directory", "test"), ("nameZH", "上傳")],
            files: [new MultipartHelper.FilePart("file", "audio.webm", audio)]);

        HttpResponseData response = await sut.HttpStart(req, clientMock.Object);

        Assert.NotNull(response);
        clientMock.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(
                              It.Is<TaskName>(n => n.Name == "main-sound-buttons"),
                              It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()),
                          Times.Once);
    }
}
