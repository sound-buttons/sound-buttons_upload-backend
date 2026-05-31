## Context

The backend is a .NET 10 isolated-worker Azure Functions app (Durable Functions)
with no tests. Relevant testability characteristics of the current code:

- **Functions** (`SoundButtons.cs`, `ProcessAudio.cs`, `UploadAudioToStorage.cs`,
  `ProcessJson.cs`, `SpeechToText.cs`, `Utility.cs`) are triggered classes that
  depend on services and the Azure SDK.
- The HTTP trigger `SoundButtons.HttpStart` takes the **abstract**
  `HttpRequestData`/`DurableTaskClient` and delegates parsing/validation to
  private helpers that operate on plain `Dictionary` inputs
  (`GetSourceInfo`, `SourceCheck`, `GetFileName`, `ProcessClip`,
  `ProcessTwitchClip`, `ProcessYoutubeClip`, `ParseMultipartFormDataAsync`).
- `SoundButtons`, `ProcessAudio`, and `SpeechToText` depend on the **concrete**
  classes `ProcessAudioService` / `OpenAiService` (no interfaces), which blocks
  mocking.
- `ProcessAudioService` shells out to `yt-dlp` (YoutubeDLSharp) and `ffmpeg`
  (Xabe.FFmpeg, static API); its constructor calls `YoutubeDLHelper.WhereIs()` +
  `FFmpeg.SetExecutablesPath`.
- `OpenAiService` calls the OpenAI HTTP API via a named `HttpClient` and reads
  `OpenAI_ApiKey` from the environment.
- `UploadAudioToStorage` / `ProcessJson` use `IAzureClientFactory<BlobServiceClient>`
  and call `BlobContainerClient.CreateIfNotExists()` in their constructors.
- `ProcessYoutubeClip` creates an ad-hoc `new HttpClient()` for scraping (not
  injected → not test-seamable, and inconsistent with the `observability-and-api-docs`
  "outbound HTTP client identity" requirement).
- `Program.cs` is the composition root (top-level statements). Regex partials and
  `SourceGenerationContext` are source-generated.

The Dockerfile is multi-stage: `base` (runtime + ffmpeg/yt-dlp), `build`
(restore), `publish`, `download` (dumb-init), `final`. The `build`/`publish`
chain produces the production image; a `test` stage can branch off `build`
without affecting `final`. The runtime base ships `ffmpeg`/`ffprobe` (from
`ghcr.io/jim60105/static-ffmpeg-upx:8.1`) and `yt-dlp`; the SDK `build` image does
not, so the `test` stage must add them for the FFmpeg/tool integration tests.

CI today is only `docker_publish.yml` (build+push on push to `master`). There is
no PR gate and no coverage reporting.

## Goals / Non-Goals

**Goals:**

- A maintainable xUnit suite (unit + integration) that covers every requirement
  in the six **application** capability specs.
- Measured line+branch coverage **≥ 85 %** of the application assembly, enforced
  so the build fails below the floor.
- Tests run **inside the Dockerfile `test` stage** (single, reproducible
  invocation with ffmpeg/yt-dlp parity) and in CI on **PR and push to `master`**,
  with coverage uploaded to **Codecov**.
- Minimal, behavior-preserving production changes to make the code testable.
- Every **infrastructure** capability requirement covered by an automated
  static/build conformance check.

**Non-Goals:**

- No true Azurite/cloud round-trip for blob storage in the core suite (would need
  Docker-in-Docker inside buildx); blob behavior is verified via Azure SDK mocks.
  An Azurite-backed integration job is left as a possible future enhancement.
- No live network calls in tests (no real YouTube/Twitch/OpenAI requests); these
  boundaries are mocked or synthesized.
- No change to runtime behavior, the HTTP/orchestration contract, or the
  production image.

## Decisions

### Decision 1: Test stack and layout

