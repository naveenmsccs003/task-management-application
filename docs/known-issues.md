# Known Issues, Risks and Open Decisions

Last updated: 2026-10-17 (Phase 3)

## Open decisions

None. Q1–Q14 and WQ1–WQ4 were resolved on 2026-10-17 by accepting the recommended defaults (Q1: GitHub + GitHub Actions). Phase 2 design approved 2026-10-17. Phase 3 is awaiting approval.

## Environment notes (development machine)

| Item | Status | Impact |
|---|---|---|
| .NET SDK | 10.0.401 installed in `~/.dotnet`, but **not on `PATH`** | Add `export PATH="$HOME/.dotnet:$PATH"` to your shell profile, or call `~/.dotnet/dotnet`. |
| Node.js | System Node is 22.22.1 (too old for Angular 22). Node 24.22.0 LTS was installed user-locally in `~/.local/opt/node-v24.22.0-linux-x64` (checksum verified); `PATH` was **not** changed | Add `export PATH="$HOME/.local/opt/node-v24.22.0-linux-x64/bin:$PATH"`, or use a version manager with `frontend/.nvmrc`. |
| Docker | Not installed (needs `sudo`) | `compose.yaml` and Testcontainers cannot run locally. Integration tests were run against a temporary PostgreSQL 18.6 extracted from Ubuntu packages, via `TMA_TEST_POSTGRES`. |
| GitHub access | Remote uses SSH; key works | `gh` CLI token is invalid — only needed for PR/CI commands (`gh auth login`). |

## Known limitations (Phase 3)

| # | Limitation | Plan |
|---|---|---|
| L1 | The Testcontainers code path and the GitHub Actions workflows have **not been executed yet** (no Docker locally). Their first run happens on GitHub after this phase is pushed. | Check the first CI run; fix anything it reports. |
| L2 | On a brand-new database, EF Core logs one `fail: ... Failed executing DbCommand` for `ops.__ef_migrations_history` before creating it. This is EF Core's normal first-run behaviour, not an error. | None needed. |
| L3 | No authentication yet; `/api/v1/system/info` and health endpoints are the only API endpoints. | Phase 4. |
| L4 | End-to-end (Playwright) project and `TaskManagement.Application.Tests` not created yet — nothing to test with them. | Phase 4. |
| L5 | The Angular app's own Content Security Policy is set by the production reverse proxy, which is not configured yet. API responses already send strict security headers. | Deployment phase. |
| L6 | `compose.yaml` uses `axllent/mailpit:latest`. | Pin a version when email is implemented (Phase 7). |

## Risks

| Risk | Mitigation |
|---|---|
| Scope is large (19 modules). | Build in phases; each phase ends with tests and approval. |
| Time-tracking accuracy if developers forget to stop. | Max session length auto-close, notifications, TL time adjustments with audit. |
| Permission mistakes leading to data exposure (IDOR). | Central authorization handlers; integration tests per endpoint for unauthorized access. |
| Malicious uploads. | Signature checks, private storage, download headers, optional malware scanning (Q9). |
| GitLab API changes or rate limits. | Isolated integration module, retries, import run logs. |

