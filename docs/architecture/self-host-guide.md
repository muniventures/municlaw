# MuniClaw Self-Host Operator Guide

This guide describes how to deploy, configure, secure, and operate MuniClaw in self-hosted environments. It covers both single-node turnkey deployments using Docker Compose and dedicated VPS worker topologies for production isolation.

---

## 1. Architecture Overview (Standalone Mode)

MuniClaw is designed to run independently of Minicloud or external proprietary cloud control planes. In standalone mode, external dependencies (such as managed databases, secret management vaults, and cloud hypervisors) are substituted with self-contained, high-performance local implementations.

### 1.1 Core Components

```
+---------------------------------------------------------------------------------+
|                                 Reverse Proxy / Ingress                         |
|                       Nginx Unprivileged (Port 8080 / 443)                      |
+---------------------------------------+-----------------------------------------+
                                        |
                   +--------------------+--------------------+
                   |                                         |
            [ / (Static SPA) ]                        [ /api (SSE & REST) ]
                   v                                         v
+--------------------------------------+   +--------------------------------------+
|          municlaw-dashboard          |   |             municlaw-api             |
|       React Router v7 / Vite SPA     |   |         ASP.NET Core (.NET 10)       |
+--------------------------------------+   +-------------------+------------------+
                                                               |
                       +---------------------------------------+
                       |
                       +--------------------+
                       |                    |
                       v                    v
        +----------------------------+  +-----------------------------------------+
        |   Persistence Adapter      |  |          Secret Store Adapter           |
        | - MuniClawFileStore (JSON) |  | - LocalEncryptedSecretStore (AES-GCM)   |
        | - PostgreSQL (sql/schema)  |  | - OpenBao / Vault KV-v2                 |
        +----------------------------+  +-----------------------------------------+
                       ^
                       | Outbound Long-Polling & SSE Heartbeats
                       |
        +--------------+----------------------------------------------------------+
        |                                                                         |
        |                             municlaw-worker                             |
        |                 Supervisor Daemon (.NET 10 BackgroundService)           |
        |                                                                         |
        |   +-----------------------+           +-----------------------------+   |
        |   |   TaskSandboxManager  |           |     OpenCode Supervisor     |   |
        |   |  /tmp/municlaw/tasks  |           |  Pinned v1.18.32 (Loopback) |   |
        |   +-----------------------+           +-----------------------------+   |
        +-------------------------------------------------------------------------+
```

### 1.2 Component Responsibilities

1. **`municlaw-dashboard`**: Responsive React web console built with Tailwind CSS and Vite. Served by an unprivileged Nginx web server on port 8080.
2. **`municlaw-api`**: The core control plane API exposing REST endpoints and Server-Sent Events (SSE). Manages organization workspaces, task lifecycles, provider credentials, and dispatch leases.
3. **`municlaw-worker`**: Dedicated supervisor daemon that connects **exclusively outbound** to the API via HTTP/HTTPS long-polling. Manages child processes, sandbox directory isolation, and the pinned OpenCode coding harness.
4. **`MuniClawFileStore`**: Embedded file-based persistence backend providing atomic snapshotting, mutex concurrency locks, and zero-external-dependency durability for single-host deployments.
5. **`LocalEncryptedSecretStore`**: Lightweight, self-contained secret store implementing authenticated AES-256-GCM encryption with HMAC verification using an operator-defined master key (`MUNICLAW_MASTER_KEY`).
6. **OpenCode Harness (v1.18.32)**: Pinned headless coding agent runtime executed strictly on loopback (`127.0.0.1`) with HTTP Basic Auth.

---

## 2. Hardware and Operating System Requirements

### 2.1 System Requirements

| Role | Minimum CPU | Minimum RAM | Minimum Disk | Recommended Storage |
| --- | --- | --- | --- | --- |
| **All-in-One Host (Compose)** | 4 vCPUs | 8 GB RAM | 40 GB | High-IOPS SSD or NVMe |
| **API & Dashboard Host** | 2 vCPUs | 4 GB RAM | 20 GB | Standard SSD |
| **Dedicated Worker VPS** | 4 vCPUs | 8 GB RAM | 50 GB | High-IOPS SSD |

### 2.2 Supported Linux Distributions

- **Ubuntu**: 22.04 LTS, 24.04 LTS
- **Debian**: 12 (Bookworm)
- **RHEL / Rocky Linux / AlmaLinux**: 9.x
- **Fedora**: 39+