- **xUnit** as the framework, **Moq** for mocking (mocks the Azure SDK's virtual
  members, `TaskOrchestrationContext`, `ILogger<T>`, and the new service
  interfaces), **coverlet** for Cobertura coverage (`coverlet.collector` for
  local/IDE report generation and **`coverlet.msbuild`** for the threshold gate —
  the two are distinct integrations; see Decision 4),
  `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` as runners.
- New project `SoundButtons.Tests/SoundButtons.Tests.csproj` (net10.0) with a
  `ProjectReference` to `SoundButtons.csproj` (referencing the Functions `Exe`
  assembly is supported). Add a `SoundButtons.sln` tying both projects so
  `dotnet test` resolves the whole solution.
- `SoundButtons.csproj` gains `InternalsVisibleTo("SoundButtons.Tests")`. Because
  `InternalsVisibleTo` exposes `internal` (not `private`) members, the pure private
  helpers the tests target directly (`GetBoundary`, `ParseMultipartFormDataAsync`,
  `GetSourceInfo`, `SourceCheck`, `GetFileName`, `ProcessJson.UpdateJson`) are
  promoted to `internal`; helpers reachable only through a public entry point stay
  private and are covered through that entry point.

### Decision 2: Production testability seams (behavior-preserving)

- Extract `IProcessAudioService` and `IOpenAiService` interfaces over the existing
  public methods; the concrete classes implement them; DI registers the interface
  → implementation; `SoundButtons`, `ProcessAudio`, `SpeechToText` depend on the
  interfaces. This enables mocking the external-process/HTTP boundaries.
- Route `ProcessYoutubeClip` scraping through the injected `IHttpClientFactory`
  named `"client"` instead of `new HttpClient()`. This makes the scrape mockable
  (via a fake `HttpMessageHandler`) and aligns with the "outbound HTTP client
  identity" requirement.
- Extract the yt-dlp `OptionSet` construction in `ProcessAudioService` into
  `internal static` builder methods (e.g. `BuildVideoIdOptionSet`,
  `BuildClipOptionSet`) that do **not** invoke tool discovery, so format selection
  (`251/140`), `DownloadSections`, and output path can be asserted **without**
  constructing the service or running the external process (the ctor's
  `YoutubeDLHelper.WhereIs()` / `FFmpeg.SetExecutablesPath()` need real binaries and
  stay in integration tests).
- Extract DI registration into an `internal static`
  `AddSoundButtonsServices(this IServiceCollection)` invoked from `Program.cs`, so
  the named `"client"` User-Agent and the service registrations are assertable in a
  test even though `Program.cs` itself is excluded from coverage.
- Provide minimal test doubles for the abstract Functions types
  (`FakeHttpRequestData`, `FakeHttpResponseData`, `TestFunctionContext`) in the
  test project to cover the thin `HttpStart`/`Healthz` shells.

### Decision 3: Unit vs integration split

- **Unit** (no external dependency, run anywhere): models (`Button.Volume`
  normalization, `Text`/`Source` construction), `FileHelper`, regex matching
  (YouTube id / YouTube clip / Twitch clip / clip-config scraping), the
  `SoundButtons` helper methods, `ProcessAudio` activity branching (mock
  `IProcessAudioService`), `UploadAudioToStorage` and `ProcessJson` (mock
  `BlobServiceClient`/`BlobContainerClient`/`BlobClient`), `SpeechToText` (mock
  `IOpenAiService`), `OpenAiService` (fake `HttpMessageHandler` + API-key guard),
  the orchestrator `RunOrchestrator` (mock `TaskOrchestrationContext`,
  asserting activity order and the missing-file abort), and `UpdateJson` logic
  (group find/create, button append, URL-encoded videoId).
- **Integration** (real binaries, deterministic, no network): `CutAudioAsync` and
  `TranscodeAudioAsync` against media synthesized locally with `ffmpeg` lavfi
  (e.g. a video+AAC/MP4 input → assert audio-only Opus WebM output, mirroring the
  clip fix), and `YoutubeDLHelper.WhereIs` against real `yt-dlp`/`ffmpeg` on
  `PATH`. These run in the Dockerfile `test` stage where the binaries exist.

