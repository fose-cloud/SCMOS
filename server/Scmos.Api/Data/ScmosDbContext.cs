using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Azure SQL, mapped onto the table and column names the register already used
/// on D1 so an exported row loads without being rewritten.
/// </summary>
public class ScmosDbContext(DbContextOptions<ScmosDbContext> options) : DbContext(options)
{
    public DbSet<OperationJob> OperationJobs => Set<OperationJob>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    /* ---- LINE: the group-to-supplier bridge, and the raw events ---- */
    public DbSet<LineGroup> LineGroups => Set<LineGroup>();
    public DbSet<LineUser> LineUsers => Set<LineUser>();
    public DbSet<LineEvent> LineEvents => Set<LineEvent>();

    /* The Communication Center. Seven tables — see MailEntities for why that is
       not the six the plan's heading claims. */
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<Email> Emails => Set<Email>();
    public DbSet<EmailParticipant> EmailParticipants => Set<EmailParticipant>();
    public DbSet<EmailAttachment> EmailAttachments => Set<EmailAttachment>();
    public DbSet<EmailEntity> EmailEntities => Set<EmailEntity>();
    public DbSet<EmailJobLink> EmailJobLinks => Set<EmailJobLink>();
    public DbSet<GraphSubscription> GraphSubscriptions => Set<GraphSubscription>();

    /// <summary>Published diesel prices, one row per change. The monthly
    /// average is computed from these — see dieselMonth.ts.</summary>
    public DbSet<DieselPrice> DieselPrices => Set<DieselPrice>();
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

    /// <summary>The lossless register imported by Customer Training Control.</summary>
    public DbSet<CustomerTrainingRecord> CustomerTrainingRecords => Set<CustomerTrainingRecord>();

    /// <summary>Append-only. Nothing in the codebase deletes from it.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AiAuditLog> AiAuditLogs => Set<AiAuditLog>();
    public DbSet<AiOperationsControl> AiOperationsControls => Set<AiOperationsControl>();
    public DbSet<AiAgentConfig> AiAgentConfigs => Set<AiAgentConfig>();
    public DbSet<AiDecision> AiDecisions => Set<AiDecision>();
    /// <summary>A carrier's machine credentials for the Carrier TMS API — see CarrierApiEntities.cs.</summary>
    public DbSet<CarrierApiClient> CarrierApiClients => Set<CarrierApiClient>();
    public DbSet<CarrierApiRequest> CarrierApiRequests => Set<CarrierApiRequest>();
    public DbSet<CarrierWebhook> CarrierWebhooks => Set<CarrierWebhook>();
    public DbSet<CarrierWebhookDelivery> CarrierWebhookDeliveries => Set<CarrierWebhookDelivery>();
    public DbSet<BusinessCalendarDay> BusinessCalendarDays => Set<BusinessCalendarDay>();
    public DbSet<BillingSlaRule> BillingSlaRules => Set<BillingSlaRule>();
    public DbSet<BillingCase> BillingCases => Set<BillingCase>();
    public DbSet<BillingInvoice> BillingInvoices => Set<BillingInvoice>();
    public DbSet<BillingInvoiceLine> BillingInvoiceLines => Set<BillingInvoiceLine>();
    public DbSet<BillingInvoiceJobLink> BillingInvoiceJobLinks => Set<BillingInvoiceJobLink>();
    public DbSet<BillingRequirementRule> BillingRequirementRules => Set<BillingRequirementRule>();
    public DbSet<BillingRequirementSnapshot> BillingRequirementSnapshots => Set<BillingRequirementSnapshot>();
    public DbSet<BillingTaxRule> BillingTaxRules => Set<BillingTaxRule>();
    public DbSet<BillingAdditionalCharge> BillingAdditionalCharges => Set<BillingAdditionalCharge>();
    public DbSet<BillingValidationRun> BillingValidationRuns => Set<BillingValidationRun>();
    public DbSet<BillingValidationResult> BillingValidationResults => Set<BillingValidationResult>();
    public DbSet<BillingReviewEvent> BillingReviewEvents => Set<BillingReviewEvent>();
    public DbSet<BillingAiAnalysis> BillingAiAnalyses => Set<BillingAiAnalysis>();
    public DbSet<OriginalDocumentPackage> OriginalDocumentPackages => Set<OriginalDocumentPackage>();
    public DbSet<BillingFinanceRecord> BillingFinanceRecords => Set<BillingFinanceRecord>();
    public DbSet<IntegrationOutboxEvent> IntegrationOutbox => Set<IntegrationOutboxEvent>();
    /// <summary>Jobs a carrier keyed in, waiting for the department — see CarrierJobRequestEntities.cs.</summary>
    public DbSet<CarrierJobRequest> CarrierJobRequests => Set<CarrierJobRequest>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierAlias> SupplierAliases => Set<SupplierAlias>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SupplierTruck> SupplierTrucks => Set<SupplierTruck>();
    public DbSet<SupplierDriver> SupplierDrivers => Set<SupplierDriver>();
    public DbSet<AuditPlan> AuditPlans => Set<AuditPlan>();
    public DbSet<AuditPlanItem> AuditPlanItems => Set<AuditPlanItem>();
    public DbSet<SupplierOnboardingItem> SupplierOnboardingItems => Set<SupplierOnboardingItem>();
    public DbSet<ActionPlanType> ActionPlanTypes => Set<ActionPlanType>();
    public DbSet<ActionPlan> ActionPlans => Set<ActionPlan>();
    public DbSet<ActionPlanItem> ActionPlanItems => Set<ActionPlanItem>();
    public DbSet<ActionPlanUpdate> ActionPlanUpdates => Set<ActionPlanUpdate>();
    public DbSet<ActionPlanReview> ActionPlanReviews => Set<ActionPlanReview>();
    public DbSet<ActionPlanScore> ActionPlanScores => Set<ActionPlanScore>();
    public DbSet<ActionPlanReference> ActionPlanReferences => Set<ActionPlanReference>();
    public DbSet<ActionPlanSkill> ActionPlanSkills => Set<ActionPlanSkill>();
    public DbSet<SkillAssessment> SkillAssessments => Set<SkillAssessment>();
    public DbSet<EvaluationCampaign> EvaluationCampaigns => Set<EvaluationCampaign>();
    public DbSet<EvaluationKpi> EvaluationKpis => Set<EvaluationKpi>();
    public DbSet<EvaluationKpiBand> EvaluationKpiBands => Set<EvaluationKpiBand>();
    public DbSet<EvaluationScoreBand> EvaluationScoreBands => Set<EvaluationScoreBand>();
    public DbSet<EvaluationDepartment> EvaluationDepartments => Set<EvaluationDepartment>();
    public DbSet<EvaluationCampaignDepartment> EvaluationCampaignDepartments => Set<EvaluationCampaignDepartment>();
    public DbSet<EvaluationQuestion> EvaluationQuestions => Set<EvaluationQuestion>();
    public DbSet<EvaluationQuestionDepartment> EvaluationQuestionDepartments => Set<EvaluationQuestionDepartment>();
    public DbSet<EvaluationCarrier> EvaluationCarriers => Set<EvaluationCarrier>();
    public DbSet<EvaluationSnapshot> EvaluationSnapshots => Set<EvaluationSnapshot>();
    public DbSet<EvaluationSnapshotMetric> EvaluationSnapshotMetrics => Set<EvaluationSnapshotMetric>();
    public DbSet<EvaluationManualScore> EvaluationManualScores => Set<EvaluationManualScore>();
    public DbSet<EvaluationEvaluator> EvaluationEvaluators => Set<EvaluationEvaluator>();
    public DbSet<EvaluationInvitation> EvaluationInvitations => Set<EvaluationInvitation>();
    public DbSet<EvaluationResponse> EvaluationResponses => Set<EvaluationResponse>();
    public DbSet<EvaluationAnswer> EvaluationAnswers => Set<EvaluationAnswer>();
    public DbSet<EvaluationResult> EvaluationResults => Set<EvaluationResult>();
    public DbSet<EvaluationDepartmentScore> EvaluationDepartmentScores => Set<EvaluationDepartmentScore>();
    public DbSet<SupplierCapacity> SupplierCapacities => Set<SupplierCapacity>();
    public DbSet<VehicleTypeRow> VehicleTypes => Set<VehicleTypeRow>();
    public DbSet<TypeMigrationBackup> TypeMigrationBackups => Set<TypeMigrationBackup>();
    /// <summary>Dropdown cells the rules propose to change, waiting for the job's owner (22 Sep 2026).</summary>
    public DbSet<JobCorrection> JobCorrections => Set<JobCorrection>();
    public DbSet<SupplierEvaluation> SupplierEvaluations => Set<SupplierEvaluation>();

