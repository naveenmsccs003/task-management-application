using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewRequestsDelays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_task_status_history_action",
                schema: "tasks",
                table: "task_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences");

            migrationBuilder.DropCheckConstraint(
                name: "ck_algorithm_submissions_decision",
                schema: "tasks",
                table: "algorithm_submissions");

            migrationBuilder.AddColumn<int>(
                name: "approved_revision",
                schema: "tasks",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "tasks",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                schema: "tasks",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_soon_notified_for",
                schema: "tasks",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_overdue",
                schema: "tasks",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "overdue_notified_on",
                schema: "tasks",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proposed_assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    algorithm_reapproval_required = table.Column<bool>(type: "boolean", nullable: false),
                    current_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    proposed_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    approved_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_requests", x => x.id);
                    table.CheckConstraint("ck_approval_requests_reason", "char_length(reason) >= 10");
                    table.CheckConstraint("ck_approval_requests_status", "status IN ('Pending', 'Approved', 'Rejected', 'Withdrawn', 'AutoCancelled')");
                    table.CheckConstraint("ck_approval_requests_type", "type IN ('Reassignment', 'DeadlineExtension')");
                    table.ForeignKey(
                        name: "fk_approval_requests_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_requests_users_decided_by",
                        column: x => x.decided_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_requests_users_proposed_assignee_id",
                        column: x => x.proposed_assignee_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_requests_users_requested_by",
                        column: x => x.requested_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delay_reports",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delay_reports", x => x.id);
                    table.CheckConstraint("ck_delay_reports_category", "category IN ('BlockedByDependency', 'UnclearRequirement', 'TechnicalIssue', 'Leave', 'Other')");
                    table.ForeignKey(
                        name: "fk_delay_reports_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delay_reports_users_reported_by",
                        column: x => x.reported_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_submissions",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    algorithm_reapproval_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_submissions", x => x.id);
                    table.CheckConstraint("ck_review_submissions_decision", "decision IN ('Pending', 'Approved', 'RevisionRequested')");
                    table.ForeignKey(
                        name: "fk_review_submissions_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_submissions_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_submissions_users_submitted_by",
                        column: x => x.submitted_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_task_status_history_action",
                schema: "tasks",
                table: "task_status_history",
                sql: "action IN ('Create', 'Update', 'Assign', 'SubmitAlgorithm', 'ApproveAlgorithm', 'RejectAlgorithm', 'Start', 'Pause', 'Resume', 'Stop', 'SubmitReview', 'ApproveReview', 'RequestRevision', 'Close', 'Reopen', 'Cancel', 'Reassign')");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_algorithm_submissions_decision",
                schema: "tasks",
                table: "algorithm_submissions",
                sql: "decision IN ('Pending', 'Approved', 'Rejected', 'Withdrawn')");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_decided_by",
                schema: "tasks",
                table: "approval_requests",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_proposed_assignee_id",
                schema: "tasks",
                table: "approval_requests",
                column: "proposed_assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_requested_by",
                schema: "tasks",
                table: "approval_requests",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_status_requested_at",
                schema: "tasks",
                table: "approval_requests",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_approval_requests_pending",
                schema: "tasks",
                table: "approval_requests",
                columns: new[] { "task_id", "type" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_delay_reports_reported_by",
                schema: "tasks",
                table: "delay_reports",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "ix_delay_reports_task_id_reported_at",
                schema: "tasks",
                table: "delay_reports",
                columns: new[] { "task_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_submissions_reviewed_by",
                schema: "tasks",
                table: "review_submissions",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_review_submissions_submitted_by",
                schema: "tasks",
                table: "review_submissions",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "ix_review_submissions_task_id_revision",
                schema: "tasks",
                table: "review_submissions",
                columns: new[] { "task_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ux_review_submissions_pending",
                schema: "tasks",
                table: "review_submissions",
                column: "task_id",
                unique: true,
                filter: "decision = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "delay_reports",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "review_submissions",
                schema: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_task_status_history_action",
                schema: "tasks",
                table: "task_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences");

            migrationBuilder.DropCheckConstraint(
                name: "ck_algorithm_submissions_decision",
                schema: "tasks",
                table: "algorithm_submissions");

            migrationBuilder.DropColumn(
                name: "approved_revision",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "closed_by",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "due_soon_notified_for",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "is_overdue",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "overdue_notified_on",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_task_status_history_action",
                schema: "tasks",
                table: "task_status_history",
                sql: "action IN ('Create', 'Update', 'Assign', 'SubmitAlgorithm', 'ApproveAlgorithm', 'RejectAlgorithm', 'Start', 'Pause', 'Resume', 'Stop', 'SubmitReview', 'ApproveReview', 'RequestRevision', 'Close', 'Reopen', 'Cancel')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                schema: "notifications",
                table: "notifications",
                sql: "type IN ('TaskAssigned', 'AlgorithmSubmitted', 'AlgorithmApproved', 'AlgorithmRejected', 'AllocationWarning', 'AllocationReached', 'SessionAutoClosed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_preferences_category",
                schema: "notifications",
                table: "notification_preferences",
                sql: "category IN ('Account', 'Assignment', 'Algorithm', 'Time')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_algorithm_submissions_decision",
                schema: "tasks",
                table: "algorithm_submissions",
                sql: "decision IN ('Pending', 'Approved', 'Rejected')");
        }
    }
}
