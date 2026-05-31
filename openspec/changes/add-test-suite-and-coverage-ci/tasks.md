## 1. Test project and tooling

- [x] 1.1 Create `SoundButtons.Tests/SoundButtons.Tests.csproj` (net10.0,
  `IsPackable=false`, nullable enable) referencing `SoundButtons.csproj`, with
  packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `Moq`, `coverlet.collector` (for local IDE/report use) and `coverlet.msbuild`
  (for the threshold gate — see 6.1; the two coverlet integrations are distinct).
- [x] 1.2 Add `SoundButtons.sln` referencing `SoundButtons` and
  `SoundButtons.Tests`.
- [x] 1.3 Add `InternalsVisibleTo("SoundButtons.Tests")` to `SoundButtons`
  (via `AssemblyAttribute` in the csproj or an `AssemblyInfo`/`InternalsVisibleTo`
  item) so internal helpers/seams are testable.
- [x] 1.4 Add a `coverlet.runsettings` (or csproj props) configuring Cobertura
  output and the exclusion filters for `Program`, `SourceGenerationContext`, the
  generated regex partials, and the OpenAPI options lambda.

## 2. Production testability seams (behavior-preserving)

- [x] 2.0 Change the pure private helpers that tests target directly
  (`GetBoundary`, `ParseMultipartFormDataAsync`, `GetSourceInfo`, `SourceCheck`,
  `GetFileName`, `ProcessClip`/`ProcessYoutubeClip`/`ProcessTwitchClip` as needed,
  and `ProcessJson.UpdateJson`) from `private` to `internal` (or move them into an
  `internal` helper class). `InternalsVisibleTo` exposes `internal`, **not**
  `private`, members, so this step is a prerequisite for the matrix in §4. Helpers
  that are only reachable through a public entry point may stay private and be
  covered through that entry point — update the traceability matrix accordingly.
- [x] 2.1 Extract `IProcessAudioService` (over `DownloadAudioAsync` overloads,
  `CutAudioAsync`, `TranscodeAudioAsync`) and `IOpenAiService` (over
  `SpeechToTextAsync`); have `ProcessAudioService`/`OpenAiService` implement them.
- [x] 2.2 Register the interfaces in `Program.cs`
  (`services.AddScoped<IProcessAudioService, ProcessAudioService>()`, etc.) and
  change `SoundButtons`, `ProcessAudio`, `SpeechToText` constructors to depend on
  the interfaces.
- [x] 2.3 In `ProcessAudioService`, extract the `OptionSet` construction into
  `internal static` builder methods (e.g. `BuildVideoIdOptionSet(Source, tempPath)`,
  `BuildClipOptionSet(url, tempPath)`) that do **not** touch tool discovery, so the
  options can be asserted without constructing the service (the ctor calls
  `YoutubeDLHelper.WhereIs()` + `FFmpeg.SetExecutablesPath()`, which require real
  binaries — keep that only in integration tests).
- [x] 2.4 Inject `IHttpClientFactory` into `SoundButtons` and make
  `ProcessYoutubeClip` use the named `"client"` instead of `new HttpClient()`.
  Verify all existing behavior is preserved (no API/orchestration changes).
- [x] 2.5 Extract DI registration into an `internal static`
  `ServiceCollectionExtensions.AddSoundButtonsServices(this IServiceCollection)`
  called from `Program.cs`, so the named `"client"` User-Agent and service
  registrations are assertable in a test (Program.cs itself stays excluded from
  coverage).

## 3. Test doubles and helpers

- [x] 3.1 Add `FakeHttpRequestData`/`FakeHttpResponseData`/`TestFunctionContext`
  doubles for the abstract Worker HTTP types.
- [x] 3.2 Add a `FakeHttpMessageHandler` (queued/predicated responses) for
  `OpenAiService` and the YouTube-clip scrape.
- [x] 3.3 Add an FFmpeg media-synthesis helper that generates deterministic inputs
  with `ffmpeg` lavfi (video+AAC/MP4, audio-only, multi-stream) for integration
  tests.
- [x] 3.4 Add a blob test-double helper that mocks the full
  `IAzureClientFactory<BlobServiceClient>` → `BlobServiceClient` →
  `BlobContainerClient` → `BlobClient` chain, including the ctor-time
  `CreateIfNotExists()` call (the `UploadAudioToStorage`/`ProcessJson` constructors
  invoke it eagerly, so it must be set up before constructing those functions to
  avoid a real Azure call).

## 4. Unit tests (per the traceability matrix in design.md)

- [x] 4.1 Models: `Button.Volume` normalization (0 → 1), `Text`/`Source`/`JsonRoot`
  construction and JSON property names.
- [x] 4.2 `audio-submission-api`: `GetBoundary`, `ParseMultipartFormDataAsync`,
  `GetSourceInfo`, `SourceCheck`, `GetFileName`, and `HttpStart` validation paths
  (bad content-type, no source, >30 MB) + kickoff/status (mock `DurableTaskClient`)
  + `Utility.Healthz`.
- [x] 4.3 `audio-acquisition-encoding`: option-builder assertions + argument
  guards; `ProcessAudioAsync` branch logic incl. failed-download guards (mock
  `IProcessAudioService`).
- [x] 4.4 `audio-processing-workflow`: `RunOrchestrator` activity ordering and
  missing-file abort/cleanup (mock `TaskOrchestrationContext`); `functionTimeout`
  config assertion; instance-id `LogContext` enrichment.
