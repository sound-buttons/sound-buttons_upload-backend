## ADDED Requirements

### Requirement: Source-generated, cached JSON serialization

The service SHALL perform JSON serialization and deserialization through cached `JsonSerializerOptions` instances backed by the System.Text.Json source generator (`SoundButtons.Json.SourceGenerationContext`), rather than allocating a new `JsonSerializerOptions` per invocation or relying solely on reflection-based metadata. The cached options SHALL combine the source-generated resolver with a reflection fallback resolver so that the polymorphic `Button.Text` (`object?`) member continues to round-trip. Adopting cached options SHALL eliminate the `CA1869` analyzer suppressions previously present in `ProcessJson`. Read strictness SHALL be preserved per call site: the config-JSON path keeps `AllowTrailingCommas = true`, while the OpenAI response deserialization keeps default strictness (it SHALL NOT begin accepting trailing commas or comments).

This modernization SHALL be behavior-preserving: the JSON wire format MUST be unchanged, specifically retaining the `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` encoder, indented output (`WriteIndented = true`) for written documents, and trailing-comma tolerance (`AllowTrailingCommas = true`) on the config-JSON read path.

#### Scenario: Serializer options are cached and source-generated

- **WHEN** the JSON serialization code in `ProcessJson` and `OpenAIService` is inspected
- **THEN** it uses cached `JsonSerializerOptions` instances whose `TypeInfoResolver` includes `SourceGenerationContext.Default`
- **AND** no `JsonSerializerOptions` is constructed per request inside the activity/serialization path
- **AND** no `#pragma warning disable CA1869` suppression remains in `ProcessJson`

#### Scenario: OpenAI response read strictness is preserved

- **WHEN** an OpenAI transcription response containing a trailing comma is deserialized after the change
- **THEN** deserialization fails exactly as it did before the change (the OpenAI options do not enable `AllowTrailingCommas`)

#### Scenario: JSON wire format is preserved byte-for-byte

- **WHEN** a representative `JsonRoot` configuration (including a `Button` whose `Text` is a JSON string and another whose `Text` is a JSON object) is serialized with the new cached options
- **THEN** the produced bytes are identical to those produced by the previous serialization configuration (UnsafeRelaxedJsonEscaping encoder, indented output)
- **AND** deserializing then re-serializing the same document yields identical bytes

#### Scenario: Source-generation context is used rather than dead code

- **WHEN** `SourceGenerationContext` is inspected
- **THEN** it declares `[JsonSerializable]` entries for the types it serializes (at least `JsonRoot` and `OpenAI.TranscriptionsResponse`)
- **AND** it is referenced by the serialization paths (it is not unused/dead code)

### Requirement: C# 14 field-backed properties for value normalization

Properties whose accessors exist solely to normalize a stored value SHALL use the C# 14 `field` keyword instead of a manually declared backing field, and SHALL NOT rely on constructor self-assignment workarounds or `CA2245` suppressions to trigger that normalization. The normalization behavior SHALL be preserved exactly.

#### Scenario: Button.Volume uses the field keyword without suppressions

- **WHEN** `SoundButtons/Models/Button.cs` is inspected
- **THEN** the `Volume` property setter uses the `field` keyword rather than an explicit `_volume` backing field
- **AND** no `Volume = Volume` self-assignment and no `#pragma warning disable CA2245` suppression remains

#### Scenario: Volume normalization behavior is unchanged

- **WHEN** a `Button` is created with a `volume` of `0` (or a `Button` is deserialized with `"volume": 0`)
- **THEN** its `Volume` resolves to `1`
- **AND** a `Button` created with a non-zero `volume` retains that exact value
