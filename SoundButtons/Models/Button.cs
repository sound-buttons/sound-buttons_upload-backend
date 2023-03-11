using System;

namespace SoundButtons.Models;

public class Button
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string filename { get; set; }
    public object text { get; set; }
    public string baseRoute { get; set; }
    private float _volume;
    public float volume
    {
        get => _volume;
        set => _volume = value == 0
                             ? 1
                             : value;
    }
    public Source source { get; set; }

#pragma warning disable CA2245 // 請勿將屬性指派給屬性自身
    public Button() => this.volume = volume;
#pragma warning restore CA2245 // 請勿將屬性指派給屬性自身

    public Button(string filename, object text, float volume, Source source)
    {
        this.id = Guid.NewGuid().ToString();
        this.filename = filename;
        this.text = text;
        this.volume = volume;
        this.source = source;
    }
}

