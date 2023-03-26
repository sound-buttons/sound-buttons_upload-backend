using Newtonsoft.Json;

namespace SoundButtons.Models;
#nullable disable

public class Request
{
    [JsonProperty("ip")]
    public string Ip { get; set; }

    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("directory")]
    public string Directory { get; set; }

    [JsonProperty("source")]
    public Source Source { get; set; }

    [JsonProperty("clip")]
    public string Clip { get; set; }

    [JsonProperty("nameZH")]
    public string NameZH { get; set; }

    [JsonProperty("nameJP")]
    public string NameJP { get; set; }

    [JsonProperty("volume")]
    public float Volume { get; set; }

    [JsonProperty("group")]
    public string Group { get; set; }

    [JsonProperty("tempPath")]
    public string TempPath { get; set; }

    [JsonProperty("toastId")]
    public string ToastId { get; set; }

    [JsonProperty("instanceId")]
    public string InstanceId { get; internal set; }
}
