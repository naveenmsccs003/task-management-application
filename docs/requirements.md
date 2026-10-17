# Requirements

Status: **Approved (Phase 1) — recommended defaults accepted 2026-10-17**
Last updated: 2026-10-17

This document lists what the system must do (functional requirements), how well it must do it (non-functional requirements), the assumptions we made, the questions that need a decision, edge cases, and acceptance criteria.

Related documents:

- [permissions-matrix.md](permissions-matrix.md) — who can do what.
- [task-workflow.md](task-workflow.md) — task states and transitions.
- [known-issues.md](known-issues.md) — open questions, risks and limitations.

Requirement IDs (for example `FR-TASK-03`) are referenced by tests and acceptance criteria. Words in **MUST** are mandatory; **SHOULD** is strongly recommended; **MAY** is optional.

---

## 1. Glossary

| Term | Meaning |
|---|---|
| Member | A person with a login account. |
| Role | A named set of permissions (Admin, TL, PM, Developer, Viewer, or custom). |
| Project membership | A link between a member and a project, with a project role. |
| Task | A unit of work. Always belongs to exactly one project. |
| Assignee | The developer currently responsible for a task. |
| Algorithm | A written plan/approach the developer submits before coding, approved by a TL. |
| Work session | One continuous period of work on a task, from start/resume to pause/stop. Timestamps come from the server. |
| Allocated hours | The time budget set for a task. |
| Checklist | A list of items copied from a template onto a task. Some items are mandatory. |
| Revision | A new round of work after a TL sends a task back. Revision numbers start at 0. |
| Approval request | A request (deadline extension, reassignment) that needs a decision from an authorized approver. |
| Audit log | An append-only record of important changes. |

---

## 2. Functional requirements

### 2.1 Authentication and password reset (AUTH)

