#!/usr/bin/env bash
# ==============================================================================
# MuniClaw Host Bootstrap Script
# Pinned Dependencies: OpenCode v1.18.32, Docker, systemd, unprivileged runtime
# Target Platforms: Ubuntu 22.04+, Debian 12+, RHEL 9+, Rocky/AlmaLinux
# ==============================================================================

set -euo pipefail

OPENCODE_VERSION="1.18.32"
MUNICLAW_USER="municlaw"
MUNICLAW_GROUP="municlaw"
MUNICLAW_HOME="/var/lib/municlaw"
MUNICLAW_TASKS_DIR="/tmp/municlaw/tasks"
MUNICLAW_CONF_DIR="/etc/municlaw"
MUNICLAW_OPT_DIR="/opt/municlaw"

# ------------------------------------------------------------------------------
# 1. Privilege & Environment Verification
# ------------------------------------------------------------------------------
if [[ "${EUID}" -ne 0 ]]; then
    echo "[ERROR] bootstrap-host.sh must be run as root (or via sudo)." >&2
    exit 1
fi

echo "=== MuniClaw Host Bootstrap Starting ==="
echo "Operating System : $(uname -s)"
echo "Kernel Release   : $(uname -r)"
echo "Machine Arch     : $(uname -m)"

# ------------------------------------------------------------------------------
# 2. Architecture Normalization
# ------------------------------------------------------------------------------
ARCH=$(uname -m)
case "${ARCH}" in
    x86_64|amd64)
        OPENCODE_ARCH="x64"
        ;;
    aarch64|arm64)
        OPENCODE_ARCH="arm64"
        ;;
    *)
        echo "[ERROR] Unsupported architecture: ${ARCH}. MuniClaw requires x86_64 or aarch64." >&2
        exit 1
        ;;
esac

# ------------------------------------------------------------------------------
# 3. Detect Package Manager & Distribution
# ------------------------------------------------------------------------------
PKG_MGR=""
if command -v apt-get >/dev/null 2>&1; then
    PKG_MGR="apt"
elif command -v dnf >/dev/null 2>&1; then
    PKG_MGR="dnf"
elif command -v yum >/dev/null 2>&1; then
    PKG_MGR="yum"
else
    echo "[WARNING] Unknown package manager. Ensure Docker and prerequisites are installed manually."
fi

# ------------------------------------------------------------------------------
# 4. Install Base System Prerequisites
# ------------------------------------------------------------------------------
echo "--- Step 1: Installing Base Prerequisites ---"
case "${PKG_MGR}" in
    apt)
        export DEBIAN_FRONTEND=noninteractive
        apt-get update -y
        apt-get install -y --no-install-recommends \
            ca-certificates \
            curl \
            gnupg \
            lsb-release \
            tar \
            gzip \
            git \
            jq \
            bash
        ;;
    dnf|yum)
        "${PKG_MGR}" install -y \
            ca-certificates \
            curl \
            tar \
            gzip \
            git \
            jq \
            bash
        ;;
esac

# ------------------------------------------------------------------------------
# 5. Check and Install Docker
# ------------------------------------------------------------------------------
echo "--- Step 2: Checking Docker Engine ---"
if command -v docker >/dev/null 2>&1; then
    echo "[OK] Docker is already installed: $(docker --version)"
else
    echo "[INFO] Docker not found. Installing Docker engine..."
    if curl -fsSL https://get.docker.com -o /tmp/get-docker.sh; then
        sh /tmp/get-docker.sh
        rm -f /tmp/get-docker.sh
        echo "[OK] Docker installed successfully."
    else
        echo "[ERROR] Failed to download Docker convenience install script." >&2
        exit 1
    fi
fi

# Ensure Docker daemon is running and enabled
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl enable --now docker || true
    echo "[OK] Docker service enabled and started."
fi

