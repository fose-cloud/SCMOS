import { env } from "cloudflare:workers";

type AppEnv = { DB: D1Database; FILES: R2Bucket };

async function ensureSchema(database: D1Database) {
  await database.batch([
    database.prepare(`CREATE TABLE IF NOT EXISTS report_uploads (
      id TEXT PRIMARY KEY,
      period TEXT NOT NULL,
      filename TEXT NOT NULL,
      object_key TEXT NOT NULL,
      row_count INTEGER NOT NULL DEFAULT 0,
      issue_count INTEGER NOT NULL DEFAULT 0,
      uploaded_at TEXT NOT NULL
    )`),
    database.prepare("CREATE INDEX IF NOT EXISTS report_uploads_period_idx ON report_uploads(period, uploaded_at)"),
  ]);
}

export async function GET() {
  const bindings = env as unknown as AppEnv;
  await ensureSchema(bindings.DB);
  const result = await bindings.DB.prepare("SELECT id, period, filename, row_count, issue_count, uploaded_at FROM report_uploads ORDER BY uploaded_at DESC LIMIT 36").all();
  return Response.json(result.results);
}

export async function POST(request: Request) {
  const bindings = env as unknown as AppEnv;
  await ensureSchema(bindings.DB);
  const form = await request.formData();
  const file = form.get("file");
  if (!(file instanceof File)) return Response.json({ error: "File is required" }, { status: 400 });
  const id = crypto.randomUUID();
  const period = String(form.get("period") ?? "Unspecified");
  const uploadedAt = new Date().toISOString();
  const safeName = file.name.replace(/[^a-zA-Z0-9._-]+/g, "_");
  const objectKey = `monthly/${period.replace(/\s+/g, "-").toLowerCase()}/${id}-${safeName}`;
  await bindings.FILES.put(objectKey, file, { httpMetadata: { contentType: file.type || "application/octet-stream" }, customMetadata: { period, originalName: file.name } });
  await bindings.DB.prepare("INSERT INTO report_uploads (id, period, filename, object_key, row_count, issue_count, uploaded_at) VALUES (?, ?, ?, ?, ?, ?, ?)")
    .bind(id, period, file.name, objectKey, Number(form.get("rows") ?? 0), Number(form.get("issues") ?? 0), uploadedAt).run();
  return Response.json({ id, stored: true, objectKey });
}
