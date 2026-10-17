# Testing Strategy

Status: **Approved (Phase 2, 2026-10-17)** — kept up to date as phases are implemented
Last updated: 2026-10-17

## 1. Principles

- Every acceptance criterion in [requirements.md](requirements.md) §7 maps to at least one automated test, named with its ID (for example `AC_WORK_1_parallel_start_creates_one_session`).
- Business rules are tested in fast unit tests; authorization and database guarantees are tested against **real PostgreSQL**, because an in-memory database would not enforce unique partial indexes, `xmin` concurrency or grants.
- A test result is reported as passed only if it actually ran and passed.

## 2. Test layers

| Layer | Project / folder | Tools | What it covers |
|---|---|---|---|
| Domain unit | `tests/TaskManagement.Domain.Tests` | xUnit v3 | Workflow transition table (every state × action), checklist rule, separation of duties, time calculations, request lifecycle |
| Application unit | `tests/TaskManagement.Application.Tests` | xUnit v3, fakes, `FakeTimeProvider` | Handler logic, notification events, allocated-hour thresholds |
| Integration | `tests/TaskManagement.IntegrationTests` | xUnit v3, `WebApplicationFactory`, Testcontainers PostgreSQL 17, MailKit test SMTP sink | Full HTTP requests: authorization matrix, workflows end-to-end in the API, DB constraints, migrations apply cleanly, audit rows, CSRF, rate limits, upload validation |
| Architecture | `tests/TaskManagement.ArchitectureTests` | xUnit v3 + reflection | Layer dependencies (Domain → nothing, etc.), module folder rules, endpoints have authorization metadata |
| Angular unit | `frontend/` | Vitest (Angular default), Angular Testing Library style | Components, guards, interceptors, permission-based visibility, form validation |
| End-to-end | `e2e/` | Playwright | Main user journeys in a real browser against the API + PostgreSQL |
| Security | CI | gitleaks, `dotnet list package --vulnerable`, `npm audit`, CodeQL; OWASP ZAP baseline before release | See [security.md](security.md) §6 |

## 3. Key test suites

**Authorization matrix (integration).** A table-driven test enumerates every endpoint and runs it as: anonymous, each seed role, member of another project, assignee vs non-assignee. Expected status (200/403/404/401) is declared next to the endpoint. A test fails if a new endpoint has no matrix entry.

**Workflow (domain).** For each of the 12 states and 15 actions, assert the action is allowed only where [task-workflow.md](task-workflow.md) says so, and that side effects (session closed, revision incremented) happen.

**Work sessions (integration).** Parallel start requests (Task.WhenAll) → exactly one active session; start on second task → 409; idempotency key replay; auto-close job with fake time.

**Notifications (integration).** Outbox rows created in the same transaction; dispatcher creates notifications once even if run twice.

**GitLab (integration).** A fake GitLab HTTP server (WireMock.Net or a minimal test server) for import idempotency, closed-issue flagging, token never returned, retry on 429.

**End-to-end journeys (Playwright).**

1. Admin creates member → member sets password via invite link (email captured from Mailpit) → signs in.
2. PM creates project and task → TL assigns → developer submits algorithm → TL rejects → developer resubmits → TL approves.
3. Developer starts, pauses, resumes, completes checklist, submits → TL requests revision → developer resubmits → TL approves → PM closes.
4. Developer requests extension → TL approves → due date updated.
5. Developer cannot open another developer's task by URL (sees "not found").

## 4. Test data

- Builders for test entities (`TaskBuilder`, `ProjectBuilder`) keep tests short and readable.
- Each integration test class gets a fresh database (template database cloned per class) for isolation and speed.
- No production data is ever used in tests.

## 5. Running tests

```bash
# Backend: all unit, architecture and integration tests
dotnet test --solution TaskManagement.slnx

# Integration tests need PostgreSQL. Either:
#  - have Docker running (Testcontainers starts postgres:18-alpine automatically), or
#  - point at an existing server where the user may CREATE DATABASE; each test fixture creates and drops its own database:
export TMA_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=postgres;Password=...;Database=postgres"

# Frontend
cd frontend
npm run lint
npx ng test --watch=false

# End-to-end (from Phase 4)
cd e2e && npx playwright test
```

## 6. CI quality gates (GitHub Actions)

A pull request can merge only if:

1. Backend builds with warnings as errors and nullable reference types enabled.
2. `dotnet format --verify-no-changes` and Angular ESLint pass.
3. All unit, integration and Angular tests pass.
4. Migrations apply to an empty database and `dotnet ef migrations has-pending-model-changes` reports none.
5. No high/critical vulnerable packages; no secrets detected.
6. Line coverage is reported (target ≥ 80 % for Domain and Application; not a hard gate on other layers).

End-to-end tests run on the main branch and before releases.

## 7. Phase status

| Phase | Tests executed | Result |
|---|---|---|
| 1 | None (documentation only) | — |
| 2 | None (documentation only) | — |
| 3 | Backend: 34 tests (Domain 10, Architecture 14, Integration 10) against PostgreSQL 18.6 via `TMA_TEST_POSTGRES`. Frontend: 11 Vitest tests. Lint, format, build, migration check, vulnerability scans. | All passed locally (2026-10-17). The Testcontainers path and GitHub Actions were not run locally (no Docker) — first run is on GitHub after push. |
