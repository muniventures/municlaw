# MuniClaw Operations Guide

This document establishes operational procedures for MuniClaw private cloud coding workspaces, covering supervisor registration, secret management, fault tolerance, retention, and disaster recovery.

---

## 1. Dedicated VPS Provisioning & Bootstrap

Each organization receives a dedicated, single-tenant Minicloud VPS managed separately from production infrastructure:

1. **Onboarding Provisioning**:
   - An organization administrator requests workspace creation via `POST /api/v1/organizations/{organizationId}/workspace`.
   - Control plane requests VPS allocation via `IMinicloudInfrastructureClient` and generates a cryptographically random 64-character registration token (`rawToken`).
   - Only the SHA-256 hash (`SupervisorRegistrationTokenHash`) is stored in the database.
2. **Supervisor Bootstrap**:
   - The dedicated VPS runs the `MuniClaw.Worker` systemd service under an unprivileged system user (`municlaw-worker`).
   - On first boot, the supervisor presents the registration token to exchange for revocable outbound API credentials (JWT/mTLS).
   - The supervisor connects exclusively outbound via HTTPS to the MuniClaw API; no inbound ports are exposed to the public internet.

---

## 2. Worker Leased Claims & Fencing Architecture

To guarantee single-task organization execution and prevent split-brain execution across worker restarts:

1. **Long-Poll Claim Loop**:
   - Worker issues `POST /api/v1/worker/claim` with `WorkerId`, `OrganizationId`, and `SupportedHarnessVersion`.
   - If work is queued and no other task is actively executing in that organization, the control plane returns:
     - `RunId`, `TaskId`, `TaskBranch`, `BaseCommit`
     - `LeaseToken`: Cryptographically random lease identifier, valid for 30 seconds.
     - `FencingToken`: Monotonically increasing epoch number per run.
2. **Heartbeat & Command Cursors**:
   - Worker must send `POST /api/v1/worker/heartbeat` every 10 seconds.
   - Valid heartbeat extends `LeaseExpiresAtUtc` by 30 seconds and delivers pending server commands (`Abort`, `Cancel`, `Resume`, `Reconcile`) after `LastAcknowledgedCommandCursor`.
3. **Lease Expiry & Split-Brain Prevention**:
   - If a heartbeat is missed and the lease expires, `ReconcileExpiredLeasesAsync` marks the run as `Failed` and invalidates the `LeaseToken`.
   - The worker's heartbeat will receive HTTP 410 Gone / `IsLeaseValid == false`, which immediately triggers hard process termination of all child processes.
   - Any event upload with an obsolete fencing token is rejected with HTTP 409 Conflict.

---

## 3. Sandboxing & Defensive Isolation

Untrusted repository code and commands execute under strict operating boundaries:

- **Filesystem**: Dedicated directory layout `/tmp/municlaw/tasks/{taskId}/worktree`, with separate non-shared `home` and `tmp`.
- **Directory Traversal**: Strictly forbidden. Relative paths escaping sandbox root are denied.
- **Docker Socket**: Access to `/var/run/docker.sock` or container daemon sockets is blocked at both platform policy and filesystem permission layers.
- **Network Boundaries**: Outbound access to link-local cloud metadata endpoints (`169.254.169.254`, `169.254.0.0/16`, `fd00:ec2::254`) and private infrastructure management networks is denied.
- **Resource Constraints**:
  - Memory: 4 GB maximum per task
  - CPU: Limited execution quota
  - Storage: 10 GB disk quota
  - Processes: Max 256 child processes

---

## 4. BYOK Credential Boundaries & Proxy

1. **Write-Only Submission**:
   - Provider keys (OpenAI, Anthropic, DeepSeek) are submitted via `POST /api/v1/organizations/{orgId}/credentials` with `Scope: Personal`.
   - Raw values are immediately written to scoped OpenBao paths: `secret/municlaw/orgs/{orgId}/users/{userId}/providers/{provider}/{id}`.
   - Raw keys are never stored in PostgreSQL, never returned in API responses, and never logged.
2. **Local Model Proxy**:
   - OpenCode connects to a supervisor-managed local model proxy on loopback (`http://127.0.0.1:{proxyPort}`).
   - The proxy injects the authenticated API key into outbound LLM requests.
   - Shell commands executed within the sandbox cannot inspect or environment-dump provider credentials.
3. **Revocation Cascades**:
   - Revoking a credential immediately terminates all active runs referencing that credential with `Status: Failed`.

---

## 5. Retention & Recovery Policies

1. **Worktree Retention**:
   - Completed task worktrees are retained for **7 days** following their latest terminal run.
   - Active tasks (`Preparing`, `Running`, `AwaitingInput`) are protected and can never be silently deleted.
   - Follow-up turns reset the 7-day retention clock from the new terminal run.
2. **Disk-Pressure Protection**:
   - If available disk space falls below threshold (1 GB), worker rejects claiming new tasks (`DiskPressureDetected`).
3. **Data Loss & Disaster Recovery**:
   - Unpushed local changes on a lost or damaged VPS cannot be recovered.
   - If a VPS is destroyed, published tasks can be reconstructed in a new session from their published remote Git task branches. Conversation logs and event histories remain durable in the MuniClaw control-plane database.
