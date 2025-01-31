using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using SoundButtons.Models;

namespace SoundButtons.Functions;

public class UploadAudioToStorage
{
    private readonly BlobContainerClient _blobContainerClient;
    private readonly ILogger _logger;

    public UploadAudioToStorage(ILogger<UploadAudioToStorage> logger, IAzureClientFactory<BlobServiceClient> blobClientFactory)
    {
        _logger = logger;
        _blobContainerClient = blobClientFactory.CreateClient("sound-buttons").GetBlobContainerClient("sound-buttons");
        _blobContainerClient.CreateIfNotExists();
    }

    [Function("UploadAudioToStorageAsync")]
    public async Task<Request> UploadAudioToStorageAsync(
        [ActivityTrigger] Request request)
    {
        using IDisposable _ = LogContext.PushProperty("InstanceId", request.InstanceId);
        string ip = request.Ip;
        string filename = request.Filename;
        string directory = request.Directory;
        string tempPath = request.TempPath;
        string fileExtension = Path.GetExtension(tempPath);

        // Get a new file name on blob storage
        BlobClient cloudBlockBlob = _blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
        if (await cloudBlockBlob.ExistsAsync())
        {
            filename += $"_{DateTime.Now.Ticks}";
            cloudBlockBlob = _blobContainerClient.GetBlobClient($"{directory}/{filename + fileExtension}");
        }

        request.Filename = filename;
        _logger.LogInformation($"Filename: {filename + fileExtension}");

        // Write audio file 
        _logger.LogInformation("Start to upload audio to blob storage {name}", _blobContainerClient.Name);
        await cloudBlockBlob.UploadAsync(tempPath, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/webm" } });
        _logger.LogInformation("Upload audio to azure finish.");

        if (null != ip)
        {
            Dictionary<string, string> metadata = new()
            {
                { "sourceIp", ip }
            };

            await cloudBlockBlob.SetMetadataAsync(metadata);
        }

        return request;
    }
}