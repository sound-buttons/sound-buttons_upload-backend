using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace SoundButtons;

public partial class SoundButtons
{
    [FunctionName("wake")]
    public async Task<IActionResult> Wake([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                                         [Blob("sound-buttons"), StorageAccount("AzureStorage")] BlobContainerClient BlobContainerClient)
        => await Task.Run(() => { return new OkResult(); });
}
