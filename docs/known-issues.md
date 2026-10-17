# Known Issues, Risks and Open Decisions

Last updated: 2026-10-17 (Phase 1)

## Open decisions

Waiting for the product owner's decision:

- Questions **Q1–Q14** in [requirements.md](requirements.md) §5.
- Workflow questions **WQ1–WQ4** in [task-workflow.md](task-workflow.md) §9.

The most important one is **Q1**: the stack says GitLab for source control, but the repository is on GitHub. That choice decides whether CI uses GitHub Actions or GitLab CI.

## Environment limitations (found during Phase 1)

| Item | Status | Impact |
|---|---|---|
| .NET 10 SDK | Not installed on the development machine | Must be installed before Phase 3 (backend skeleton). |
| PostgreSQL | Not installed | Needed for local runs; Docker or a local install. |
| Docker | Not installed | Needed for Testcontainers integration tests and local PostgreSQL/mail catcher. |
| Node.js | v22.22.1 installed | Check against the Angular version's supported Node range in Phase 3. |
| GitHub CLI | Signed-in token is invalid | Pushing to the remote needs `gh auth login` (or a git credential). |

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
