# Known Issues, Risks and Open Decisions

Last updated: 2026-10-17 (Phase 7)

## Open decisions

None. Q1–Q14 and WQ1–WQ4 were resolved on 2026-10-17 by accepting the recommended defaults (Q1: GitHub + GitHub Actions). Phase 2 design, Phase 3 foundation, Phases 4–6 (authentication and administration; projects, tasks, comments; algorithms, checklists, work sessions, time) approved 2026-10-17. Phase 7 is awaiting approval.

## Environment notes (development machine)

| Item | Status | Impact |
|---|---|---|
| .NET SDK | 10.0.401 installed in `~/.dotnet`, but **not on `PATH`** | Add `export PATH="$HOME/.dotnet:$PATH"` to your shell profile, or call `~/.dotnet/dotnet`. |
| Node.js | System Node is 22.22.1 (too old for Angular 22). Node 24.22.0 LTS was installed user-locally in `~/.local/opt/node-v24.22.0-linux-x64` (checksum verified); `PATH` was **not** changed | Add `export PATH="$HOME/.local/opt/node-v24.22.0-linux-x64/bin:$PATH"`, or use a version manager with `frontend/.nvmrc`. |
| Docker | Not installed (needs `sudo`) | `compose.yaml` and Testcontainers cannot run locally. Integration and end-to-end tests were run against a temporary PostgreSQL 18.6 extracted from Ubuntu packages (via `TMA_TEST_POSTGRES` / `TMA_E2E_DB`). It lives in a temporary folder and is gone after a reboot. |
| Playwright | Chromium for Playwright is in `~/.cache/ms-playwright` | Needed only for `e2e/`. |
| GitHub access | Remote uses SSH; key works | `gh` CLI token is invalid — only needed for PR/CI commands (`gh auth login`). |

## Known limitations

