using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "limit_notified_revision",
                schema: "tasks",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "warn_notified_revision",
                schema: "tasks",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_runs",
                schema: "ops",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.job_name);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => new { x.user_id, x.category });
                    table.CheckConstraint("ck_notification_preferences_category", "category IN ('Account', 'Assignment', 'Algorithm', 'Time')");
                    table.ForeignKey(
                        name: "fk_notification_preferences_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_type", "type IN ('TaskAssigned', 'AlgorithmSubmitted', 'AlgorithmApproved', 'AlgorithmRejected', 'AllocationWarning', 'AllocationReached', 'SessionAutoClosed')");
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_id",
                        column: x => x.recipient_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                schema: "notifications",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_created_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                schema: "notifications",
                table: "notifications",
                column: "recipient_id",
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_runs",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropColumn(
                name: "limit_notified_revision",
                schema: "tasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "warn_notified_revision",
                schema: "tasks",
                table: "tasks");
        }
    }
}
