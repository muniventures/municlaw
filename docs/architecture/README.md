# Architecture

This directory is the architecture source of truth. All runtime architecture below is the target design; repository structure and specifications are established, application implementation has not started.

Read [agent-instructions.md](agent-instructions.md), then the relevant guide:

- [Backend](backend.md): API, core, data and service boundaries.
- [Frontend](frontend.md): separate console and route/module patterns.
- [Runtime](runtime.md): supervisor, sandbox and harness.
- [Data and integrations](data-integrations.md): identity, Git, models and Minicloud API contracts.
- [Security](security.md): tenant, credential and execution boundaries.
- [Domain map](domain-map.md): ownership and module lookup.

The [MVP overview](../../features/product/municlaw/1.municlaw.md) defines scope. Its execution plan owns live implementation status. Update architecture in the same change as affected behavior. Do not document planned behavior as implemented.
