## Context

The backend is an Azure Functions (.NET 10 isolated worker) service that clips
YouTube audio, writes results to Azure Blob Storage, and coordinates work with
Durable Functions. It is packaged as a container and deployed to Kubernetes via
the in-repo Helm chart (`helm/`). The backend pod talks to Azure Storage and an
in-pod Azurite emulator container; it never calls the Kubernetes API server.

The Deployment pod template (`helm/templates/backend-deployment.yaml`) does not
set `automountServiceAccountToken`. Kubernetes therefore defaults it to `true`
and projects the `default` ServiceAccount token into the container at
`/var/run/secrets/kubernetes.io/serviceaccount/`. This is an unused credential
mounted on disk inside a process that handles untrusted remote input (YouTube
URLs processed by `yt-dlp`/FFmpeg), increasing the blast radius of any RCE.

The chart has no `ServiceAccount` template; the pod runs as the namespace
`default` ServiceAccount. `_helpers.tpl` defines an unused
`soundbuttons.serviceAccountName` helper referencing a `.Values.serviceAccount`
block that does not exist in `values.yaml` (orphaned, out of scope here).

## Goals / Non-Goals

**Goals:**
- Stop mounting the unused ServiceAccount API token into the backend pod by
  default (defense-in-depth: remove an unnecessary credential).
- Keep the change configurable via a Helm value, defaulting to the secure
  posture (`false`).
- Make zero functional change to the running workload.

**Non-Goals:**
- Creating a dedicated ServiceAccount or RBAC objects for the backend.
- Modifying the orphaned `serviceAccount` helper in `_helpers.tpl` or adding a
  `serviceAccount.create` workflow.
- Broader pod-hardening (read-only root FS, seccomp, dropping capabilities,
  `securityContext` for the `azurite-emulator` sidecar). Those may be follow-ups
  under the same capability but are out of scope for this change.
- Any application code, container image, API, or CI changes.

## Decisions

### Decision 1: Disable the token at the pod level, not the ServiceAccount level

`automountServiceAccountToken` can be set on a ServiceAccount or on a Pod spec;
the Pod-level setting wins when both are present. We set it on the Deployment pod
template.

Rationale: The pod currently uses the namespace-shared `default` ServiceAccount.
Setting `automountServiceAccountToken: false` on that ServiceAccount would affect
every other pod in the namespace that also uses `default` — an unacceptable
side-effect for a chart. The Pod-level setting is self-contained, affects only
this workload, and needs no new RBAC/ServiceAccount object.

Alternatives considered:
- *Create a dedicated ServiceAccount with automount disabled and bind the pod to
  it.* More "correct" long-term and isolates identity, but adds a new template,
  a `serviceAccountName` on the pod, and values plumbing for marginal extra
  benefit given the token is simply not needed. Deferred as a possible follow-up.
- *Set it on the `default` ServiceAccount.* Rejected — mutates a cluster-shared
  object and leaks behavior to unrelated pods.

### Decision 2: Expose a Helm value defaulting to `false`

Add `kubernetes.backend.automountServiceAccountToken: false` to `values.yaml`
and render it directly in the Deployment pod template:
`automountServiceAccountToken: {{ .Values.kubernetes.backend.automountServiceAccountToken }}`.
The key is defined in the chart defaults and normal Helm value overrides
preserve omitted defaults, so a direct reference is appropriate and is the
conventional Helm idiom. (An operator could still break the path by replacing
the `kubernetes.backend` map wholesale, e.g. `--set kubernetes.backend=null`,
but that is an explicit misuse outside the chart's supported configuration.)

Rationale: Keeps the secure default while allowing an opt-in for any future
operator who genuinely needs API access, without forking the template. The key
is always emitted so the pod never silently falls back to the Kubernetes cluster
default of `true`.

Alternatives considered:
- *Render with the nil-safe `dig` helper
  (`dig "kubernetes" "backend" "automountServiceAccountToken" false .Values`).*
  Rejected — this top-level form failed because Helm exposes `.Values` as a
  `chartutil.Values`, not the plain `map[string]interface{}` that `dig` expects,
  raising an "interface conversion" error (confirmed via `helm lint`). Coercion
  workarounds exist but are unnecessary here; the direct reference is simpler and
  adequate because the key is defined in the chart defaults.
- *Render with `default false .Values...`.* Equivalent truth table, but adds no
  value over the direct reference since the key is always defined, and a bare
  `default` chain still errors if an intermediate map is nil.
- *Hardcode `automountServiceAccountToken: false` with no value.* Simpler, but
  removes the escape hatch and is inconsistent with the chart's value-driven
  style. Rejected.

## Risks / Trade-offs

- **A future feature needs the Kubernetes API (e.g. in-cluster discovery).** →
  Mitigation: the value can be flipped to `true` via `--set`, or a dedicated
  ServiceAccount added later. No token is needed today.
- **Go-template falsiness causes the key to be omitted, letting Kubernetes
  default back to `true`.** → Mitigation: the key is always defined in
  `values.yaml` and rendered with a direct reference, so the literal
  `automountServiceAccountToken` key is always emitted; verify with
  `helm template` that the rendered output contains
  `automountServiceAccountToken: false`.
- **Operators relying on the previously-mounted token via custom tooling.** →
  Extremely unlikely (pre-release, 0 users, no in-cluster API use); documented in
  the value comment and reversible via `--set`.

## Migration Plan

1. Add the value and render it in the Deployment template.
2. Validate with `helm template`/`helm lint` that the rendered pod spec contains
   `automountServiceAccountToken: false` by default and `true` when overridden.
3. On the next chart deploy, the rolling update recreates the pod without the
   token volume.

Rollback: set `kubernetes.backend.automountServiceAccountToken=true` (or revert
the change); the next rollout restores the previous behavior. No data migration.

## Open Questions

None.