    public DbSet<FuelBand> FuelBands => Set<FuelBand>();
    public DbSet<RateLane> RateLanes => Set<RateLane>();

    /// <summary>The request side of the rate book: what was asked, and of whom.</summary>
    public DbSet<JourneyDistance> JourneyDistances => Set<JourneyDistance>();
    public DbSet<QuoteVehicleRate> QuoteVehicleRates => Set<QuoteVehicleRate>();
    public DbSet<QuoteExtra> QuoteExtras => Set<QuoteExtra>();
    public DbSet<QuoteSetting> QuoteSettings => Set<QuoteSetting>();
    public DbSet<RateInquiry> RateInquiries => Set<RateInquiry>();
    public DbSet<RateInquiryLane> RateInquiryLanes => Set<RateInquiryLane>();
    public DbSet<RateInquiryPrice> RateInquiryPrices => Set<RateInquiryPrice>();
    public DbSet<RatePrice> RatePrices => Set<RatePrice>();
    public DbSet<RateSurcharge> RateSurcharges => Set<RateSurcharge>();
    public DbSet<ReportArchiveEntry> ReportArchive => Set<ReportArchiveEntry>();

    // A customer's own paperwork. Separate tables from the ones above on
    // purpose — see the note at the top of CustomerDocumentEntities.cs.
    public DbSet<CustomerRateBand> CustomerRateBands => Set<CustomerRateBand>();
    public DbSet<CustomerRateLane> CustomerRateLanes => Set<CustomerRateLane>();
    public DbSet<CustomerRatePrice> CustomerRatePrices => Set<CustomerRatePrice>();
    public DbSet<CargoFormTemplate> CargoFormTemplates => Set<CargoFormTemplate>();

