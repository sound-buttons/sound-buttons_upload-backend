using Newtonsoft.Json;
using System.Collections.Generic;

namespace SoundButtons.Models;

public class ButtonGroup
{
    [JsonProperty("name")]
    public Text Name { get; set; }

    [JsonProperty("baseRoute")]
    public string? BaseRoute { get; set; }

    [JsonProperty("buttons")]
    public List<Button> Buttons { get; set; }

    public ButtonGroup() {
        Buttons = new List<Button>();
        Name = new Text();
    }
}
