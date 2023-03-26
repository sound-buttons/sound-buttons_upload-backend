
using Newtonsoft.Json;

namespace SoundButtons.Models;

public class Text
{
    [JsonProperty("zh-tw")]
    public string? ZhTw { get; set; }

    [JsonProperty("ja")]
    public string? Ja { get; set; }

    public Text() { }

    public Text(string zhTw, string ja)
    {
        ZhTw = zhTw;
        Ja = ja;
    }
}
