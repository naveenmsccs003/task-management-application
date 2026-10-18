using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GitLabImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences");

            migrationBuilder.EnsureSchema(
                name: "gitlab");

            migrationBuilder.AddColumn<bool>(
                name: "gitlab_issue_closed",
                schema: "tasks",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "connections",
                schema: "gitlab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    token_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    token_hint = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connections", x => x.id);
                    table.CheckConstraint("ck_connections_status", "status IN ('Unknown', 'Healthy', 'Unhealthy')");
                    table.ForeignKey(
                        name: "fk_connections_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_runs",
                schema: "gitlab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    updated_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_runs", x => x.id);
                    table.CheckConstraint("ck_import_runs_kind", "kind IN ('Import', 'Sync', 'Webhook')");
                    table.ForeignKey(
                        name: "fk_import_runs_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_import_runs_users_started_by",
                        column: x => x.started_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "issue_links",
                schema: "gitlab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gitlab_project_id = table.Column<long>(type: "bigint", nullable: false),
                    issue_iid = table.Column<long>(type: "bigint", nullable: false),
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    web_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    gitlab_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pending_changes = table.Column<string>(type: "jsonb", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_links", x => x.id);
                    table.CheckConstraint("ck_issue_links_state", "state IN ('Opened', 'Closed', 'Missing')");
                    table.ForeignKey(
                        name: "fk_issue_links_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "gitlab",
                        principalTable: "connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_issue_links_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_links",
                schema: "gitlab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gitlab_project_id = table.Column<long>(type: "bigint", nullable: false),
                    path_with_namespace = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    web_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sync_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    webhook_secret_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_links_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "gitlab",
                        principalTable: "connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_links_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_links_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications",
                sql: "type IN ('TaskAssigned', 'AlgorithmSubmitted', 'AlgorithmApproved', 'AlgorithmRejected', 'AllocationWarning', 'AllocationReached', 'SessionAutoClosed', 'SubmittedForReview', 'ReviewApproved', 'RevisionRequested', 'TaskClosed', 'TaskReopened', 'ReassignmentRequested', 'ReassignmentDecided', 'TaskReassigned', 'ExtensionRequested', 'ExtensionDecided', 'DelayReported', 'DueSoon', 'Overdue', 'GitLabIssueClosed', 'GitLabIssueMissing')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences",
                sql: "category IN ('Account', 'Assignment', 'Algorithm', 'Time', 'Review', 'Requests', 'Deadlines', 'GitLab')");

            migrationBuilder.CreateIndex(
                name: "ix_connections_created_by",
                schema: "gitlab",
                table: "connections",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_connections_name",
                schema: "gitlab",
                table: "connections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_project_id_started_at",
                schema: "gitlab",
                table: "import_runs",
                columns: new[] { "project_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_started_by",
                schema: "gitlab",
                table: "import_runs",
                column: "started_by");

            migrationBuilder.CreateIndex(
                name: "ix_issue_links_connection_id_gitlab_project_id_issue_iid",
                schema: "gitlab",
                table: "issue_links",
                columns: new[] { "connection_id", "gitlab_project_id", "issue_iid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_links_task_id",
                schema: "gitlab",
                table: "issue_links",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_links_connection_id_gitlab_project_id",
                schema: "gitlab",
                table: "project_links",
                columns: new[] { "connection_id", "gitlab_project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_links_created_by",
                schema: "gitlab",
                table: "project_links",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_project_links_project_id",
                schema: "gitlab",
                table: "project_links",
                column: "project_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_runs",
                schema: "gitlab");

            migrationBuilder.DropTable(
                name: "issue_links",
                schema: "gitlab");

            migrationBuilder.DropTable(
                name: "project_links",
                schema: "gitlab");

            migrationBuilder.DropTable(
                name: "connections",
                schema: "gitlab");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "gitlab_issue_closed",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications",
                sql: "type IN ('TaskAssigned', 'AlgorithmSubmitted', 'AlgorithmApproved', 'AlgorithmRejected', 'AllocationWarning', 'AllocationReached', 'SessionAutoClosed', 'SubmittedForReview', 'ReviewApproved', 'RevisionRequested', 'TaskClosed', 'TaskReopened', 'ReassignmentRequested', 'ReassignmentDecided', 'TaskReassigned', 'ExtensionRequested', 'ExtensionDecided', 'DelayReported', 'DueSoon', 'Overdue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences",
                sql: "category IN ('Account', 'Assignment', 'Algorithm', 'Time', 'Review', 'Requests', 'Deadlines')");
        }
    }
}
