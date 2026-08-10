import { index, integer, sqliteTable, text } from "drizzle-orm/sqlite-core";

export const reportUploads = sqliteTable("report_uploads", {
  id: text("id").primaryKey(),
  period: text("period").notNull(),
  filename: text("filename").notNull(),
  objectKey: text("object_key").notNull(),
  rowCount: integer("row_count").notNull().default(0),
  issueCount: integer("issue_count").notNull().default(0),
  uploadedAt: text("uploaded_at").notNull(),
}, (table) => [index("report_uploads_period_idx").on(table.period, table.uploadedAt)]);

export const operationEntries = sqliteTable("operation_entries", {
  id: text("id").primaryKey(),
  ownerName: text("owner_name").notNull(),
  workDate: text("work_date").notNull(),
  reportingPeriod: text("reporting_period").notNull(),
  flow: text("flow").notNull(),
  customer: text("customer").notNull(),
  subcontractor: text("subcontractor").notNull(),
  jobCode: text("job_code").notNull(),
  containerNo: text("container_no"),
  equipmentType: text("equipment_type"),
  planAt: text("plan_at").notNull(),
  actualAt: text("actual_at"),
  operationStatus: text("operation_status").notNull(),
  validationStatus: text("validation_status").notNull(),
  otdStatus: text("otd_status").notNull(),
  remark: text("remark"),
  submittedBy: text("submitted_by").notNull(),
  submittedAt: text("submitted_at").notNull(),
  updatedAt: text("updated_at").notNull(),
}, (table) => [
  index("operation_entries_owner_date_idx").on(table.ownerName, table.workDate),
  index("operation_entries_period_flow_idx").on(table.reportingPeriod, table.flow),
]);

export const operationUploads = sqliteTable("operation_uploads", {
  id: text("id").primaryKey(),
  uploadId: text("upload_id").notNull(),
  ownerName: text("owner_name").notNull(),
  flow: text("flow").notNull(),
  submittedBy: text("submitted_by").notNull(),
  submittedAt: text("submitted_at").notNull(),
}, (table) => [index("operation_uploads_owner_idx").on(table.ownerName, table.submittedAt)]);
