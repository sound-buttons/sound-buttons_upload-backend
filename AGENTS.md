# Sound Buttons Upload Backend

This is the **upload backend** for [Sound Buttons](https://sound-buttons.click) — an
Azure Functions app that ingests user-submitted audio clips, acquires/encodes them, runs
optional speech-to-text, and publishes the result to Azure Blob Storage.

- **Runtime:** Azure Functions **v4** on the **.NET 10 isolated worker** (`OutputType=Exe`).
- **Pipeline:** Durable Functions orchestration. An HTTP trigger kicks off an orchestrator
  that fans out to activity functions:
  - `SoundButtons.HttpStart` (`[Function("sound-buttons")]`) — validates the multipart
    upload, resolves the source (YouTube id / YouTube clip / Twitch clip / direct file),
    then schedules the orchestrator.
  - `ProcessAudio` (`[Function("ProcessAudioAsync")]`) — downloads (yt-dlp via
    `YoutubeDLSharp`), cuts, and transcodes (Xabe.FFmpeg) audio.
  - `SpeechToText` (`[Function("SpeechToTextAsync")]`) — optional transcription via the
    OpenAI API (sentinel-gated).
  - `UploadAudioToStorage` (`[Function("UploadAudioToStorageAsync")]`) — uploads the encoded
    audio to Blob Storage.
  - `ProcessJson` (`[Function("ProcessJsonFile")]`) — updates the per-character button-config
    JSON (read-modify-write).
  - `Utility.Healthz` — `/api/healthz` health probe.
- **Observability:** Serilog (console + Seq sink), `LogContext` enriched with the Durable
  instance id. OpenAPI/Swagger exposed via the Functions OpenAPI extension.

## Layout

- `SoundButtons/Functions/` — Function entry points (HTTP trigger, orchestrator, activities).
- `SoundButtons/Services/` — `IProcessAudioService`/`ProcessAudioService` (yt-dlp + ffmpeg)
  and `IOpenAiService`/`OpenAiService` (OpenAI transcription). Depend on the **interfaces**.
- `SoundButtons/Helper/` — `YoutubeDLHelper` (tool discovery), `FileHelper`.
- `SoundButtons/Models/` — POCOs (`Button`, `Source`, `Text`, `JsonRoot`, `Request`, …).
- `SoundButtons/Json/SourceGenerationContext.cs` — `System.Text.Json` source generation.
- `SoundButtons/ServiceCollectionExtensions.cs` — DI composition root
  (`AddSoundButtonsServices`); `Program.cs` stays a thin entry point.
- `SoundButtons.Tests/` — xUnit unit + integration tests.
- `helm/` — Kubernetes chart. `Dockerfile` — multi-stage container build.
- `openspec/` — spec-driven specs (`specs/`) and changes (`changes/`).

## Build, test, run

- **Do not assume the host `dotnet` can build `net10.0`.** If the local SDK is older, build
  and test inside the SDK 10 container (`mcr.microsoft.com/dotnet/sdk:10.0`).
- **Run locally:** `docker compose up --build` (bundles the Azurite storage emulator). The
  host listens on `http://localhost:7071`; health check at `/api/healthz`.
- **Tests + coverage gate:** the authoritative path is the Dockerfile `test` stage, which
  runs `dotnet test` and enforces a **≥85% line AND branch** coverage gate via
  `coverlet.msbuild` (`Threshold=85`, `ThresholdType=line,branch`, `ThresholdStat=total`).
  The Functions host settings (e.g. the 10-minute `functionTimeout`) live in
  `SoundButtons/host.json`. Build targets:
  - `docker build --target test -f Dockerfile .` — run the suite + gate.
  - `docker build --target report --output type=local,dest=./out -f Dockerfile .` — export
    `coverage.cobertura.xml` + TRX under `./out/testresults/`.
  - `docker build --target final -f Dockerfile .` — verify the production image still builds.
- **CI:** `.github/workflows/test.yml` (PR + push to `master`) runs the test stage, builds
  `final`, uploads coverage to Codecov (fork-safe), and runs infra conformance checks.
  `.github/workflows/docker_publish.yml` builds and pushes the production image.

## Coding conventions

- C# with **file-scoped namespaces**, **`<Nullable>enable</Nullable>`**, primary
  constructors, and `partial` classes for source-generated `[GeneratedRegex]`.
- Write **all code comments and documentation in English**; use XML doc comments for
  public/internal APIs that need clarification.
- Prefer constructor dependency injection. Register services in
  `ServiceCollectionExtensions.AddSoundButtonsServices`, not inline in `Program.cs`.
- Outbound HTTP uses the **named `IHttpClient` `"client"`** (carries the User-Agent
  identity). Blob access uses the named `IAzureClientFactory<BlobServiceClient>`
  `"sound-buttons"`. Do not `new HttpClient()`.
- Configuration comes from environment variables. Note the two distinct storage settings:
  **`AzureWebJobsStorage`** (Azure Functions / Durable runtime storage) and **`AzureStorage`**
  (application blob upload + JSON publishing via the named blob client). Others include
  `OpenAI_ApiKey`, `Seq_ServerUrl`, `Seq_ApiKey`. Never commit secrets; `local.settings.json`
  is publish-excluded.
- Keep production helpers that tests target `internal` (exposed via
  `InternalsVisibleTo("SoundButtons.Tests")`), not `private`.

## Testing conventions

- xUnit + Moq + coverlet. Tag test classes with `[Trait("spec", "<capability>")]` to map to
  the OpenSpec capability under `openspec/specs/`.
- **Unit tests must not hit the network or real Azure/OpenAI** — use the fakes in
  `SoundButtons.Tests/Fakes/` (HTTP, Function context, blob, multipart).
- **Integration tests** use real `ffmpeg`/`yt-dlp` and synthesize deterministic media with
  ffmpeg `lavfi` (still no network). They are gated by the custom `[FfmpegFact]` skippable
  attribute when the tools are absent.
- Tests run **non-parallel** (`CollectionBehavior(DisableTestParallelization = true)`) — they
  mutate global `PATH`/CWD/env. Always restore mutated global state in a `finally`.
- When adding coverage, prefer covering the negative/error branches (invalid input, missing
  source, blob collisions, missing JSON, OpenAI failures, download guards).

## Infrastructure constraints (enforced by CI conformance checks — keep them true)

- `SoundButtons.csproj`: `TargetFramework=net10.0`, `AzureFunctionsVersion=v4`.
- `Dockerfile`: .NET 10 base/build/sdk images; ffmpeg pinned to `static-ffmpeg-upx:8.1`;
  Deno runtime copied from `denoland/deno:bin`; `dumb-init v1.2.5` from the dedicated
  checksum-verified `download` stage (verified with `sha256sum -c -`), not bundled with
  the ffmpeg image; `ENTRYPOINT` first element is `dumb-init` (no `--single-child`);
  `STOPSIGNAL SIGINT`. The image runs non-root.
- Helm: `automountServiceAccountToken: false` by default on the backend Deployment.
- `Dockerfile` must pass `hadolint`.

## OpenSpec workflow

This project is **spec-driven** (`openspec/config.yaml: schema: spec-driven`). For
non-trivial changes, follow the OpenSpec change workflow (proposal → design → tasks → delta
specs under `openspec/changes/<id>/`) and validate with
`openspec validate <change-id> --strict` before implementing. `openspec/specs/` holds the
current capability specs.

