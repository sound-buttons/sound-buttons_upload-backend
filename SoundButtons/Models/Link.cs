using Newtonsoft.Json;

namespace SoundButtons.Models;
#nullable disable

public class Link
{
    [JsonProperty("youtube")]
    public string Youtube { get; set; }

    [JsonProperty("twitter")]
    public string Twitter { get; set; }

    [JsonProperty("facebook")]
    public string Facebook { get; set; }

    [JsonProperty("instagram")]
    public string Instagram { get; set; }

    [JsonProperty("discord")]
    public string Discord { get; set; }

    [JsonProperty("other")]
    public string Other { get; set; }

    public Link() { }
}
