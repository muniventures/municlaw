# Security

Target requirements, not implemented guarantees.

- Authenticate and authorize all object reads, streams, approvals, credentials, worker claims and Git side effects by organization.
- Keep platform authority outside untrusted repositories and harness configuration. Sandbox filesystem, network, processes and resources independently of model instructions.
- Store credentials only in a scoped secret store; never in git, database events, prompts, logs or browser responses. Separate supervisor credentials from the coding sandbox.
- Bind approvals to actor, run, action and content version. Recheck membership before executing. Reject stale approvals.
- Default delivery requires review; no automatic merge, force push or production deployment.
- Separate coding VPSes from production. Block infrastructure metadata and management networks; do not mount host Docker sockets.
- Record metadata-only audit evidence. Use finite retention and explicit workspace deletion semantics.
- Use synthetic data in examples and tests. Keep credentials, production inventory and private-source dependencies outside this public-source design.

T00/T90 must demonstrate these boundaries before a hosted release. A public repository is not evidence of a security audit.
