# API Design

Status: **Approved (Phase 2, 2026-10-17)** — kept up to date as phases are implemented
Last updated: 2026-10-17

REST-style JSON API built with ASP.NET Core Minimal APIs, documented with OpenAPI (`/openapi/v1.json`, development and test environments).

## 1. Conventions

| Topic | Rule |
|---|---|
| Base path | `/api/v1` — the version is in the URL. Breaking changes require `/api/v2`. |
| Format | JSON, camelCase properties, enums as strings, dates `YYYY-MM-DD`, timestamps ISO 8601 UTC (`2026-10-17T09:30:00Z`). |
| Identifiers | UUIDs in URLs. Task keys (`TMA-42`) are accepted only by the search endpoint. |
| Authentication | HttpOnly, Secure, SameSite=Lax cookie `__Host-tma` set by `/auth/login`. No bearer tokens for the browser. |
| CSRF | All `POST/PUT/PATCH/DELETE` require header `X-XSRF-TOKEN` matching the `XSRF-TOKEN` cookie (Angular sends it automatically). Validated by an endpoint filter for every unsafe method. |
| Workflow actions | `POST /tasks/{id}/<action>` (e.g. `/start`, `/submit-review`). There is no endpoint that sets `status` directly. |
| Updates | `PATCH` with the full editable field set plus `version` (from `xmin`). Mismatched version → **409**. |
| Idempotency | Work-session actions and create endpoints accept an `Idempotency-Key` header (UUID). A repeated key returns the first response. |
| Pagination | `?page=1&pageSize=25` (max 100). Response: `{ items, page, pageSize, totalCount }`. |
| Sorting / filtering | `?sort=dueDate,-priority` (minus = descending) and explicit filter parameters. Only allow-listed fields. |
| Errors | RFC 9457 Problem Details (see §3). |
| Status codes | 200 OK, 201 Created (+ `Location`), 204 No Content, 400 validation, 401 not signed in, 403 forbidden, 404 not found *or not visible*, 409 workflow/concurrency conflict, 413 file too large, 415 file type not allowed, 422 not used, 429 rate limited. |

## 2. Authorization behavior

Every endpoint declares its required permission. For record endpoints the order is:

1. Load the record **with a visibility filter** for the current user → **404** if not found or not visible (does not leak existence).
2. Check the action's permission and scope → **403**.
3. Run the domain rule → **409** with an error code.

## 3. Error format

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "This action is not allowed in the task's current state.",
  "status": 409,
  "code": "task.invalid_transition",
  "detail": "Task TMA-42 is 'In review'. Allowed actions: approve, request-revision, cancel.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": { }
}
```

- `type` is the standard link for the HTTP status (set by ASP.NET Core). `code` is the stable application error code used by Angular; `title`/`detail` are human-readable.
- `errors` (validation only) maps field names to messages.
- In production, unexpected errors return a generic 500 with only `title`, `status` and `traceId`.

## 4. Endpoints

Permission codes refer to [permissions-matrix.md](permissions-matrix.md).

### 4.1 Authentication and profile

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/auth/antiforgery` | anonymous | Sets `XSRF-TOKEN` cookie |
| POST | `/auth/login` | anonymous | `{ email, password, rememberMe }` → 200 / 401 generic / `mfaRequired` |
| POST | `/auth/login/mfa` | partial session | `{ code }` |
| POST | `/auth/logout` | signed in | |
| POST | `/auth/forgot-password` | anonymous, rate limited | Always 202 |
| POST | `/auth/reset-password` | anonymous, rate limited | `{ token, newPassword }` (also used for invite links) |
| POST | `/auth/change-password` | `profile.manage` | `{ currentPassword, newPassword }` |
| GET/POST/DELETE | `/auth/mfa` | `profile.manage` | Set up / enable / disable TOTP; recovery codes |
| GET | `/me` | signed in | Profile, global role, effective global permissions, time zone |
| PATCH | `/me` | `profile.manage` | Name, time zone |
| GET/PUT | `/me/notification-preferences` | `profile.manage` | |

No registration endpoint exists (FR-AUTH-01).

**Implemented in Phase 4:** antiforgery, login, logout, forgot/reset/change password, `GET`/`PATCH /me`. Not yet: `rememberMe` (every session lasts `Auth:SessionLifetime`, default 8 h, sliding), the MFA endpoints (follow-up step, see [known-issues.md](known-issues.md)) and notification preferences (notifications phase).

### 4.2 Members, roles, settings

