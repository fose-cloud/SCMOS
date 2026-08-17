using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreRunMonitoringDelayIncident : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delay_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    responsible = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    classified_by = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    classifier_basis = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    impact_minutes = table.Column<int>(type: "int", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    notified_team = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    recovery_action = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    against_carrier = table.Column<bool>(type: "bit", nullable: false),
                    recorded_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delay_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incident_cases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    kind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    w_what = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    w_where = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    w_when = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    w_who = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    w_why = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    w_how = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    ai_summary = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    root_cause = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    corrective_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    preventive_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    responsible_person = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    due_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    follow_up_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    effectiveness_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    approved_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    raised_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_cases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incident_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    case_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    uploaded_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_evidence", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pre_run_checks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    shipment_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    carrier = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    confirmed_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    truck_no = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    driver = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    driver_contact = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    correction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    escalation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    response_minutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_run_checks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipment_milestones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    planned_at = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    actual_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    carrier = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    truck_no = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    driver = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    delay_reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    photo_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_milestones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "delay_category_idx",
                table: "delay_records",
                columns: new[] { "category", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "delay_job_idx",
                table: "delay_records",
                columns: new[] { "job_key", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "incident_reference_idx",
                table: "incident_cases",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "incident_stage_idx",
                table: "incident_cases",
                columns: new[] { "stage", "due_date" });

            migrationBuilder.CreateIndex(
                name: "incident_evidence_case_idx",
                table: "incident_evidence",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "pre_run_carrier_idx",
                table: "pre_run_checks",
                columns: new[] { "carrier", "outcome" });

            migrationBuilder.CreateIndex(
                name: "pre_run_date_idx",
                table: "pre_run_checks",
                columns: new[] { "shipment_date", "outcome" });

            migrationBuilder.CreateIndex(
                name: "pre_run_job_idx",
                table: "pre_run_checks",
                columns: new[] { "job_key", "outcome" });

            migrationBuilder.CreateIndex(
                name: "shipment_milestone_job_stage_idx",
                table: "shipment_milestones",
                columns: new[] { "job_key", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "shipment_milestone_stage_idx",
                table: "shipment_milestones",
                columns: new[] { "stage", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delay_records");

            migrationBuilder.DropTable(
                name: "incident_cases");

            migrationBuilder.DropTable(
                name: "incident_evidence");

            migrationBuilder.DropTable(
                name: "pre_run_checks");

            migrationBuilder.DropTable(
                name: "shipment_milestones");
        }
    }
}
