# Known Issues, Risks and Open Decisions

Last updated: 2026-10-17 (Phase 2)

## Open decisions

None. Q1–Q14 and WQ1–WQ4 were resolved on 2026-10-17 by accepting the recommended defaults (Q1: GitHub + GitHub Actions). Phase 2 design is awaiting approval.

## Environment limitations (found during Phase 1)

| Item | Status | Impact |
|---|---|---|
| .NET 10 SDK | Not installed on the development machine | Must be installed before Phase 3 (backend skeleton). |
| PostgreSQL | Not installed | Needed for local runs; Docker or a local install. |
| Docker | Not installed | Needed for Testcontainers integration tests and local PostgreSQL/mail catcher. |
| Node.js | v22.22.1 installed | Angular 22.2 requires `^22.22.3 \|\| ^24.15.0 \|\| >=26`. **Upgrade Node before Phase 3.** |
| GitHub access | Remote uses SSH (`git@github.com:naveenmsccs003/task-management-application.git`); SSH key works | `gh` CLI token is invalid — only needed for PR/CI commands (`gh auth login`). |

## Risks

| Risk | Mitigation |
|---|---|
| Scope is large (19 modules). | Build in phases; each phase ends with tests and approval. |
| Time-tracking accuracy if developers forget to stop. | Max session length auto-close, notifications, TL time adjustments with audit. |
| Permission mistakes leading to data exposure (IDOR). | Central authorization handlers; integration tests per endpoint for unauthorized access. |
| Malicious uploads. | Signature checks, private storage, download headers, optional malware scanning (Q9). |
| GitLab API changes or rate limits. | Isolated integration module, retries, import run logs. |

## Known limitations

None yet — no code exists.
