-- ==============================================================================
-- MuniClaw Self-Hosted PostgreSQL Production Schema DDL
-- Target: PostgreSQL 14+ / 16+
-- Task: T10 (Standalone Persistence and Secrets for Self-Host Distribution)
-- ==============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ------------------------------------------------------------------------------
-- 1. Organizations
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS organizations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    slug VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_organizations_slug UNIQUE (slug)
);

CREATE INDEX IF NOT EXISTS ix_organizations_slug ON organizations (slug);

-- ------------------------------------------------------------------------------
-- 2. Users
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    external_subject_id VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL,
    is_allowlisted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_users_external_subject_id UNIQUE (external_subject_id)
);

CREATE INDEX IF NOT EXISTS ix_users_external_subject_id ON users (external_subject_id);
CREATE INDEX IF NOT EXISTS ix_users_email ON users (email);

-- ------------------------------------------------------------------------------
-- 3. Memberships
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS memberships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role VARCHAR(32) NOT NULL DEFAULT 'Member',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_memberships_org_user UNIQUE (organization_id, user_id),
    CONSTRAINT ck_memberships_role CHECK (role IN ('Admin', 'Member'))
);

CREATE INDEX IF NOT EXISTS ix_memberships_org_id ON memberships (organization_id);
CREATE INDEX IF NOT EXISTS ix_memberships_user_id ON memberships (user_id);

-- ------------------------------------------------------------------------------
-- 4. Coding Workspaces
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS coding_workspaces (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    minicloud_server_id VARCHAR(255),
    status VARCHAR(32) NOT NULL DEFAULT 'Pending',
    vps_ip_address VARCHAR(64),
    supervisor_registration_token_hash VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    failure_reason TEXT,
    CONSTRAINT ck_coding_workspaces_status CHECK (status IN ('Pending', 'Ready', 'Failed', 'Terminated'))
);

CREATE INDEX IF NOT EXISTS ix_coding_workspaces_org_id ON coding_workspaces (organization_id);
CREATE INDEX IF NOT EXISTS ix_coding_workspaces_status ON coding_workspaces (status);

-- ------------------------------------------------------------------------------
-- 5. Repository Connections
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS repository_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    git_host VARCHAR(32) NOT NULL,
    external_account_id VARCHAR(255) NOT NULL,
    installation_id VARCHAR(255),
    repository_id VARCHAR(255) NOT NULL,
    repository_full_name VARCHAR(255) NOT NULL,
    default_branch VARCHAR(100) NOT NULL,
    secret_reference_path TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_repository_connections_git_host CHECK (git_host IN ('GitHub', 'GitLab'))
);

CREATE INDEX IF NOT EXISTS ix_repository_connections_org_id ON repository_connections (organization_id);
CREATE INDEX IF NOT EXISTS ix_repository_connections_repo_full_name ON repository_connections (repository_full_name);

