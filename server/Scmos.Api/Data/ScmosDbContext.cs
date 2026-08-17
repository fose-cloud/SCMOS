using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// Azure SQL, mapped onto the table and column names the register already used
/// on D1 so an exported row loads without being rewritten.
/// </summary>
public class ScmosDbContext(DbContextOptions<ScmosDbContext> options) : DbContext(options)
{
    public DbSet<OperationJob> OperationJobs => Set<OperationJob>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<SupplierRequest> SupplierRequests => Set<SupplierRequest>();
    public DbSet<PreRunCheck> PreRunChecks => Set<PreRunCheck>();
    public DbSet<ShipmentMilestone> ShipmentMilestones => Set<ShipmentMilestone>();
    public DbSet<DelayRecord> DelayRecords => Set<DelayRecord>();
    public DbSet<IncidentCase> IncidentCases => Set<IncidentCase>();
    public DbSet<IncidentEvidence> IncidentEvidence => Set<IncidentEvidence>();
    public DbSet<ReportUpload> ReportUploads => Set<ReportUpload>();
    public DbSet<OperationUpload> OperationUploads => Set<OperationUpload>();
    public DbSet<OperationEntry> OperationEntries => Set<OperationEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<OperationJob>(job =>
        {
            job.ToTable("operation_jobs");
            job.HasKey(j => j.Key);
            // `key` is a reserved word in T-SQL; EF brackets it, but every hand-written
            // statement in JobsRepository has to as well.
            job.Property(j => j.Key).HasColumnName("key").HasMaxLength(80);
            job.Property(j => j.Cat).HasColumnName("cat").HasMaxLength(20);
            job.Property(j => j.Owner).HasColumnName("owner").HasMaxLength(60);
            job.Property(j => j.OwnerId).HasColumnName("owner_id").HasMaxLength(20).HasDefaultValue("");
            job.Property(j => j.WorkDate).HasColumnName("work_date").HasMaxLength(20);
            job.Property(j => j.Customer).HasColumnName("customer").HasMaxLength(200).HasDefaultValue("");
            job.Property(j => j.Trucker).HasColumnName("trucker").HasMaxLength(200).HasDefaultValue("");
            job.Property(j => j.JobCode).HasColumnName("job_code").HasMaxLength(80).HasDefaultValue("");
            job.Property(j => j.Container).HasColumnName("container").HasMaxLength(40).HasDefaultValue("");
            job.Property(j => j.Status).HasColumnName("status").HasMaxLength(60).HasDefaultValue("");
            job.Property(j => j.Data).HasColumnName("data").HasColumnType("nvarchar(max)");
            job.Property(j => j.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            job.Property(j => j.UpdatedAt).HasColumnName("updated_at");

            job.HasIndex(j => new { j.Owner, j.WorkDate }).HasDatabaseName("operation_jobs_owner_idx");
            job.HasIndex(j => new { j.OwnerId, j.WorkDate }).HasDatabaseName("operation_jobs_owner_id_idx");
            job.HasIndex(j => new { j.Cat, j.Status }).HasDatabaseName("operation_jobs_cat_status_idx");
        });

        model.Entity<WorkflowEvent>(entry =>
        {
            entry.ToTable("workflow_events");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(24);
            entry.Property(e => e.FromStage).HasColumnName("from_stage").HasMaxLength(40);
            entry.Property(e => e.ToStage).HasColumnName("to_stage").HasMaxLength(40);
            entry.Property(e => e.Hold).HasColumnName("hold").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.By).HasColumnName("by_user").HasMaxLength(120);
            entry.Property(e => e.At).HasColumnName("at");
            // Reading a job's workflow means reading its events newest first.
            entry.HasIndex(e => new { e.JobKey, e.Id }).HasDatabaseName("workflow_events_job_idx");
        });

