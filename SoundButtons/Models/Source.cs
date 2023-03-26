using Newtonsoft.Json;

namespace SoundButtons.Models;
#nullable disable

public class Source
{
    [JsonProperty("videoId")]
    public string VideoId { get; set; }

    [JsonProperty("start")]
    public double Start { get; set; }

    [JsonProperty("end")]
    public double End { get; set; }

    public Source() { }

    public Source(string videoId, double start, double end)
    {
        this.VideoId = videoId;
        this.Start = start;
        this.End = end;
    }
}
