## ADDED Requirements

### Requirement: Automated test suite

The project SHALL provide an automated test suite (xUnit) consisting of unit tests
and integration tests, in a dedicated `SoundButtons.Tests` project that references
the application project. Unit tests SHALL run without any network access or
external service, mocking external boundaries (Azure Blob Storage, the OpenAI HTTP
API, and the yt-dlp process). Integration tests SHALL exercise the real FFmpeg
encode/cut/transcode paths against locally-synthesized media and the runtime tool
discovery against real `yt-dlp`/`ffmpeg` binaries, without network access.

#### Scenario: Suite runs offline

- **GIVEN** the test suite
- **WHEN** it is executed with no network connectivity
- **THEN** all unit tests pass
- **AND** integration tests rely only on locally-generated media and locally-present binaries (no YouTube/Twitch/OpenAI calls)

#### Scenario: Unit and integration tests are both present

- **WHEN** the test project is inspected
- **THEN** it contains unit tests for the functions, services, helpers, and models
- **AND** integration tests that run real FFmpeg conversions

### Requirement: Application requirement traceability

The test suite SHALL cover every requirement defined in the application capability
specs (`audio-submission-api`, `audio-acquisition-encoding`,
`audio-processing-workflow`, `blob-storage-publishing`,
`speech-to-text-transcription`, and `observability-and-api-docs`) with at least
one test each. The mapping from requirement to test SHALL be discoverable (for
example via per-capability test traits).

#### Scenario: Every application requirement has a covering test

- **GIVEN** the application capability specs
- **WHEN** the traceability mapping is reviewed
- **THEN** each requirement is associated with at least one test that verifies its behavior

#### Scenario: Clip transcode behavior is verified

- **GIVEN** an integration test feeding a video-plus-AAC input through the audio-only transform
- **WHEN** the test runs
- **THEN** it asserts the output is an audio-only Opus WebM with no video stream

### Requirement: Minimum code coverage threshold

The test run SHALL measure code coverage of the application assembly and SHALL
enforce a minimum total coverage of at least 85 percent, failing the test command
(and therefore the containerized test stage and CI) when coverage is below the
threshold. Code that is not meaningfully unit-testable — the composition root
(`Program.cs`), source-generated code (the JSON serialization context and regex
partials), and the static OpenAPI configuration — MAY be excluded from the metric,
and any exclusions SHALL be documented.

#### Scenario: Build fails below the threshold

- **GIVEN** a test run whose measured total coverage is below 85 percent
- **WHEN** the coverage threshold is evaluated
- **THEN** the test command exits non-zero

#### Scenario: Coverage report is produced

- **WHEN** the suite runs with coverage collection
- **THEN** a Cobertura coverage report is produced for upload/inspection

### Requirement: Production testability seams

The production code SHALL expose the seams required for isolated testing without
changing externally observable behavior: the audio-processing and OpenAI services
SHALL be consumed through interfaces registered in dependency injection, the
YouTube-clip scraping SHALL use the injected named HTTP client rather than an
ad-hoc client, and internal helpers SHALL be visible to the test assembly.

#### Scenario: Services are injected via interfaces

- **WHEN** the dependency-injection registrations are inspected
- **THEN** the audio-processing and OpenAI services are registered and consumed through interfaces that can be mocked in tests

#### Scenario: Scraping uses the named HTTP client

- **WHEN** the YouTube-clip scraping path is inspected
- **THEN** it obtains its `HttpClient` from the injected factory (the named `"client"`) rather than constructing one directly

#### Scenario: Behavior is unchanged

- **GIVEN** the testability refactors
- **WHEN** the application runs
- **THEN** the HTTP API, orchestration chain, and audio/blob behavior are identical to before the refactors

### Requirement: Containerized test execution

The `Dockerfile` SHALL provide a `test` stage that runs the full test suite with
coverage collection and has the `ffmpeg`, `ffprobe`, and `yt-dlp` binaries
available for the integration tests, and a `report` stage that exports the
coverage and test-result reports for extraction. The production (`final`) image
SHALL be unaffected by these stages.

#### Scenario: Test stage runs the suite

- **WHEN** the image is built with `--target test`
- **THEN** the test suite executes inside the build and a test or sub-threshold-coverage failure fails the build

#### Scenario: Report stage exports results

- **WHEN** the image is built with `--target report --output type=local,dest=./out`
- **THEN** the Cobertura coverage report and the test results are written to the output directory

#### Scenario: Production image is unchanged

- **WHEN** the `final` image is built
- **THEN** its contents do not include the test project or test-only binaries, and the runtime behavior is unchanged

### Requirement: CI test-and-coverage gate

A CI workflow SHALL run the test suite on every pull request and on every push to
`master`, executing the suite via the Dockerfile `test` stage, and SHALL upload
the coverage report to Codecov. The workflow SHALL fail when tests fail or
coverage is below the threshold.

#### Scenario: Tests run on pull requests and master pushes

- **GIVEN** a pull request or a push to `master`
- **WHEN** CI runs
- **THEN** the test workflow builds the Dockerfile `test`/`report` target and runs the suite

#### Scenario: Coverage is published to Codecov

- **WHEN** the test workflow completes the build
- **THEN** it uploads the Cobertura coverage report to Codecov using the configured token

#### Scenario: Failing tests block the workflow

- **GIVEN** a change that breaks a test or drops coverage below the threshold
- **WHEN** CI runs
- **THEN** the workflow fails

### Requirement: Infrastructure requirement conformance checks

The CI SHALL include static/build conformance checks that cover the
infrastructure capabilities (`dotnet-runtime-platform`, `container-init-process`,
`kubernetes-deployment-security`), because those capabilities are not exercised by
.NET unit tests. These checks SHALL cover their requirements: linting the
`Dockerfile`, asserting the pinned image/tool versions and the init-process
contract (PID-1 `dumb-init`, `STOPSIGNAL SIGINT`, checksum verification,
decoupling from the FFmpeg image), asserting the target .NET framework, and
asserting that the Helm templates render `automountServiceAccountToken: false`.

#### Scenario: Dockerfile and init contract are checked

- **WHEN** CI runs
- **THEN** `hadolint` passes and assertions confirm the `dumb-init` ENTRYPOINT/`STOPSIGNAL`, the checksum step, and that `dumb-init` is not copied from the FFmpeg image

#### Scenario: Kubernetes token automount is checked

- **WHEN** the Helm chart is rendered in CI
- **THEN** an assertion confirms `automountServiceAccountToken: false` is present

#### Scenario: Runtime platform is checked

- **WHEN** CI runs
- **THEN** assertions confirm the target framework is `net10.0` and the pinned non-.NET tool versions are present in the `Dockerfile`
