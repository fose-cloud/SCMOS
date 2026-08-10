CREATE TABLE `operation_entries` (
	`id` text PRIMARY KEY NOT NULL,
	`owner_name` text NOT NULL,
	`work_date` text NOT NULL,
	`reporting_period` text NOT NULL,
	`flow` text NOT NULL,
	`customer` text NOT NULL,
	`subcontractor` text NOT NULL,
	`job_code` text NOT NULL,
	`container_no` text,
	`equipment_type` text,
	`plan_at` text NOT NULL,
	`actual_at` text,
	`operation_status` text NOT NULL,
	`validation_status` text NOT NULL,
	`otd_status` text NOT NULL,
	`remark` text,
	`submitted_by` text NOT NULL,
	`submitted_at` text NOT NULL,
	`updated_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `operation_entries_owner_date_idx` ON `operation_entries` (`owner_name`,`work_date`);
--> statement-breakpoint
CREATE INDEX `operation_entries_period_flow_idx` ON `operation_entries` (`reporting_period`,`flow`);
--> statement-breakpoint
CREATE TABLE `operation_uploads` (
	`id` text PRIMARY KEY NOT NULL,
	`upload_id` text NOT NULL,
	`owner_name` text NOT NULL,
	`flow` text NOT NULL,
	`submitted_by` text NOT NULL,
	`submitted_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `operation_uploads_owner_idx` ON `operation_uploads` (`owner_name`,`submitted_at`);
