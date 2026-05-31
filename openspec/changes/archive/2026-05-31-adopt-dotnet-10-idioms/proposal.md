## Why

The service was recently migrated from .NET 8 to .NET 10 (C# 14), but the codebase still uses pre-.NET-10 patterns that the new SDK now lets us improve: a manual backing field with a self-assignment workaround (and a `CA2245` suppression) on `Button.Volume`, JSON serialization that allocates a fresh `JsonSerializerOptions` on every invocation (two `CA1869` suppressions) and relies on runtime reflection while a source-generated `JsonSerializerContext` already exists but is dead code. Doing this work now—while the project has 0 users and no backward-compatibility constraints—removes analyzer suppressions, reduces per-request allocations, and makes serialization reflection-light without changing any externally observable behavior.

To be precise about what is actually new: the `field`-keyword item is the only genuinely **C# 14-specific** feature adopted here. The JSON consolidation (cached `JsonSerializerOptions`, source generation, `JsonTypeInfoResolver.Combine`) and the `CancellationToken.None` / dead-`?? ""` cleanups are **opportunistic modernization unlocked by the .NET 10 migration** (clearing analyzer suppressions and reviving dead code), not features new to .NET 10. The change is framed honestly on that basis.

## What Changes

- Adopt the **C# 14 `field` keyword** for `Button.Volume`, removing the explicit `_volume` backing field, the `Volume = Volume` constructor workaround, and the `#pragma warning disable CA2245` suppression. The volume-normalization behavior (a value of `0` becomes `1`) is preserved exactly.
- Consolidate the **config-JSON** (de)serialization in `ProcessJson` onto a **single cached `static readonly JsonSerializerOptions`** instance, backed by the **System.Text.Json source generator** via the existing (currently unused) `SourceGenerationContext`, combined with a reflection fallback resolver so the polymorphic `Button.Text` (`object?`) member continues to round-trip exactly. `OpenAIService` is given a **separate cached options** instance that uses the same source-generated resolver but keeps its current default read strictness (it must NOT start tolerating trailing commas/comments in OpenAI responses). This removes both `#pragma warning disable CA1869` suppressions in `ProcessJson` and the per-call options allocation. The config-JSON wire format is unchanged: `UnsafeRelaxedJsonEscaping` encoder, `WriteIndented` output, and trailing-comma tolerance on read.
- Apply small, behavior-preserving idiomatic cleanups enabled alongside the modernization: replace `new CancellationToken()` with `CancellationToken.None` in `ProcessAudioService`, and remove the dead `?? ""` on the non-nullable `Path.GetExtension(...)` result.
- This is an **internal modernization only**: no HTTP/Durable/Blob behavior, no JSON wire format, and no public API changes.

## Capabilities

### New Capabilities

<!-- None. This change adds no new capability. -->

### Modified Capabilities

- `dotnet-runtime-platform`: Add quality requirements that the codebase adopt C# 14 field-backed properties (no manual backing field solely for value normalization) and that JSON serialization use a shared, source-generated, cached serializer (no per-invocation `JsonSerializerOptions` allocation, no `CA1869` suppression) while preserving the existing JSON wire format.

## Impact

- **Code**: `SoundButtons/Models/Button.cs`, `SoundButtons/Functions/ProcessJson.cs`, `SoundButtons/Services/OpenAIService.cs`, `SoundButtons/Json/SourceGenerationContext.cs`, `SoundButtons/Functions/SoundButtons.cs` (dead `?? ""`), `SoundButtons/Services/ProcessAudioService.cs` (`CancellationToken.None`).
- **Tests**: `SoundButtons.Tests` gains coverage for volume normalization and JSON round-trip equivalence (wire-format and `Button.Text` polymorphism preserved).
- **Behavior / contracts**: None. No change to HTTP routes, Durable orchestration, Blob outputs, OpenAPI document, or JSON output bytes.
- **Dependencies**: None added or removed; `System.Text.Json` source generation is already available in the .NET 10 SDK.