| # | Limitation | Plan |
|---|---|---|
| L1 | ~~Testcontainers path and GitHub Actions not yet executed.~~ **Resolved 2026-10-17:** first run of CI (all jobs incl. Testcontainers integration tests and gitleaks) and CodeQL succeeded. | — |
| L2 | On a brand-new database, EF Core logs one `fail: ... Failed executing DbCommand` for `ops.__ef_migrations_history` before creating it. This is EF Core's normal first-run behaviour, not an error. | None needed. |
| L3 | ~~No authentication yet.~~ **Resolved in Phase 4:** sign-in, password reset/invite, members, roles and permissions, settings and audit log. | — |
| L4 | ~~End-to-end project and `TaskManagement.Application.Tests` not created yet.~~ **Resolved in Phase 4.** | — |
| L5 | The Angular app's own Content Security Policy is set by the production reverse proxy, which is not configured yet. API responses already send strict security headers. | Deployment phase. |
| L6 | ~~`compose.yaml` used `axllent/mailpit:latest`.~~ **Resolved in Phase 7:** pinned to `v1.31.3`. | — |
| L7 | **MFA (TOTP) is not implemented yet.** Decision Q6 requires it for Admins (FR-AUTH-13). Until then Admin accounts are protected by password policy, lockout and rate limits only. | Separate follow-up step before any production use. |
| L8 | No "remember me" option: every session lasts `Auth:SessionLifetime` (default 8 hours, extended while in use). | With MFA follow-up. |
| L9 | Only the per-IP limits on sign-in and password endpoints exist; the general 300 requests/minute per-user limit ([api-design.md](api-design.md) §6) is not configured yet. | Before production. |
| L10 | Chrome lists some time zones under old names (e.g. `Asia/Calcutta`) that the server's time-zone database may not know. The Angular app maps the known old names to current ones (`Asia/Kolkata`); an unmapped old name is rejected by the server with a clear message. | None needed unless users report one. |
| L11 | Emails are sent by a background worker every 5 seconds, so an invite or reset email arrives a few seconds after the request. | By design (outbox, decision D7). |
| L12 | ~~Assignment did not notify the assignee and TL.~~ **Resolved in Phase 7.** | — |
| L13 | Task and project descriptions are shown as plain text (line breaks kept); Markdown is stored but not rendered yet. Plain text is safe; rendering needs a sanitizing Markdown renderer. | Before release (with attachments or hardening). |
| L14 | The Overdue flag is computed on read, comparing the due date with today's date in UTC, not the member's time zone, and no overdue/due-soon notifications are sent. | Phase 8 (daily job, `is_overdue`, notifications). |
| L15 | Changing an assignee after the first assignment (reassignment with approval, FR-ASG-04) is not available yet, so "tasks I was assigned to" visibility is exercised only through first assignments. | Phase 8. |
| L16 | Admin has no task permissions in the seed matrix (Admin is not automatically a TL), so an Admin can create projects and manage members but cannot create or assign tasks unless given a project role. | By design (permissions matrix, A6). |
| L17 | Task numbers come from a separate `projects.task_counters` row instead of a column on `projects` (see database-design.md §3.4), to avoid false concurrency conflicts on project edits. | None needed. |
| L19 | ~~On a heavily loaded machine the "Add member" form could be submitted before its role list had loaded.~~ **Resolved in Phase 6:** the button stays disabled until the form has loaded. | — |
| L20 | ~~No notifications for algorithm events, allocated-hour thresholds or auto-closed sessions.~~ **Resolved in Phase 7.** | — |
| L21 | Submitting for review, the mandatory-checklist check at submission (FR-CHK-05, AC-CHK-1), reopening checklist items on a revision (FR-CHK-06) and "algorithm re-approval required" (FR-ALG-06) are not available yet, so an In progress task can only be paused, stopped or cancelled. | Phase 8 (review). |
| L22 | Algorithm submissions take text only; attachments (FR-ALG-02) come with the attachments phase. | Phase 9. |
| L23 | Timesheets and time reports by member, project and date range (FR-TIME-10) are not available; the task page shows per-task time only. | Phase 11. |
| L24 | `Idempotency-Key` is honoured on the four work actions; create endpoints do not use it yet (repeated creates are prevented by the UI disabling buttons while saving). | Hardening (Phase 12). |
| L25 | The background jobs run on every app instance. If two instances close the same session at once, the second save hits the task's concurrency check, is recorded as a failed run on the System status page, and the next run finds nothing to do — no double close or double notification. | A database lock could make them single-runner if this becomes noisy. |
| L26 | Due-soon and overdue notifications (FR-NOT-03) are not sent yet; they need the daily due-date job. | Phase 8 (with the Overdue flag, L14). |
| L27 | An open notification connection (SignalR) is not cut when the member signs out in another tab or is deactivated; it ends at the next reconnect, and the member only ever receives their own notifications on it. | Hardening (Phase 12). |
| L28 | Notifications are pushed after the save that creates them. A save inside an explicit transaction would push before its commit; none of the current notification-producing actions use one. | None needed now; revisit if one does. |
| L29 | Audit log retention (FR-AUD-06) is a DBA script, not an in-app setting, because the application role can never delete audit rows (FR-AUD-03). | By design. |
| L30 | Fixed in Phase 7: the end-to-end suite assumed a warm Angular dev server; from a cold start (as in CI) the first pages can take over ten seconds to compile, so Phase 5/6 CI runs may have needed their retry. Waits are now 20 s and the first journey is marked slow. The test helper that flushes emails also now drains the whole queue instead of one batch of 20. | Resolved. |
| L18 | Fixed in Phase 5: the e2e mail helper decoded every email as quoted-printable, which corrupted links whose token started with two hex characters (`?token=9B…`), and two e2e steps could type into a list page's search box before the form page appeared. Both caused occasional false failures. | Resolved. |

## Risks

| Risk | Mitigation |
|---|---|
| Scope is large (19 modules). | Build in phases; each phase ends with tests and approval. |
| Time-tracking accuracy if developers forget to stop. | Max session length auto-close, notifications, TL time adjustments with audit. |
| Permission mistakes leading to data exposure (IDOR). | Central authorization handlers; integration tests per endpoint for unauthorized access. |
| Malicious uploads. | Signature checks, private storage, download headers, optional malware scanning (Q9). |
| GitLab API changes or rate limits. | Isolated integration module, retries, import run logs. |