    public DbSet<AiTool> AiTools => Set<AiTool>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<ReportUpload> ReportUploads => Set<ReportUpload>();
    public DbSet<OperationUpload> OperationUploads => Set<OperationUpload>();
    public DbSet<OperationEntry> OperationEntries => Set<OperationEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        AiAuditLog.Configure(model);
        AiOperationsControl.Configure(model);
        AiAgentConfig.Configure(model);
        AiDecision.Configure(model);
        CarrierApiClient.Configure(model);
        CarrierApiRequest.Configure(model);
        CarrierWebhook.Configure(model);
        CarrierWebhookDelivery.Configure(model);
        CarrierBillingFoundationModel.Configure(model);
        CarrierBillingModel.Configure(model);
        CarrierBillingValidationModel.Configure(model);
        CarrierBillingOriginalModel.Configure(model);
        CarrierBillingFinanceModel.Configure(model);
        CarrierJobRequest.Configure(model);
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
            entry.Property(e => e.EventAt).HasColumnName("event_at");
            // SCMOS for everything written before LINE existed, which is what
            // those rows are.
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(12).HasDefaultValue(EventSource.Scmos);
            entry.Property(e => e.LineEventId).HasColumnName("line_event_id").HasDefaultValue(0L);
            // Reading a job's workflow means reading its events newest first.
            entry.HasIndex(e => new { e.JobKey, e.Id }).HasDatabaseName("workflow_events_job_idx");
        });

        /* ------------------------------------------------------------- LINE */

        model.Entity<DieselPrice>(entry =>
        {
            entry.ToTable("diesel_prices");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.EffectiveDate).HasColumnName("effective_date").HasMaxLength(10);
            entry.Property(e => e.Price).HasColumnName("price").HasPrecision(6, 2);
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.RecordedBy).HasColumnName("recorded_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            // One price per day, enforced by the database. Two rows for one day
            // would be two answers to what the month averaged.
            entry.HasIndex(e => e.EffectiveDate).IsUnique().HasDatabaseName("diesel_prices_date_idx");
        });

        model.Entity<LineGroup>(entry =>
        {
            entry.ToTable("line_groups");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.LineGroupId).HasColumnName("line_group_id").HasMaxLength(64);
            entry.Property(e => e.GroupName).HasColumnName("group_name").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id").HasDefaultValue(0L);
            entry.Property(e => e.GroupType).HasColumnName("group_type").HasMaxLength(16).HasDefaultValue(LineGroupType.Vendor);
            entry.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // One row per group, enforced by the database. The mapping is the
            // authorisation, and two rows for one group would be two answers to
            // "whose work is this".
            entry.HasIndex(e => e.LineGroupId).IsUnique().HasDatabaseName("line_groups_id_idx");
        });

        model.Entity<LineUser>(entry =>
        {
            entry.ToTable("line_users");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.LineUserId).HasColumnName("line_user_id").HasMaxLength(64);
            entry.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id").HasDefaultValue(0L);
            entry.Property(e => e.StaffId).HasColumnName("staff_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Role).HasColumnName("role").HasMaxLength(24).HasDefaultValue("UNKNOWN");
            entry.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => e.LineUserId).IsUnique().HasDatabaseName("line_users_id_idx");
        });

        model.Entity<LineEvent>(entry =>
        {
            entry.ToTable("line_events");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.WebhookEventId).HasColumnName("webhook_event_id").HasMaxLength(64).HasDefaultValue("");
            entry.Property(e => e.LineMessageId).HasColumnName("line_message_id").HasMaxLength(64);
            entry.Property(e => e.LineGroupId).HasColumnName("line_group_id").HasMaxLength(64).HasDefaultValue("");
            entry.Property(e => e.LineUserId).HasColumnName("line_user_id").HasMaxLength(64).HasDefaultValue("");
            entry.Property(e => e.MessageType).HasColumnName("message_type").HasMaxLength(24).HasDefaultValue("");
            entry.Property(e => e.RawText).HasColumnName("raw_text").HasMaxLength(4000).HasDefaultValue("");
            // No length: a webhook body is whatever LINE sends, and truncating
            // the only copy of the evidence defeats the point of keeping it.
            entry.Property(e => e.RawPayload).HasColumnName("raw_payload").HasDefaultValue("");
            entry.Property(e => e.ReceivedAt).HasColumnName("received_at");
            entry.Property(e => e.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(16).HasDefaultValue(LineProcessing.Received);
            entry.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entry.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.JobNumber).HasColumnName("job_number").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.ParsedStatus).HasColumnName("parsed_status").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.Confidence).HasColumnName("confidence").HasDefaultValue(0d);
            entry.Property(e => e.MatchedRules).HasColumnName("matched_rules").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.Warnings).HasColumnName("warnings").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.ImageKey).HasColumnName("image_key").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.ImageReading).HasColumnName("image_reading").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.ImageNote).HasColumnName("image_note").HasMaxLength(500).HasDefaultValue("");

            // Idempotency, enforced by the database rather than by a check the
            // worker might skip. LINE retries a webhook it did not get a fast
            // enough answer to, so the same message will arrive twice.
            entry.HasIndex(e => e.LineMessageId).IsUnique().HasDatabaseName("line_events_message_idx");
            // What the worker asks for: the oldest thing not yet dealt with.
            entry.HasIndex(e => new { e.ProcessingStatus, e.ReceivedAt }).HasDatabaseName("line_events_queue_idx");
            // And what the review screen asks for.
            entry.HasIndex(e => new { e.LineGroupId, e.ReceivedAt }).HasDatabaseName("line_events_group_idx");
        });

        /* ---------------------------------------------------- the mailbox */

        model.Entity<Mailbox>(entry =>
        {
            entry.ToTable("mailboxes");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Address).HasColumnName("address").HasMaxLength(MailText.Address);
            entry.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(MailText.PersonName).HasDefaultValue("");
            entry.Property(e => e.GraphUserId).HasColumnName("graph_user_id").HasMaxLength(64).HasDefaultValue("");
            entry.Property(e => e.FolderId).HasColumnName("folder_id").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(false);
            entry.Property(e => e.LastSyncedAt).HasColumnName("last_synced_at");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // One row per address. Two would be two places the same mail lands,
            // and a message would be stored, extracted and linked twice.
            entry.HasIndex(e => e.Address).IsUnique().HasDatabaseName("mailboxes_address_idx");
        });

        model.Entity<Email>(entry =>
        {
            entry.ToTable("emails");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.MailboxId).HasColumnName("mailbox_id");
            entry.Property(e => e.GraphMessageId).HasColumnName("graph_message_id").HasMaxLength(MailText.GraphId);
            entry.Property(e => e.ConversationId).HasColumnName("conversation_id").HasMaxLength(MailText.GraphId).HasDefaultValue("");
            entry.Property(e => e.InternetMessageId).HasColumnName("internet_message_id").HasMaxLength(MailText.GraphId).HasDefaultValue("");
            entry.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(MailText.Subject).HasDefaultValue("");
            entry.Property(e => e.FromAddress).HasColumnName("from_address").HasMaxLength(MailText.Address).HasDefaultValue("");
            entry.Property(e => e.FromName).HasColumnName("from_name").HasMaxLength(MailText.PersonName).HasDefaultValue("");
            // No length on either body. A shipping line's mail is whatever they
            // sent, and truncating the only copy would take the evidence out of
            // the record a person reviews a link against.
            entry.Property(e => e.BodyText).HasColumnName("body_text").HasDefaultValue("");
            entry.Property(e => e.BodyHtml).HasColumnName("body_html").HasDefaultValue("");
            entry.Property(e => e.SentAt).HasColumnName("sent_at");
            entry.Property(e => e.ReceivedAt).HasColumnName("received_at");
            entry.Property(e => e.HasAttachments).HasColumnName("has_attachments").HasDefaultValue(false);
            entry.Property(e => e.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(16).HasDefaultValue(MailProcessing.Received);
            entry.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entry.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");

            // The whole of idempotency, exactly as the specification asks. Graph
            // redelivers a notification it did not get a prompt answer to, so
            // the same message arrives more than once.
            entry.HasIndex(e => new { e.MailboxId, e.GraphMessageId }).IsUnique().HasDatabaseName("emails_message_idx");
            // What the worker asks for: the oldest thing not yet dealt with.
            entry.HasIndex(e => new { e.ProcessingStatus, e.ReceivedAt }).HasDatabaseName("emails_queue_idx");
            // And what the inbox screen asks for.
            entry.HasIndex(e => new { e.MailboxId, e.ReceivedAt }).HasDatabaseName("emails_inbox_idx");
            entry.HasIndex(e => e.ConversationId).HasDatabaseName("emails_conversation_idx");
        });

        model.Entity<EmailParticipant>(entry =>
        {
            entry.ToTable("email_participants");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.EmailId).HasColumnName("email_id");
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(8).HasDefaultValue(MailParticipant.To);
            entry.Property(e => e.Address).HasColumnName("address").HasMaxLength(MailText.Address);
            entry.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(MailText.PersonName).HasDefaultValue("");
            entry.HasIndex(e => e.EmailId).HasDatabaseName("email_participants_email_idx");
            // "Which messages went to this customer" is the question this table
            // exists to answer.
            entry.HasIndex(e => e.Address).HasDatabaseName("email_participants_address_idx");
        });

        model.Entity<EmailAttachment>(entry =>
        {
            entry.ToTable("email_attachments");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.EmailId).HasColumnName("email_id");
            entry.Property(e => e.GraphAttachmentId).HasColumnName("graph_attachment_id").HasMaxLength(MailText.GraphId).HasDefaultValue("");
            entry.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(MailText.FileName).HasDefaultValue("");
            entry.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(MailText.ContentType).HasDefaultValue("");
            entry.Property(e => e.SizeBytes).HasColumnName("size_bytes").HasDefaultValue(0L);
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(16).HasDefaultValue(MailAttachments.Kind.File);
            entry.Property(e => e.StoredDocumentId).HasColumnName("stored_document_id").HasDefaultValue(0L);
            entry.Property(e => e.FetchAttempts).HasColumnName("fetch_attempts").HasDefaultValue(0);
            entry.Property(e => e.FetchError).HasColumnName("fetch_error").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.FetchedAt).HasColumnName("fetched_at");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.HasIndex(e => e.EmailId).HasDatabaseName("email_attachments_email_idx");
            // What the fetch pass asks for every fifteen seconds: the ones with
            // no document yet that have attempts left. Without this it is a
            // scan of every attachment the system has ever recorded.
            entry.HasIndex(e => new { e.StoredDocumentId, e.FetchAttempts })
                .HasDatabaseName("email_attachments_pending_idx");
            // A re-fetch of the same attachment must not store it twice.
            entry.HasIndex(e => new { e.EmailId, e.GraphAttachmentId }).IsUnique().HasDatabaseName("email_attachments_graph_idx");
        });

        model.Entity<EmailEntity>(entry =>
        {
            entry.ToTable("email_entities");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.EmailId).HasColumnName("email_id");
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(24);
            entry.Property(e => e.Value).HasColumnName("value").HasMaxLength(120);
            entry.Property(e => e.InSubject).HasColumnName("in_subject").HasDefaultValue(false);
            entry.Property(e => e.Labelled).HasColumnName("labelled").HasDefaultValue(false);
            entry.Property(e => e.WellFormed).HasColumnName("well_formed").HasDefaultValue(true);
            entry.HasIndex(e => e.EmailId).HasDatabaseName("email_entities_email_idx");
            // The matcher's own question: which messages name this container.
            entry.HasIndex(e => new { e.Kind, e.Value }).HasDatabaseName("email_entities_value_idx");
            // One find of one kind per message. The extractor already reports a
            // value once; this is the database saying so too.
            entry.HasIndex(e => new { e.EmailId, e.Kind, e.Value }).IsUnique().HasDatabaseName("email_entities_once_idx");
        });

        model.Entity<EmailJobLink>(entry =>
        {
            entry.ToTable("email_job_links");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.EmailId).HasColumnName("email_id");
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.MatchedOn).HasColumnName("matched_on").HasMaxLength(24).HasDefaultValue("");
            entry.Property(e => e.MatchedValue).HasColumnName("matched_value").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Confidence).HasColumnName("confidence").HasDefaultValue(0d);
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(MailLink.Suggested);
            entry.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.HasIndex(e => e.EmailId).HasDatabaseName("email_job_links_email_idx");
            // "What mail is there about this job" — the reason the job screen
            // will want this table at all.
            entry.HasIndex(e => e.JobKey).HasDatabaseName("email_job_links_job_idx");
            // One link between a message and a job. A second row would let the
            // same message be both suggested and rejected against one job.
            entry.HasIndex(e => new { e.EmailId, e.JobKey }).IsUnique().HasDatabaseName("email_job_links_once_idx");
        });

        model.Entity<GraphSubscription>(entry =>
        {
            entry.ToTable("graph_subscriptions");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.MailboxId).HasColumnName("mailbox_id");
            entry.Property(e => e.SubscriptionId).HasColumnName("subscription_id").HasMaxLength(120);
            entry.Property(e => e.Resource).HasColumnName("resource").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.NotificationUrl).HasColumnName("notification_url").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.ClientState).HasColumnName("client_state").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entry.Property(e => e.LastRenewedAt).HasColumnName("last_renewed_at");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(MailSubscription.Active);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.HasIndex(e => e.SubscriptionId).IsUnique().HasDatabaseName("graph_subscriptions_id_idx");
            // What the renewal loop asks for: the live ones, soonest to die first.
            entry.HasIndex(e => new { e.Status, e.ExpiresAt }).HasDatabaseName("graph_subscriptions_renew_idx");
        });

        model.Entity<SupplierRequest>(entry =>
        {
            entry.ToTable("supplier_requests");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.Rank).HasColumnName("rank");
            entry.Property(e => e.Carrier).HasColumnName("carrier").HasMaxLength(120);
            entry.Property(e => e.QuotedPrice).HasColumnName("quoted_price");
            entry.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(20).HasDefaultValue("pending");
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.ReasonCode).HasColumnName("reason_code").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.Remark).HasColumnName("remark").HasMaxLength(500).HasDefaultValue("");
            entry.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(120);
            entry.Property(e => e.RequestedAt).HasColumnName("requested_at");
            entry.Property(e => e.RespondedAt).HasColumnName("responded_at");
            entry.Property(e => e.RespondedBy).HasColumnName("responded_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.PreviousRequestId).HasColumnName("previous_request_id");
            entry.Property(e => e.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.Ignore(e => e.ResponseMinutes);
            entry.HasIndex(e => new { e.JobKey, e.Rank }).HasDatabaseName("supplier_requests_job_idx");
            entry.HasIndex(e => new { e.Carrier, e.Outcome }).HasDatabaseName("supplier_requests_carrier_idx");
            entry.HasIndex(e => e.JobKey).IsUnique()
                .HasFilter("[outcome] IN ('pending','confirmed')")
                .HasDatabaseName("supplier_requests_one_active_job_idx");
            entry.HasIndex(e => new { e.SupplierId, e.Outcome })
                .HasDatabaseName("supplier_requests_supplier_outcome_idx");
            entry.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_supplier_requests_suppliers_supplier_id");
            entry.HasOne<SupplierRequest>().WithMany().HasForeignKey(e => e.PreviousRequestId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_supplier_requests_previous_request");
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

        model.Entity<JourneyDistance>(entry =>
        {
            entry.ToTable("journey_distances");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Key).HasColumnName("journey_key").HasMaxLength(420).HasDefaultValue("");
            entry.Property(e => e.FromPlace).HasColumnName("from_place").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.ToPlace).HasColumnName("to_place").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.Km).HasColumnName("km");
            entry.Property(e => e.SetBy).HasColumnName("set_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.SetAt).HasColumnName("set_at");
            entry.Property(e => e.UsedCount).HasColumnName("used_count");
            // One road, one length. Two rows for the same journey is the very
            // thing the key exists to stop.
            entry.HasIndex(e => e.Key).IsUnique().HasDatabaseName("journey_key_idx");
        });

        model.Entity<ReportArchiveEntry>(entry =>
        {
            entry.ToTable("report_archive");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Month).HasColumnName("month").HasMaxLength(7).HasDefaultValue("");
            entry.Property(e => e.TakenAt).HasColumnName("taken_at");
            entry.Property(e => e.TakenBy).HasColumnName("taken_by").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.Document).HasColumnName("document");
            entry.Property(e => e.Trips).HasColumnName("trips");
            entry.Property(e => e.Measurable).HasColumnName("measurable");
            // Nullable on purpose: a zero would sort every unmeasured month as
            // the worst month on record.
            entry.Property(e => e.Otd).HasColumnName("otd");
            // One snapshot per customer per month. The scheduler is allowed to
            // run twice — App Service recycles, and a month must not be
            // archived twice because a container restarted at midnight.
            entry.HasIndex(e => new { e.Customer, e.Month }).IsUnique()
                .HasDatabaseName("report_archive_month_idx");
        });

        model.Entity<QuoteVehicleRate>(entry =>
        {
            entry.ToTable("quote_vehicle_rates");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Code).HasColumnName("code").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Label).HasColumnName("label").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.PerKm).HasColumnName("per_km");
            entry.Property(e => e.BaseCharge).HasColumnName("base_charge");
            entry.Property(e => e.Chill).HasColumnName("chill").HasPrecision(6, 3).HasDefaultValue(1m);
            entry.Property(e => e.DangerousGoods).HasColumnName("dangerous_goods");
            entry.Property(e => e.Position).HasColumnName("position");
            // One row per vehicle. Two rows for the same truck is two prices for
            // one journey, which is the thing this card exists to stop.
            entry.HasIndex(e => e.Code).IsUnique().HasDatabaseName("quote_vehicle_code_idx");
        });

        model.Entity<QuoteExtra>(entry =>
        {
            entry.ToTable("quote_extras");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.Label).HasColumnName("label").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.Basis).HasColumnName("basis").HasMaxLength(20).HasDefaultValue("flat");
            entry.Property(e => e.Rate).HasColumnName("rate").HasPrecision(12, 2);
            entry.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
            entry.Property(e => e.Position).HasColumnName("position");
        });

        model.Entity<QuoteSetting>(entry =>
        {
            entry.ToTable("quote_settings");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.MarginPercent).HasColumnName("margin_percent").HasPrecision(6, 3).HasDefaultValue(10m);
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
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
            entry.Property(e => e.Domestic).HasColumnName("domestic").HasDefaultValue(false);
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
            entry.Property(e => e.Driver).HasColumnName("driver").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.ContainerNo).HasColumnName("container_no").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.Licence).HasColumnName("licence").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.AccidentGrade).HasColumnName("accident_grade").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.ScorecardColumn).HasColumnName("scorecard_column").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.CaseId).HasColumnName("case_id");
            entry.HasIndex(e => e.CaseId).HasDatabaseName("operational_issue_case_idx");
            entry.HasOne<IncidentCase>().WithMany().HasForeignKey(e => e.CaseId).OnDelete(DeleteBehavior.Restrict);
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

        model.Entity<CustomerTrainingRecord>(entry =>
        {
            entry.ToTable("customer_training_records");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.SequenceNo).HasColumnName("sequence_no").HasMaxLength(40).HasDefaultValue("");
            entry.Property(e => e.CourseCustomer).HasColumnName("course_customer").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(160).HasDefaultValue("");
            entry.Property(e => e.Company).HasColumnName("company").HasMaxLength(240).HasDefaultValue("");
            entry.Property(e => e.DriverLicenseNo).HasColumnName("driver_license_no").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.LicenseType).HasColumnName("license_type").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.EffectiveDate).HasColumnName("effective_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.ExpiryDate).HasColumnName("expiry_date").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(e => e.ExpiryDate).HasDatabaseName("customer_training_expiry_idx");
            entry.HasIndex(e => e.DriverLicenseNo).HasDatabaseName("customer_training_license_idx");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.HasIndex(e => e.SupplierId).HasDatabaseName("customer_training_supplier_idx");
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
            entry.Property(e => e.BillingCaseId).HasColumnName("billing_case_id");
            entry.Property(e => e.BillingInvoiceId).HasColumnName("billing_invoice_id");
            entry.Property(e => e.IssueId).HasColumnName("issue_id");
            entry.Property(e => e.DriverId).HasColumnName("driver_id");
            entry.Property(e => e.TruckId).HasColumnName("truck_id");
            entry.Property(e => e.FleetDriverId).HasColumnName("fleet_driver_id");
            entry.Property(e => e.ActionPlanId).HasColumnName("action_plan_id");
            entry.Property(e => e.ActionPlanItemId).HasColumnName("action_plan_item_id");
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
            entry.HasIndex(e => e.BillingCaseId).HasDatabaseName("document_billing_case_idx");
            entry.HasIndex(e => e.BillingInvoiceId).HasDatabaseName("document_billing_invoice_idx");
            entry.HasIndex(e => e.IssueId).HasDatabaseName("document_issue_idx");
            entry.HasIndex(e => e.TruckId).HasDatabaseName("document_truck_idx");
            entry.HasIndex(e => e.FleetDriverId).HasDatabaseName("document_fleet_driver_idx");
            entry.HasIndex(e => e.ActionPlanId).HasDatabaseName("document_action_plan_idx");
            entry.HasOne<BillingCase>().WithMany().HasForeignKey(e => e.BillingCaseId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_documents_billing_cases_billing_case_id");
            entry.HasOne<BillingInvoice>().WithMany().HasForeignKey(e => e.BillingInvoiceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_documents_billing_invoices_billing_invoice_id");
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
            // Sixty is what SignIn.Describe is bounded to; a directory is free
            // to name more methods than anybody expects, and the rule truncates
            // rather than letting the insert fail on somebody's rate change.
            entry.Property(e => e.SignInMethod).HasColumnName("sign_in_method")
                .HasMaxLength(60).HasDefaultValue("");
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

            // The ASL/BSL list. Widths are the longest value in the file with
            // room to spare, because the file is re-exported from ABS and the
            // next export will have a longer address in it than this one did.
            e.Property(x => x.AbsNo).HasMaxLength(32).HasDefaultValue("");
            e.Property(x => x.ListType).HasMaxLength(8).HasDefaultValue("");
            e.Property(x => x.LegalName).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.ContactPerson).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Telephone).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Fax).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Email).HasMaxLength(250).HasDefaultValue("");
            e.Property(x => x.Website).HasMaxLength(250).HasDefaultValue("");
            e.Property(x => x.CreditTerm).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.ServicesRequired).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.MainSpType).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.TypeOfService).HasMaxLength(300).HasDefaultValue("");
            // Existing rows are carriers: every supplier in the register before
            // this import got there by carrying work. Defaulting to false would
            // empty the scorecard on the day the column was added.
            e.Property(x => x.IsCarrier).HasDefaultValue(true);

            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("suppliers_code_idx");
            e.HasIndex(x => x.Name).HasDatabaseName("suppliers_name_idx");
            // The join back to ABS, and what the import matches on first.
            e.HasIndex(x => x.AbsNo).HasDatabaseName("suppliers_abs_no_idx");
            // "Our carriers" is the question most screens ask of this table.
            e.HasIndex(x => x.IsCarrier).HasDatabaseName("suppliers_carrier_idx");
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
            e.Property(x => x.Kind).HasMaxLength(10).HasDefaultValue(FleetDocuments.Head);
            e.Property(x => x.CreatedBy).HasMaxLength(120).HasDefaultValue("");
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
            e.Property(x => x.CreatedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("supplier_driver_idx");
        });

        // The EHS audit plan and a new subcontractor's onboarding checklist (1 Oct 2026).
        model.Entity<AuditPlan>(e =>
        {
            e.ToTable("audit_plans");
            e.Property(x => x.Title).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.PreparedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ReviewedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ReviewedDate).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.SecondReviewedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.ApprovedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Revision).HasMaxLength(20).HasDefaultValue("00");
            e.Property(x => x.UpdatedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => x.Year).IsUnique().HasDatabaseName("audit_plan_year_idx");
        });

        model.Entity<AuditPlanItem>(e =>
        {
            e.ToTable("audit_plan_items");
            e.Property(x => x.Kind).HasMaxLength(20).HasDefaultValue(AuditPlanRules.ReAudit);
            e.Property(x => x.Company).HasMaxLength(240).HasDefaultValue("");
            e.Property(x => x.Target).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.PersonInCharge).HasMaxLength(400).HasDefaultValue("");
            e.Property(x => x.AuditDate).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Schedule).HasMaxLength(20).HasDefaultValue(AuditPlanRules.Fixed);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(AuditPlanRules.Planned);
            e.Property(x => x.NextDate).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Remark).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.FindingSentDate).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.ReportSentDate).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.CreatedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.UpdatedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => x.Year).HasDatabaseName("audit_plan_item_year_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("audit_plan_item_supplier_idx");
        });

        model.Entity<SupplierOnboardingItem>(e =>
        {
            e.ToTable("supplier_onboarding_items");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(VendorOnboarding.Pending);
            e.Property(x => x.Remark).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.UpdatedBy).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(x => new { x.SupplierId, x.Code }).IsUnique().HasDatabaseName("supplier_onboarding_idx");
        });

        // Subcontract Management's Action Plan (1 Oct 2026).
        model.Entity<ActionPlanType>(e =>
        {
            e.ToTable("action_plan_types");
            e.Property(x => x.DevelopmentType).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.DevelopmentType, x.Name }).IsUnique().HasDatabaseName("action_plan_type_name_idx");
        });

        model.Entity<ActionPlan>(e =>
        {
            e.ToTable("action_plans");
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique().HasDatabaseName("action_plan_number_idx");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.DevelopmentType).HasMaxLength(20);
            e.Property(x => x.Category).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Period).HasMaxLength(20).HasDefaultValue("annual");
            e.Property(x => x.Department).HasMaxLength(120).HasDefaultValue("Subcontract Management");
            e.Property(x => x.Priority).HasMaxLength(20).HasDefaultValue("medium");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(ActionPlanRules.Draft);
            e.Property(x => x.Description).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.Objective).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.ExpectedOutcome).HasMaxLength(2000).HasDefaultValue("");
            foreach (var date in new[] { nameof(ActionPlan.StartDate), nameof(ActionPlan.TargetDate), nameof(ActionPlan.ActualCompletionDate), nameof(ActionPlan.ReviewDate) })
                e.Property(date).HasMaxLength(20).HasDefaultValue("");
            foreach (var text in new[] { nameof(ActionPlan.OwnerId), nameof(ActionPlan.EmployeeId), nameof(ActionPlan.TargetType) })
                e.Property(text).HasMaxLength(60).HasDefaultValue("");
            foreach (var text in new[] { nameof(ActionPlan.OwnerName), nameof(ActionPlan.EmployeeName), nameof(ActionPlan.Position), nameof(ActionPlan.Team),
                nameof(ActionPlan.Supervisor), nameof(ActionPlan.TargetName), nameof(ActionPlan.DevelopmentArea), nameof(ActionPlan.CurrentLevel),
                nameof(ActionPlan.TargetLevel), nameof(ActionPlan.Method), nameof(ActionPlan.Coach), nameof(ActionPlan.CarrierContact),
                nameof(ActionPlan.EvaluationMethod), nameof(ActionPlan.Metric), nameof(ActionPlan.CreatedBy), nameof(ActionPlan.UpdatedBy) })
                e.Property(text).HasMaxLength(200).HasDefaultValue("");
            foreach (var text in new[] { nameof(ActionPlan.Gap), nameof(ActionPlan.RootCause), nameof(ActionPlan.Result), nameof(ActionPlan.CancelReason) })
                e.Property(text).HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.Baseline).HasPrecision(12, 2);
            e.Property(x => x.TargetValue).HasPrecision(12, 2);
            e.Property(x => x.ActualValue).HasPrecision(12, 2);
            e.HasIndex(x => x.Year).HasDatabaseName("action_plan_year_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("action_plan_supplier_idx");
            e.HasIndex(x => x.EmployeeId).HasDatabaseName("action_plan_employee_idx");
        });

        model.Entity<ActionPlanItem>(e =>
        {
            e.ToTable("action_plan_items");
            e.Property(x => x.Action).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(2000).HasDefaultValue("");
            foreach (var text in new[] { nameof(ActionPlanItem.OwnerName), nameof(ActionPlanItem.SupportingPerson), nameof(ActionPlanItem.SupportingDepartment),
                nameof(ActionPlanItem.TrainingTitle), nameof(ActionPlanItem.TrainingType), nameof(ActionPlanItem.Trainer), nameof(ActionPlanItem.TrainingProvider),
                nameof(ActionPlanItem.CreatedBy), nameof(ActionPlanItem.UpdatedBy) })
                e.Property(text).HasMaxLength(200).HasDefaultValue("");
            foreach (var text in new[] { nameof(ActionPlanItem.ExpectedResult), nameof(ActionPlanItem.ActualResult), nameof(ActionPlanItem.Remark), nameof(ActionPlanItem.Participants) })
                e.Property(text).HasMaxLength(1000).HasDefaultValue("");
            foreach (var date in new[] { nameof(ActionPlanItem.StartDate), nameof(ActionPlanItem.TargetDate), nameof(ActionPlanItem.ActualCompletionDate),
                nameof(ActionPlanItem.TrainingDate), nameof(ActionPlanItem.CertificateExpiry) })
                e.Property(date).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.OwnerId).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.Priority).HasMaxLength(20).HasDefaultValue("medium");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(ActionPlanRules.Planned);
            e.HasIndex(x => x.PlanId).HasDatabaseName("action_plan_item_plan_idx");
            e.HasOne<ActionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ActionPlanUpdate>(e =>
        {
            e.ToTable("action_plan_updates");
            e.Property(x => x.Comment).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.StatusBefore).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.StatusAfter).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.CreatedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => x.PlanId).HasDatabaseName("action_plan_update_plan_idx");
            e.HasOne<ActionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ActionPlanReview>(e =>
        {
            e.ToTable("action_plan_reviews");
            e.Property(x => x.SubmittedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Reviewer).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Result).HasMaxLength(30).HasDefaultValue("");
            e.Property(x => x.Comment).HasMaxLength(2000).HasDefaultValue("");
            e.HasIndex(x => x.PlanId).HasDatabaseName("action_plan_review_plan_idx");
            e.HasOne<ActionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ActionPlanScore>(e =>
        {
            e.ToTable("action_plan_scores");
            e.Property(x => x.Dimension).HasMaxLength(60);
            e.Property(x => x.AssessedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => x.PlanId).HasDatabaseName("action_plan_score_plan_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("action_plan_score_supplier_idx");
            e.HasOne<ActionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        // The Skill Matrix (1 Oct 2026, Action Plan round two).
        model.Entity<ActionPlanSkill>(e =>
        {
            e.ToTable("action_plan_skills");
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.Category, x.Name }).IsUnique().HasDatabaseName("action_plan_skill_name_idx");
        });

        model.Entity<SkillAssessment>(e =>
        {
            e.ToTable("skill_assessments");
            e.Property(x => x.EmployeeId).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.AssessedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.EmployeeId, x.SkillId }).HasDatabaseName("skill_assessment_employee_idx");
            e.HasOne<ActionPlanSkill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        // Annual Carrier Evaluation (1 Oct 2026).
        model.Entity<EvaluationCampaign>(e =>
        {
            e.ToTable("evaluation_campaigns");
            e.Property(x => x.Code).HasMaxLength(30);
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("evaluation_campaign_code_idx");
            e.HasIndex(x => x.Year).HasDatabaseName("evaluation_campaign_year_idx");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.SystemWeight).HasPrecision(7, 4);
            e.Property(x => x.HumanWeight).HasPrecision(7, 4);
            e.Property(x => x.MinimumSystemCoverage).HasPrecision(7, 4);
            e.Property(x => x.Status).HasMaxLength(30).HasDefaultValue(AnnualEvaluationRules.Draft);
            e.Property(x => x.LockedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.CreatedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.UpdatedBy).HasMaxLength(200).HasDefaultValue("");
        });

        model.Entity<EvaluationKpi>(e =>
        {
            e.ToTable("evaluation_kpis");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.NameTh).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Weight).HasPrecision(7, 4);
            e.Property(x => x.FallbackScore).HasPrecision(7, 4);
            e.Property(x => x.Method).HasMaxLength(20).HasDefaultValue(AnnualEvaluationRules.Band);
            e.Property(x => x.Direction).HasMaxLength(20).HasDefaultValue(AnnualEvaluationRules.Higher);
            e.Property(x => x.Measure).HasMaxLength(500).HasDefaultValue("");
            e.HasIndex(x => new { x.CampaignId, x.Code }).IsUnique().HasDatabaseName("evaluation_kpi_code_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationKpiBand>(e =>
        {
            e.ToTable("evaluation_kpi_bands");
            e.Property(x => x.Threshold).HasPrecision(12, 4);
            e.Property(x => x.Score).HasPrecision(7, 4);
            e.HasIndex(x => x.KpiId).HasDatabaseName("evaluation_kpi_band_kpi_idx");
            e.HasOne<EvaluationKpi>().WithMany().HasForeignKey(x => x.KpiId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationScoreBand>(e =>
        {
            e.ToTable("evaluation_score_bands");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Label).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.MinScore).HasPrecision(7, 4);
            e.HasIndex(x => new { x.CampaignId, x.Code }).IsUnique().HasDatabaseName("evaluation_score_band_code_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationDepartment>(e =>
        {
            e.ToTable("evaluation_departments");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Active).HasDefaultValue(true);
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("evaluation_department_code_idx");
        });

        model.Entity<EvaluationCampaignDepartment>(e =>
        {
            e.ToTable("evaluation_campaign_departments");
            e.Property(x => x.Weight).HasPrecision(9, 4);
            e.HasIndex(x => new { x.CampaignId, x.DepartmentId }).IsUnique().HasDatabaseName("evaluation_campaign_department_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvaluationDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationQuestion>(e =>
        {
            e.ToTable("evaluation_questions");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Text).HasMaxLength(300);
            e.Property(x => x.TextTh).HasMaxLength(300).HasDefaultValue("");
            e.Property(x => x.Weight).HasPrecision(7, 4);
            e.HasIndex(x => new { x.CampaignId, x.Code }).IsUnique().HasDatabaseName("evaluation_question_code_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationQuestionDepartment>(e =>
        {
            e.ToTable("evaluation_question_departments");
            e.HasIndex(x => new { x.QuestionId, x.DepartmentId }).IsUnique().HasDatabaseName("evaluation_question_department_idx");
            e.HasOne<EvaluationQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvaluationDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationCarrier>(e =>
        {
            e.ToTable("evaluation_carriers");
            e.Property(x => x.ExcludedReason).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.Eligibility).HasMaxLength(30).HasDefaultValue("");
            e.Property(x => x.Decision).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.DecisionNote).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.DecidedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.AddedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.Included).HasDefaultValue(true);
            e.HasIndex(x => new { x.CampaignId, x.SupplierId }).IsUnique().HasDatabaseName("evaluation_carrier_idx");
            e.HasIndex(x => x.SupplierId).HasDatabaseName("evaluation_carrier_supplier_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationSnapshot>(e =>
        {
            e.ToTable("evaluation_snapshots");
            e.Property(x => x.Reason).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.GeneratedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.EvaluationCarrierId, x.Version }).IsUnique().HasDatabaseName("evaluation_snapshot_version_idx");
            e.HasIndex(x => x.CampaignId).HasDatabaseName("evaluation_snapshot_campaign_idx");
            e.HasOne<EvaluationCarrier>().WithMany().HasForeignKey(x => x.EvaluationCarrierId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationSnapshotMetric>(e =>
        {
            e.ToTable("evaluation_snapshot_metrics");
            e.Property(x => x.Code).HasMaxLength(60);
            e.Property(x => x.Status).HasMaxLength(30).HasDefaultValue(AnnualEvaluationRules.Available);
            e.Property(x => x.Value).HasPrecision(18, 6);
            e.Property(x => x.Numerator).HasPrecision(18, 6);
            e.Property(x => x.Denominator).HasPrecision(18, 6);
            e.Property(x => x.Formula).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.Note).HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.Sources).HasDefaultValue("[]");
            e.HasIndex(x => new { x.SnapshotId, x.Code }).IsUnique().HasDatabaseName("evaluation_snapshot_metric_idx");
            e.HasOne<EvaluationSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationManualScore>(e =>
        {
            e.ToTable("evaluation_manual_scores");
            e.Property(x => x.KpiCode).HasMaxLength(40);
            e.Property(x => x.Score).HasPrecision(7, 4);
            e.Property(x => x.Note).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.AssessedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.EvaluationCarrierId, x.KpiCode }).IsUnique().HasDatabaseName("evaluation_manual_score_idx");
            e.HasOne<EvaluationCarrier>().WithMany().HasForeignKey(x => x.EvaluationCarrierId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationEvaluator>(e =>
        {
            e.ToTable("evaluation_evaluators");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(254).HasDefaultValue("");
            e.Property(x => x.StaffId).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.Active).HasDefaultValue(true);
            e.Property(x => x.CreatedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.CampaignId, x.DepartmentId }).HasDatabaseName("evaluation_evaluator_department_idx");
            e.HasOne<EvaluationCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvaluationDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationInvitation>(e =>
        {
            e.ToTable("evaluation_invitations");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("evaluation_invitation_token_idx");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(AnnualEvaluationRules.InvitationPending);
            e.Property(x => x.RevokedBy).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.RevokeReason).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.CreatedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.CampaignId, x.Status }).HasDatabaseName("evaluation_invitation_status_idx");
            e.HasIndex(x => new { x.EvaluatorId, x.EvaluationCarrierId }).HasDatabaseName("evaluation_invitation_pair_idx");
            e.HasOne<EvaluationEvaluator>().WithMany().HasForeignKey(x => x.EvaluatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvaluationCarrier>().WithMany().HasForeignKey(x => x.EvaluationCarrierId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationResponse>(e =>
        {
            e.ToTable("evaluation_responses");
            e.Property(x => x.Comment).HasMaxLength(4000).HasDefaultValue("");
            e.HasIndex(x => x.InvitationId).IsUnique().HasDatabaseName("evaluation_response_invitation_idx");
            e.HasIndex(x => new { x.EvaluationCarrierId, x.DepartmentId }).HasDatabaseName("evaluation_response_carrier_idx");
            e.HasOne<EvaluationInvitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationAnswer>(e =>
        {
            e.ToTable("evaluation_answers");
            e.Property(x => x.Comment).HasMaxLength(2000).HasDefaultValue("");
            e.HasIndex(x => new { x.ResponseId, x.QuestionId }).IsUnique().HasDatabaseName("evaluation_answer_idx");
            e.HasOne<EvaluationResponse>().WithMany().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EvaluationQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationResult>(e =>
        {
            e.ToTable("evaluation_results");
            e.Property(x => x.SystemScore).HasPrecision(9, 4);
            e.Property(x => x.HumanScore).HasPrecision(9, 4);
            e.Property(x => x.FinalScore).HasPrecision(9, 4);
            e.Property(x => x.SystemWeightAvailable).HasPrecision(7, 4);
            e.Property(x => x.Band).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.Detail).HasDefaultValue("{}");
            e.Property(x => x.Reason).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.CalculatedBy).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => new { x.EvaluationCarrierId, x.Version }).IsUnique().HasDatabaseName("evaluation_result_version_idx");
            e.HasOne<EvaluationCarrier>().WithMany().HasForeignKey(x => x.EvaluationCarrierId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EvaluationDepartmentScore>(e =>
        {
            e.ToTable("evaluation_department_scores");
            e.Property(x => x.Score).HasPrecision(9, 4);
            e.Property(x => x.Weight).HasPrecision(9, 4);
            e.HasIndex(x => x.ResultId).HasDatabaseName("evaluation_department_score_result_idx");
            e.HasOne<EvaluationResult>().WithMany().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ActionPlanReference>(e =>
        {
            e.ToTable("action_plan_references");
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.RefId).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Label).HasMaxLength(300).HasDefaultValue("");
            e.HasIndex(x => x.PlanId).HasDatabaseName("action_plan_reference_plan_idx");
            e.HasOne<ActionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<TypeMigrationBackup>(e =>
        {
            e.ToTable("type_migration_backup");
            e.Property(x => x.JobKey).HasMaxLength(60);
            e.Property(x => x.Batch).HasMaxLength(40);
            e.Property(x => x.OldType).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.NewType).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.OldProduct).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.NewProduct).HasMaxLength(200).HasDefaultValue("");
            e.HasIndex(x => x.Batch).HasDatabaseName("type_migration_batch_idx");
        });

        model.Entity<VehicleTypeRow>(e =>
        {
            e.ToTable("vehicle_types");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Label).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.UpdatedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Active).HasDefaultValue(true);
            // One row per code. Two spellings of one lorry is the problem this
            // table exists to end, so the database refuses to hold them.
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("vehicle_type_code_idx");
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
            e.Property(x => x.Source).HasMaxLength(20).HasDefaultValue(SupplierEvaluation.ScmosSource);
            e.Property(x => x.FinalPercent).HasPrecision(7, 4);
            e.Property(x => x.Result).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.ImportedBy).HasMaxLength(200).HasDefaultValue("");
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
            e.Property(x => x.PromotedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.RotationCustomer).HasMaxLength(200);
            e.HasIndex(x => x.SupplierId).HasDatabaseName("rate_lane_supplier_idx");
            // The move looks a lane up by where it came from, once per row it
            // writes. Without this that is a scan of the whole rate book per
            // lane moved.
            e.HasIndex(x => x.FromInquiryLaneId).HasDatabaseName("rate_lane_from_inquiry_idx");
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
            e.Property(x => x.RequesterId).HasMaxLength(128).HasDefaultValue("");
            e.Property(x => x.PayloadHash).HasMaxLength(64).HasDefaultValue("");
            e.Property(x => x.CorrelationId).HasMaxLength(64).HasDefaultValue("");
            e.Property(x => x.DecidedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.DecisionNote).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.AppliedBy).HasMaxLength(120).HasDefaultValue("");
            e.Property(x => x.Result).HasMaxLength(1000).HasDefaultValue("");
            e.HasIndex(x => new { x.State, x.RequestedAt }).HasDatabaseName("approval_state_idx");
        });

        model.Entity<JobCorrection>(entry =>
        {
            entry.ToTable("job_corrections");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Batch).HasColumnName("batch").HasMaxLength(20);
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(e => e.JobCode).HasColumnName("job_code").HasMaxLength(80).HasDefaultValue("");
            entry.Property(e => e.OwnerId).HasColumnName("owner_id").HasMaxLength(20).HasDefaultValue("");
            entry.Property(e => e.Field).HasColumnName("field").HasMaxLength(20);
            entry.Property(e => e.FromValue).HasColumnName("from_value").HasMaxLength(200);
            entry.Property(e => e.ToValue).HasColumnName("to_value").HasMaxLength(200);
            entry.Property(e => e.Rule).HasColumnName("rule").HasMaxLength(40);
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(300).HasDefaultValue("");
            entry.Property(e => e.ProposedBy).HasColumnName("proposed_by").HasMaxLength(120);
            entry.Property(e => e.ProposedAt).HasColumnName("proposed_at");
            entry.Property(e => e.State).HasColumnName("state").HasMaxLength(12).HasDefaultValue(CorrectionState.Pending);
            entry.Property(e => e.DecidedBy).HasColumnName("decided_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(300).HasDefaultValue("");
            entry.HasIndex(e => new { e.State, e.JobKey }).HasDatabaseName("job_corrections_state_idx");
            // One open proposal per cell: a second run over the same cell supersedes the first, never doubles it.
            entry.HasIndex(e => new { e.JobKey, e.Field }).IsUnique().HasFilter("[state] = 'pending'").HasDatabaseName("job_corrections_pending_cell_idx");
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

        /* ------------------------------------------ a customer's own papers */

        model.Entity<CustomerRateBand>(entry =>
        {
            entry.ToTable("customer_rate_bands");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(180);
            entry.Property(e => e.Label).HasColumnName("label").HasMaxLength(60);
            entry.Property(e => e.MinPrice).HasColumnName("min_price").HasPrecision(9, 2);
            entry.Property(e => e.MaxPrice).HasColumnName("max_price").HasPrecision(9, 2);
            entry.Property(e => e.Position).HasColumnName("position");
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(8).HasDefaultValue(RateKind.Cost);
            // One card per customer per side is read at a time, and it is read
            // whole. The fuel clause is a contract term and the two sides have
            // their own — they agree today and need not tomorrow.
            entry.HasIndex(e => new { e.Customer, e.Kind, e.Position }).HasDatabaseName("customer_rate_bands_idx");
        });

        model.Entity<CustomerRateLane>(entry =>
        {
            entry.ToTable("customer_rate_lanes");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(180);
            entry.Property(e => e.Carrier).HasColumnName("carrier").HasMaxLength(180);
            entry.Property(e => e.FromPlace).HasColumnName("from_place").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.ToPlace).HasColumnName("to_place").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.PostalCode).HasColumnName("postal_code").HasMaxLength(20).HasDefaultValue("");
            // COST for everything stored before the selling card existed, which
            // is what those rows are. A default of "" would have left every one
            // of them matching neither side and vanishing off the screen.
            entry.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(8).HasDefaultValue(RateKind.Cost);
            entry.Property(e => e.CargoType).HasColumnName("cargo_type").HasMaxLength(20).HasDefaultValue("");
            // The kind joins the index: every read filters on it, and the two
            // sides of one customer's card are the same lanes twice over.
            entry.HasIndex(e => new { e.Customer, e.Carrier, e.Kind }).HasDatabaseName("customer_rate_lanes_idx");
        });

        model.Entity<CustomerRatePrice>(entry =>
        {
            entry.ToTable("customer_rate_prices");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.LaneId).HasColumnName("lane_id");
            entry.Property(e => e.Vehicle).HasColumnName("vehicle").HasMaxLength(20);
            entry.Property(e => e.BandPosition).HasColumnName("band_position");
            entry.Property(e => e.Price).HasColumnName("price");
            entry.HasIndex(e => e.LaneId).HasDatabaseName("customer_rate_prices_lane_idx");
        });

        model.Entity<CargoFormTemplate>(entry =>
        {
            entry.ToTable("cargo_form_templates");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.Customer).HasColumnName("customer").HasMaxLength(180);
            entry.Property(e => e.SourceFile).HasColumnName("source_file").HasMaxLength(260).HasDefaultValue("");
            entry.Property(e => e.Columns).HasColumnName("columns").HasMaxLength(1000).HasDefaultValue("");
            // A customer has one receipt shape, so uploading the folder twice
            // replaces rather than doubles it.
            entry.HasIndex(e => e.Customer).IsUnique().HasDatabaseName("cargo_form_templates_customer_idx");
        });
    }
}