# ------------------------------------------------------------------------------
# 6. Install Pinned OpenCode v1.18.32 Binary
# ------------------------------------------------------------------------------
echo "--- Step 3: Installing Pinned OpenCode v${OPENCODE_VERSION} (${OPENCODE_ARCH}) ---"
OPENCODE_BIN="/usr/local/bin/opencode"
OPENCODE_INSTALLED=false

if command -v opencode >/dev/null 2>&1; then
    CURRENT_VERSION=$(opencode --version 2>&1 || echo "unknown")
    if [[ "${CURRENT_VERSION}" == *"${OPENCODE_VERSION}"* ]]; then
        echo "[OK] Pinned OpenCode v${OPENCODE_VERSION} is already installed at $(command -v opencode)."
        OPENCODE_INSTALLED=true
    else
        echo "[INFO] Existing OpenCode (${CURRENT_VERSION}) differs from required v${OPENCODE_VERSION}. Updating..."
    fi
fi

if [[ "${OPENCODE_INSTALLED}" = false ]]; then
    DOWNLOAD_URL_TAR="https://github.com/anomalyco/opencode/releases/download/v${OPENCODE_VERSION}/opencode-linux-${OPENCODE_ARCH}.tar.gz"
    DOWNLOAD_URL_BIN="https://github.com/anomalyco/opencode/releases/download/v${OPENCODE_VERSION}/opencode-linux-${OPENCODE_ARCH}"
    TMP_DIR=$(mktemp -d /tmp/opencode-install-XXXXXX)

    echo "Downloading OpenCode v${OPENCODE_VERSION} from GitHub releases..."
    if curl -fsSL "${DOWNLOAD_URL_TAR}" -o "${TMP_DIR}/opencode.tar.gz" 2>/dev/null; then
        tar -xzf "${TMP_DIR}/opencode.tar.gz" -C "${TMP_DIR}"
        if [[ -f "${TMP_DIR}/opencode" ]]; then
            install -m 0755 "${TMP_DIR}/opencode" "${OPENCODE_BIN}"
        elif [[ -f "${TMP_DIR}/opencode-linux-${OPENCODE_ARCH}" ]]; then
            install -m 0755 "${TMP_DIR}/opencode-linux-${OPENCODE_ARCH}" "${OPENCODE_BIN}"
        fi
        echo "[OK] Installed OpenCode tarball to ${OPENCODE_BIN}."
    elif curl -fsSL "${DOWNLOAD_URL_BIN}" -o "${TMP_DIR}/opencode" 2>/dev/null; then
        install -m 0755 "${TMP_DIR}/opencode" "${OPENCODE_BIN}"
        echo "[OK] Installed OpenCode standalone binary to ${OPENCODE_BIN}."
    else
        echo "[WARNING] Could not download OpenCode binary directly from GitHub (air-gapped or network boundary)."
        echo "          Ensure the OpenCode v${OPENCODE_VERSION} executable is manually placed at ${OPENCODE_BIN}."
    fi
    rm -rf "${TMP_DIR}"
fi

# ------------------------------------------------------------------------------
# 7. Create System User and Group
# ------------------------------------------------------------------------------
echo "--- Step 4: Configuring System User '${MUNICLAW_USER}' ---"
if ! getent group "${MUNICLAW_GROUP}" >/dev/null 2>&1; then
    groupadd --system "${MUNICLAW_GROUP}"
    echo "[OK] Created group ${MUNICLAW_GROUP}."
fi

if ! id -u "${MUNICLAW_USER}" >/dev/null 2>&1; then
    useradd \
        --system \
        --shell /bin/bash \
        --home-dir "${MUNICLAW_HOME}" \
        --create-home \
        --gid "${MUNICLAW_GROUP}" \
        --comment "MuniClaw Autonomous Agent Runtime" \
        "${MUNICLAW_USER}"
    echo "[OK] Created user ${MUNICLAW_USER}."
else
    echo "[OK] User ${MUNICLAW_USER} already exists."
fi