| Method | Path | Permission |
|---|---|---|
| GET | `/members` | `members.view` |
| POST | `/members` | `members.create` (sends invite) |
| GET | `/members/{id}` | `members.view` |
| PATCH | `/members/{id}` | `members.update` |
| POST | `/members/{id}/deactivate` · `/reactivate` | `members.update` |
| POST | `/members/{id}/send-password-reset` | `members.reset-password` |
| GET | `/roles` · `/permissions` | `roles.view` |
| POST/PATCH/DELETE | `/roles[/{id}]` | `roles.manage` |
| PUT | `/roles/{id}/permissions` | `roles.manage` — `[{ code, scope }]` |
| GET | `/settings` | `settings.manage` — all settings groups |
| PUT | `/settings/{group}` | `settings.manage` — replaces one validated group, e.g. `/settings/auth` (Phase 4: `auth`; Phase 6: `work` — `maxSessionHours` 1–24, `pauseReasonRequired`, `allocationWarningPercent` 50–99) |

### 4.3 Projects

| Method | Path | Permission |
|---|---|---|
| GET | `/projects` | `projects.view` (filtered to memberships) |
| POST | `/projects` | `projects.create` |
| GET/PATCH | `/projects/{id}` | `projects.view` / `projects.update` |
| POST | `/projects/{id}/archive` · `/complete` · `/reactivate` | `projects.update` |
| GET | `/projects/{id}/members` | `projects.view` |
| POST | `/projects/{id}/members` | `projects.members.manage` |
| PATCH/DELETE | `/projects/{id}/members/{userId}` | `projects.members.manage` |
| GET | `/projects/{id}/my-permissions` | signed in (for UI) |
| GET | `/projects/{id}/member-candidates?search=` | `projects.members.manage` — active accounts not yet in the project (id, name, email only) |
| GET | `/projects/roles` | signed in — roles that can be given in a project (all except Admin) |

**Implemented in Phase 5.** Also `POST /projects/{id}/hold` (status On hold). Status actions take `{ confirmOpenTasks }`: completing or archiving a project with open tasks returns 409 `project.has_open_tasks` until it is `true` (FR-PROJ-04). Project endpoints require a signed-in member; `projects.view`, `projects.update` and `projects.members.manage` are checked per project because they depend on the caller's project role (a Global grant on the global role applies everywhere). Creating a project needs a Global `projects.create` grant; a creator without Global `projects.view` (e.g. a PM) becomes the project's PM member.

### 4.4 Tasks

| Method | Path | Permission |
|---|---|---|
| GET | `/tasks` | `tasks.view` — filters: `projectId, status[], assigneeId, priority[], dueFrom, dueTo, overdue, q` |
| GET | `/tasks/by-key/{key}` | `tasks.view` |
| POST | `/projects/{projectId}/tasks` | `tasks.create` |
| GET | `/tasks/{id}` | `tasks.view` — includes current state, allowed actions for the caller, flags, totals |
| PATCH | `/tasks/{id}` | `tasks.update` |
| DELETE | `/tasks/{id}` | `tasks.delete-draft` (Draft without history only) |
| GET | `/tasks/{id}/history` | `tasks.view` — status, assignment, revision timeline |
| GET/POST | `/tasks/{id}/comments` | `tasks.view` / `tasks.comment` |
| PATCH/DELETE | `/tasks/{id}/comments/{commentId}` | author within 15 min / author or TL/PM |

**Implemented in Phase 5**, plus `POST /tasks` with `projectId` in the body (a missing project answers 400, AC-TASK-1). `GET /tasks` also takes `sort` (allow-listed: `key`, `title`, `dueDate`, `priority`, `createdAt`, `updatedAt`; default `dueDate,key`) and the usual paging. `q` matches a task key exactly (`TMA-42`) or searches titles. "TL/PM" for deleting comments means `tasks.update` in the project.

### 4.5 Workflow actions

All `POST /tasks/{id}/…`, return the updated `TaskDto`.

| Path | Transition | Body | Permission |
|---|---|---|---|
| `/assign` | T2 | `{ assigneeId }` | `tasks.assign` |
| `/algorithm` | T3 | `{ content, attachmentIds[] }` | `algorithms.submit` (assignee) |
| `/algorithm/{submissionId}/approve` | T4 | `{ comment? }` | `algorithms.approve` |
| `/algorithm/{submissionId}/reject` | T5 | `{ comment }` | `algorithms.approve` |
| `/start` | T6 | — (Idempotency-Key) | `tasks.work` (assignee) |
| `/pause` | T7 | `{ reason? }` | `tasks.work` |
| `/resume` | T8 | — | `tasks.work` |
| `/stop` | T9 | `{ reason }` | `tasks.work` |
| `/submit-review` | T10 | `{ summary, attachmentIds[] }` | `tasks.submit` |
| `/approve` | T11 | `{ comment? }` | `tasks.review` |
| `/request-revision` | T12 | `{ comment, reopenChecklistItemIds[], algorithmReapprovalRequired }` | `tasks.review` |
| `/close` | T13 | `{ comment? }` | `tasks.close` |
| `/reopen` | T14 | `{ reason }` | `tasks.reopen` |
| `/cancel` | T15 | `{ reason }` | `tasks.cancel` |