| ID | Requirement |
|---|---|
| FR-AUTH-01 | Public self-registration MUST be disabled. There is no sign-up page or endpoint. |
| FR-AUTH-02 | Members sign in with email and password. |
| FR-AUTH-03 | Passwords MUST be stored only as salted, slow hashes (ASP.NET Core Identity's PBKDF2 hasher or stronger). |
| FR-AUTH-04 | Password policy: minimum 12 characters; common/breached-password checks SHOULD be applied; no forced composition rules beyond length (NIST SP 800-63B guidance). Configurable in global settings. |
| FR-AUTH-05 | Accounts MUST lock after a configurable number of failed sign-ins (default 5 failures → 15-minute lockout). |
| FR-AUTH-06 | "Forgot password" MUST always show the same response whether or not the email exists (no account enumeration). |
| FR-AUTH-07 | Password reset tokens MUST be random, single-use, expire after a configurable time (default 30 minutes), and be stored only as a hash. |
| FR-AUTH-08 | Using a reset token, or changing a password, MUST invalidate the member's other sessions and any other outstanding reset tokens. |
| FR-AUTH-09 | New accounts created by an admin receive a one-time "set your password" link (same mechanism as reset, default expiry 72 hours). Admins never see or choose the member's password. |
| FR-AUTH-10 | Members can change their own password (requires the current password). |
| FR-AUTH-11 | Deactivated members MUST be unable to sign in, and their active sessions MUST end at their next request. |
| FR-AUTH-12 | Sign-in, sign-out, failed sign-in, lockout, and password reset events MUST be audit logged (without passwords or tokens). |
| FR-AUTH-13 | Multi-factor authentication (TOTP) SHOULD be supported and MAY be required for Admins by a global setting. *(See question Q6.)* |

### 2.2 Member management (MEM)

| ID | Requirement |
|---|---|
| FR-MEM-01 | Only members with `members.create` (Admin by default) can create login accounts. |
| FR-MEM-02 | Member fields: full name, email (unique, case-insensitive), global role, time zone, active flag, optional employee code. |
| FR-MEM-03 | Members are deactivated, never hard-deleted, so history stays intact. |
| FR-MEM-04 | Admins can change a member's global role, reactivate, or force a password reset. |
| FR-MEM-05 | The system MUST prevent removing or deactivating the last active Admin. |
| FR-MEM-06 | A member cannot change their own role or deactivate themselves. |
| FR-MEM-07 | Members can view and edit their own profile (name, time zone, notification preferences) but not their email or role. |

### 2.3 Roles, permissions and global settings (ROLE)

| ID | Requirement |
|---|---|
| FR-ROLE-01 | Roles and permissions are stored as database records, not hard-coded checks on role names. |
| FR-ROLE-02 | Seed roles: Admin, Team Leader (TL), Project Manager (PM), Developer, Viewer, with the defaults in [permissions-matrix.md](permissions-matrix.md). |
| FR-ROLE-03 | Admins can create custom roles and change role-permission assignments. Seed roles cannot be deleted; the Admin role cannot lose `roles.manage` or `members.create`. |
| FR-ROLE-04 | Permissions have a scope: **Global** (whole system), **Project** (only projects where the member belongs), or **Own** (only tasks/records assigned to or created by the member). |
| FR-ROLE-05 | Every API endpoint MUST enforce permissions on the server. Hiding buttons in Angular is only for convenience. |
| FR-ROLE-06 | Task endpoints MUST check both project access (membership) and task-level access (assignment/ownership) before returning or changing data. |
| FR-ROLE-07 | Permission changes take effect within one minute without requiring users to sign out (permissions are re-read or cache-invalidated). |
| FR-ROLE-08 | Global settings (password policy, lockout, token lifetimes, max work-session length, allocated-hour warning threshold, attachment limits, allowed file types, working hours) are editable by members with `settings.manage`. |
| FR-ROLE-09 | Role, permission and setting changes MUST be audit logged with old and new values. |

### 2.4 Project management (PROJ)

| ID | Requirement |
|---|---|
| FR-PROJ-01 | Members with `projects.create` can create projects: name, unique key (for example `TMA`), description, start date, target end date, status (Active, On Hold, Completed, Archived). |
| FR-PROJ-02 | Project members are added with a project role (TL, PM, Developer, Viewer). A project MUST have at least one TL or PM before tasks can be created. |
| FR-PROJ-03 | Members see only projects where they are members, unless they hold a Global-scope project permission (Admin). |
| FR-PROJ-04 | Archived projects are read-only. Completing or archiving a project with open tasks requires confirmation and leaves those tasks unchanged but read-only. |
| FR-PROJ-05 | Removing a member from a project is blocked while they have open tasks assigned in it; tasks must be reassigned first. |
| FR-PROJ-06 | A project MAY be linked to one GitLab project (see IMP). |

### 2.5 Task management (TASK)

| ID | Requirement |
|---|---|
| FR-TASK-01 | Every task MUST belong to exactly one project. A task cannot be moved between projects. *(See Q12.)* |
| FR-TASK-02 | Only members with `tasks.create` in that project (TL/PM by default) can create tasks. |
| FR-TASK-03 | Task fields: key (for example `TMA-42`, generated), title, description (Markdown, sanitized), priority (Low, Medium, High, Critical), type (Feature, Bug, Improvement, Research, Other), start date, due date, allocated hours, "algorithm required" flag, checklist template, labels, status, current assignee, current revision number, GitLab link (optional). |
| FR-TASK-04 | "Algorithm required" defaults from the task type/project setting and can be changed by TL/PM only before work starts. |
| FR-TASK-05 | Task status changes only through the workflow actions in [task-workflow.md](task-workflow.md). There is no "set status" endpoint. |
| FR-TASK-06 | Developers see only tasks they are (or were) assigned to, unless their role grants `tasks.view` at Project scope. |
| FR-TASK-07 | Task lists support filtering (project, status, assignee, priority, due date range, overdue), sorting, searching by key/title, and server-side pagination. |
| FR-TASK-08 | Tasks support comments. Comments are editable by the author for 15 minutes; edits keep history; deletion is a soft delete. |
| FR-TASK-09 | Tasks are never hard-deleted once work has started. Draft tasks with no history MAY be deleted by TL/PM. Other tasks are Cancelled with a reason. |
| FR-TASK-10 | Concurrent edits MUST be detected (optimistic concurrency). The second save gets a "changed by someone else" error instead of overwriting. |

### 2.6 Task assignments (ASG)

| ID | Requirement |
|---|---|
| FR-ASG-01 | Only TL/PM (`tasks.assign`) in the task's project can assign a task. The assignee must be an active project member whose project role can work on tasks. |
| FR-ASG-02 | A task has one current assignee at a time. *(See Q3.)* |
| FR-ASG-03 | The first assignment of a Draft task needs no approval. |
| FR-ASG-04 | Changing the assignee after the first assignment is a **reassignment** and follows REV rules (reason and approval required). |
| FR-ASG-05 | Every assignment and reassignment is kept in assignment history: from, to, by, reason, approver, time. |
| FR-ASG-06 | The assignee and the project's TL(s) are notified on assignment and reassignment. |

### 2.7 Algorithm submission and TL approval (ALG)

| ID | Requirement |
|---|---|
| FR-ALG-01 | If a task requires an algorithm, the assignee MUST submit one, and a TL MUST approve it before the task can be started. |
| FR-ALG-02 | An algorithm submission contains text (Markdown) and optional attachments. |
| FR-ALG-03 | A TL approves or rejects. Rejection requires a comment. The developer can then submit a new version. |
| FR-ALG-04 | All versions, decisions, reviewers, comments and times are kept. Submitted versions cannot be edited. |
| FR-ALG-05 | A TL cannot approve their own algorithm submission (if a TL is also the assignee, another TL or a PM with `algorithms.approve` must approve). |
| FR-ALG-06 | If a revision changes the approach, the TL MAY mark "algorithm re-approval required" when requesting a revision. |

### 2.8 Task start, stop, pause and resume (WORK)

| ID | Requirement |
|---|---|
| FR-WORK-01 | Only the current assignee can start, pause, resume or stop work on a task. |
| FR-WORK-02 | **Start** is allowed only when the task is Ready to start (assigned, and algorithm approved if required) and the due date/project are not blocking. |
| FR-WORK-03 | **Pause** ends the current work session with a pause reason (optional by default; configurable) and moves the task to Paused. |
| FR-WORK-04 | **Resume** starts a new work session on a Paused task. |
| FR-WORK-05 | **Stop** ends the current work session and moves the task to Stopped (no work planned until the developer starts again — for example end of day or blocked). A stop reason is required. *(See Q4 on the difference between Pause and Stop.)* |
| FR-WORK-06 | Submitting a task for review automatically ends any active work session. |
| FR-WORK-07 | Start/resume/pause/stop requests MUST be idempotent: repeating the same request (for example a double click or network retry) MUST NOT create a second session. |

### 2.9 Work sessions and allocated hours (TIME)

| ID | Requirement |
|---|---|
| FR-TIME-01 | Each work session stores task, member, revision number, start time, end time, end reason (Paused, Stopped, Submitted, Auto-closed, Reassigned, Cancelled, Member deactivated), and duration. |
| FR-TIME-02 | All timestamps come from the server clock in UTC. The client never sends start or end times. |
| FR-TIME-03 | A member MUST have at most one active work session across all tasks. A task MUST have at most one active work session. This is enforced by the database (unique partial index), not only in code. |
| FR-TIME-04 | Work sessions cannot be edited or deleted by normal users. Corrections are made as separate **time adjustment** records (by TL/PM with `time.adjust`), with a reason, and are audit logged. |
| FR-TIME-05 | Total time spent = sum of closed sessions + elapsed time of any active session + approved adjustments. |
| FR-TIME-06 | When total time reaches a warning threshold (default 80 %, configurable) the assignee is notified. |
| FR-TIME-07 | When total time reaches 100 % of allocated hours, the assignee and the project TL(s) MUST be notified, once per task revision. |
| FR-TIME-08 | Reaching allocated hours does not stop the session; time over the allocation is reported as overrun. *(See Q5.)* |
| FR-TIME-09 | A background job auto-closes sessions that exceed the maximum session length (default 10 hours, configurable), records end reason Auto-closed, and notifies the member and TL. The TL can adjust time afterwards. |
| FR-TIME-10 | Time reports show time per task, per revision, per member, per project and per date range, using the viewer's time zone for display. |

### 2.10 Notifications (NOT)

| ID | Requirement |
|---|---|
| FR-NOT-01 | In-app notifications with unread count, list, mark-as-read and mark-all-as-read. |
| FR-NOT-02 | Email notifications for important events; members can turn off non-mandatory email categories. |
| FR-NOT-03 | Events: assigned, reassigned, algorithm submitted/approved/rejected, allocated-hour warning and limit, session auto-closed, submitted for review, revision requested, approved, closed, extension/reassignment requested/decided, due soon (configurable, default 24 h), overdue, GitLab issue closed externally, password reset/changed, account created. |
| FR-NOT-04 | Notifications are produced in the same database transaction as the business change (outbox pattern) and delivered by a background worker, so a failed email never rolls back a business action and no notification is lost. |
| FR-NOT-05 | Email delivery retries with backoff; permanent failures are logged and visible to Admins. |
| FR-NOT-06 | Notification content MUST NOT include secrets, tokens (except the reset link itself in the reset email), or data the recipient is not allowed to see. |
| FR-NOT-07 | In-app notifications SHOULD appear without a page refresh (SignalR), with polling as a fallback. |

### 2.11 Checklist templates and completion (CHK)

| ID | Requirement |
|---|---|
| FR-CHK-01 | Members with `checklists.manage` can create checklist templates (global or per project) with ordered items, each marked mandatory or optional. |
| FR-CHK-02 | When a template is applied to a task, its items are **copied** to the task. Later template changes do not change existing tasks. |
| FR-CHK-03 | TL/PM can add task-specific items before review. |
| FR-CHK-04 | The assignee ticks items, optionally with a note. Who ticked and when is stored. |
| FR-CHK-05 | Submitting for review MUST be rejected by the server if any mandatory item is not complete. |
| FR-CHK-06 | When the TL requests a revision, they choose which checklist items are reopened. |

### 2.12 TL review and final closure (REV-W)

| ID | Requirement |
|---|---|
| FR-RVW-01 | The assignee submits the task for review with a summary. Mandatory checklist items must be complete. |
| FR-RVW-02 | A TL with `tasks.review` in the project either approves or requests a revision (comment required). |
| FR-RVW-03 | A TL cannot approve review of a task where they are the assignee. |
| FR-RVW-04 | Final closure requires a prior TL approval of the current revision. Closure is performed by a TL or PM with `tasks.close`. *(See Q2 on whether approval and closure are one step.)* |
| FR-RVW-05 | Closed tasks are read-only. Reopening requires `tasks.reopen`, a reason, and starts a new revision. |

### 2.13 Attachments (ATT)

| ID | Requirement |
|---|---|
| FR-ATT-01 | Images, videos and documents can be attached to tasks, comments, algorithm submissions and review submissions. |
| FR-ATT-02 | Allowed types and size limits are global settings. Defaults: images (PNG, JPEG, WebP, GIF) 10 MB; documents (PDF, DOCX, XLSX, PPTX, TXT, MD, CSV, ZIP) 25 MB; videos (MP4, WebM) 200 MB. SVG and HTML are **not** allowed by default (script risk). |
| FR-ATT-03 | File type is checked by content signature ("magic bytes"), not only by extension or the browser-supplied content type. |
| FR-ATT-04 | Files are stored in private storage outside the web root under random names. Original file names are stored as metadata and sanitized when downloaded. |
| FR-ATT-05 | Downloads go through an API endpoint that checks the same permissions as the parent record, or through a short-lived signed URL issued after that check. |
| FR-ATT-06 | Downloads are sent with `Content-Disposition: attachment` (except approved inline image/video previews), `X-Content-Type-Options: nosniff`, and the stored content type. |
| FR-ATT-07 | Uploaded files SHOULD be malware-scanned before they can be downloaded. *(See Q9.)* |
| FR-ATT-08 | Attachments are soft-deleted; deleting is allowed for the uploader (while the task is not Closed) or TL/PM. |

### 2.14 Revision and reassignment management (REV)

| ID | Requirement |
|---|---|
| FR-REV-01 | A revision request from TL review increments the task revision number and keeps all previous time, checklist, review and attachment history linked to the earlier revision. |
| FR-REV-02 | A reassignment request contains: new assignee, reason (required, min 10 characters), and requester. |
| FR-REV-03 | A reassignment MUST be approved by an authorized approver before it takes effect. The requester cannot approve their own request. *(See Q7.)* |
| FR-REV-04 | When a reassignment is approved, the old assignee's active session (if any) is closed with end reason Reassigned, and the new assignee is notified. |
| FR-REV-05 | On reassignment, the approved algorithm stays valid unless the approver marks "algorithm re-approval required". |
| FR-REV-06 | Only one pending reassignment request per task at a time. |

### 2.15 Deadline extensions and delay reporting (DL)

| ID | Requirement |
|---|---|
| FR-DL-01 | The assignee (or TL/PM) can request a deadline extension with a new date and a reason. |
| FR-DL-02 | Extensions require approval by a TL/PM with `deadlines.approve`, who cannot be the requester. The approver can approve, reject, or approve with a different date. |
| FR-DL-03 | The original due date and every extension are kept. Reports show original vs current due date. |
| FR-DL-04 | Only one pending extension request per task at a time. |
| FR-DL-05 | A daily background job marks tasks overdue when the due date has passed and the task is not Approved/Closed/Cancelled, and notifies the assignee and TL. |
| FR-DL-06 | The assignee can file a **delay report** (category: blocked by dependency, unclear requirement, technical issue, leave, other; plus description) on any open task. Delay reports do not change the due date. |

### 2.16 GitLab project and issue import (IMP)

| ID | Requirement |
|---|---|
| FR-IMP-01 | Admins configure one or more GitLab connections: base URL (gitlab.com or self-managed) and an access token with minimal read scope (`read_api`). |
| FR-IMP-02 | Access tokens are encrypted at rest (ASP.NET Core Data Protection with keys outside the database, or a secret store) and never returned by the API, written to logs, or shown in the UI after saving. |
| FR-IMP-03 | A PM/TL with `gitlab.import` can link an internal project to a GitLab project and import selected open issues as Draft tasks. |
| FR-IMP-04 | Imports are idempotent: an issue is identified by (connection, GitLab project ID, issue IID); importing again updates the link instead of creating a duplicate. |
| FR-IMP-05 | Imported fields: title, description (sanitized), labels, milestone due date (as suggested due date), web URL. Assignees are **not** mapped automatically. *(See Q10.)* |
| FR-IMP-06 | A scheduled sync (default every 15 minutes) and optionally a GitLab webhook (verified with a secret token) detect issue changes. |
| FR-IMP-07 | When a GitLab issue is closed, the internal task MUST NOT be closed. The task is flagged "GitLab issue closed" and the TL is notified. |
| FR-IMP-08 | Import runs are recorded: who, when, counts created/updated/skipped/failed, and error messages (without secrets). |
| FR-IMP-09 | GitLab API rate limits and failures are handled with retries and do not block the rest of the application. |

### 2.17 Dashboards and reports (RPT)

| ID | Requirement |
|---|---|
| FR-RPT-01 | Developer dashboard: my tasks by status, active session timer, due soon, overdue, hours this week, pending algorithm/review feedback. |
| FR-RPT-02 | TL dashboard: pending algorithm approvals, pending reviews, tasks over allocated hours, overdue tasks, pending extension/reassignment requests, team workload. |
| FR-RPT-03 | PM dashboard: project progress (tasks by status), allocated vs actual hours, overdue trend, extensions count, revision counts. |
| FR-RPT-04 | Admin dashboard: member counts, failed sign-ins, integration status, background job health, email failures. |
| FR-RPT-05 | Reports: time spent (by member/project/task/date range), allocated vs actual, overdue tasks, deadline extensions, revisions per task, delay reports. |
| FR-RPT-06 | Reports respect the viewer's permissions — they only include projects and tasks the viewer may see. |
| FR-RPT-07 | Reports can be exported as CSV. Exported cells are protected against spreadsheet formula injection. |

### 2.18 Audit logs (AUD)

| ID | Requirement |
|---|---|
| FR-AUD-01 | Audit entries store: time (UTC), actor member ID, actor IP, action, entity type, entity ID, project ID (if any), changed fields with old/new values, correlation ID. |
| FR-AUD-02 | Audited actions include: authentication events, member/role/permission/setting changes, project/membership changes, task create/update/status change, assignment, reassignment, algorithm decisions, reviews, closure, reopen, time adjustments, session auto-close, extensions, attachment upload/download/delete, GitLab configuration and imports, report exports. |
| FR-AUD-03 | Audit entries are append-only: no API to edit or delete them; the application's database user has no UPDATE/DELETE rights on the audit table. |
| FR-AUD-04 | Secrets, passwords, tokens and file contents are never written to audit logs. |
| FR-AUD-05 | Audit logs are searchable by date range, actor, entity and action by members with `audit.view`. |
| FR-AUD-06 | Audit log retention is configurable (default: keep at least 1 year). |

### 2.19 Administration and system settings (ADM)

| ID | Requirement |
|---|---|
| FR-ADM-01 | Admin screens: members, roles & permissions, global settings, checklist templates, GitLab connections, notification/email status, background jobs status, audit log. |
| FR-ADM-02 | Health endpoints: liveness and readiness (database, storage, email). Detailed health information is visible only to Admins. |
| FR-ADM-03 | An initial Admin account is created by a one-time setup command or deployment secret, not through a public endpoint. |

---

## 3. Non-functional requirements

| ID | Area | Requirement |
|---|---|---|
| NFR-SEC-01 | Security | Follow OWASP ASVS 5.0 Level 2 as the target baseline and the OWASP Top 10. |
| NFR-SEC-02 | Security | HTTPS only in testing and production; HSTS enabled; secure, HttpOnly, SameSite cookies. |
| NFR-SEC-03 | Security | Anti-forgery (CSRF) protection for cookie-authenticated state-changing requests. |
| NFR-SEC-04 | Security | Content Security Policy without `unsafe-inline` scripts; security headers (nosniff, frame-ancestors none, referrer-policy). |
| NFR-SEC-05 | Security | Rate limiting on sign-in, forgot-password, reset-password and upload endpoints. |
| NFR-SEC-06 | Security | Secrets come from environment variables or a secret store, never from committed files. Repository includes secret scanning in CI. |
| NFR-SEC-07 | Security | Errors return RFC 9457 Problem Details with a correlation ID; no stack traces or internal details in production. |
| NFR-SEC-08 | Security | Dependencies are scanned for known vulnerabilities in CI (`dotnet list package --vulnerable`, `npm audit`). |
| NFR-SEC-09 | Security | Object-level authorization tests exist for every endpoint that takes an ID (prevent IDOR). |
| NFR-PERF-01 | Performance | p95 API response time under 500 ms for normal requests at the expected load (see A9), excluding file transfer. |
| NFR-PERF-02 | Performance | All lists are paginated server-side (default 25, maximum 100 per page). |
| NFR-PERF-03 | Performance | Large uploads are streamed to storage, not buffered fully in memory. |
| NFR-REL-01 | Reliability | Business changes and their audit/outbox records are saved in one transaction. |
| NFR-REL-02 | Reliability | Background jobs are safe to run more than once (idempotent) and survive restarts. |
| NFR-REL-03 | Reliability | Database backups daily with point-in-time recovery in production; restore procedure documented and tested. |
| NFR-OBS-01 | Observability | Structured logs (JSON) with correlation IDs; no personal data beyond member IDs; no secrets. |
| NFR-OBS-02 | Observability | OpenTelemetry-compatible metrics and traces SHOULD be available. |
| NFR-MNT-01 | Maintainability | Modular monolith with clear module boundaries (details in Phase 2 architecture). |
| NFR-MNT-02 | Maintainability | Database changes only through EF Core migrations, reviewed in merge requests. |
| NFR-MNT-03 | Maintainability | Automated tests: backend unit + integration (real PostgreSQL via Testcontainers), Angular unit tests, end-to-end tests for main workflows. CI blocks merges on failures. |
| NFR-UX-01 | Usability | Responsive UI for desktop and tablet; usable on mobile for time tracking. |
| NFR-UX-02 | Accessibility | WCAG 2.2 AA target. |
| NFR-UX-03 | Usability | Times shown in the member's time zone; stored in UTC. |
| NFR-COMP-01 | Compatibility | Latest two versions of Chrome, Edge, Firefox and Safari. |
| NFR-PRIV-01 | Privacy | Personal data limited to what is needed (name, email, work records). Deactivated members' data kept for history; anonymization procedure documented. |
| NFR-API-01 | API | OpenAPI document generated from code; versioned routes (`/api/v1/...`). |

---

## 4. Assumptions

These are what we will build unless you decide otherwise.

| ID | Assumption |
|---|---|
| A1 | **Two levels of role:** each member has one global role (mainly to identify Admins and set defaults) and a project role per project membership. A person can be TL in one project and Developer in another. Project-scoped permissions come from the project role. |
| A2 | One current assignee per task (no multi-developer tasks). Larger work is split into several tasks. |
| A3 | Pause = short break, task stays "in progress" from the team's view. Stop = work halted (end of day, blocked); a reason is required. Both close the current work session. |
| A4 | Approval (TL) and closure (TL or PM) are two separate actions, so a PM can do a final check (for example deployment done) after technical approval. A project setting can enable "close on approval" to merge them. |
| A5 | Exceeding allocated hours notifies but does not block work. |
| A6 | Reassignment requested by a TL is approved by a PM of the project (and vice versa); Admins can approve any request. Requester never approves their own request. |
| A7 | Authentication uses ASP.NET Core Identity with a secure HttpOnly cookie, with Angular served from the same site (no tokens in browser storage). |
| A8 | Email delivery via SMTP (any provider); development uses a local mail catcher. |
| A9 | Expected scale: up to ~500 members, ~200 concurrent users, ~100,000 tasks, ~1 TB attachments over time. Single region. |
| A10 | Attachment storage is an abstraction with a local-disk implementation for development and S3-compatible (or Azure Blob) private bucket for production. |
| A11 | Interface language: English only. Dates/times localized by time zone only. |
| A12 | Working hours/holiday calendars are not needed in phase 1 of the product; durations are wall-clock time. |
| A13 | Deployment target: Linux containers (Docker) behind a reverse proxy; exact hosting decided in the deployment phase. |
| A14 | GitLab integration is read-only from GitLab (we never write back to GitLab in the first release). |

---

## 5. Decisions

On 2026-10-17 the product owner approved Phase 1 and accepted the **recommended default** for every question below (Q1–Q14) and for WQ1–WQ4 in [task-workflow.md](task-workflow.md). Assumptions A1–A14 are therefore confirmed. Any later change is recorded here with a date.

| ID | Question | Decision (accepted default) |
|---|---|---|
| Q1 | **Source control:** the stack says GitLab, but the repository you gave is on GitHub (`naveenmsccs003/task-management-application`). Should the code live on GitHub, GitLab, or both (mirror)? This affects CI (GitHub Actions vs GitLab CI). The *GitLab issue import feature* is independent of this. | Use the GitHub repo you gave, with GitHub Actions; keep GitLab only as an integration. |
| Q2 | Should TL approval and final closure be separate steps (A4) or a single step? | Separate, with a per-project "close on approval" option. |
| Q3 | Can a task have more than one developer at once? | No (A2). |
| Q4 | Is the Pause vs Stop distinction in A3 what you intend? | Yes as in A3. |
| Q5 | Should work be blocked (or need TL approval to continue) once allocated hours are reached? | Notify only (A5). |
| Q6 | Is MFA required, and for which roles? | Optional for all; required for Admins. |
| Q7 | Who approves reassignments and deadline extensions — is A6 correct? Should Admin approval also be allowed? | A6, Admin allowed. |
| Q8 | Should PMs (not only TLs) be allowed to approve algorithms? | No — TL only by default, configurable via permissions. |
| Q9 | Is malware scanning of uploads required (for example ClamAV)? If not available, uploads are still type-checked and served safely. | Yes, ClamAV in testing/production; files held as "pending scan" until clean. |
| Q10 | Should GitLab assignees be mapped to internal members by email during import? | No — TL assigns after import. |
| Q11 | Is there a data retention requirement (for example delete attachments of closed projects after N years)? | Keep everything; Admin-triggered archive later. |
| Q12 | Do tasks need sub-tasks or dependencies (task A blocks task B)? | Not in the first release. |
| Q13 | Is single sign-on (Microsoft Entra ID / Google / LDAP) needed now or later? | Later; design keeps it possible. |
| Q14 | Where will production run (cloud provider / on-premises)? | Decide in deployment phase; build container-ready. |

---

## 6. Edge cases

The workflow and tests must handle these.

**Authentication and accounts**

1. Reset requested twice → only the newest token works; the older one is invalidated.
2. Reset token used after the member is deactivated → rejected.
3. Member deactivated while having an active work session → session closed (end reason Member deactivated), TL notified, open tasks flagged for reassignment.
4. Admin attempts to deactivate the last Admin, or themselves → rejected.
5. Email changed by Admin → old outstanding reset links invalidated.

**Permissions**

6. Developer guesses another task's ID → 404 (not 403), so task existence is not leaked.
7. Member removed from a project → loses access immediately, including attachment downloads and old notifications' links.
8. Role permission removed while a user is signed in → effective within one minute.
9. User is TL and assignee of the same task → cannot approve their own algorithm or review.

**Work sessions and time**

10. Double-click on Start, or two browser tabs → only one session created (idempotency + database unique index).
11. Developer starts task B while task A has an active session → rejected with a clear message ("Pause or stop task A first"). *(Alternative: auto-pause A — see WQ3 in task-workflow.md; default is reject.)*
12. Browser closed / laptop asleep with session running → session continues on the server; auto-closed after the max length.
13. Session spans midnight or a DST change → durations computed in UTC, so correct.
14. Allocated hours reached during an active session → background job detects and notifies (not just on stop).
15. Allocated hours changed by TL after the warning was sent → notifications re-evaluated against the new value.
16. Allocated hours = 0 or not set → no limit notifications; shown as "not estimated".
17. Task reassigned while old assignee is working → old session closed with end reason Reassigned.
18. Server clock correction → servers are NTP-synchronized and use UTC; a negative duration is never stored (end time is clamped to the start time and the event is logged).

**Workflow**

19. Submit for review with an unchecked mandatory item → rejected with the list of missing items.
20. Template updated after being applied → existing task checklists unchanged.
21. Algorithm approved, then task reassigned → algorithm stays approved unless re-approval required.
22. TL requests revision while the developer's previous session is still open → impossible because submit closed it; but if a time adjustment is pending it stays linked to the earlier revision.
23. Two TLs act on the same review at the same time → second one gets a concurrency error.
24. Extension requested when one is already pending → rejected.
25. Extension approved with a date in the past → rejected.
26. Task cancelled with pending approval requests → requests auto-cancelled.
27. Project archived with pending requests → requests auto-cancelled; tasks read-only.

**GitLab**

28. Same issue imported twice → updated, not duplicated.
29. Issue moved to another GitLab project or deleted → task flagged; not deleted.
30. Issue closed in GitLab → task flagged only, never closed (mandatory rule).
31. Token revoked/expired → connection marked unhealthy, Admin notified, sync paused.
32. GitLab unreachable or rate-limited → retries with backoff; rest of the app unaffected.

**Attachments**

33. File renamed `malware.exe` → `report.pdf` → rejected by signature check.
34. Very large upload interrupted → partial file removed; no orphan record.
35. File name with path characters (`../../x`) or Unicode tricks → sanitized; storage uses random names.
36. Attachment link shared with someone without access → refused (signed URLs expire within minutes and access is re-checked on direct download).

**Notifications and reports**

37. Email server down → in-app notifications still work; emails retried.
38. CSV export with a title like `=HYPERLINK(...)` → cell escaped.

---

## 7. Acceptance criteria

Written as *Given / When / Then*. Each will become at least one automated test.

### Authentication

- **AC-AUTH-1** Given no session, when anyone calls any registration URL, then the API returns 404 and the UI shows no sign-up option.
- **AC-AUTH-2** Given a member with a valid password, when they sign in, then a secure HttpOnly cookie is set and `/api/v1/me` returns their profile and effective permissions.
- **AC-AUTH-3** Given 5 failed sign-ins, when the 6th attempt is made with the correct password within 15 minutes, then sign-in is refused and a lockout audit entry exists.
- **AC-AUTH-4** Given an unknown email, when "forgot password" is submitted, then the response is identical to a known email and no email is sent.
- **AC-AUTH-5** Given a reset token, when it is used once successfully, then using it again fails; and when it is used after 30 minutes, it fails.
- **AC-AUTH-6** Given the database, when inspected, then no password, reset token, or GitLab token is stored in plain text.

### Members and roles

- **AC-MEM-1** Given a Developer, when they call the create-member endpoint, then the API returns 403 and no account is created.
- **AC-MEM-2** Given one active Admin, when that Admin is deactivated, then the request is rejected.
- **AC-ROLE-1** Given an Admin removes `tasks.create` from the PM role, when a PM creates a task within one minute after, then the API returns 403.

### Projects and tasks

- **AC-TASK-1** Given a task creation request without a project, then the API returns 400.
- **AC-TASK-2** Given a Developer, when they request a task in a project where they are not a member, then the API returns 404.
- **AC-TASK-3** Given a Developer member of a project, when they list tasks, then only tasks assigned to them (now or previously) are returned.
- **AC-TASK-4** Given two users edit the same task, when the second one saves, then the API returns 409 and the first change is kept.

### Algorithm

- **AC-ALG-1** Given a task with "algorithm required" and no approved algorithm, when the assignee starts work, then the API returns 409 with the reason "algorithm approval required".
- **AC-ALG-2** Given a rejected algorithm, when the TL rejects without a comment, then the API returns 400.
- **AC-ALG-3** Given a TL who is the assignee, when they approve their own algorithm, then the API returns 403.

### Work sessions

- **AC-WORK-1** Given a Ready task, when the assignee sends Start twice in parallel, then exactly one active session exists.
- **AC-WORK-2** Given an active session on task A, when the same member starts task B, then the API returns 409 and no new session exists.
- **AC-WORK-3** Given a Start request containing a client timestamp, then the timestamp is ignored and server time is used.
- **AC-WORK-4** Given someone other than the assignee (even a TL), when they call Start, then the API returns 403.
- **AC-TIME-1** Given allocated hours of 2 and a running session, when total time reaches 2 hours, then the assignee and TL each receive exactly one notification.
- **AC-TIME-2** Given a session longer than the max length, when the job runs, then the session is closed as Auto-closed and the member and TL are notified.

### Checklist, review and closure

- **AC-CHK-1** Given a task with one unchecked mandatory item, when the assignee submits for review, then the API returns 409 listing that item.
- **AC-RVW-1** Given a task In review, when the TL requests a revision without a comment, then the API returns 400.
- **AC-RVW-2** Given a revision request, then the revision number increases by one and previous history remains readable.
- **AC-RVW-3** Given a task not yet approved, when a PM closes it, then the API returns 409.
- **AC-RVW-4** Given an approved task, when a TL/PM closes it, then the status is Closed and further edits return 409.

### Reassignment and deadlines

- **AC-REV-1** Given a reassignment request without a reason, then the API returns 400.
- **AC-REV-2** Given a reassignment request, when its requester tries to approve it, then the API returns 403.
- **AC-REV-3** Given an approved reassignment while the old assignee has an active session, then that session ends with end reason Reassigned and assignment history shows both members.
- **AC-DL-1** Given an extension approval, then both the original and new due dates are visible in history.

### Attachments

- **AC-ATT-1** Given a member without access to a task, when they request one of its attachments by ID, then the API returns 404.
- **AC-ATT-2** Given an executable renamed to `.pdf`, when uploaded, then the API returns 400 and nothing is stored.
- **AC-ATT-3** Given a download, then the response includes `X-Content-Type-Options: nosniff` and `Content-Disposition: attachment`.

### GitLab

- **AC-IMP-1** Given an issue already imported, when imported again, then no new task is created.
- **AC-IMP-2** Given an imported issue is closed in GitLab, when sync runs, then the internal task status is unchanged, the task is flagged, and the TL is notified.
- **AC-IMP-3** Given a saved GitLab connection, when it is read through the API, then the token is not included.

### Audit and errors

- **AC-AUD-1** Given any status change, then an audit entry exists with actor, old status and new status.
- **AC-AUD-2** Given the application's database user, when it tries to UPDATE or DELETE an audit row, then the database refuses.
- **AC-ERR-1** Given an unhandled server error in production mode, then the response is Problem Details with a correlation ID and no stack trace.
