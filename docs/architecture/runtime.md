# Runtime

Target runtime: one dedicated coding VPS per organization, managed by Minicloud and separate from production application hosts.

The trusted worker connects outbound to the MuniClaw API using a revocable scoped identity. It supervises one active task at a time in the MVP. Each task has a separate sandbox, worktree, home and OpenCode state. OpenCode runs as an unprivileged pinned headless dependency reachable only by the supervisor.

Enforce filesystem/network/resource boundaries outside repository-controlled configuration. Never expose host Docker sockets, control-plane database access, or Minicloud administrative credentials. Isolated build support must be qualified separately.

Persist task/session mappings and bounded event buffers. Fenced ownership, lease-loss termination and startup reconciliation prevent overlapping execution. Browser disconnection does not stop a task. VPS/disk loss is explicitly reported; transparent recovery of unpushed work is not promised.

T00 qualifies proxy support, kernel/sandbox requirements, cancellation bounds, versions and licensing. `infra/` will hold generic sandbox/deployment templates after those decisions. No production deployment files exist yet.
