namespace SoundButtons.Models;

public class Source
{
    public string videoId { get; set; }
    public double start { get; set; }
    public double end { get; set; }

    public Source() { }

    public Source(string videoId, double start, double end)
    {
        this.videoId = videoId;
        this.start = start;
        this.end = end;
    }
}
