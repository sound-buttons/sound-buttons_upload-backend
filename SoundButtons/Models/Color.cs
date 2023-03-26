using Newtonsoft.Json;

namespace SoundButtons.Models;
#nullable disable

public class Color
{
    [JsonProperty("primary")]
    public string Primary { get; set; }

    [JsonProperty("secondary")]
    public string Secondary { get; set; }

    public Color() { }
}
