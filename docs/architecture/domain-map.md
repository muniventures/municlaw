# Domain map

| Domain | Owner | Implementation destination |
| --- | --- | --- |
| Accounts and organization authorization | MuniClaw control plane | `modules/api`, `modules/core` |
| Projects, tasks, runs, approvals and delivery | MuniClaw | `modules/core` |
| Git/model/infrastructure integration contracts | MuniClaw | `modules/core/Integrations` |
| VPS supervisor, sandbox and OpenCode adapter | MuniClaw | `modules/worker` |
| Coding console | MuniClaw | `modules/ui/dashboard` |
| VPS provider lifecycle and DNS | Minicloud through API | Separate private repository |

All implementation destinations are planned. See the feature registry for status.
