using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SoundButtons.Models;
#nullable disable

public class JsonRoot
{
    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("fullName")] public string FullName { get; set; }

    [JsonPropertyName("fullConfigURL")] public string FullConfigURL { get; set; }

    [JsonPropertyName("imgSrc")] public string[] ImgSrc { get; set; }

    [JsonPropertyName("intro")] public string Intro { get; set; }

    [JsonPropertyName("color")] public Color Color { get; set; }

    [JsonPropertyName("link")] public Link Link { get; set; }

    [JsonPropertyName("introButton")] public IntroButton IntroButton { get; set; }

    [JsonPropertyName("buttonGroups")] public List<ButtonGroup> ButtonGroups { get; set; }
}