namespace SoundButtons.Models;

public class Request
{
    public string ip { get; set; }
    public string filename { get; set; }
    public string directory { get; set; }
    public Source source { get; set; }
    public string clip { get; set; }
    public string nameZH { get; set; }
    public string nameJP { get; set; }
    public float volume { get; set; }
    public string group { get; set; }
    public string tempPath { get; set; }
    public string toastId { get; set; }
    public string instanceId { get; internal set; }
}
