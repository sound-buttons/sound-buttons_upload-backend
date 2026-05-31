using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Azure;
using Moq;

namespace SoundButtons.Tests.Fakes;

/// <summary>
///     Builds mocked Azure Blob Storage clients so functions depending on
///     <see cref="IAzureClientFactory{BlobServiceClient}" /> can be unit tested without a
///     real storage account. All blob operations are intercepted in-memory.
/// </summary>
public static class BlobMocks
{
    public static IAzureClientFactory<BlobServiceClient> CreateFactory(Mock<BlobContainerClient> container)
    {
        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(container.Object);

        var factory = new Mock<IAzureClientFactory<BlobServiceClient>>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(service.Object);
        return factory.Object;
    }

    public static Mock<BlobContainerClient> CreateContainer()
    {
        var container = new Mock<BlobContainerClient>();
        container.Setup(c => c.Name).Returns("sound-buttons");
        return container;
    }

    public static Mock<BlobClient> CreateBlob(bool exists, string? readContent = null)
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(b => b.Name).Returns("blob");
        blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));
        blob.Setup(b => b.Exists(It.IsAny<CancellationToken>()))
            .Returns(Response.FromValue(exists, Mock.Of<Response>()));

        if (readContent is not null)
        {
            blob.Setup(b => b.OpenReadAsync(It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(readContent)));
        }

        blob.Setup(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));
        blob.Setup(b => b.UploadAsync(It.IsAny<string>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));
        blob.Setup(b => b.SetMetadataAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobInfo>(), Mock.Of<Response>()));

        return blob;
    }
}
