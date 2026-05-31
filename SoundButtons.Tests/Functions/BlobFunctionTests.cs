using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoundButtons.Functions;
using SoundButtons.Models;
using SoundButtons.Tests.Fakes;
using Xunit;

namespace SoundButtons.Tests.Functions;

[Trait("spec", "blob-storage-publishing")]
public class UploadAudioToStorageTests
{
    private static Request MakeRequest(string tempPath)
        => new()
        {
            Ip = "1.2.3.4",
            Filename = "myfile",
            Directory = "dir",
            TempPath = tempPath,
            InstanceId = "i",
            Source = new Source()
        };

    [Fact]
    public async Task UploadAudioToStorageAsync_NoCollision_UploadsAndSetsMetadata()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        Mock<BlobClient> blob = BlobMocks.CreateBlob(exists: false);
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blob.Object);

        var function = new UploadAudioToStorage(NullLogger<UploadAudioToStorage>.Instance, BlobMocks.CreateFactory(container));
        Request result = await function.UploadAudioToStorageAsync(MakeRequest("/tmp/a.webm"));

        Assert.Equal("myfile", result.Filename);
        blob.Verify(b => b.UploadAsync("/tmp/a.webm", It.Is<BlobUploadOptions>(o => o.HttpHeaders!.ContentType == "audio/webm"), It.IsAny<CancellationToken>()), Times.Once);
        blob.Verify(b => b.SetMetadataAsync(It.IsAny<System.Collections.Generic.IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAudioToStorageAsync_Collision_AppendsSuffix()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        Mock<BlobClient> existing = BlobMocks.CreateBlob(exists: true);
        Mock<BlobClient> renamed = BlobMocks.CreateBlob(exists: false);
        container.SetupSequence(c => c.GetBlobClient(It.IsAny<string>()))
                 .Returns(existing.Object)
                 .Returns(renamed.Object);

        var function = new UploadAudioToStorage(NullLogger<UploadAudioToStorage>.Instance, BlobMocks.CreateFactory(container));
        Request result = await function.UploadAudioToStorageAsync(MakeRequest("/tmp/a.webm"));

        Assert.StartsWith("myfile_", result.Filename);
        renamed.Verify(b => b.UploadAsync("/tmp/a.webm", It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

[Trait("spec", "blob-storage-publishing")]
public class ProcessJsonTests
{
    private static Request MakeRequest()
        => new()
        {
            Directory = "dir",
            Filename = "myfile",
            TempPath = "/tmp/a.webm",
            NameZH = "中文",
            NameJP = "日本語",
            Volume = 0.8f,
            Group = "問候",
            InstanceId = "i",
            Source = new Source("vid id", 0, 1)
        };

    private const string SampleJson = """
        {"name":"char","buttonGroups":[{"name":{"zh-tw":"問候","ja":"問候"},"baseRoute":"r","buttons":[]}]}
        """;

    [Fact]
    public async Task ProcessJsonFile_JsonMissing_ReturnsWithoutUpload()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        Mock<BlobClient> jsonBlob = BlobMocks.CreateBlob(exists: false);
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(jsonBlob.Object);

        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));
        await function.ProcessJsonFile(MakeRequest());

        jsonBlob.Verify(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessJsonFile_ValidJson_UploadsCurrentAndBackup()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        Mock<BlobClient> jsonBlob = BlobMocks.CreateBlob(exists: true, readContent: SampleJson);
        Mock<BlobClient> backupBlob = BlobMocks.CreateBlob(exists: false);
        container.Setup(c => c.GetBlobClient(It.Is<string>(s => s.EndsWith("dir.json")))).Returns(jsonBlob.Object);
        container.Setup(c => c.GetBlobClient(It.Is<string>(s => s.Contains("UploadJson")))).Returns(backupBlob.Object);

        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));
        await function.ProcessJsonFile(MakeRequest());

        jsonBlob.Verify(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        backupBlob.Verify(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UpdateJson_ExistingGroup_AddsButtonAndEncodesVideoId()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(BlobMocks.CreateBlob(false).Object);
        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));

        var root = new JsonRoot
        {
            ButtonGroups = [new ButtonGroup { Name = new Text("問候", "問候"), Buttons = [] }]
        };
        var source = new Source("vid id", 0, 1);
        Request request = MakeRequest();

        JsonRoot result = function.UpdateJson(root, "dir", "myfile.webm", request, source);

        Assert.Single(result.ButtonGroups);
        ButtonGroup group = result.ButtonGroups[0];
        Button button = Assert.Single(group.Buttons);
        Assert.Equal("myfile.webm", button.Filename);
        Assert.Equal("vid+id", source.VideoId); // URL-encoded
    }

    [Fact]
    public async Task ProcessJsonFile_InvalidJson_ReturnsWithoutUpload()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        Mock<BlobClient> jsonBlob = BlobMocks.CreateBlob(exists: true, readContent: "null");
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(jsonBlob.Object);

        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));
        await function.ProcessJsonFile(MakeRequest());

        jsonBlob.Verify(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void UpdateJson_ExistingGroupWithEmptyJa_BackfillsJaFromZhTw()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(BlobMocks.CreateBlob(false).Object);
        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));

        var root = new JsonRoot
        {
            ButtonGroups = [new ButtonGroup { Name = new Text("問候", ""), Buttons = [] }]
        };

        JsonRoot result = function.UpdateJson(root, "dir", "myfile.webm", MakeRequest(), new Source("v", 0, 1));

        Assert.Equal("問候", result.ButtonGroups[0].Name.Ja);
    }

    [Fact]
    public void UpdateJson_MatchesGroupByJaName()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(BlobMocks.CreateBlob(false).Object);
        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));

        // ZhTw differs from the group key but Ja matches, exercising the second
        // predicate branch of the group lookup.
        var root = new JsonRoot
        {
            ButtonGroups = [new ButtonGroup { Name = new Text("greetings", "問候"), Buttons = [] }]
        };

        JsonRoot result = function.UpdateJson(root, "dir", "myfile.webm", MakeRequest(), new Source("v", 0, 1));

        Assert.Single(result.ButtonGroups); // matched existing group, did not create a new one
        Assert.Single(result.ButtonGroups[0].Buttons);
    }

    [Fact]
    public void UpdateJson_NewGroup_CreatesGroup()
    {
        Mock<BlobContainerClient> container = BlobMocks.CreateContainer();
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(BlobMocks.CreateBlob(false).Object);
        var function = new ProcessJson(NullLogger<ProcessJson>.Instance, BlobMocks.CreateFactory(container));

        var root = new JsonRoot { ButtonGroups = [] };
        Request request = MakeRequest();

        JsonRoot result = function.UpdateJson(root, "dir", "myfile.webm", request, new Source("v", 0, 1));

        ButtonGroup group = Assert.Single(result.ButtonGroups);
        Assert.Equal("問候", group.Name.ZhTw);
        Assert.Single(group.Buttons);
    }
}
