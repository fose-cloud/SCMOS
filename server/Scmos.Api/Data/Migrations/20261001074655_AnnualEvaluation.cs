using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnnualEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FinalPercent",
                table: "supplier_evaluations",
                type: "decimal(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImportedAt",
                table: "supplier_evaluations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedBy",
                table: "supplier_evaluations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "supplier_evaluations",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "supplier_evaluations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "scmos");

            migrationBuilder.CreateTable(
                name: "evaluation_campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    OpenOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SystemWeight = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    HumanWeight = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    MinimumJobs = table.Column<int>(type: "int", nullable: false),
                    MinimumSystemCoverage = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    CommentRequiredAtOrBelow = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_carriers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Included = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExcludedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    TotalJobs = table.Column<int>(type: "int", nullable: true),
                    CompletedJobs = table.Column<int>(type: "int", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Eligibility = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: ""),
                    Decision = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AddedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_carriers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_carriers_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_carriers_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_kpis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    NameTh = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    Weight = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "band"),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "higher"),
                    Measure = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    FallbackScore = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_kpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_kpis_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TextTh = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    Weight = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_questions_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_score_bands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    MinScore = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_score_bands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_score_bands_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_campaign_departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_campaign_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_campaign_departments_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_campaign_departments_evaluation_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "evaluation_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_evaluators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false, defaultValue: ""),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_evaluators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_evaluators_evaluation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "evaluation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_evaluators_evaluation_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "evaluation_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_manual_scores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationCarrierId = table.Column<int>(type: "int", nullable: false),
                    KpiCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    AssessedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_manual_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_manual_scores_evaluation_carriers_EvaluationCarrierId",
                        column: x => x.EvaluationCarrierId,
                        principalTable: "evaluation_carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_results",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationCarrierId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Current = table.Column<bool>(type: "bit", nullable: false),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    CampaignVersion = table.Column<int>(type: "int", nullable: false),
                    SystemScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    HumanScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FinalScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    SystemWeightAvailable = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    Band = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    CalculatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_results_evaluation_carriers_EvaluationCarrierId",
                        column: x => x.EvaluationCarrierId,
                        principalTable: "evaluation_carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    EvaluationCarrierId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Current = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    GeneratedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_snapshots_evaluation_carriers_EvaluationCarrierId",
                        column: x => x.EvaluationCarrierId,
                        principalTable: "evaluation_carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_kpi_bands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KpiId = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_kpi_bands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_kpi_bands_evaluation_kpis_KpiId",
                        column: x => x.KpiId,
                        principalTable: "evaluation_kpis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_question_departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    CommentRequiredAtOrBelow = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_question_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_question_departments_evaluation_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "evaluation_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_question_departments_evaluation_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "evaluation_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_invitations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    EvaluatorId = table.Column<int>(type: "int", nullable: false),
                    EvaluationCarrierId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    RevokeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_invitations_evaluation_carriers_EvaluationCarrierId",
                        column: x => x.EvaluationCarrierId,
                        principalTable: "evaluation_carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_invitations_evaluation_evaluators_EvaluatorId",
                        column: x => x.EvaluatorId,
                        principalTable: "evaluation_evaluators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_department_scores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResultId = table.Column<long>(type: "bigint", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    Responses = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_department_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_department_scores_evaluation_results_ResultId",
                        column: x => x.ResultId,
                        principalTable: "evaluation_results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_snapshot_metrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "available"),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Numerator = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Denominator = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Formula = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    Sources = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    External = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_snapshot_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_snapshot_metrics_evaluation_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "evaluation_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_responses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvitationId = table.Column<long>(type: "bigint", nullable: false),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    EvaluationCarrierId = table.Column<int>(type: "int", nullable: false),
                    EvaluatorId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: ""),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_responses_evaluation_invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "evaluation_invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_answers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResponseId = table.Column<long>(type: "bigint", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_answers_evaluation_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "evaluation_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_answers_evaluation_responses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "evaluation_responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "evaluation_answer_idx",
                table: "evaluation_answers",
                columns: new[] { "ResponseId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_answers_QuestionId",
                table: "evaluation_answers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "evaluation_campaign_department_idx",
                table: "evaluation_campaign_departments",
                columns: new[] { "CampaignId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_campaign_departments_DepartmentId",
                table: "evaluation_campaign_departments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "evaluation_campaign_code_idx",
                table: "evaluation_campaigns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_campaign_year_idx",
                table: "evaluation_campaigns",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "evaluation_carrier_idx",
                table: "evaluation_carriers",
                columns: new[] { "CampaignId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_carrier_supplier_idx",
                table: "evaluation_carriers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "evaluation_department_score_result_idx",
                table: "evaluation_department_scores",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "evaluation_department_code_idx",
                table: "evaluation_departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_evaluator_department_idx",
                table: "evaluation_evaluators",
                columns: new[] { "CampaignId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_evaluators_DepartmentId",
                table: "evaluation_evaluators",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "evaluation_invitation_pair_idx",
                table: "evaluation_invitations",
                columns: new[] { "EvaluatorId", "EvaluationCarrierId" });

            migrationBuilder.CreateIndex(
                name: "evaluation_invitation_status_idx",
                table: "evaluation_invitations",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "evaluation_invitation_token_idx",
                table: "evaluation_invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_invitations_EvaluationCarrierId",
                table: "evaluation_invitations",
                column: "EvaluationCarrierId");

            migrationBuilder.CreateIndex(
                name: "evaluation_kpi_band_kpi_idx",
                table: "evaluation_kpi_bands",
                column: "KpiId");

            migrationBuilder.CreateIndex(
                name: "evaluation_kpi_code_idx",
                table: "evaluation_kpis",
                columns: new[] { "CampaignId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_manual_score_idx",
                table: "evaluation_manual_scores",
                columns: new[] { "EvaluationCarrierId", "KpiCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_question_department_idx",
                table: "evaluation_question_departments",
                columns: new[] { "QuestionId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_question_departments_DepartmentId",
                table: "evaluation_question_departments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "evaluation_question_code_idx",
                table: "evaluation_questions",
                columns: new[] { "CampaignId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_response_carrier_idx",
                table: "evaluation_responses",
                columns: new[] { "EvaluationCarrierId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "evaluation_response_invitation_idx",
                table: "evaluation_responses",
                column: "InvitationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_result_version_idx",
                table: "evaluation_results",
                columns: new[] { "EvaluationCarrierId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_score_band_code_idx",
                table: "evaluation_score_bands",
                columns: new[] { "CampaignId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_snapshot_metric_idx",
                table: "evaluation_snapshot_metrics",
                columns: new[] { "SnapshotId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "evaluation_snapshot_campaign_idx",
                table: "evaluation_snapshots",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "evaluation_snapshot_version_idx",
                table: "evaluation_snapshots",
                columns: new[] { "EvaluationCarrierId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_answers");

            migrationBuilder.DropTable(
                name: "evaluation_campaign_departments");

            migrationBuilder.DropTable(
                name: "evaluation_department_scores");

            migrationBuilder.DropTable(
                name: "evaluation_kpi_bands");

            migrationBuilder.DropTable(
                name: "evaluation_manual_scores");

            migrationBuilder.DropTable(
                name: "evaluation_question_departments");

            migrationBuilder.DropTable(
                name: "evaluation_score_bands");

            migrationBuilder.DropTable(
                name: "evaluation_snapshot_metrics");

            migrationBuilder.DropTable(
                name: "evaluation_responses");

            migrationBuilder.DropTable(
                name: "evaluation_results");

            migrationBuilder.DropTable(
                name: "evaluation_kpis");

            migrationBuilder.DropTable(
                name: "evaluation_questions");

            migrationBuilder.DropTable(
                name: "evaluation_snapshots");

            migrationBuilder.DropTable(
                name: "evaluation_invitations");

            migrationBuilder.DropTable(
                name: "evaluation_carriers");

            migrationBuilder.DropTable(
                name: "evaluation_evaluators");

            migrationBuilder.DropTable(
                name: "evaluation_campaigns");

            migrationBuilder.DropTable(
                name: "evaluation_departments");

            migrationBuilder.DropColumn(
                name: "FinalPercent",
                table: "supplier_evaluations");

            migrationBuilder.DropColumn(
                name: "ImportedAt",
                table: "supplier_evaluations");

            migrationBuilder.DropColumn(
                name: "ImportedBy",
                table: "supplier_evaluations");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "supplier_evaluations");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "supplier_evaluations");
        }
    }
}
