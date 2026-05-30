## ADDED Requirements

### Requirement: ServiceAccount API-token automounting disabled by default

The backend Helm chart SHALL NOT mount a Kubernetes ServiceAccount API token
into the backend pod by default. The backend workload does not call the
Kubernetes API server, so the chart SHALL set `automountServiceAccountToken:
false` on the backend Deployment pod template, removing an unused credential
from the running container.

The setting SHALL be applied at the **pod** level (in the Deployment pod
template `spec`) so that it covers every container in the backend pod (the
`soundbuttonsbackend` worker and the `azurite-emulator` sidecar) and does not
modify any cluster-shared object such as the `default` ServiceAccount.

The behavior SHALL be configurable through a Helm value so that an operator can
opt back in when required, and the default value of that setting SHALL be
`false` (token NOT mounted).

#### Scenario: Token automount disabled in rendered chart by default

- **WHEN** the chart is rendered with default values (no override supplied)
- **THEN** the backend Deployment pod template `spec.automountServiceAccountToken` is `false`

#### Scenario: No token mounted into the running pod

- **WHEN** the backend pod is scheduled with the default (disabled) setting
- **THEN** no ServiceAccount API token is projected into any container of the pod (neither `soundbuttonsbackend` nor `azurite-emulator`)
- **AND** the path `/var/run/secrets/kubernetes.io/serviceaccount/` is absent inside the containers

#### Scenario: Setting is exposed as a Helm value defaulting to false

- **WHEN** `helm/values.yaml` is inspected
- **THEN** a key `kubernetes.backend.automountServiceAccountToken` exists with the value `false`
- **AND** the Deployment template renders `automountServiceAccountToken` from that value

#### Scenario: Operator can re-enable automounting

- **WHEN** the chart is rendered with `kubernetes.backend.automountServiceAccountToken=true`
- **THEN** the backend Deployment pod template `spec.automountServiceAccountToken` is `true`

#### Scenario: Workload behavior is unchanged

- **WHEN** the backend pod runs with the token automount disabled
- **THEN** the Azure Functions isolated worker starts and the `/api/healthz` liveness probe succeeds
- **AND** audio processing, Azure Blob Storage access, and Durable orchestration continue to function, because none of them depend on the Kubernetes API token