### Decision 4: Coverage threshold and exclusions

- Enforce **≥ 85 %** via **coverlet.msbuild**
  (`dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`
  `/p:Threshold=85 /p:ThresholdType=line,branch /p:ThresholdStat=total`) so
  `dotnet test` (and therefore the Docker `test` stage and CI) fails below the
  floor. The threshold MSBuild properties belong to `coverlet.msbuild`, **not** the
  `--collect "XPlat Code Coverage"` data-collector path, so the gate uses the
  MSBuild integration to guarantee the build actually fails below 85 %.
- `ThresholdStat=total` measures the whole application assembly (line **and**
  branch), not per-file. Reaching 85 % branch coverage requires deliberately
  exercising the negative branches (invalid durations, missing/oversized uploads,
  blob collisions, JSON-missing/OOM retry, OpenAI key/HTTP failures), which the
  unit-test tasks call out explicitly.
- Exclude from the metric, via `[ExcludeFromCodeCoverage]` and/or coverlet
  filters, code that is not meaningfully unit-testable: `Program.cs` (composition
  root), the source-generated `SourceGenerationContext`, the generated regex
  partial methods, and the static OpenAPI configuration lambda. The exclusion
  list is documented so the 85 % reflects real logic coverage.

### Decision 5: Dockerfile `test` and `report` stages

- `FROM build AS test`: copy the test project and restore it **for the build
  platform** (do NOT reuse the production `-a $TARGETARCH` restore — tests run on
  `$BUILDPLATFORM`, so a target-arch restore would mismatch RIDs / run under
  emulation). Copy sources, add `ffmpeg`/`ffprobe`/`yt-dlp` (same pinned sources as
  `base`) for the integration tests, then run
  `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`
  `/p:Threshold=85 /p:ThresholdType=line,branch /p:ThresholdStat=total`
  `--results-directory /testresults`. The test workflow builds native
  `linux/amd64`. Use `--mount=type=bind` / `COPY` per the containerfile-creator
  pattern.
- `FROM scratch AS report`: copy `/testresults` (Cobertura `coverage.cobertura.xml`
  and the TRX) so CI can extract them via
  `docker build --target report --output type=local,dest=./out .`.
- The `final` stage still derives from `base` + `publish` only — the `test`/
  `report` stages are off the production path, so the shipped image is byte-for-
  byte unaffected. `.dockerignore` is relaxed to let the test sources into the
  build context.

### Decision 6: CI workflow and Codecov

- New `.github/workflows/test.yml` triggered on `pull_request` and `push` to
  `master`. Steps: checkout (submodules), set up Buildx, `docker build --target
  report --output type=local,dest=./out .` (this runs the suite during the build;
  a test failure or sub-threshold coverage fails the job), then
  `codecov/codecov-action@v5` uploads `./out/**/coverage.cobertura.xml` using the
  `CODECOV_TOKEN` secret. The Codecov step is guarded so fork PRs (which cannot
  read the secret) do not fail the job — gate it on the token being present / a
  non-fork event and set `fail_ci_if_error: false`. Because the Docker `test` stage
  already fails the build on test or sub-threshold failure, the coverage gate does
  not depend on Codecov. Optionally publish the TRX as a check artifact.
- Keep `docker_publish.yml` unchanged; publishing remains gated by `master` and
  now implicitly benefits from the separate PR test gate.

### Decision 7: Requirement traceability

Each application-capability requirement maps to at least one test (matrix below).
Infrastructure-capability requirements are not exercisable by .NET unit tests, so
they map to automated static/build conformance checks run in CI; the task asks
that every spec requirement be covered by an automated check, and this treats those
CI checks as first-class, named, pass/fail test cases (not as untested gaps). A
short "traceability" note in the test project (xUnit `[Trait("spec",
"<capability>")]` on test classes) keeps the application mapping discoverable and
lets coverage be filtered per capability.

