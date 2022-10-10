using System.Text.Json.Serialization;

namespace SoundButtons.Models;

public class Text
{
    [JsonPropertyName("zh-tw")]
    public string ZhTw { get; set; }
    public string ja { get; set; }

    public Text() { }

    public Text(string zhTw, string ja)
    {
        ZhTw = zhTw;
        this.ja = ja;
    }
}
