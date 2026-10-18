# Permissions Matrix

Status: **Approved (Phase 1) — recommended defaults accepted 2026-10-17**
Last updated: 2026-10-17

## How permissions work

1. **Permissions are records.** Each permission has a code (for example `tasks.create`). Roles are records that hold a list of permissions with a scope. Admins can change these assignments (FR-ROLE-01, FR-ROLE-03). The tables below are the **seed defaults**.
2. **Two kinds of role** (assumption A1):
   - **Global role** — one per member. Used for system-wide permissions (Admin) and as the default.
   - **Project role** — one per project membership. Used for permissions inside that project.
3. **Scopes:**
   - **G (Global)** — applies everywhere, including projects the member does not belong to.
   - **P (Project)** — applies only in projects where the member is a member with that project role.
   - **O (Own)** — applies only to records assigned to, or created by, the member (for example "my tasks", "my sessions").
   - **—** — not granted.
4. **Server-side checks.** Every request is checked in this order:
   1. Is the member signed in and active?
   2. Does the member have the permission (from global role, or project role in the record's project)?
   3. Does the scope match? (project membership for P; assignment/ownership for O)
   4. Does the workflow allow the action now? (for example, Start only when Ready — see [task-workflow.md](task-workflow.md))
   5. Separation-of-duties rules (cannot approve own request/work).
5. **Not found vs forbidden.** If the member cannot see the record at all, the API answers **404**. If they can see it but not perform the action, the API answers **403**. Workflow conflicts answer **409**.
6. **Frontend.** Angular reads the effective permission list from `/api/v1/me` and project-specific permissions from the project endpoint to hide or disable buttons. This is only for usability.

## Roles (seed)

| Role | Purpose |
|---|---|
| Admin | Runs the system: accounts, roles, settings, integrations, audit. Has Global scope on read/administration. Not automatically a TL — business approvals by Admin are limited to approval requests (A6). |
| Team Leader (TL) | Technical lead of a project: creates and assigns tasks, approves algorithms, reviews work, approves requests. |
| Project Manager (PM) | Manages a project: members, schedule, tasks, assignments, deadlines, closure, reports. |
| Developer | Works on assigned tasks: algorithm, time tracking, checklist, submission, requests. |
| Viewer | Read-only access to projects where they are a member. |

## Matrix

Columns: **Adm** = Admin, **TL**, **PM**, **Dev** = Developer, **View** = Viewer.

### Accounts, roles, settings

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `members.view` | List/view members | G | P | P | — | — |
| `members.create` | Create login accounts | G | — | — | — | — |
| `members.update` | Edit member, change global role, deactivate/reactivate | G | — | — | — | — |
| `members.reset-password` | Force password reset email | G | — | — | — | — |
| `profile.manage` | Edit own profile, password, notification preferences | O | O | O | O | O |
| `roles.view` | View roles and permissions | G | — | — | — | — |
| `roles.manage` | Create/edit roles and role permissions | G | — | — | — | — |
| `settings.manage` | Edit global settings | G | — | — | — | — |

### Projects

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `projects.view` | View project details | G | P | P | P | P |
| `projects.create` | Create projects | G | — | G | — | — |
| `projects.update` | Edit project details/status, archive | G | — | P | — | — |
| `projects.members.manage` | Add/remove project members and set project roles | G | — | P | — | — |

*Note: a PM who creates a project automatically becomes its PM member.*

### Tasks

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `tasks.view` | View tasks | G | P | P | O | P |
| `tasks.create` | Create tasks | — | P | P | — | — |
| `tasks.update` | Edit task fields (title, description, priority, allocated hours, dates*, algorithm-required flag) | — | P | P | — | — |
| `tasks.assign` | First assignment / request reassignment | — | P | P | — | — |
| `tasks.cancel` | Cancel a task with a reason | — | P | P | — | — |
| `tasks.delete-draft` | Delete Draft task without history | — | P | P | — | — |
| `tasks.comment` | Add comments | — | P | P | O | — |
| `tasks.work` | Start / pause / resume / stop own work | — | O | — | O | — |
| `tasks.submit` | Submit for review | — | O | — | O | — |
| `tasks.review` | Approve review or request revision | — | P | — | — | — |
| `tasks.close` | Final closure after TL approval | — | P | P | — | — |
| `tasks.reopen` | Reopen a Closed task (new revision) | — | P | P | — | — |

\* After the first assignment, changing the due date goes through a deadline extension (`deadlines.request` / `deadlines.approve`), not `tasks.update`.

*TL has `tasks.work` / `tasks.submit` at Own scope so a TL can also be the assignee of a task. Separation-of-duties rules then stop them approving their own work.*

### Algorithm, checklist

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `algorithms.view` | View algorithm submissions | G | P | P | O | P |
| `algorithms.submit` | Submit algorithm for own task | — | O | — | O | — |
| `algorithms.approve` | Approve/reject algorithm | — | P | — | — | — |
| `checklists.manage` | Manage global checklist templates | G | — | — | — | — |
| `checklists.manage-project` | Manage project templates; add task-specific items | — | P | P | — | — |
| `checklists.complete` | Tick items on own task | — | O | — | O | — |

### Time

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `time.view` | View work sessions and totals | G | P | P | O | — |
| `time.adjust` | Add time adjustments with reason | — | P | P | — | — |

### Requests (reassignment, deadline extension, delay)

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `deadlines.request` | Request deadline extension | — | P | P | O | — |
| `deadlines.approve` | Approve/reject deadline extension | G | P | P | — | — |
| `reassignments.approve` | Approve/reject reassignment | G | P | P | — | — |
| `delays.report` | File a delay report | — | O | — | O | — |
| `delays.view` | View delay reports | G | P | P | O | — |

Separation-of-duties for approvals (assumption A6):

- The requester can never approve their own request.
- Reassignment requested by a TL → approved by a PM of the project (or Admin). Requested by a PM → approved by a TL of the project (or Admin).
- Deadline extension requested by the developer → approved by TL or PM of the project. Requested by a TL/PM → approved by the other role (or Admin).
- If a project has only one TL/PM, an Admin must approve.

### Attachments

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `attachments.view` | Download attachments | follows parent record's view permission ||||
| `attachments.upload` | Upload to a record | — | P | P | O | — |
| `attachments.delete` | Delete attachment | — | P | P | O (own uploads, task not Closed) | — |

### Notifications, reports, audit, integration, administration

| Permission | Description | Adm | TL | PM | Dev | View |
|---|---|---|---|---|---|---|
| `notifications.view` | Own notifications | O | O | O | O | O |
| `dashboard.view` | Role-appropriate dashboard | G | P | P | O | P |
| `reports.view` | View reports | G | P | P | O | P |
| `reports.export` | Export CSV | G | P | P | — | — |

*Reports and dashboards (Phase 11):* a report also needs the permission behind its data — `tasks.view`, plus `time.view` for the time and allocated-vs-actual reports and `delays.view` for delay reports — and only covers the projects (and, for Own scope, the tasks) where the member holds all of them. With the seed grants a Viewer therefore gets the overdue, extension and revision reports only, and a Developer sees only tasks they are or were assigned to. The dashboard's project overview needs `dashboard.view` with Project or Global scope; with Own scope (Developer) it shows only the member's own work.
| `audit.view` | View audit log | G | — | — | — | — |
| `audit.view-project` | View audit entries of a project | G | P | P | — | — |
| `gitlab.connections.manage` | Configure GitLab connections/tokens | G | — | — | — | — |
| `gitlab.import` | Link project and import issues | — | P | P | — | — |
| `system.health` | Detailed health, job and email status | G | — | — | — | — |

## Separation-of-duties rules (always enforced, cannot be turned off by permissions)

| Rule | Applies to |
|---|---|
| Cannot approve own algorithm | `algorithms.approve` |
| Cannot review/approve own submitted work | `tasks.review` |
| Cannot approve own reassignment or extension request | `reassignments.approve`, `deadlines.approve` |
| Cannot change own roles or deactivate self | `members.update`, `projects.members.manage` |
| Cannot remove the last Admin, or Admin's `roles.manage` / `members.create` | `roles.manage`, `members.update` |

## Open points

See [requirements.md](requirements.md) §5: Q2 (closure), Q7 (approvers), Q8 (PM approving algorithms).