---

## 3. Master Key Generation and Cryptography

MuniClaw protects provider credentials (OpenAI, Anthropic, DeepSeek, GitHub tokens) using authenticated encryption at rest via `LocalEncryptedSecretStore`.

### 3.1 Generating a 256-Bit Master Key

Before starting the service, generate a cryptographically strong 256-bit (32-byte) master key:

```bash
# Generate 64-character hexadecimal key (256-bit)
openssl rand -hex 32
```

Save this key securely in your secret manager or environment configuration (`MUNICLAW_MASTER_KEY`).

### 3.2 Cryptographic Security Invariant

- **Cipher**: AES-256-GCM (Galois/Counter Mode).
- **IV / Nonce**: 96-bit cryptographically secure random nonce generated per secret write.
- **Authentication Tag**: 128-bit MAC tag verifying payload authenticity and integrity.
- **Key Derivation**: HKDF-SHA256 derived from `MUNICLAW_MASTER_KEY` with organization context isolation.
- **Filesystem Permissions**: The secrets directory `/var/lib/municlaw/secrets` is strictly restricted to permission `0700`, and all secret files are created with `0600`.
- **Tamper Detection**: Any bit modification or payload corruption triggers an immediate `CryptographicException` and prevents credential emission.

---

## 4. Quickstart: Single-Node Docker Compose

For single-node self-hosting, MuniClaw provides a production-grade Docker Compose file in `deploy/docker-compose.self-host.yml`.

### 4.1 Step 1: Clone Repository and Navigate to Deploy Directory

```bash
git clone https://github.com/municlaw/municlaw.git
cd municlaw
```

### 4.2 Step 2: Configure Environment Variables

Create an environment file named `.env` alongside the Compose file:

```bash
cat << 'EOF' > deploy/.env
# ==============================================================================
# MuniClaw Self-Host Environment Configuration
# ==============================================================================

# Master cryptographic key for encrypting provider credentials (AES-256-GCM)
# Generate with: openssl rand -hex 32
MUNICLAW_MASTER_KEY=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

# External HTTP port for Web Dashboard
MUNICLAW_PORT=8080

# Internal API port (optional exposure)
MUNICLAW_API_PORT=5000

# Persistent storage paths on container volumes
MUNICLAW_STORAGE_PATH=/var/lib/municlaw/data
MUNICLAW_SECRET_PATH=/var/lib/municlaw/secrets

# Default Organization ID for single-tenant mode
MUNICLAW_ORGANIZATION_ID=00000000-0000-0000-0000-000000000001
EOF
```

### 4.3 Step 3: Launch Stack

Start all containers in detached mode:

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.self-host.yml up -d --build
```

### 4.4 Step 4: Verify Deployment Health

Check the status of the containers:

```bash
docker compose -f deploy/docker-compose.self-host.yml ps
```

Expected output:
```
NAME                 IMAGE                  COMMAND                  SERVICE              STATUS
municlaw-api         deploy-municlaw-api    "dotnet MuniClaw.Api…"   municlaw-api         running (healthy)
municlaw-worker      deploy-municlaw-worker "dotnet MuniClaw.Wor…"   municlaw-worker      running
municlaw-dashboard   deploy-municlaw-dash…  "/docker-entrypoint.…"   municlaw-dashboard   running
```

Verify the API health check directly:

```bash
curl -f http://localhost:5000/health
# {"status":"healthy","service":"MuniClaw.Api"}
```

Open your browser and navigate to `http://<your-server-ip>:8080` to access the MuniClaw Web Console.

---

## 5. Production Topology: Dedicated Worker VPS

For production environments executing untrusted user code or running large coding tasks, separating the **Worker** onto a dedicated VPS is strongly recommended.

```
+------------------------------------+        Outbound HTTPS Long-Polling
|      Control Plane (API Host)      |<=========================================+
| - municlaw-api                     |                                          |
| - municlaw-dashboard               |                                          |
| - Persistent database/secrets      |                                          |
+------------------------------------+                                          |
                                                                                |
+-------------------------------------------------------------------------------+--+
|                             Dedicated Worker VPS                                 |
|                                                                                  |
| - User: municlaw (unprivileged system user)                                      |
| - Service: municlaw-worker.service (systemd)                                     |
| - Pinned OpenCode v1.18.32 (/usr/local/bin/opencode)                             |
| - Sandbox Directory: /tmp/municlaw/tasks (1777 sticky bit)                       |
+----------------------------------------------------------------------------------+
```

