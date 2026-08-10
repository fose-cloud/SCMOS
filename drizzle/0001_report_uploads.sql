CREATE TABLE `report_uploads` (
	`id` text PRIMARY KEY NOT NULL,
	`period` text NOT NULL,
	`filename` text NOT NULL,
	`object_key` text NOT NULL,
	`row_count` integer DEFAULT 0 NOT NULL,
	`issue_count` integer DEFAULT 0 NOT NULL,
	`uploaded_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `report_uploads_period_idx` ON `report_uploads` (`period`,`uploaded_at`);
