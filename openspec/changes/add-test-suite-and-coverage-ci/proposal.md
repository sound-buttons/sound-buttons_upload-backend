## Why

The backend has **no automated tests and no CI test gate**: every push goes
straight to the image build, so a regression in the submission API, the
orchestration chain, audio encoding, or the blob/JSON publishing logic can ship
undetected. The recent audio-only-clip fix had to be verified by hand. The
project also has six application capability specs in `openspec/specs/` whose
behavior is currently unverified by code. We want a durable safety net: a unit +
integration test suite that covers every spec requirement, a measured coverage
floor (≥ 85 %), and a CI gate that runs the tests on every pull request and push
to `master`, publishing coverage to Codecov.

## What Changes

- **Add a test project** `SoundButtons.Tests` (xUnit + Moq + coverlet) and a
  solution file, with `InternalsVisibleTo` so internal helpers are testable.
- **Add unit tests** covering the models, helpers, services, activity functions,
  and the Durable orchestrator, mocking external boundaries (Azure Blob via the
  Azure SDK's virtual members, OpenAI via a fake `HttpMessageHandler`, yt-dlp via
  an abstracted option/runner seam).
- **Add integration tests** that exercise the real FFmpeg encode/cut/transcode
  paths on locally-synthesized media (no network) and the runtime tool-discovery
  helper against real `yt-dlp`/`ffmpeg` binaries.
- **Introduce minimal testability seams** in production code without changing
  external behavior: extract `IProcessAudioService` and `IOpenAiService`
  interfaces (registered in DI), and route the YouTube-clip scraping through the
  injected named `HttpClient` instead of an ad-hoc `new HttpClient()`.
- **Enforce a coverage floor** of ≥ 85 % via a coverlet threshold, with a small,
  documented exclusion list (composition root `Program.cs`, source-generated
  regex/JSON context, OpenAPI options).
- **Add a Dockerfile `test` stage and `report` stage** (per the
  containerfile-creator pattern): the `test` stage runs the whole suite with
  coverage collection (and has `ffmpeg`/`ffprobe`/`yt-dlp` available for the
  integration tests); the `report` stage (`FROM scratch`) exports the Cobertura
  coverage and TRX results. The production `final` image is unchanged.
- **Add a `test` GitHub Actions workflow** that runs on pull requests and pushes
  to `master`, executes the suite via the Dockerfile `test` stage, extracts the
  coverage report, and uploads it to **Codecov**; the build fails if tests fail
  or coverage is below the threshold.
- **Cover the infrastructure capabilities** (`container-init-process`,
  `dotnet-runtime-platform`, `kubernetes-deployment-security`) with static/build
  conformance checks in CI (hadolint, `helm template` assertions, build-time
  image assertions), since they are not exercised by .NET unit tests.

## Capabilities

### New Capabilities

- `automated-testing-and-coverage`: Defines the project's test suite, the
  requirement-to-test traceability obligation, the minimum coverage threshold and
  its enforcement, the production testability seams, the containerized test
  execution (Dockerfile `test`/`report` stages), and the CI test-and-coverage
  gate with Codecov publishing.

### Modified Capabilities

- None. The testability seams (service interfaces, DI registration, injected
  `HttpClient` for scraping) are internal refactors that preserve all existing
  capability behavior; no existing requirement changes.

## Impact

- **New**: `SoundButtons.Tests/` project, `SoundButtons.sln`, test doubles/helpers,
  `.github/workflows/test.yml`, a `codecov.yml` (optional config), Dockerfile
  `test` + `report` stages.
- **Modified (production, behavior-preserving)**: `Program.cs` (register service
  interfaces), `ProcessAudioService`/`OpenAiService` (implement interfaces),
  `SoundButtons.cs` (inject `IHttpClientFactory` for clip scraping), the
  consuming functions (depend on interfaces), `SoundButtons.csproj`
  (`InternalsVisibleTo`), `.dockerignore` (allow test sources into the build
  context for the `test` stage).
- **No change** to the runtime image contents, the HTTP API surface, the
  orchestration behavior, Helm runtime templates, or the existing
  `docker_publish` workflow.
- **CI requires** a `CODECOV_TOKEN` repository secret.
