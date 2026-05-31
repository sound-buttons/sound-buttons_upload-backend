using System.Collections.Generic;
using SoundButtons.Models;
using Xunit;

namespace SoundButtons.Tests.Models;

[Trait("spec", "blob-storage-publishing")]
public class ModelTests
{
    [Fact]
    public void Source_Constructor_AssignsProperties()
    {
        var source = new Source("vid", 1.5, 3.5);

        Assert.Equal("vid", source.VideoId);
        Assert.Equal(1.5, source.Start);
        Assert.Equal(3.5, source.End);
    }

    [Fact]
    public void Source_DefaultConstructor_Works()
    {
        var source = new Source { VideoId = "x", Start = 0, End = 1 };
        Assert.Equal("x", source.VideoId);
    }

    [Fact]
    public void Text_Constructor_AssignsProperties()
    {
        var text = new Text("中文", "日本語");

        Assert.Equal("中文", text.ZhTw);
        Assert.Equal("日本語", text.Ja);
    }

    [Fact]
    public void Request_PropertiesRoundTrip()
    {
        var request = new Request
        {
            Ip = "1.2.3.4",
            Filename = "f",
            Directory = "d",
            Source = new Source("v", 0, 1),
            Clip = "c",
            NameZH = "zh",
            NameJP = "jp",
            Volume = 0.5f,
            Group = "g",
            TempPath = "/tmp/x",
            ToastId = "42"
        };

        Assert.Equal("1.2.3.4", request.Ip);
        Assert.Equal("f", request.Filename);
        Assert.Equal("d", request.Directory);
        Assert.Equal("v", request.Source.VideoId);
        Assert.Equal("c", request.Clip);
        Assert.Equal("zh", request.NameZH);
        Assert.Equal("jp", request.NameJP);
        Assert.Equal(0.5f, request.Volume);
        Assert.Equal("g", request.Group);
        Assert.Equal("/tmp/x", request.TempPath);
        Assert.Equal("42", request.ToastId);
    }

    [Fact]
    public void ButtonGroup_Defaults_AreInitialized()
    {
        var group = new ButtonGroup();

        Assert.NotNull(group.Name);
        Assert.NotNull(group.Buttons);
        Assert.Empty(group.Buttons);
    }

    [Fact]
    public void JsonRoot_ButtonGroups_RoundTrip()
    {
        var root = new JsonRoot
        {
            Name = "char",
            ButtonGroups = [new ButtonGroup { BaseRoute = "r" }]
        };

        Assert.Equal("char", root.Name);
        Assert.Single(root.ButtonGroups);
        Assert.Equal("r", root.ButtonGroups[0].BaseRoute);
    }

    [Fact]
    public void OpenAi_TranscriptionsResponse_RoundTrip()
    {
        var response = new OpenAI.TranscriptionsResponse
        {
            Task = "transcribe",
            Language = "ja",
            Duration = 1.0,
            Text = "hello",
            Segments = [new OpenAI.Segment { Id = 1, Text = "seg" }]
        };

        Assert.Equal("transcribe", response.Task);
        Assert.Equal("ja", response.Language);
        Assert.Equal("hello", response.Text);
        Assert.Single(response.Segments);
        Assert.Equal("seg", response.Segments[0].Text);
    }
}