# Grant docker access if docker group exists
if getent group docker >/dev/null 2>&1; then
    usermod -aG docker "${MUNICLAW_USER}"
    echo "[OK] Added ${MUNICLAW_USER} to docker group."
fi

# ------------------------------------------------------------------------------
# 8. Directory Provisioning & Permission Hardening
# ------------------------------------------------------------------------------
echo "--- Step 5: Preparing Directories and Hardening Permissions ---"

# Persistent state directory: 0750
mkdir -p "${MUNICLAW_HOME}"
chown "${MUNICLAW_USER}:${MUNICLAW_GROUP}" "${MUNICLAW_HOME}"
chmod 0750 "${MUNICLAW_HOME}"

# Data store directory: 0700
mkdir -p "${MUNICLAW_HOME}/data"
chown "${MUNICLAW_USER}:${MUNICLAW_GROUP}" "${MUNICLAW_HOME}/data"
chmod 0700 "${MUNICLAW_HOME}/data"

# Local encrypted secrets directory: 0700 (Security Invariant)
mkdir -p "${MUNICLAW_HOME}/secrets"
chown "${MUNICLAW_USER}:${MUNICLAW_GROUP}" "${MUNICLAW_HOME}/secrets"
chmod 0700 "${MUNICLAW_HOME}/secrets"

# Sandbox tasks scratch directory: 1777 (Sticky bit prevents cross-task interference)
mkdir -p "${MUNICLAW_TASKS_DIR}"
chown "${MUNICLAW_USER}:${MUNICLAW_GROUP}" "${MUNICLAW_TASKS_DIR}"
chmod 1777 "${MUNICLAW_TASKS_DIR}"

# Configuration directory: 0750 root:municlaw
mkdir -p "${MUNICLAW_CONF_DIR}"
chown "root:${MUNICLAW_GROUP}" "${MUNICLAW_CONF_DIR}"
chmod 0750 "${MUNICLAW_CONF_DIR}"

# Binaries directory for standalone worker: 0755
mkdir -p "${MUNICLAW_OPT_DIR}/worker"
chown -R "root:${MUNICLAW_GROUP}" "${MUNICLAW_OPT_DIR}"
chmod -R 0755 "${MUNICLAW_OPT_DIR}"

# Create default worker environment template if not present
WORKER_ENV_FILE="${MUNICLAW_CONF_DIR}/worker.env"
if [[ ! -f "${WORKER_ENV_FILE}" ]]; then
    cat <<EOF > "${WORKER_ENV_FILE}"
# MuniClaw Worker Daemon Configuration
# Generated by bootstrap-host.sh on $(date -u +"%Y-%m-%dT%H:%M:%SZ")

MUNICLAW_API_URL=http://127.0.0.1:5000
MUNICLAW_ORGANIZATION_ID=00000000-0000-0000-0000-000000000001
MUNICLAW_TASKS_ROOT=/tmp/municlaw/tasks
OPENCODE_EXECUTABLE_PATH=/usr/local/bin/opencode
OPENCODE_HOST=127.0.0.1
OPENCODE_PORT=4096
OPENCODE_PASSWORD=opencode-vps-secret
EOF
    chown "root:${MUNICLAW_GROUP}" "${WORKER_ENV_FILE}"
    chmod 0640 "${WORKER_ENV_FILE}"
    echo "[OK] Created worker environment template at ${WORKER_ENV_FILE}."
fi

echo "=== MuniClaw Host Bootstrap Completed Successfully ==="
echo ""
echo "Next steps:"
echo "  1. Single-node Docker deployment:"
echo "     docker compose -f deploy/docker-compose.self-host.yml up -d"
echo ""
echo "  2. Dedicated VPS Worker deployment:"
echo "     - Configure /etc/municlaw/worker.env with your organization ID and API URL"
echo "     - Install scripts/self-host/municlaw-worker.service to /etc/systemd/system/"
echo "     - Run: systemctl daemon-reload && systemctl enable --now municlaw-worker.service"
echo ""