### 5.1 Step 1: Bootstrap the Worker Host

Transfer the repository or the bootstrap script to the dedicated worker VPS and run it as root:

```bash
sudo ./scripts/self-host/bootstrap-host.sh
```

The bootstrap script automatically:
1. Detects host architecture (`x86_64` or `aarch64`).
2. Installs base utilities (`curl`, `git`, `jq`, `ca-certificates`).
3. Installs Docker engine if missing.
4. Downloads and installs the pinned OpenCode binary (`v1.18.32`) to `/usr/local/bin/opencode`.
5. Creates the dedicated `municlaw` system user and group.
6. Provisions `/tmp/municlaw/tasks` with sticky permissions (`1777`).
7. Hardens `/var/lib/municlaw` permissions (`0750` / `0700`).

### 5.2 Step 2: Configure Worker Environment

Edit `/etc/municlaw/worker.env` to point to your MuniClaw API:

```bash
sudo cat << 'EOF' > /etc/municlaw/worker.env
# MuniClaw Dedicated Worker Configuration
MUNICLAW_API_URL=https://api.municlaw.yourdomain.com
MUNICLAW_ORGANIZATION_ID=00000000-0000-0000-0000-000000000001
MUNICLAW_TASKS_ROOT=/tmp/municlaw/tasks

# Pinned OpenCode Harness Configuration
OPENCODE_EXECUTABLE_PATH=/usr/local/bin/opencode
OPENCODE_HOST=127.0.0.1
OPENCODE_PORT=4096
OPENCODE_PASSWORD=your-secure-internal-loopback-password
EOF
```

Ensure the configuration file permissions are restricted:

```bash
sudo chown root:municlaw /etc/municlaw/worker.env
sudo chmod 0640 /etc/municlaw/worker.env
```

### 5.3 Step 3: Install and Start Systemd Service

Copy the systemd unit file and activate the worker:

```bash
sudo cp scripts/self-host/municlaw-worker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now municlaw-worker.service
```

### 5.4 Step 4: Verify Service Status

```bash
sudo systemctl status municlaw-worker.service
```

Inspect live worker supervisor logs:

```bash
sudo journalctl -u municlaw-worker.service -f
```

---

## 6. Storage, Backup, and Disaster Recovery

### 6.1 Data Storage Layout

In standalone mode, MuniClaw stores persistent state under `/var/lib/municlaw`:

```
/var/lib/municlaw/
|-- data/
|   |-- municlaw-store.json          # Main transactional entity store
|   |-- municlaw-store.json.tmp      # Atomic swap target during flushes
|   `-- snapshots/                   # Historical snapshots
`-- secrets/
    |-- {orgId}/
    |   `-- credentials/
    |       `-- {credId}.enc         # AES-256-GCM encrypted payload
    `-- .metadata                    # Store metadata and IV records
```

### 6.2 Atomic Write Guarantees

`MuniClawFileStore` writes changes through an atomic write-replace pattern:
1. Data is written and flushed to a temporary staging file (`.json.tmp`).
2. `FileStream.Flush(flushToDisk: true)` forces write buffers to physical storage.
3. An atomic file replace/move swaps the temporary file into the canonical destination (`.json`).
4. Concurrency is guarded by in-memory readers-writer locks and cross-process mutexes.

### 6.3 Backup Procedure

To take a consistent backup while MuniClaw is running:

```bash
BACKUP_DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR="/var/backups/municlaw/${BACKUP_DATE}"

mkdir -p "${BACKUP_DIR}"

# 1. Backup persistent data
tar -czf "${BACKUP_DIR}/municlaw_data.tar.gz" -C /var/lib/municlaw data

# 2. Backup encrypted secrets (preserving 0700/0600 permissions)
tar -czf "${BACKUP_DIR}/municlaw_secrets.tar.gz" -C /var/lib/municlaw secrets

# 3. Store SHA-256 checksums
cd "${BACKUP_DIR}" && sha256sum *.tar.gz > SHA256SUMS

echo "Backup completed successfully at ${BACKUP_DIR}"
```

> [!IMPORTANT]
> The encrypted secrets archive (`municlaw_secrets.tar.gz`) is undecryptable without the corresponding `MUNICLAW_MASTER_KEY`. Always store your master key in an independent, secure password manager or key vault.

### 6.4 Restore Procedure

To restore MuniClaw from a backup archive:

```bash
# 1. Stop services
docker compose -f deploy/docker-compose.self-host.yml down
# (or if using systemd: sudo systemctl stop municlaw-worker.service)

