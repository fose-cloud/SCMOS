using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommunicationCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email_id = table.Column<long>(type: "bigint", nullable: false),
                    graph_attachment_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    file_name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    content_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    stored_document_id = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_entities",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    value = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    in_subject = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    labelled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    well_formed = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_entities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_job_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email_id = table.Column<long>(type: "bigint", nullable: false),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    matched_on = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: ""),
                    matched_value = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    confidence = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "SUGGESTED"),
                    confirmed_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_job_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_participants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, defaultValue: "TO"),
                    address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_participants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "emails",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    mailbox_id = table.Column<long>(type: "bigint", nullable: false),
                    graph_message_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    conversation_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    internet_message_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    from_address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false, defaultValue: ""),
                    from_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    body_text = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    body_html = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    has_attachments = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    processing_status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "RECEIVED"),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    error_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    retry_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "graph_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    mailbox_id = table.Column<long>(type: "bigint", nullable: false),
                    subscription_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    resource = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    notification_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    client_state = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_renewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mailboxes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    graph_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    folder_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailboxes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "email_attachments_email_idx",
                table: "email_attachments",
                column: "email_id");

            migrationBuilder.CreateIndex(
                name: "email_attachments_graph_idx",
                table: "email_attachments",
                columns: new[] { "email_id", "graph_attachment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "email_entities_email_idx",
                table: "email_entities",
                column: "email_id");

            migrationBuilder.CreateIndex(
                name: "email_entities_once_idx",
                table: "email_entities",
                columns: new[] { "email_id", "kind", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "email_entities_value_idx",
                table: "email_entities",
                columns: new[] { "kind", "value" });

            migrationBuilder.CreateIndex(
                name: "email_job_links_email_idx",
                table: "email_job_links",
                column: "email_id");

            migrationBuilder.CreateIndex(
                name: "email_job_links_job_idx",
                table: "email_job_links",
                column: "job_key");

            migrationBuilder.CreateIndex(
                name: "email_job_links_once_idx",
                table: "email_job_links",
                columns: new[] { "email_id", "job_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "email_participants_address_idx",
                table: "email_participants",
                column: "address");

            migrationBuilder.CreateIndex(
                name: "email_participants_email_idx",
                table: "email_participants",
                column: "email_id");

            migrationBuilder.CreateIndex(
                name: "emails_conversation_idx",
                table: "emails",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "emails_inbox_idx",
                table: "emails",
                columns: new[] { "mailbox_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "emails_message_idx",
                table: "emails",
                columns: new[] { "mailbox_id", "graph_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "emails_queue_idx",
                table: "emails",
                columns: new[] { "processing_status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "graph_subscriptions_id_idx",
                table: "graph_subscriptions",
                column: "subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "graph_subscriptions_renew_idx",
                table: "graph_subscriptions",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "mailboxes_address_idx",
                table: "mailboxes",
                column: "address",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_attachments");

            migrationBuilder.DropTable(
                name: "email_entities");

            migrationBuilder.DropTable(
                name: "email_job_links");

            migrationBuilder.DropTable(
                name: "email_participants");

            migrationBuilder.DropTable(
                name: "emails");

            migrationBuilder.DropTable(
                name: "graph_subscriptions");

            migrationBuilder.DropTable(
                name: "mailboxes");
        }
    }
}