-- ------------------------------------------------------------------------------
-- 6. Projects
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS projects (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    repository_connection_id UUID NOT NULL REFERENCES repository_connections(id) ON DELETE RESTRICT,
    name VARCHAR(255) NOT NULL,
    default_base_branch VARCHAR(100) NOT NULL,
    setup_commands TEXT,
    setup_commands_version INT NOT NULL DEFAULT 1,
    setup_commands_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_projects_org_id ON projects (organization_id);
CREATE INDEX IF NOT EXISTS ix_projects_repository_connection_id ON projects (repository_connection_id);

-- ------------------------------------------------------------------------------
-- 7. Provider Credentials
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS provider_credentials (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    owning_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider_name VARCHAR(64) NOT NULL,
    label VARCHAR(255) NOT NULL,
    scope VARCHAR(32) NOT NULL DEFAULT 'Personal',
    policy JSONB,
    secret_reference_path TEXT NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at TIMESTAMPTZ,
    CONSTRAINT ck_provider_credentials_scope CHECK (scope IN ('Personal', 'Organization'))
);

CREATE INDEX IF NOT EXISTS ix_provider_credentials_org_user ON provider_credentials (organization_id, owning_user_id);
CREATE INDEX IF NOT EXISTS ix_provider_credentials_scope ON provider_credentials (organization_id, scope);
CREATE INDEX IF NOT EXISTS ix_provider_credentials_active ON provider_credentials (organization_id, is_revoked);

-- ------------------------------------------------------------------------------
-- 8. Tasks
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tasks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'Open',
    task_branch VARCHAR(255) NOT NULL,
    base_branch VARCHAR(100) NOT NULL,
    base_commit_sha VARCHAR(64),
    created_by_user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    archived_at TIMESTAMPTZ,
    CONSTRAINT ck_tasks_status CHECK (status IN ('Open', 'InProgress', 'Completed', 'Failed', 'Cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_tasks_org_id ON tasks (organization_id);
CREATE INDEX IF NOT EXISTS ix_tasks_project_id ON tasks (project_id);
CREATE INDEX IF NOT EXISTS ix_tasks_status ON tasks (status);

-- ------------------------------------------------------------------------------
-- 9. Task Runs
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS task_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id UUID NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    run_index INT NOT NULL DEFAULT 1,
    status VARCHAR(32) NOT NULL DEFAULT 'Queued',
    provider_credential_reference_id UUID NOT NULL REFERENCES provider_credentials(id) ON DELETE RESTRICT,
    resolved_model VARCHAR(100) NOT NULL,
    instruction TEXT NOT NULL,
    harness_version VARCHAR(32) NOT NULL,
    lease_token VARCHAR(255),
    fencing_token BIGINT NOT NULL DEFAULT 0,
    lease_expires_at_utc TIMESTAMPTZ,
    max_budget_usd NUMERIC(10, 4),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    failure_reason TEXT,
    CONSTRAINT uq_task_runs_task_index UNIQUE (task_id, run_index),
    CONSTRAINT ck_task_runs_status CHECK (status IN (
        'Queued', 'Preparing', 'Running', 'AwaitingInput', 'Cancelling', 'Completed', 'Failed', 'Cancelled'
    ))
);

CREATE INDEX IF NOT EXISTS ix_task_runs_task_id ON task_runs (task_id);
CREATE INDEX IF NOT EXISTS ix_task_runs_org_status ON task_runs (organization_id, status);
CREATE INDEX IF NOT EXISTS ix_task_runs_lease_expires ON task_runs (lease_expires_at_utc);

-- ------------------------------------------------------------------------------
-- 10. Task Events
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS task_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id UUID NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    run_id UUID NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE,
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    sequence_number BIGINT NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    payload_json JSONB NOT NULL,
    is_truncated BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_task_events_run_sequence UNIQUE (run_id, sequence_number)
);

CREATE INDEX IF NOT EXISTS ix_task_events_run_seq ON task_events (run_id, sequence_number ASC);
CREATE INDEX IF NOT EXISTS ix_task_events_task_id ON task_events (task_id);

-- ------------------------------------------------------------------------------
-- 11. Approval Requests
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS approval_requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    task_id UUID NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    run_id UUID NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE,
    capability VARCHAR(64) NOT NULL,
    action_description TEXT NOT NULL,
    content_version_hash VARCHAR(128) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'Pending',
    decided_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    decision VARCHAR(32),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    decided_at TIMESTAMPTZ,
    CONSTRAINT ck_approval_requests_status CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Expired')),
    CONSTRAINT ck_approval_requests_decision CHECK (decision IS NULL OR decision IN ('AllowOnce', 'AllowAlways', 'Deny'))
);

CREATE INDEX IF NOT EXISTS ix_approval_requests_run_id ON approval_requests (run_id);
CREATE INDEX IF NOT EXISTS ix_approval_requests_org_status ON approval_requests (organization_id, status);

-- ------------------------------------------------------------------------------
-- 12. Deliveries
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS deliveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    task_id UUID NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    run_id UUID NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE,
    git_host VARCHAR(32) NOT NULL,
    repository_id VARCHAR(255) NOT NULL,
    base_commit_sha VARCHAR(64) NOT NULL,
    reviewed_commit_sha VARCHAR(64) NOT NULL,
    target_branch VARCHAR(255) NOT NULL,
    remote_pr_number VARCHAR(64),
    remote_pr_url TEXT,
    published_commit_sha VARCHAR(64),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_deliveries_git_host CHECK (git_host IN ('GitHub', 'GitLab'))
);

CREATE INDEX IF NOT EXISTS ix_deliveries_task_id ON deliveries (task_id);
CREATE INDEX IF NOT EXISTS ix_deliveries_org_id ON deliveries (organization_id);

-- ------------------------------------------------------------------------------
-- 13. Usage Records
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usage_records (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    task_id UUID NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    run_id UUID NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE,
    provider VARCHAR(32) NOT NULL,
    model_name VARCHAR(100) NOT NULL,
    prompt_tokens BIGINT NOT NULL DEFAULT 0,
    completion_tokens BIGINT NOT NULL DEFAULT 0,
    estimated_cost_usd NUMERIC(10, 6),
    price_provenance VARCHAR(255) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_usage_records_provider CHECK (provider IN ('OpenAI', 'Anthropic', 'DeepSeek'))
);

CREATE INDEX IF NOT EXISTS ix_usage_records_org_id ON usage_records (organization_id);
CREATE INDEX IF NOT EXISTS ix_usage_records_run_id ON usage_records (run_id);
