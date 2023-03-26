using Newtonsoft.Json;
using System.Collections.Generic;

namespace SoundButtons.Models;
#nullable disable

public class JsonRoot
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("fullName")]
    public string FullName { get; set; }

    [JsonProperty("fullConfigURL")]
    public string FullConfigURL { get; set; }

    [JsonProperty("imgSrc")]
    public string[] ImgSrc { get; set; }

    [JsonProperty("intro")]
    public string Intro { get; set; }

    [JsonProperty("color")]
    public Color Color { get; set; }

    [JsonProperty("link")]
    public Link Link { get; set; }

    [JsonProperty("introButton")]
    public IntroButton IntroButton { get; set; }

    [JsonProperty("buttonGroups")]
    public List<ButtonGroup> ButtonGroups { get; set; }

    public JsonRoot() { }
}
