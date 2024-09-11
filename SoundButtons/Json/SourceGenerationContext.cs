using System.Text.Json;
using System.Text.Json.Serialization;
using SoundButtons.Models;

namespace SoundButtons.Json;

// Must read:
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation?pivots=dotnet-8-0
[JsonSerializable(typeof(JsonRoot))]
[JsonSerializable(typeof(Text))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
internal partial class SourceGenerationContext : JsonSerializerContext;
