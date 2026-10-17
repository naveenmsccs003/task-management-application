using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectsAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.EnsureSchema(
                name: "tasks");

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    target_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    algorithm_required_default = table.Column<bool>(type: "boolean", nullable: false),
                    close_on_approval = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.CheckConstraint("ck_projects_key", "key ~ '^[A-Z][A-Z0-9]{1,9}$'");
                    table.CheckConstraint("ck_projects_status", "status IN ('Active', 'OnHold', 'Completed', 'Archived')");
                    table.ForeignKey(
                        name: "fk_projects_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_users_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_roles_project_role_id",
                        column: x => x.project_role_id,
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_added_by",
                        column: x => x.added_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_removed_by",
                        column: x => x.removed_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_counters",
                schema: "projects",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_counters", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_task_counters_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    original_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    allocated_minutes = table.Column<int>(type: "integer", nullable: true),
                    algorithm_required = table.Column<bool>(type: "boolean", nullable: false),
                    labels = table.Column<List<string>>(type: "text[]", nullable: false),
                    current_revision = table.Column<int>(type: "integer", nullable: false),
                    first_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.CheckConstraint("ck_tasks_allocated_minutes", "allocated_minutes IS NULL OR allocated_minutes > 0");
                    table.CheckConstraint("ck_tasks_due_after_start", "due_date IS NULL OR start_date IS NULL OR due_date >= start_date");
                    table.CheckConstraint("ck_tasks_priority", "priority IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("ck_tasks_status", "status IN ('Draft', 'AwaitingAlgorithm', 'AlgorithmSubmitted', 'ReadyToStart', 'InProgress', 'Paused', 'Stopped', 'InReview', 'RevisionRequested', 'Approved', 'Closed', 'Cancelled')");
                    table.CheckConstraint("ck_tasks_type", "type IN ('Feature', 'Bug', 'Improvement', 'Research', 'Other')");
                    table.ForeignKey(
                        name: "fk_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_assignee_id",
                        column: x => x.assignee_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_cancelled_by",
                        column: x => x.cancelled_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_assignments",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignments", x => x.id);
                    table.CheckConstraint("ck_task_assignments_kind", "kind IN ('Initial', 'Reassignment')");
                    table.ForeignKey(
                        name: "fk_task_assignments_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignments_users_assigned_by",
                        column: x => x.assigned_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignments_users_from_user_id",
                        column: x => x.from_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignments_users_to_user_id",
                        column: x => x.to_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comments_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_comments_users_author_id",
                        column: x => x.author_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_comments_users_deleted_by",
                        column: x => x.deleted_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_status_history",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_status_history", x => x.id);
                    table.CheckConstraint("ck_task_status_history_action", "action IN ('Create', 'Update', 'Assign', 'SubmitAlgorithm', 'ApproveAlgorithm', 'RejectAlgorithm', 'Start', 'Pause', 'Resume', 'Stop', 'SubmitReview', 'ApproveReview', 'RequestRevision', 'Close', 'Reopen', 'Cancel')");
                    table.CheckConstraint("ck_task_status_history_to_status", "to_status IN ('Draft', 'AwaitingAlgorithm', 'AlgorithmSubmitted', 'ReadyToStart', 'InProgress', 'Paused', 'Stopped', 'InReview', 'RevisionRequested', 'Approved', 'Closed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_task_status_history_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_status_history_users_actor_id",
                        column: x => x.actor_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comment_edits",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_body = table.Column<string>(type: "text", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comment_edits", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comment_edits_task_comments_comment_id",
                        column: x => x.comment_id,
                        principalSchema: "tasks",
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_added_by",
                schema: "projects",
                table: "project_members",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_id_user_id",
                schema: "projects",
                table: "project_members",
                columns: new[] { "project_id", "user_id" },
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_role_id",
                schema: "projects",
                table: "project_members",
                column: "project_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_removed_by",
                schema: "projects",
                table: "project_members",
                column: "removed_by");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                schema: "projects",
                table: "project_members",
                column: "user_id",
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_by",
                schema: "projects",
                table: "projects",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_projects_key",
                schema: "projects",
                table: "projects",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_status",
                schema: "projects",
                table: "projects",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_projects_updated_by",
                schema: "projects",
                table: "projects",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_assigned_by",
                schema: "tasks",
                table: "task_assignments",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_from_user_id",
                schema: "tasks",
                table: "task_assignments",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_task_id_occurred_at",
                schema: "tasks",
                table: "task_assignments",
                columns: new[] { "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_to_user_id_task_id",
                schema: "tasks",
                table: "task_assignments",
                columns: new[] { "to_user_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_edits_comment_id",
                schema: "tasks",
                table: "task_comment_edits",
                column: "comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_author_id",
                schema: "tasks",
                table: "task_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_deleted_by",
                schema: "tasks",
                table: "task_comments",
                column: "deleted_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_task_id_created_at",
                schema: "tasks",
                table: "task_comments",
                columns: new[] { "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_history_actor_id",
                schema: "tasks",
                table: "task_status_history",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_history_task_id_occurred_at",
                schema: "tasks",
                table: "task_status_history",
                columns: new[] { "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_id_status",
                schema: "tasks",
                table: "tasks",
                columns: new[] { "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_cancelled_by",
                schema: "tasks",
                table: "tasks",
                column: "cancelled_by");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by",
                schema: "tasks",
                table: "tasks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_due_date",
                schema: "tasks",
                table: "tasks",
                column: "due_date",
                filter: "status NOT IN ('Approved', 'Closed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_key",
                schema: "tasks",
                table: "tasks",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_number",
                schema: "tasks",
                table: "tasks",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_status",
                schema: "tasks",
                table: "tasks",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_updated_by",
                schema: "tasks",
                table: "tasks",
                column: "updated_by");

            // Title search (FR-TASK-07) matches lower(title) with LIKE '%text%'; a trigram index keeps that fast.
            // pg_trgm is a trusted extension, so the database owner (tma_migrator) may create it.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE INDEX ix_tasks_title_trgm ON tasks.tasks USING gin (lower(title) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS tasks.ix_tasks_title_trgm;");

            migrationBuilder.DropTable(
                name: "project_members",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "task_assignments",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_comment_edits",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_counters",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "task_status_history",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_comments",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "projects");
        }
    }
}