## Requirement Traceability Matrix

**Application capabilities → tests (xUnit):**

| Capability | Requirement | Covering test (type) |
|---|---|---|
| audio-submission-api | Submission endpoint accepts multipart form-data | `ParseMultipartFormDataAsync`/`GetBoundary` on a real multipart stream (unit) |
| audio-submission-api | Source resolution from submission inputs | `GetSourceInfo` (videoId, URL→id strip, start/end parse) (unit) |
| audio-submission-api | Submission input validation | `HttpStart` no-source → 400, file > 30 MB → 400, bad content-type → 400 (unit, fake HttpRequestData) |
| audio-submission-api | Output filename derivation | `GetFileName` (sanitize, GUID fallback) (unit) |
| audio-submission-api | Workflow kickoff and status response | `HttpStart` schedules orchestration + returns status (unit, mock `DurableTaskClient`) |
| audio-submission-api | Health check endpoint | `Utility.Healthz` → 200 (unit) |
| audio-acquisition-encoding | Download audio from a YouTube video id | `BuildVideoIdOptionSet` asserts `251/140`, sections, output; empty id → throws (unit) |
| audio-acquisition-encoding | Download audio from a clip URL | `BuildClipOptionSet` asserts options; empty url → throws (unit) |
| audio-acquisition-encoding | Guard against failed downloads | `ProcessAudioAsync` returns tempPath when file missing, for both branches (unit, mock service) |
| audio-acquisition-encoding | Trim a downloaded clip to its duration | `CutAudioAsync` on synthesized media trims to duration (integration) |
| audio-acquisition-encoding | Transcode media to audio-only WebM without video | `TranscodeAudioAsync` video+AAC → audio-only Opus WebM; no-audio input → throws (integration) |
| audio-acquisition-encoding | Runtime tool discovery | `YoutubeDLHelper.WhereIs` resolves binaries from PATH/dirs (integration + unit with dummy files) |
| audio-processing-workflow | Orchestrator coordinates the activity chain in order | `RunOrchestrator` calls ProcessAudio→Upload→STT→ProcessJson in order (unit, mock context) |
| audio-processing-workflow | Missing audio file aborts the workflow | `RunOrchestrator` returns false + cleanup when file absent (unit) |
| audio-processing-workflow | Temporary file cleanup | `CleanUp` deletes temp; orchestrator cleans on success/abort (unit) |
| audio-processing-workflow | Instance-id correlation | Activities push `InstanceId` into `LogContext` (unit, assert enriched property) |
| audio-processing-workflow | Bounded execution time | Assert `host.json` `functionTimeout` = `00:10:00` (unit, config assertion) |
| blob-storage-publishing | Audio blob upload | `UploadAudioToStorageAsync` uploads with `audio/webm` content type (unit, mock blob) |
| blob-storage-publishing | Filename collision avoidance | existing blob → filename suffixed with ticks (unit) |
| blob-storage-publishing | Source IP recorded as blob metadata | `SetMetadataAsync` called with `sourceIp` when ip present (unit) |
| blob-storage-publishing | Button-catalog JSON is read before update | `ProcessJsonFile` reads `<dir>/<dir>.json`; missing → critical log + return (unit) |
| blob-storage-publishing | New button appended to the correct group | `UpdateJson` finds/creates group, appends button (unit) |
| blob-storage-publishing | Injection-safe source field | `UpdateJson` URL-encodes `Source.VideoId` (unit) |
| blob-storage-publishing | Catalog write with timestamped backup | dual `UploadAsync` (canonical + `UploadJson/<ts>.json`) (unit) |
| speech-to-text-transcription | Opt-in transcription via sentinel name | `SpeechToTextAsync` only transcribes when `NameJP == "[useSTT]"` (unit, mock `IOpenAiService`) |
| speech-to-text-transcription | Whisper transcription request shape | `OpenAiService.SpeechToTextAsync` posts multipart (model `whisper-1`, `verbose_json`, language) (unit, fake handler) |
| speech-to-text-transcription | API key guard | missing key → returns empty response, logs critical (unit) |
| speech-to-text-transcription | Graceful degradation on failure | `HttpRequestException` swallowed, request returned unchanged (unit) |
| observability-and-api-docs | Structured logging via Serilog | `LogContext` `InstanceId` enrichment asserted on an activity (unit) |
| observability-and-api-docs | Instance-id log correlation | same as above across activities (unit) |
| observability-and-api-docs | Outbound HTTP client identity | named `"client"` carries the User-Agent product headers; scraping uses it (unit, factory config) |
| observability-and-api-docs | OpenAPI document exposure | OpenAPI options expose V3; covered by build + a CI smoke assertion (build/CI) |

