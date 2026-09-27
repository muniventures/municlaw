# Feature: <Feature Name>

Scope: <one line scope>

## Execution split

Execution for this feature is split into:

- `features/<area>/<feature>/0.execution-plan.md`
- `features/<area>/<feature>/subspecs/01.<contract>.md`
- `features/<area>/<feature>/subspecs/10.<runtime-or-backend>.md`
- `features/<area>/<feature>/subspecs/20.<cli-or-user-surface>.md`
- `features/<area>/<feature>/subspecs/90.<stabilization>.md`

Add or remove sub-spec entries as needed, but keep the overview spec as the narrative entry point and keep live execution tracking in `0.execution-plan.md`.

## Folder placement

Specs live under an area subgroup:

- `features/platform/<feature>/...`
- `features/product/<feature>/...`
- `features/integrations/<feature>/...`
- `features/security/<feature>/...`

Choose the most specific stable area name. Use lowercase kebab-case for area and feature folders.

## Agent instructions

- Use stable task IDs (`T00`, `T10`, `T20`, etc.) for all executable work items.
- Keep live task status in `0.execution-plan.md`, not in this overview file.
- After finishing any task, update the relevant sub-spec, `0.execution-plan.md`, and `features/FEATURES_REGISTRY.md`.
- If you use an execution-plan style list, append `[completed]` to finished task titles.

## Goal

<What outcome this feature must deliver.>

## Current patterns learned

<Document existing scripts, commands, templates, modules, and constraints already present in the codebase.>

## Scope and non-goals

### In scope

- <item>

### Out of scope

- <item>

## Requirements

### User experience requirements

- <item>

### Runtime requirements

- <item>

### CLI requirements

- <item>

### Data and validation

- <item>

### Security

- <item>

### Verification

- <item>

## Execution-plan expectations

- `0.execution-plan.md` owns the task index, dependencies, parallelization, phase plan, and live status.
- Every executable sub-spec has a stable task ID that matches the execution plan.
- Use `Depends on` and `Can run in parallel with` fields explicitly.

## Decisions and open questions

### Decisions locked

- <decision>

### Open questions

- <question or `None`>
