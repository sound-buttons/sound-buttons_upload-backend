using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Models;
using SoundButtons.Services;
using SoundButtons.Tests.Fakes;
using Xunit;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "audio-processing-workflow")]
public class RunOrchestratorTests
{
    private static SoundButtons.Functions.SoundButtons CreateSut()
        => new(NullLogger<SoundButtons.Functions.SoundButtons>.Instance,
               Mock.Of<IProcessAudioService>(),
               new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK, "")));

    [Fact]
    public async Task RunOrchestrator_CoordinatesActivityChain_InOrder()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "sb-orch-" + System.Guid.NewGuid().ToString("n") + ".webm");
        File.WriteAllText(tempFile, "audio");
        var request = new Request { TempPath = tempFile, NameZH = "name", InstanceId = "i", Source = new Source() };

        var sequence = new System.Collections.Generic.List<string>();
        var context = new Mock<TaskOrchestrationContext>();
        context.Setup(c => c.GetInput<Request>()).Returns(request);
        context.Setup(c => c.CallActivityAsync<Request>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .Returns((TaskName name, object _, TaskOptions _) =>
               {
                   sequence.Add(name.Name);
                   return Task.FromResult(request);
               });
        context.Setup(c => c.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .Returns((TaskName name, object _, TaskOptions _) =>
               {
                   sequence.Add(name.Name);
                   return Task.CompletedTask;
               });

        bool result = await CreateSut().RunOrchestrator(context.Object);

        Assert.True(result);
        Assert.Equal(["UploadAudioToStorageAsync", "SpeechToTextAsync", "ProcessJsonFile"], sequence);
        Assert.False(File.Exists(tempFile)); // cleaned up
    }

    [Fact]
    public async Task RunOrchestrator_DownloadsWhenTempPathEmpty()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "sb-orch-" + System.Guid.NewGuid().ToString("n") + ".webm");
        File.WriteAllText(tempFile, "audio");
        var request = new Request { TempPath = "", NameZH = "name", InstanceId = "i", Source = new Source() };

        var context = new Mock<TaskOrchestrationContext>();
        context.Setup(c => c.GetInput<Request>()).Returns(request);
        context.Setup(c => c.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .ReturnsAsync(tempFile);
        context.Setup(c => c.CallActivityAsync<Request>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .ReturnsAsync(request);
        context.Setup(c => c.CallActivityAsync(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .Returns(Task.CompletedTask);

        bool result = await CreateSut().RunOrchestrator(context.Object);

        Assert.True(result);
        context.Verify(c => c.CallActivityAsync<string>(
                           It.Is<TaskName>(n => n.Name == "ProcessAudioAsync"), It.IsAny<object>(), It.IsAny<TaskOptions>()),
                       Times.Once);
    }

    [Fact]
    public async Task RunOrchestrator_MissingFile_AbortsAndReturnsFalse()
    {
        string missing = Path.Combine(Path.GetTempPath(), "sb-missing-" + System.Guid.NewGuid().ToString("n") + ".webm");
        var request = new Request { TempPath = "", InstanceId = "i", Source = new Source() };

        var context = new Mock<TaskOrchestrationContext>();
        context.Setup(c => c.GetInput<Request>()).Returns(request);
        context.Setup(c => c.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
               .ReturnsAsync(missing);

        bool result = await CreateSut().RunOrchestrator(context.Object);

        Assert.False(result);
        context.Verify(c => c.CallActivityAsync<Request>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()),
                       Times.Never);
    }
}
