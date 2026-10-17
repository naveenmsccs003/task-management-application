using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "files");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scan_attempts = table.Column<int>(type: "integer", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.CheckConstraint("ck_attachments_owner_type", "owner_type IN ('Task', 'Comment', 'Algorithm', 'Review')");
                    table.CheckConstraint("ck_attachments_scan_status", "scan_status IN ('Pending', 'Clean', 'Infected', 'Failed')");
                    table.CheckConstraint("ck_attachments_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_attachments_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "tasks",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_users_deleted_by",
                        column: x => x.deleted_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_deleted_by",
                schema: "files",
                table: "attachments",
                column: "deleted_by");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_drafts",
                schema: "files",
                table: "attachments",
                column: "uploaded_at",
                filter: "owner_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_owner_type_owner_id",
                schema: "files",
                table: "attachments",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_project_id",
                schema: "files",
                table: "attachments",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_storage_key",
                schema: "files",
                table: "attachments",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachments_task_id_uploaded_at",
                schema: "files",
                table: "attachments",
                columns: new[] { "task_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_uploaded_by",
                schema: "files",
                table: "attachments",
                column: "uploaded_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "files");
        }
    }
}
