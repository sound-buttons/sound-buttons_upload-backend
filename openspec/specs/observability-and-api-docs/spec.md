# observability-and-api-docs Specification

## Purpose

Defines the cross-cutting operational concerns of the
`sound-buttons_upload-backend` service: structured logging via Serilog (console
plus optional Seq sink) with per-submission instance-id correlation, the outbound
HTTP client identity, and the OpenAPI document exposure for the HTTP endpoints.
Implemented in `SoundButtons/Program.cs` (host/log/service configuration) and used
throughout the functions via `Serilog.Context.LogContext`.

## Requirements

### Requirement: Structured logging via Serilog

The service SHALL configure Serilog as the logging provider with a console sink and
a Seq sink. Framework noise SHALL be suppressed by overriding the `Microsoft`,
`Microsoft.Hosting.Lifetime`, and `System` sources to the `Fatal` minimum level
while the application default remains verbose. The Seq sink is always registered
and reads its server URL and API key from the `Seq_ServerUrl` and `Seq_ApiKey`
environment variables.

#### Scenario: Console and Seq sinks configured

- **WHEN** the host starts
- **THEN** Serilog writes to the console with a structured output template
- **AND** Serilog writes to Seq using `Seq_ServerUrl`/`Seq_ApiKey`
- **AND** `Microsoft`, `Microsoft.Hosting.Lifetime`, and `System` log sources are overridden to `Fatal`

### Requirement: Instance-id log correlation

Functions that participate in a submission SHALL enrich their log scope with the
orchestration `InstanceId` via `LogContext`, so all log entries for one submission
share a correlatable property and the configuration enriches from the log context.

#### Scenario: Logs enriched from context

- **GIVEN** an activity or orchestrator processing a submission with an `InstanceId`
- **WHEN** it logs
- **THEN** the `InstanceId` property is attached to the log events

### Requirement: Outbound HTTP client identity

The service SHALL register a named HTTP client (`client`) whose default
`User-Agent` identifies the application (including the `.NET` product,
`Sound-Buttons` product, and the `https://sound-buttons.click` reference). This
named client is used by the OpenAI transcription service. Note that the YouTube
clip-page scraping path currently uses an ad-hoc `new HttpClient()` and therefore
does not carry this configured `User-Agent`.

#### Scenario: User-Agent set on the named client

- **WHEN** the named HTTP client (`client`) is created
- **THEN** its default `User-Agent` header includes the `.NET`, `Sound-Buttons`, and `(+https://sound-buttons.click)` products

#### Scenario: Clip scraping uses an ad-hoc client

- **WHEN** the YouTube clip page is fetched during source resolution
- **THEN** the request is made with an ad-hoc `HttpClient` that does not apply the named-client `User-Agent`

### Requirement: OpenAPI document exposure

The service SHALL expose an OpenAPI v3 document for its HTTP endpoints using the
Functions OpenAPI extension, with server host names resolved from the request host
and neither HTTP nor HTTPS force-rewriting applied.

#### Scenario: OpenAPI v3 configured

- **WHEN** the host configures OpenAPI options
- **THEN** the OpenAPI version is V3
- **AND** the requesting host name is included in the served document
- **AND** `ForceHttps` and `ForceHttp` are both disabled