        model.Entity<SupplierRequest>(entry =>
        {
            entry.ToTable("supplier_requests");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.Rank).HasColumnName("rank");
            entry.Property(e => e.Carrier).HasColumnName("carrier").HasMaxLength(120);
            entry.Property(e => e.QuotedPrice).HasColumnName("quoted_price");
            entry.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(20).HasDefaultValue("pending");
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(120);
            entry.Property(e => e.RequestedAt).HasColumnName("requested_at");
            entry.Property(e => e.RespondedAt).HasColumnName("responded_at");
            entry.Ignore(e => e.ResponseMinutes);
            entry.HasIndex(e => new { e.JobKey, e.Rank }).HasDatabaseName("supplier_requests_job_idx");
            entry.HasIndex(e => new { e.Carrier, e.Outcome }).HasDatabaseName("supplier_requests_carrier_idx");
        });

        model.Entity<PreRunCheck>(entry =>
        {
            entry.ToTable("pre_run_checks");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.ShipmentDate).HasColumnName("shipment_date").HasMaxLength(20);
            entry.Property(e => e.Carrier).HasColumnName("carrier").HasMaxLength(120);
            entry.Property(e => e.SentAt).HasColumnName("sent_at");
            entry.Property(e => e.SentBy).HasColumnName("sent_by").HasMaxLength(120);
            entry.Property(e => e.RespondedAt).HasColumnName("responded_at");
            entry.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.TruckNo).HasColumnName("truck_no").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Driver).HasColumnName("driver").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.DriverContact).HasColumnName("driver_contact").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.Correction).HasColumnName("correction").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(20).HasDefaultValue("pending");
            entry.Property(e => e.Escalation).HasColumnName("escalation").HasMaxLength(20).HasDefaultValue("none");
            entry.Property(e => e.ResponseMinutes).HasColumnName("response_minutes");

            entry.HasIndex(e => new { e.ShipmentDate, e.Outcome }).HasDatabaseName("pre_run_date_idx");
            entry.HasIndex(e => new { e.Carrier, e.Outcome }).HasDatabaseName("pre_run_carrier_idx");
            // One open check per job: sending the list twice is a re-send, not a
            // second measurement, and two open rows would double-count the SLA.
            entry.HasIndex(e => new { e.JobKey, e.Outcome }).HasDatabaseName("pre_run_job_idx");
        });

        model.Entity<ShipmentMilestone>(entry =>
        {
            entry.ToTable("shipment_milestones");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.Stage).HasColumnName("stage").HasMaxLength(40);
            entry.Property(e => e.PlannedAt).HasColumnName("planned_at").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.ActualAt).HasColumnName("actual_at");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
            entry.Property(e => e.Carrier).HasColumnName("carrier").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.TruckNo).HasColumnName("truck_no").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Driver).HasColumnName("driver").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.DelayReason).HasColumnName("delay_reason").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.PhotoKey).HasColumnName("photo_key").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            // One row per job and stage: a milestone is a fact about a point in
            // the journey, and a job cannot have been dispatched twice.
            entry.HasIndex(e => new { e.JobKey, e.Stage }).IsUnique().HasDatabaseName("shipment_milestone_job_stage_idx");
            entry.HasIndex(e => new { e.Stage, e.Status }).HasDatabaseName("shipment_milestone_stage_idx");
        });

        model.Entity<DelayRecord>(entry =>
        {
            entry.ToTable("delay_records");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.Stage).HasColumnName("stage").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.Category).HasColumnName("category").HasMaxLength(20);
            entry.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.Responsible).HasColumnName("responsible").HasMaxLength(24);
            entry.Property(e => e.ClassifiedBy).HasColumnName("classified_by").HasMaxLength(10);
            entry.Property(e => e.ClassifierBasis).HasColumnName("classifier_basis").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.DetectedAt).HasColumnName("detected_at");
            entry.Property(e => e.ImpactMinutes).HasColumnName("impact_minutes");
            entry.Property(e => e.NotifiedAt).HasColumnName("notified_at");
            entry.Property(e => e.NotifiedTeam).HasColumnName("notified_team").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.RecoveryAction).HasColumnName("recovery_action").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entry.Property(e => e.AgainstCarrier).HasColumnName("against_carrier");
            entry.Property(e => e.RecordedBy).HasColumnName("recorded_by").HasMaxLength(120);
            entry.HasIndex(e => new { e.JobKey, e.DetectedAt }).HasDatabaseName("delay_job_idx");
            entry.HasIndex(e => new { e.Category, e.DetectedAt }).HasDatabaseName("delay_category_idx");
        });

        model.Entity<IncidentCase>(entry =>
        {
            entry.ToTable("incident_cases");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Reference).HasColumnName("reference").HasMaxLength(40);
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(8);
            entry.Property(e => e.Category).HasColumnName("category").HasMaxLength(20).HasDefaultValue("other");
            entry.Property(e => e.Title).HasColumnName("title").HasMaxLength(300);
            entry.Property(e => e.Stage).HasColumnName("stage").HasMaxLength(20);
            entry.Property(e => e.What).HasColumnName("w_what").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.Where).HasColumnName("w_where").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.When).HasColumnName("w_when").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Who).HasColumnName("w_who").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.Why).HasColumnName("w_why").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.How).HasColumnName("w_how").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.AiSummary).HasColumnName("ai_summary").HasColumnType("nvarchar(max)").HasDefaultValue("");
            entry.Property(e => e.RootCause).HasColumnName("root_cause").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.CorrectiveAction).HasColumnName("corrective_action").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.PreventiveAction).HasColumnName("preventive_action").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.ResponsiblePerson).HasColumnName("responsible_person").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.DueDate).HasColumnName("due_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.FollowUpNote).HasColumnName("follow_up_note").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.EffectivenessNote).HasColumnName("effectiveness_note").HasMaxLength(1000).HasDefaultValue("");
            entry.Property(e => e.ApprovedBy).HasColumnName("approved_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entry.Property(e => e.RaisedBy).HasColumnName("raised_by").HasMaxLength(120);
            entry.Property(e => e.RaisedAt).HasColumnName("raised_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => e.Reference).IsUnique().HasDatabaseName("incident_reference_idx");
            entry.HasIndex(e => new { e.Stage, e.DueDate }).HasDatabaseName("incident_stage_idx");
        });

        model.Entity<IncidentEvidence>(entry =>
        {
            entry.ToTable("incident_evidence");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.CaseId).HasColumnName("case_id");
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(30);
            entry.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(260);
            entry.Property(e => e.ObjectKey).HasColumnName("object_key").HasMaxLength(400);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.UploadedBy).HasColumnName("uploaded_by").HasMaxLength(120);
            entry.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
            entry.HasIndex(e => e.CaseId).HasDatabaseName("incident_evidence_case_idx");
        });

        model.Entity<ReportUpload>(upload =>
        {
            upload.ToTable("report_uploads");
            upload.HasKey(u => u.Id);
            upload.Property(u => u.Id).HasColumnName("id");
            upload.Property(u => u.Period).HasColumnName("period").HasMaxLength(40);
            upload.Property(u => u.Filename).HasColumnName("filename").HasMaxLength(260);
            upload.Property(u => u.ObjectKey).HasColumnName("object_key").HasMaxLength(400);
            upload.Property(u => u.RowCount).HasColumnName("row_count").HasDefaultValue(0);
            upload.Property(u => u.IssueCount).HasColumnName("issue_count").HasDefaultValue(0);
            upload.Property(u => u.UploadedAt).HasColumnName("uploaded_at");
            upload.HasIndex(u => new { u.Period, u.UploadedAt }).HasDatabaseName("report_uploads_period_idx");
        });

        model.Entity<OperationUpload>(upload =>
        {
            upload.ToTable("operation_uploads");
            upload.HasKey(u => u.Id);
            upload.Property(u => u.Id).HasColumnName("id");
            upload.Property(u => u.UploadId).HasColumnName("upload_id");
            upload.Property(u => u.OwnerName).HasColumnName("owner_name").HasMaxLength(60);
            upload.Property(u => u.Flow).HasColumnName("flow").HasMaxLength(20);
            upload.Property(u => u.SubmittedBy).HasColumnName("submitted_by").HasMaxLength(120);
            upload.Property(u => u.SubmittedAt).HasColumnName("submitted_at");
            upload.HasIndex(u => new { u.OwnerName, u.SubmittedAt }).HasDatabaseName("operation_uploads_owner_idx");
        });

        model.Entity<OperationEntry>(entry =>
        {
            entry.ToTable("operation_entries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.OwnerName).HasColumnName("owner_name").HasMaxLength(40);
            entry.Property(e => e.WorkDate).HasColumnName("work_date").HasMaxLength(10);
            entry.Property(e => e.ReportingPeriod).HasColumnName("reporting_period").HasMaxLength(20);
            entry.Property(e => e.Flow).HasColumnName("flow").HasMaxLength(10);
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(180);
            entry.Property(e => e.Subcontractor).HasColumnName("subcontractor").HasMaxLength(180);
            entry.Property(e => e.JobCode).HasColumnName("job_code").HasMaxLength(80);
            entry.Property(e => e.ContainerNo).HasColumnName("container_no").HasMaxLength(80);
            entry.Property(e => e.EquipmentType).HasColumnName("equipment_type").HasMaxLength(40);
            entry.Property(e => e.PlanAt).HasColumnName("plan_at").HasMaxLength(32);
            entry.Property(e => e.ActualAt).HasColumnName("actual_at").HasMaxLength(32);
            entry.Property(e => e.OperationStatus).HasColumnName("operation_status").HasMaxLength(40);
            entry.Property(e => e.ValidationStatus).HasColumnName("validation_status").HasMaxLength(40);
            entry.Property(e => e.OtdStatus).HasColumnName("otd_status").HasMaxLength(40);
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(500);
            entry.Property(e => e.SubmittedBy).HasColumnName("submitted_by").HasMaxLength(120);
            entry.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => new { e.OwnerName, e.WorkDate }).HasDatabaseName("operation_entries_owner_date_idx");
            entry.HasIndex(e => new { e.ReportingPeriod, e.Flow }).HasDatabaseName("operation_entries_period_flow_idx");
        });
    }
}