**Implemented in Phase 5:** `/assign` (body may also carry `dueDate` when the task has none yet, WQ1) and `/cancel`.

**Implemented in Phase 6:** `/algorithm` (T3; `attachmentIds` with the attachments phase), `/algorithm/{submissionId}/approve` · `/reject` (T4/T5), `/start`, `/pause`, `/resume`, `/stop` (T6–T9) and `GET /tasks/{id}/algorithm`. The four work actions accept an `Idempotency-Key` header: the first successful response is stored for 24 hours and replayed for the same key and endpoint; the same key on another endpoint answers 409 `idempotency.key_reused`. Work actions are the assignee's only (403 `work.not_assignee` for anyone else, AC-WORK-4); starting while another task runs answers 409 `work.session_active` naming that task. Starting a task that still needs its algorithm answers 409 `task.algorithm_approval_required` (AC-ALG-1). Approving or rejecting is refused with 403 `algorithm.own_submission` when the reviewer is the task's assignee. `allowedActions` now also lists `start`, `pause`, `resume`, `stop`, `submitAlgorithm`, `reviewAlgorithm`, `completeChecklist`, `manageChecklist` and `adjustTime`; `TaskDto` gains `spentSeconds`, `algorithmApproved`, `checklist` (totals), `flags.overAllocated`, `canViewAlgorithm` and `canViewTime`. Review, close and reopen arrive in Phase 8.

GET `/tasks/{id}/algorithm` lists all submissions (`algorithms.view`).

### 4.6 Checklists

| Method | Path | Permission |
|---|---|---|
| GET/POST/PATCH | `/checklist-templates[/{id}]` | `checklists.manage` (global) / `checklists.manage-project` (`?projectId=`) |
| GET | `/tasks/{id}/checklist` | `tasks.view` |
| POST | `/tasks/{id}/checklist` | `checklists.manage-project` — add task-specific item |
| POST | `/tasks/{id}/checklist/{itemId}/complete` · `/uncomplete` | `checklists.complete` (assignee) — `{ note? }` |

**Implemented in Phase 6.** `GET /checklist-templates` without `projectId` lists global templates (needs `checklists.manage`); with `projectId` it lists the active global templates plus that project's templates (anyone who can see the project, so templates can be picked for tasks). Templates are created with `{ projectId?, name, isActive, items[{ text, isMandatory }] }` and updated (PATCH, with `version`) by replacing the items; there is no delete — deactivate instead. Task create takes an optional `checklistTemplateId` (otherwise the project's `defaultChecklistTemplateId`, a new project field, applies). Items can be added until the task is submitted for review; the assignee ticks items while the task is Ready to start, In progress, Paused, Stopped or Revision requested.

### 4.7 Time

| Method | Path | Permission |
|---|---|---|
| GET | `/me/active-session` | signed in — for the timer bar |
| GET | `/tasks/{id}/time` | `time.view` — sessions, adjustments, totals per revision |
| POST | `/tasks/{id}/time-adjustments` | `time.adjust` — `{ userId, minutes, reason }` |
| GET | `/timesheets?userId=&from=&to=` | `time.view` (own, or project scope) |

**Implemented in Phase 6:** `/me/active-session` (204 when nothing runs; includes `serverTime` so the timer uses the server clock), `/tasks/{id}/time` (totals, overrun, percentage of the allocation, per revision and member, sessions, adjustments) and `/tasks/{id}/time-adjustments` (`minutes` non-zero, at most one week either way; only for someone who worked on or was assigned to the task). Timesheets arrive with reports (Phase 11).

### 4.8 Requests and delays

| Method | Path | Permission |
|---|---|---|
| GET | `/tasks/{id}/requests` | `tasks.view` |
| POST | `/tasks/{id}/reassignment-requests` | `tasks.assign` — `{ newAssigneeId, reason, algorithmReapprovalRequired }` |
| POST | `/tasks/{id}/extension-requests` | `deadlines.request` — `{ proposedDueDate, reason }` |
| POST | `/requests/{requestId}/approve` | `reassignments.approve` / `deadlines.approve` — `{ comment?, approvedDueDate? }` |
| POST | `/requests/{requestId}/reject` | same — `{ comment }` |
| POST | `/requests/{requestId}/withdraw` | requester |
| GET | `/requests?status=Pending&projectId=` | approver's queue |
| GET/POST | `/tasks/{id}/delay-reports` | `delays.view` / `delays.report` |

