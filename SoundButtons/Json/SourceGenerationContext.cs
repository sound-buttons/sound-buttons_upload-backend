using System.Text.Json.Serialization;
using SoundButtons.Models;

namespace SoundButtons.Json;

// System.Text.Json source-generated metadata for the types the service (de)serializes.
// Declaring JsonRoot pulls in every reachable model type (ButtonGroup, Button, Source,
// Text, Color, Link, IntroButton); TranscriptionsResponse covers the OpenAI path.
//
// NOTE: serializer behavior (encoder, indentation, trailing-comma tolerance) is configured
// on the runtime JsonSerializerOptions in SoundButtons.Json.JsonSerialization, NOT via
// [JsonSourceGenerationOptions]. The runtime options are authoritative.
[JsonSerializable(typeof(JsonRoot))]
[JsonSerializable(typeof(OpenAI.TranscriptionsResponse))]
internal partial class SourceGenerationContext : JsonSerializerContext;
