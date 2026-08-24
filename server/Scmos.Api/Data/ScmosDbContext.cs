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
    public DbSet<OperationalIssue> OperationalIssues => Set<OperationalIssue>();
    public DbSet<RotationAssignment> RotationAssignments => Set<RotationAssignment>();

    /// <summary>Every file the system holds — a job's, a supplier's, a case's.</summary>
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();

    /// <summary>Who may sign in, and as what.</summary>
    public DbSet<StaffMember> Staff => Set<StaffMember>();

    /// <summary>Append-only in spirit: a grant is revoked, never removed.</summary>
    public DbSet<JobDelegation> JobDelegations => Set<JobDelegation>();

    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<TrainingCourse> TrainingCourses => Set<TrainingCourse>();
    public DbSet<CustomerTrainingRequirement> CustomerTrainingRequirements
        => Set<CustomerTrainingRequirement>();

    /// <summary>Append-only in practice: renewal writes a new row, never an update.</summary>
    public DbSet<DriverTraining> DriverTrainings => Set<DriverTraining>();

    /// <summary>Append-only. Nothing in the codebase deletes from it.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierAlias> SupplierAliases => Set<SupplierAlias>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SupplierTruck> SupplierTrucks => Set<SupplierTruck>();
    public DbSet<SupplierDriver> SupplierDrivers => Set<SupplierDriver>();
    public DbSet<SupplierCapacity> SupplierCapacities => Set<SupplierCapacity>();
    public DbSet<SupplierEvaluation> SupplierEvaluations => Set<SupplierEvaluation>();

    public DbSet<FuelBand> FuelBands => Set<FuelBand>();
    public DbSet<RateLane> RateLanes => Set<RateLane>();

    /// <summary>The request side of the rate book: what was asked, and of whom.</summary>
    public DbSet<RateInquiry> RateInquiries => Set<RateInquiry>();
    public DbSet<RateInquiryLane> RateInquiryLanes => Set<RateInquiryLane>();
    public DbSet<RateInquiryPrice> RateInquiryPrices => Set<RateInquiryPrice>();
    public DbSet<RatePrice> RatePrices => Set<RatePrice>();
    public DbSet<RateSurcharge> RateSurcharges => Set<RateSurcharge>();

    public DbSet<AiTool> AiTools => Set<AiTool>();
    public DbSet<Approval> Approvals => Set<Approval>();
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
            entry.Property(e => e.Company).HasColumnName("company").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Grade).HasColumnName("grade").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.NcClause).HasColumnName("nc_clause").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Team).HasColumnName("team").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.RequestedOn).HasColumnName("requested_on").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.ImmediateAction).HasColumnName("immediate_action").HasDefaultValue("");
            entry.Property(e => e.ImmediateBy).HasColumnName("immediate_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ImmediateDue).HasColumnName("immediate_due").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.DocumentsToRevise).HasColumnName("documents_to_revise").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.FollowUpBy).HasColumnName("follow_up_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ApprovalOutcome).HasColumnName("approval_outcome").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.ApprovalNote).HasColumnName("approval_note").HasDefaultValue("");
            entry.Property(e => e.TeamNote).HasColumnName("team_note").HasDefaultValue("");
            entry.Property(e => e.ApprovedBy).HasColumnName("approved_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entry.Property(e => e.RaisedBy).HasColumnName("raised_by").HasMaxLength(120);
            entry.Property(e => e.RaisedAt).HasColumnName("raised_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => e.Reference).IsUnique().HasDatabaseName("incident_reference_idx");
            entry.HasIndex(e => new { e.Stage, e.DueDate }).HasDatabaseName("incident_stage_idx");
        });

        // One table for every file. The indexes are the three questions actually
        // asked of it: what is attached to this job, to this supplier, to this
        // case — plus the unique key, which is what stops the same blob being
        // recorded twice if an upload is retried.
        // The directory an administrator edits. Email is unique because it is
        // what a sign-in is matched on — two rows claiming the same address
        // would make "who is this" depend on row order.
        model.Entity<StaffMember>(entry =>
        {
            entry.ToTable("staff");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").HasMaxLength(20);
            entry.Property(e => e.Email).HasColumnName("email").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.Name).HasColumnName("name").HasMaxLength(120);
            entry.Property(e => e.Account).HasColumnName("account").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Role).HasColumnName("role").HasMaxLength(40);
            entry.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => e.Email).IsUnique().HasDatabaseName("staff_email_idx")
                .HasFilter("[email] <> ''");
            entry.HasIndex(e => e.Account).HasDatabaseName("staff_account_idx");
        });

        model.Entity<JobDelegation>(entry =>
        {
            entry.ToTable("job_delegations");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.OwnerId).HasColumnName("owner_id").HasMaxLength(20);
            entry.Property(e => e.DelegateId).HasColumnName("delegate_id").HasMaxLength(20);
            entry.Property(e => e.FromDate).HasColumnName("from_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.ToDate).HasColumnName("to_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.Revoked).HasColumnName("revoked").HasDefaultValue(false);
            entry.Property(e => e.RevokedBy).HasColumnName("revoked_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            // The question asked on every write: who is this person covering for.
            entry.HasIndex(e => e.DelegateId).HasDatabaseName("delegation_delegate_idx");
            entry.HasIndex(e => e.OwnerId).HasDatabaseName("delegation_owner_idx");
        });

        model.Entity<RateInquiry>(entry =>
        {
            entry.ToTable("rate_inquiries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Number).HasColumnName("number");
            entry.Property(e => e.InquiredOn).HasColumnName("inquired_on").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Requestor).HasColumnName("requestor").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.RequestorId).HasColumnName("requestor_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.FuelBand).HasColumnName("fuel_band").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            // "What did we ask for this customer" and "what have I raised" are
            // the two questions the screen opens with.
            entry.HasIndex(e => e.Customer).HasDatabaseName("rate_inquiry_customer_idx");
            entry.HasIndex(e => new { e.RequestorId, e.Id }).HasDatabaseName("rate_inquiry_requestor_idx");
        });

        model.Entity<RateInquiryLane>(entry =>
        {
            entry.ToTable("rate_inquiry_lanes");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.InquiryId).HasColumnName("inquiry_id");
            entry.Property(e => e.FromPlace).HasColumnName("from_place").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.ToPlace).HasColumnName("to_place").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.County).HasColumnName("county").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Carriers).HasColumnName("carriers").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.Fcl).HasColumnName("fcl").HasDefaultValue(false);
            entry.Property(e => e.Lcl).HasColumnName("lcl").HasDefaultValue(false);
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(600).HasDefaultValue("");
            entry.HasIndex(e => e.InquiryId).HasDatabaseName("rate_inquiry_lane_inquiry_idx");
        });

        model.Entity<RateInquiryPrice>(entry =>
        {
            entry.ToTable("rate_inquiry_prices");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.LaneId).HasColumnName("lane_id");
            entry.Property(e => e.Vehicle).HasColumnName("vehicle").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Price).HasColumnName("price");
            // One price per vehicle per lane: a second figure for the same box is
            // a correction, and a correction that leaves the old number behind
            // makes the lane unreadable.
            entry.HasIndex(e => new { e.LaneId, e.Vehicle }).IsUnique()
                .HasDatabaseName("rate_inquiry_price_lane_vehicle_idx");
        });

        model.Entity<OperationalIssue>(entry =>
        {
            entry.ToTable("operational_issues");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Code).HasColumnName("code").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.FoundOn).HasColumnName("found_on").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.FoundAt).HasColumnName("found_at").HasMaxLength(10).HasDefaultValue("");
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Reporter).HasColumnName("reporter").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.JobRef).HasColumnName("job_ref").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Detail).HasColumnName("detail").HasDefaultValue("");
            entry.Property(e => e.Category).HasColumnName("category").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Impact).HasColumnName("impact").HasDefaultValue("");
            entry.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Owner).HasColumnName("owner").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.OwnerId).HasColumnName("owner_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.DueOn).HasColumnName("due_on").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("");
            entry.Property(e => e.RootCause).HasColumnName("root_cause").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            // The code is how the team refers to an issue out loud, so two rows
            // may not share one. Imported rows keep the codes the sheet already
            // issued, which is only safe because this refuses a duplicate.
            entry.HasIndex(e => e.Code).IsUnique().HasDatabaseName("operational_issue_code_idx");
            // "What is still open" opens the screen; "what went wrong on this
            // job" is what the job's own page asks.
            entry.HasIndex(e => new { e.Status, e.Id }).HasDatabaseName("operational_issue_status_idx");
            entry.HasIndex(e => e.JobKey).HasDatabaseName("operational_issue_job_idx");
        });

        model.Entity<RotationAssignment>(entry =>
        {
            entry.ToTable("rotation_assignments");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Sheet).HasColumnName("sheet").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Import).HasColumnName("is_import").HasDefaultValue(false);
            entry.Property(e => e.Export).HasColumnName("is_export").HasDefaultValue(false);
            entry.Property(e => e.Fcl).HasColumnName("is_fcl").HasDefaultValue(false);
            entry.Property(e => e.Lcl).HasColumnName("is_lcl").HasDefaultValue(false);
            entry.Property(e => e.Domestic).HasColumnName("is_domestic").HasDefaultValue(false);
            entry.Property(e => e.PrimaryContact).HasColumnName("primary_contact").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.PrimaryEmail).HasColumnName("primary_email").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.PrimaryId).HasColumnName("primary_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.BackupContact).HasColumnName("backup_contact").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.BackupEmail).HasColumnName("backup_email").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.Backup2Contact).HasColumnName("backup2_contact").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.Backup2Email).HasColumnName("backup2_email").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.SubFcl).HasColumnName("sub_fcl").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.SubLcl).HasColumnName("sub_lcl").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.CsLcb).HasColumnName("cs_lcb").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            // "Whose customer is this" and "what does this person hold" are the
            // only two questions this table is ever asked.
            entry.HasIndex(e => e.Customer).HasDatabaseName("rotation_customer_idx");
            entry.HasIndex(e => e.PrimaryId).HasDatabaseName("rotation_primary_idx");
        });

        model.Entity<Driver>(entry =>
        {
            entry.ToTable("drivers");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Name).HasColumnName("name").HasMaxLength(160);
            entry.Property(e => e.DriverIdNo).HasColumnName("driver_id_no").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.PhotoDocumentId).HasColumnName("photo_document_id");
            entry.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // The licence number is what a certificate is issued against, so it
            // is the value the register is searched by and the one that stops
            // the same person being entered twice under two spellings.
            entry.HasIndex(e => e.DriverIdNo).IsUnique().HasDatabaseName("drivers_id_no_idx")
                .HasFilter("[driver_id_no] <> ''");
            entry.HasIndex(e => e.SupplierId).HasDatabaseName("drivers_supplier_idx");
        });

        model.Entity<TrainingCourse>(entry =>
        {
            entry.ToTable("training_courses");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Code).HasColumnName("code").HasMaxLength(40);
            entry.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
            entry.Property(e => e.ValidMonths).HasColumnName("valid_months").HasDefaultValue(12);
            entry.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(400).HasDefaultValue("");
            entry.HasIndex(e => e.Code).IsUnique().HasDatabaseName("training_course_code_idx");
        });

        model.Entity<CustomerTrainingRequirement>(entry =>
        {
            entry.ToTable("customer_training_requirements");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(200);
            entry.Property(e => e.CourseId).HasColumnName("course_id");
            entry.Property(e => e.ValidMonths).HasColumnName("valid_months");
            entry.Property(e => e.Mandatory).HasColumnName("mandatory").HasDefaultValue(true);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // One row per customer per course; asking for the same course twice
            // is a data-entry slip, not two requirements.
            entry.HasIndex(e => new { e.Customer, e.CourseId }).IsUnique()
                .HasDatabaseName("customer_course_idx");
        });

        model.Entity<DriverTraining>(entry =>
        {
            entry.ToTable("driver_training");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.DriverId).HasColumnName("driver_id");
            entry.Property(e => e.CourseId).HasColumnName("course_id");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.TrainingDate).HasColumnName("training_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.ExpiryDate).HasColumnName("expiry_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.CertificateNo).HasColumnName("certificate_no").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(600).HasDefaultValue("");
            entry.Property(e => e.DocumentId).HasColumnName("document_id");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.Voided).HasColumnName("voided").HasDefaultValue(false);
            entry.Property(e => e.VoidReason).HasColumnName("void_reason").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.VoidedBy).HasColumnName("voided_by").HasMaxLength(120).HasDefaultValue("");
            // The question asked constantly is "what is this driver's latest
            // record for this course", so that is what the index answers.
            entry.HasIndex(e => new { e.DriverId, e.CourseId }).HasDatabaseName("driver_training_idx");
            entry.HasIndex(e => e.ExpiryDate).HasDatabaseName("driver_training_expiry_idx");
        });

        model.Entity<StoredDocument>(entry =>
        {
            entry.ToTable("documents");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(20);
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.CaseId).HasColumnName("case_id");
            entry.Property(e => e.DriverId).HasColumnName("driver_id");
            entry.Property(e => e.Folder).HasColumnName("folder").HasMaxLength(30);
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Year).HasColumnName("year").HasMaxLength(4).HasDefaultValue("");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.JobRef).HasColumnName("job_ref").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(260);
            entry.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entry.Property(e => e.ObjectKey).HasColumnName("object_key").HasMaxLength(400);
            entry.Property(e => e.BlobUrl).HasColumnName("blob_url").HasMaxLength(700).HasDefaultValue("");
            entry.Property(e => e.ExpiryDate).HasColumnName("expiry_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.UploadedBy).HasColumnName("uploaded_by").HasMaxLength(120);
            entry.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
            entry.HasIndex(e => e.ObjectKey).IsUnique().HasDatabaseName("document_key_idx");
            entry.HasIndex(e => new { e.JobKey, e.Folder }).HasDatabaseName("document_job_idx");
            entry.HasIndex(e => e.SupplierId).HasDatabaseName("document_supplier_idx");
            entry.HasIndex(e => e.CaseId).HasDatabaseName("document_case_idx");
        });

        // The indexes are the three questions an audit asks: what happened to
        // this record, what did this person do, and what happened in this window.
        model.Entity<AuditEvent>(entry =>
        {
            entry.ToTable("audit_events");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.At).HasColumnName("at");
            entry.Property(e => e.Who).HasColumnName("who").HasMaxLength(160);
            entry.Property(e => e.WhoId).HasColumnName("who_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Role).HasColumnName("role").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Action).HasColumnName("action").HasMaxLength(40);
            entry.Property(e => e.Entity).HasColumnName("entity").HasMaxLength(40);
            entry.Property(e => e.EntityId).HasColumnName("entity_id").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.EntityLabel).HasColumnName("entity_label").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Field).HasColumnName("field").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.OldValue).HasColumnName("old_value").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.NewValue).HasColumnName("new_value").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("web");
            entry.HasIndex(e => new { e.Entity, e.EntityId }).HasDatabaseName("audit_entity_idx");
            entry.HasIndex(e => e.Who).HasDatabaseName("audit_who_idx");
            entry.HasIndex(e => e.At).HasDatabaseName("audit_at_idx");
        });

        // Supplier, rate and AI-permission tables. Column names stay snake_case
        // like the rest of the schema; EF's defaults for keys and lengths are
        // fine everywhere the value is not something the team types.
        model.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("draft");
            e.Property(x => x.VendorNo).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.TaxId).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.Address).HasMaxLength(400).HasDefaultValue("");
            e.Property(x => x.ServiceArea).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.ServiceType).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ApprovedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.LastEvaluatedPeriod).HasMaxLength(20).HasDefaultValue("");
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("suppliers_code_idx");
            e.HasIndex(x => x.Name).HasDatabaseName("suppliers_name_idx");
        });

        model.Entity<SupplierAlias>(e =>
        {
            e.ToTable("supplier_aliases");
            e.Property(x => x.Alias).HasMaxLength(160);
            e.Property(x => x.Source).HasMaxLength(20).HasDefaultValue("");
            e.HasIndex(x => x.Alias).IsUnique().HasDatabaseName("supplier_alias_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_alias_supplier_idx");
        });

        model.Entity<SupplierContact>(e =>
        {
            e.ToTable("supplier_contacts");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(80).HasDefaultValue("");
            e.Property(x => x.Phone).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.Email).HasMaxLength(160).HasDefaultValue("");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_contact_idx");
        });

        model.Entity<SupplierTruck>(e =>
        {
            e.ToTable("supplier_trucks");
            e.Property(x => x.Plate).HasMaxLength(60);
            e.Property(x => x.VehicleType).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.RegistrationExpiry).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("active");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_truck_idx");
        });

        model.Entity<SupplierDriver>(e =>
        {
            e.ToTable("supplier_drivers");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.LicenceNo).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.LicenceExpiry).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.TrainingExpiry).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("active");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_driver_idx");
        });

        model.Entity<SupplierCapacity>(e =>
        {
            e.ToTable("supplier_capacity");
            e.Property(x => x.Date).HasMaxLength(20);
            e.Property(x => x.VehicleType).HasMaxLength(20);
            e.Property(x => x.UpdatedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => new { x.Date, x.VehicleType }).HasDatabaseName("supplier_capacity_date_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_capacity_supplier_idx");
        });

        model.Entity<SupplierEvaluation>(e =>
        {
            e.ToTable("supplier_evaluations");
            e.Property(x => x.Period).HasMaxLength(20);
            e.Property(x => x.Grade).HasMaxLength(10).HasDefaultValue("");
            e.Property(x => x.Note).HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.Stage).HasMaxLength(20).HasDefaultValue("draft");
            e.Property(x => x.EvaluatedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ApprovedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => new { x.SupplierId, x.Period }).IsUnique().HasDatabaseName("supplier_evaluation_idx");
        });

        model.Entity<FuelBand>(e =>
        {
            e.ToTable("fuel_bands");
            e.Property(x => x.Label).HasMaxLength(40);
            e.Property(x => x.MinPrice).HasPrecision(6, 2);
            e.Property(x => x.MaxPrice).HasPrecision(6, 2);
            e.HasIndex(x => x.Position).IsUnique().HasDatabaseName("fuel_band_position_idx");
        });

        model.Entity<RateLane>(e =>
        {
            e.ToTable("rate_lanes");
            e.Property(x => x.Carrier).HasMaxLength(120);
            e.Property(x => x.Service).HasMaxLength(20);
            e.Property(x => x.Customer).HasMaxLength(300).HasDefaultValue("");
            e.Property(x => x.FromPlace).HasMaxLength(400).HasDefaultValue("");
            e.Property(x => x.ToPlace).HasMaxLength(400).HasDefaultValue("");
            e.Property(x => x.County).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Remark).HasMaxLength(300).HasDefaultValue("");
            e.Property(x => x.SourceFile).HasMaxLength(300).HasDefaultValue("");
            e.HasIndex(x => new { x.Carrier, x.Service }).HasDatabaseName("rate_lane_carrier_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("rate_lane_supplier_idx");
        });

        model.Entity<RatePrice>(e =>
        {
            e.ToTable("rate_prices");
            e.Property(x => x.Vehicle).HasMaxLength(20);
            // The lookup is always lane plus vehicle plus band, so that is the index.
            e.HasIndex(x => new { x.LaneId, x.Vehicle, x.BandPosition }).HasDatabaseName("rate_price_lookup_idx");
        });

        model.Entity<RateSurcharge>(e =>
        {
            e.ToTable("rate_surcharges");
            e.Property(x => x.Service).HasMaxLength(20);
            e.Property(x => x.No).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Currency).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Rate).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.Unit).HasMaxLength(80).HasDefaultValue("");
        });

        model.Entity<AiTool>(e =>
        {
            e.ToTable("ai_tools");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Agent).HasMaxLength(30);
            e.Property(x => x.Permission).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(400).HasDefaultValue("");
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ai_tool_name_idx");
        });

        model.Entity<Approval>(e =>
        {
            e.ToTable("approvals");
            e.Property(x => x.Tool).HasMaxLength(60);
            e.Property(x => x.Agent).HasMaxLength(30).HasDefaultValue("");
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)");
            e.Property(x => x.State).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(x => x.RequestedBy).HasMaxLength(120);
            e.Property(x => x.DecidedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.DecisionNote).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.Result).HasMaxLength(1000).HasDefaultValue("");
            e.HasIndex(x => new { x.State, x.RequestedAt }).HasDatabaseName("approval_state_idx");
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