### 4.9 Attachments

| Method | Path | Permission |
|---|---|---|
| POST | `/tasks/{id}/attachments` | `attachments.upload` — `multipart/form-data`, one file, `ownerType`, `ownerId` |
| GET | `/tasks/{id}/attachments` | `tasks.view` |
| GET | `/attachments/{id}/download` | parent view permission; streams file or 302 to signed URL (≤ 5 min) |
| GET | `/attachments/{id}/preview` | same; images/videos inline only when scan is Clean |
| DELETE | `/attachments/{id}` | `attachments.delete` |

Downloads of files with scan status other than `Clean` return 409 (`attachment.not_scanned` / `attachment.infected`).

### 4.10 Notifications

| Method | Path | Permission |
|---|---|---|
| GET | `/notifications?unreadOnly=` | own |
| GET | `/notifications/unread-count` | own |
| POST | `/notifications/{id}/read` · `/notifications/read-all` | own |
| WS | `/hubs/notifications` | signed in (SignalR) |

### 4.11 GitLab

| Method | Path | Permission |
|---|---|---|
| GET/POST/PATCH/DELETE | `/gitlab/connections[/{id}]` | `gitlab.connections.manage` — token write-only |
| POST | `/gitlab/connections/{id}/test` | `gitlab.connections.manage` |
| GET | `/gitlab/connections/{id}/projects?search=` | `gitlab.import` |
| PUT/DELETE | `/projects/{id}/gitlab-link` | `gitlab.import` |
| GET | `/projects/{id}/gitlab/issues?state=opened&search=` | `gitlab.import` — preview with "already imported" marker |
| POST | `/projects/{id}/gitlab/import` | `gitlab.import` — `{ issueIids[] }` |
| POST | `/projects/{id}/gitlab/sync` | `gitlab.import` |
| GET | `/projects/{id}/gitlab/import-runs` | `gitlab.import` |
| POST | `/tasks/{id}/gitlab/accept-changes` | `tasks.update` |
| POST | `/webhooks/gitlab/{projectLinkId}` | anonymous; requires `X-Gitlab-Token` secret; no cookie/CSRF |

### 4.12 Dashboards, reports, audit, administration

| Method | Path | Permission |
|---|---|---|
| GET | `/dashboard` | `dashboard.view` — content depends on role |
| GET | `/reports/time` · `/allocated-vs-actual` · `/overdue` · `/extensions` · `/revisions` · `/delays` | `reports.view` |
| GET | same `+ ?format=csv` | `reports.export` |
| GET | `/audit-logs?from=&to=&actorId=&entityType=&entityId=&action=` | `audit.view` |
| GET | `/projects/{id}/audit-logs` | `audit.view-project` |
| GET | `/admin/system-status` | `system.health` — jobs, email queue, integrations |
| GET | `/health/live` · `/health/ready` | anonymous, minimal body |
| GET | `/system/info` | anonymous — `{ name, version, serverTimeUtc }` (implemented in Phase 3) |

## 5. Main DTO shapes

```jsonc
// TaskDto (GET /tasks/{id})
{
  "id": "0192…", "key": "TMA-42", "projectId": "…", "title": "…", "description": "…",
  "type": "Feature", "priority": "High", "status": "InProgress",
  "assignee": { "id": "…", "name": "…" },
  "dueDate": "2026-11-01", "originalDueDate": "2026-10-25",
  "allocatedMinutes": 480, "spentSeconds": 21600,
  "algorithmRequired": true, "algorithmStatus": "Approved",
  "currentRevision": 1,
  "flags": { "overdue": false, "overAllocated": false, "gitlabIssueClosed": false,
             "extensionPending": true, "reassignmentPending": false },
  "checklist": { "mandatoryTotal": 5, "mandatoryDone": 3 },
  "allowedActions": ["pause", "stop", "submitReview"],
  "version": 81234
}
```

`allowedActions` is computed by the server with the same rules used to enforce actions, so the UI never guesses.

## 6. Rate limits (defaults)

| Endpoint group | Limit |
|---|---|
| `/auth/login`, `/auth/login/mfa` | 10 / minute per IP, plus account lockout |
| `/auth/forgot-password`, `/auth/reset-password` | 5 / 15 minutes per IP |
| Uploads | 30 / minute per user |
| Everything else | 300 / minute per user |
