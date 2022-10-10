using System.Collections.Generic;

namespace SoundButtons.Models;

public class JsonRoot
{
    public string name { get; set; }
    public string fullName { get; set; }
    public string fullConfigURL { get; set; }
    public string[] imgSrc { get; set; }
    public string intro { get; set; }
    public Color color { get; set; }
    public Link link { get; set; }
    public IntroButton introButton { get; set; }
    public List<ButtonGroup> buttonGroups { get; set; }

    public JsonRoot() { }
}
