# Task Management Application

A role-based task management application with project and task workflows, algorithm approval, work-session time tracking, checklists, TL review, attachments, deadline and reassignment approvals, GitLab issue import, reports and audit logs.

**Current status: Phases 1–4 approved. Phase 5 — projects, project members, tasks, first assignment, cancel and comments (awaiting approval). MFA for Admins is a separate follow-up step before production. Algorithm approval, checklists and time tracking come in Phase 6.**

## Planned technology

| Part | Choice |
|---|---|
| Backend | C# / ASP.NET Core Web API on .NET 10 LTS |
| Frontend | Angular 22 with TypeScript and Angular Material |
| Database | PostgreSQL 18 with Entity Framework Core 10 migrations |
| API docs | OpenAPI |
| Auth | ASP.NET Core Identity, secure cookie, role/permission records |
| Tests | xUnit v3 unit + integration tests (Testcontainers), Vitest Angular unit tests, Playwright end-to-end tests |
| Source control / CI | GitHub + GitHub Actions (GitLab is an integration source only) |

## Documentation

| Document | Status |
|---|---|
| [docs/requirements.md](docs/requirements.md) | Approved |
| [docs/permissions-matrix.md](docs/permissions-matrix.md) | Approved |
| [docs/task-workflow.md](docs/task-workflow.md) | Approved |
| [docs/known-issues.md](docs/known-issues.md) | Updated each phase |
| [docs/architecture.md](docs/architecture.md) | Approved |
| [docs/database-design.md](docs/database-design.md) | Approved |
| [docs/api-design.md](docs/api-design.md) | Approved |
| [docs/security.md](docs/security.md) | Approved |
| [docs/testing.md](docs/testing.md) | Approved; results updated each phase |
| [docs/deployment.md](docs/deployment.md) | Local development documented; updated each phase |
| docs/gitlab-integration.md | GitLab integration phase |

## Proposed phases

Each phase ends with builds, tests, a report, and an approval checkpoint.

1. **Requirements and workflow design** — approved.
2. **Architecture and design** — approved: module boundaries, database schema, API design, security design, test strategy, deployment outline.
3. **Foundation** — approved: solution skeleton, Angular app, PostgreSQL + migrations, error handling, logging, OpenAPI, CI pipeline.
4. **Authentication, members, roles, permissions, settings, audit log base** — approved. MFA follows as a separate step.
5. **Projects, tasks, assignments, comments** — current phase.
6. **Algorithm approval, checklists, work sessions, allocated hours.**
7. **Notifications (in-app, email, background jobs).**
8. **Review, closure, revisions, reassignment, deadline extensions, delay reports.**
9. **Attachments (private storage).**
10. **GitLab import.**
11. **Dashboards and reports.**
12. **Hardening** — security review, end-to-end tests, performance checks, deployment documentation.

## Repository layout

```
src/            ASP.NET Core backend: Domain, Application, Infrastructure, Api
tests/          Domain, Application, architecture and integration tests (xUnit v3)
e2e/            Playwright end-to-end tests
frontend/       Angular 22 app
deploy/db/      Database roles and grants script
docs/           Requirements and design documents
compose.yaml    Local PostgreSQL 18 + Mailpit
```

## Getting started

Prerequisites: .NET SDK 10.0.401+, Node.js 24.22 LTS (`frontend/.nvmrc`), PostgreSQL 18 (or Docker).

```bash
cp .env.example .env && docker compose up -d     # local PostgreSQL + Mailpit (or use your own PostgreSQL 18)
dotnet user-secrets --project src/TaskManagement.Api set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=tma;Username=postgres;Password=<your password>"
dotnet run --project src/TaskManagement.Api      # http://localhost:5080
cd frontend && npm ci && npm start               # http://localhost:4200
dotnet run --project src/TaskManagement.Api -- create-admin --email you@example.com --name "Your Name"
                                                 # first Admin; invite email lands in storage/mail/
```

Tests:

```bash
dotnet test --solution TaskManagement.slnx       # needs Docker, or TMA_TEST_POSTGRES (see docs/testing.md)
cd frontend && npm run lint && npx ng test --watch=false
cd e2e && npx playwright test                    # needs TMA_E2E_DB (see docs/testing.md)
```

Details: [docs/deployment.md](docs/deployment.md) and [docs/testing.md](docs/testing.md).
