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
