using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkAlgorithmsChecklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "time");

            migrationBuilder.AddColumn<Guid>(
                name: "algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "checklist_template_id",
                schema: "tasks",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "tasks",
                table: "task_status_history",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "default_checklist_template_id",
                schema: "projects",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "algorithm_submissions",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_algorithm_submissions", x => x.id);
                    table.CheckConstraint("ck_algorithm_submissions_decision", "decision IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_algorithm_submissions_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_algorithm_submissions_users_decided_by",
                        column: x => x.decided_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_algorithm_submissions_users_submitted_by",
                        column: x => x.submitted_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_templates_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_checklist_templates_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_checklist_templates_users_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "ops",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.user_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reopened_in_revision = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_checklist_items_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_checklist_items_users_completed_by",
                        column: x => x.completed_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_adjustments",
                schema: "time",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_adjustments", x => x.id);
                    table.CheckConstraint("ck_time_adjustments_minutes", "minutes <> 0");
                    table.ForeignKey(
                        name: "fk_time_adjustments_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_time_adjustments_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_time_adjustments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_sessions",
                schema: "time",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_sessions", x => x.id);
                    table.CheckConstraint("ck_work_sessions_end_after_start", "ended_at IS NULL OR ended_at >= started_at");
                    table.CheckConstraint("ck_work_sessions_end_reason", "end_reason IN ('Paused', 'Stopped', 'Submitted', 'AutoClosed', 'Reassigned', 'Cancelled', 'MemberDeactivated')");
                    table.ForeignKey(
                        name: "fk_work_sessions_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checklist_template_items",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_template_items_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "tasks",
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks",
                column: "algorithm_approved_submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_checklist_template_id",
                schema: "tasks",
                table: "tasks",
                column: "checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_default_checklist_template_id",
                schema: "projects",
                table: "projects",
                column: "default_checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_algorithm_submissions_decided_by",
                schema: "tasks",
                table: "algorithm_submissions",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_algorithm_submissions_submitted_by",
                schema: "tasks",
                table: "algorithm_submissions",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "ix_algorithm_submissions_task_id_version",
                schema: "tasks",
                table: "algorithm_submissions",
                columns: new[] { "task_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_algorithm_submissions_pending",
                schema: "tasks",
                table: "algorithm_submissions",
                column: "task_id",
                unique: true,
                filter: "decision = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_template_items_template_id",
                schema: "tasks",
                table: "checklist_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_created_by",
                schema: "tasks",
                table: "checklist_templates",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_project_id",
                schema: "tasks",
                table: "checklist_templates",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_updated_by",
                schema: "tasks",
                table: "checklist_templates",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "ops",
                table: "idempotency_keys",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_task_checklist_items_completed_by",
                schema: "tasks",
                table: "task_checklist_items",
                column: "completed_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_checklist_items_task_id_sort_order",
                schema: "tasks",
                table: "task_checklist_items",
                columns: new[] { "task_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_created_by",
                schema: "time",
                table: "time_adjustments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_task_id_revision",
                schema: "time",
                table: "time_adjustments",
                columns: new[] { "task_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_time_adjustments_user_id",
                schema: "time",
                table: "time_adjustments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_sessions_task_id_revision",
                schema: "time",
                table: "work_sessions",
                columns: new[] { "task_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_work_sessions_user_id_started_at",
                schema: "time",
                table: "work_sessions",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_work_sessions_active_task",
                schema: "time",
                table: "work_sessions",
                column: "task_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_work_sessions_active_user",
                schema: "time",
                table: "work_sessions",
                column: "user_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_checklist_templates_default_checklist_template_id",
                schema: "projects",
                table: "projects",
                column: "default_checklist_template_id",
                principalSchema: "tasks",
                principalTable: "checklist_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_algorithm_submissions_algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks",
                column: "algorithm_approved_submission_id",
                principalSchema: "tasks",
                principalTable: "algorithm_submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_checklist_templates_checklist_template_id",
                schema: "tasks",
                table: "tasks",
                column: "checklist_template_id",
                principalSchema: "tasks",
                principalTable: "checklist_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_projects_checklist_templates_default_checklist_template_id",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_algorithm_submissions_algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_checklist_templates_checklist_template_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "algorithm_submissions",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "checklist_template_items",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "task_checklist_items",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "time_adjustments",
                schema: "time");

            migrationBuilder.DropTable(
                name: "work_sessions",
                schema: "time");

            migrationBuilder.DropTable(
                name: "checklist_templates",
                schema: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_checklist_template_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_projects_default_checklist_template_id",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "algorithm_approved_submission_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "checklist_template_id",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "default_checklist_template_id",
                schema: "projects",
                table: "projects");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "tasks",
                table: "task_status_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