**Infrastructure capabilities → static/build checks (CI):**

| Capability | Requirement | Covering check |
|---|---|---|
| dotnet-runtime-platform | Target .NET runtime version / isolated-worker / images on .NET 10 / behavior preserved / OpenAPI available / CI publishes image | Assert `TargetFramework`=`net10.0` & `AzureFunctionsVersion`=`v4`; Dockerfile base/build/sdk tags `*10.0`; `docker build` of `final` succeeds in CI |
| dotnet-runtime-platform | Non-.NET image/tool versions unchanged | Assert Dockerfile pins (ffmpeg `8.1`, dumb-init `v1.2.5`) via a grep/test step |
| container-init-process | dumb-init PID 1 / pinned+verified / decoupled | hadolint + build-time assertions: ENTRYPOINT first elem `dumb-init`, `STOPSIGNAL SIGINT`, checksum step present, no dumb-init COPY from the ffmpeg image |
| kubernetes-deployment-security | ServiceAccount token automount disabled by default | `helm template` renders `automountServiceAccountToken: false` (CI assertion) |

## Alternatives Considered

- **Run `dotnet test` directly on the GitHub runner** (not via Docker). Simpler,
  but diverges from the task's "run via the Dockerfile test stage" requirement and
  loses ffmpeg/yt-dlp parity. The Docker `test` stage gives a single reproducible
  environment with the same binaries as production. Rejected as the primary path
  (the runner could still be a fallback).
- **Azurite / Testcontainers for real blob round-trip.** True fidelity, but
  Testcontainers needs Docker-in-Docker, which is not available inside a `docker
  build` stage; running Azurite as a CI service would split the suite out of the
  Docker stage. Azure SDK mocks give sufficient behavior verification for the
  publishing logic. Kept as a future enhancement.
- **NSubstitute instead of Moq.** Equivalent; Moq chosen for its established
  pattern of mocking the Azure SDK's virtual members and `TaskOrchestrationContext`.
- **Skip service interfaces, make methods `virtual`.** Works but is less
  idiomatic and leaks test concerns into the shape of the concrete classes.
  Interfaces are cleaner and also document the seams.

## Risks / Trade-offs

- **Faking the abstract Functions HTTP types** adds test-double code; mitigated by
  keeping `HttpStart` thin and testing the bulk of logic through the internal
  dictionary-based helpers.
- **Hitting 85 % with heavy external I/O**: mitigated by the option-builder seams,
  service interfaces, FFmpeg integration tests on synthesized media, and a
  documented exclusion list for the composition root / generated code.
- **`test` stage needs ffmpeg/yt-dlp**: added from the same pinned sources as
  `base`; this only affects the off-path `test` stage, not `final`.
- **Codecov requires `CODECOV_TOKEN`**: documented as a required repo secret; the
  workflow still fails the build on test/coverage failure independent of Codecov
  upload success (upload marked non-blocking if desired).
- **Determinism**: tests avoid all network; the FFmpeg integration inputs are
  generated locally, so no reliance on YouTube/Twitch/OpenAI availability.