- [x] 4.5 `blob-storage-publishing`: `UploadAudioToStorageAsync` (content type,
  collision suffix, IP metadata) and `ProcessJsonFile`/`UpdateJson` (read-before-
  update, group find/create, button append, URL-encoded videoId, dual timestamped
  write) with mocked blob clients.
- [x] 4.6 `speech-to-text-transcription`: `SpeechToText` sentinel gating + graceful
  `HttpRequestException` degradation (mock `IOpenAiService`); `OpenAiService`
  request shape + API-key guard (fake handler).
- [x] 4.7 `observability-and-api-docs`: named `"client"` User-Agent identity and
  scrape-via-named-client; instance-id correlation (shared with 4.4).
- [x] 4.8 Tag test classes with `[Trait("spec", "<capability>")]` for traceability.

## 5. Integration tests (real FFmpeg / tool discovery, no network)

- [x] 5.1 `TranscodeAudioAsync`: video+AAC/MP4 input → assert ffprobe shows a
  single Opus audio stream in WebM and no video; no-audio input → throws.
- [x] 5.2 `CutAudioAsync`: synthesized input trimmed to the requested duration
  (±tolerance) and remains a valid `.webm`.
- [x] 5.3 `YoutubeDLHelper.WhereIs`: resolves `yt-dlp`/`ffmpeg` from `PATH`
  (real binaries in the test stage) and from a temp dir with dummy files (unit).

## 6. Coverage gate

- [x] 6.1 Enforce the gate with **coverlet.msbuild** (the threshold MSBuild
  properties do not apply to the `--collect "XPlat Code Coverage"` data-collector
  path). Run:
  `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`
  `/p:Threshold=85 /p:ThresholdType=line,branch /p:ThresholdStat=total`
  with the documented exclusions. `ThresholdStat=total` measures the whole
  application assembly (not per-file). Produce the Cobertura report for Codecov.
- [x] 6.2 Iterate tests until total line+branch coverage of the application
  assembly is ≥ 85 % and the threshold gate passes locally. Deliberately cover the
  negative/error branches that dominate this codebase: `SourceCheck` invalid/empty
  durations; missing source / bad content-type / >30 MB upload; blob exists vs not
  (collision suffix); JSON missing/null + OOM retry; OpenAI key-missing / HTTP
  failure / language-omitted; download-succeeded vs file-missing guards.

## 7. Dockerfile `test` and `report` stages

- [x] 7.1 Add `FROM build AS test`: copy `SoundButtons.Tests` csproj and restore
  it **for the build platform** — do NOT reuse the production `-a $TARGETARCH`
  restore, since tests execute on `$BUILDPLATFORM` and a target-arch restore would
  run under emulation / mismatch RIDs. Add `ffmpeg`/`ffprobe`/`yt-dlp` from the
  same pinned sources as `base`; copy sources; run
  `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`
  `/p:Threshold=85 /p:ThresholdType=line,branch /p:ThresholdStat=total`
  `--results-directory /testresults` (per §6.1). Use bind/cache mounts per the
  containerfile-creator pattern. The test workflow targets native `linux/amd64`.
- [x] 7.2 Add `FROM scratch AS report` copying `/testresults` (the Cobertura XML
  and TRX) for `--output type=local` extraction.
- [x] 7.3 Relax `.dockerignore` so the test sources reach the build context (and,
  only if a Docker-stage check parses OpenSpec markdown, un-exclude
  `openspec/specs/**/*.md` — prefer to keep markdown parsing out of the image).
  Confirm the `final` image build is unchanged (test/report stages are off-path).
- [x] 7.4 Run `hadolint Dockerfile` and confirm it passes.

## 8. CI workflow and Codecov

- [x] 8.1 Add `.github/workflows/test.yml` on `pull_request` and `push` to
  `master`: checkout (submodules), set up Buildx, `docker build --target report
  --output type=local,dest=./out .` (this runs the suite + threshold gate inside
  the `test` stage, so the build fails on test/coverage failure regardless of
  Codecov). Then upload `./out/**/coverage.cobertura.xml` to Codecov with
  `codecov/codecov-action@v5`. Guard the upload so fork PRs (which cannot read
  `secrets.CODECOV_TOKEN`) do not fail the job: gate the Codecov step on the token
  being present (or non-fork events) and set `fail_ci_if_error: false` — the gate
  is already enforced by the Docker `test` stage.
- [x] 8.2 Add the infra-capability conformance checks (Dockerfile pin/ENTRYPOINT/
  STOPSIGNAL assertions, `helm template` automount assertion, csproj TFM
  assertion) as steps/scripts in the workflow.
- [x] 8.3 Add a `codecov.yml` (and optionally a README coverage badge) and
  document the required `CODECOV_TOKEN` secret.

## 9. Validation

- [x] 9.1 `dotnet test` locally (or in the SDK 10 container) passes with coverage
  ≥ 85 %.
- [x] 9.2 `docker build --target test` succeeds (suite passes inside the image);
  `docker build --target report --output type=local,dest=./out .` extracts the
  Cobertura + TRX files.
- [x] 9.3 `docker build --target final` still succeeds and the image is unchanged.
- [x] 9.4 `openspec validate add-test-suite-and-coverage-ci --strict` passes.

## 10. Commit

- [x] 10.1 Commit the change with a conventional message and the `Co-authored-by`
  trailer.