# 2. Extract data to target location
tar -xzf /var/backups/municlaw/20260928_120000/municlaw_data.tar.gz -C /var/lib/municlaw/
tar -xzf /var/backups/municlaw/20260928_120000/municlaw_secrets.tar.gz -C /var/lib/municlaw/

# 3. Enforce strict permissions
chown -R municlaw:municlaw /var/lib/municlaw
chmod 0700 /var/lib/municlaw/data
chmod 0700 /var/lib/municlaw/secrets
find /var/lib/municlaw/secrets -type f -exec chmod 0600 {} +

# 4. Restart stack
docker compose -f deploy/docker-compose.self-host.yml up -d
```

### 6.5 Worktree Retention and Disk Space Policy

- **7-Day Retention Rule**: Completed task worktrees in `/tmp/municlaw/tasks/{taskId}` are retained for 7 days after the last completed run, allowing operators to inspect artifacts and git logs.
- **Active Task Protection**: Tasks in `Preparing`, `Running`, or `AwaitingInput` states are strictly protected from retention sweeps.
- **Disk Pressure Threshold**: The worker checks free disk space prior to accepting task claims. If available space falls below 1 GB, claims are rejected with `DiskPressureDetected` to prevent host exhaustion.

---

## 7. Reverse Proxy and SSL Termination

In production, place an SSL-terminating reverse proxy (e.g., Caddy, Traefik, or Nginx) in front of the MuniClaw dashboard.

### 7.1 Caddyfile Example

```caddy
municlaw.example.com {
    encode gzip zstd

    # Route all requests to MuniClaw dashboard container
    reverse_proxy 127.0.0.1:8080 {
        # Streaming settings for Server-Sent Events (SSE)
        flush_interval -1
    }
}
```

### 7.2 Nginx SSL Proxy Example

```nginx
server {
    listen 443 ssl http2;
    server_name municlaw.example.com;

    ssl_certificate /etc/letsencrypt/live/municlaw.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/municlaw.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Disable buffering for SSE streaming
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }
}
```

---

## 8. Troubleshooting and Operations

### 8.1 Common Operational Scenarios

#### Symptom: Server-Sent Events (SSE) Stream Fails or Lags
- **Root Cause**: An intermediate proxy or load balancer is buffering the HTTP response stream.
- **Resolution**: Ensure `proxy_buffering off;` and `X-Accel-Buffering: no` headers are present in all intermediate proxies. In Nginx, set `proxy_read_timeout 86400s;`.

#### Symptom: `CryptographicException: Tag mismatch` or Corrupted Secret
- **Root Cause**: The secret file was modified, truncated, or encrypted with a different `MUNICLAW_MASTER_KEY`.
- **Resolution**: Verify that the `MUNICLAW_MASTER_KEY` environment variable has not changed. If corrupted, re-enter the provider API key in the web console under **Connections**.

#### Symptom: Worker Reports `Disk pressure admission rejected`
- **Root Cause**: Host free disk space on `/tmp/municlaw/tasks` dropped below the 1 GB threshold.
- **Resolution**: Free up disk space or prune old docker volumes. To purge completed worktrees older than 7 days manually:
  ```bash
  # Check task directory sizes
  du -sh /tmp/municlaw/tasks/*
  ```

#### Symptom: Worker Refuses to Claim Tasks (`HarnessVersionMismatch`)
- **Root Cause**: The installed OpenCode executable does not report version `1.18.32`.
- **Resolution**: Re-run `bootstrap-host.sh` or verify `/usr/local/bin/opencode --version`. MuniClaw strictly requires pinned OpenCode `1.18.32` to guarantee event contract compatibility.

### 8.2 Inspecting Logs

```bash
# View combined Compose logs
docker compose -f deploy/docker-compose.self-host.yml logs -f --tail=100

# Inspect specific service
docker compose -f deploy/docker-compose.self-host.yml logs -f municlaw-api
docker compose -f deploy/docker-compose.self-host.yml logs -f municlaw-worker
docker compose -f deploy/docker-compose.self-host.yml logs -f municlaw-dashboard

# Dedicated VPS systemd worker logs
sudo journalctl -u municlaw-worker.service -n 100 -f
```
