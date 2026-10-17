# Task Management Application

A role-based task management application with project and task workflows, algorithm approval, work-session time tracking, checklists, TL review, attachments, deadline and reassignment approvals, GitLab issue import, reports and audit logs.

**Current status: Phase 1 — requirements and workflow design (awaiting approval). There is no application code yet.**

## Planned technology

| Part | Choice |
|---|---|
| Backend | C# / ASP.NET Core Web API on .NET 10 LTS |
| Frontend | Angular (current supported version) with TypeScript |
| Database | PostgreSQL with Entity Framework Core migrations |
| API docs | OpenAPI |
| Auth | ASP.NET Core Identity, secure cookie, role/permission records |
| Tests | xUnit unit + integration tests (Testcontainers), Angular unit tests, Playwright end-to-end tests |

## Documentation

| Document | Status |
|---|---|
| [docs/requirements.md](docs/requirements.md) | Phase 1 draft |
| [docs/permissions-matrix.md](docs/permissions-matrix.md) | Phase 1 draft |
| [docs/task-workflow.md](docs/task-workflow.md) | Phase 1 draft |
| [docs/known-issues.md](docs/known-issues.md) | Phase 1 draft |
| docs/architecture.md | Phase 2 |
| docs/database-design.md | Phase 2 |
| docs/api-design.md | Phase 2 |
| docs/security.md | Phase 2 |
| docs/testing.md | Phase 2 |
| docs/deployment.md | Phase 2 (outline), updated each phase |
| docs/gitlab-integration.md | GitLab integration phase |

## Proposed phases

Each phase ends with builds, tests, a report, and an approval checkpoint.

1. **Requirements and workflow design** — this phase.
2. **Architecture and design** — module boundaries, database schema, API design, security design, test strategy, deployment outline.
3. **Foundation** — solution skeleton, Angular app, PostgreSQL + migrations, error handling, logging, OpenAPI, CI pipeline.
4. **Authentication, members, roles, permissions, settings, audit log base.**
5. **Projects, tasks, assignments, comments.**
6. **Algorithm approval, checklists, work sessions, allocated hours.**
7. **Notifications (in-app, email, background jobs).**
8. **Review, closure, revisions, reassignment, deadline extensions, delay reports.**
9. **Attachments (private storage).**
10. **GitLab import.**
11. **Dashboards and reports.**
12. **Hardening** — security review, end-to-end tests, performance checks, deployment documentation.

## Getting started

Setup instructions will be added in Phase 3.
