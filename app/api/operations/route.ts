import { env } from "cloudflare:workers";
import { getChatGPTUser } from "../../chatgpt-auth";

type AppEnv = { DB: D1Database };

const owners = new Set(["Maliwan", "Ananya", "Jiratchaya", "Uthai", "Watsana"]);
const flows = new Set(["Import", "Export"]);

async function ensureSchema(database: D1Database) {
  await database.batch([
    database.prepare(`CREATE TABLE IF NOT EXISTS operation_entries (
      id TEXT PRIMARY KEY,
      owner_name TEXT NOT NULL,
      work_date TEXT NOT NULL,
      reporting_period TEXT NOT NULL,
      flow TEXT NOT NULL,
      customer TEXT NOT NULL,
      subcontractor TEXT NOT NULL,
      job_code TEXT NOT NULL,
      container_no TEXT,
      equipment_type TEXT,
      plan_at TEXT NOT NULL,
      actual_at TEXT,
      operation_status TEXT NOT NULL,
      validation_status TEXT NOT NULL,
      otd_status TEXT NOT NULL,
      remark TEXT,
      submitted_by TEXT NOT NULL,
      submitted_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    )`),
    database.prepare("CREATE INDEX IF NOT EXISTS operation_entries_owner_date_idx ON operation_entries(owner_name, work_date)"),
    database.prepare("CREATE INDEX IF NOT EXISTS operation_entries_period_flow_idx ON operation_entries(reporting_period, flow)"),
  ]);
}

function clean(value: unknown, max = 180) {
  return String(value ?? "").trim().slice(0, max);
}

export async function GET(request: Request) {
  const user = await getChatGPTUser();
  if (!user) return Response.json({ error: "Sign in is required" }, { status: 401 });
  const database = (env as unknown as AppEnv).DB;
  await ensureSchema(database);
  const url = new URL(request.url);
  const owner = clean(url.searchParams.get("owner"), 40);
  const period = clean(url.searchParams.get("period"), 20);
  const conditions: string[] = [];
  const bindings: string[] = [];
  if (owner && owner !== "All") { conditions.push("owner_name = ?"); bindings.push(owner); }
  if (period) { conditions.push("reporting_period = ?"); bindings.push(period); }
  const where = conditions.length ? `WHERE ${conditions.join(" AND ")}` : "";
  const query = database.prepare(`SELECT * FROM operation_entries ${where} ORDER BY work_date DESC, submitted_at DESC LIMIT 500`);
  const result = bindings.length ? await query.bind(...bindings).all() : await query.all();
  return Response.json({ records: result.results, viewer: { email: user.email, name: user.displayName } });
}

export async function POST(request: Request) {
  const user = await getChatGPTUser();
  if (!user) return Response.json({ error: "Sign in is required" }, { status: 401 });
  const database = (env as unknown as AppEnv).DB;
  await ensureSchema(database);
  const body = await request.json<Record<string, unknown>>();
  const ownerName = clean(body.ownerName, 40);
  const flow = clean(body.flow, 10);
  const workDate = clean(body.workDate, 10);
  const customer = clean(body.customer);
  const subcontractor = clean(body.subcontractor);
  const jobCode = clean(body.jobCode, 80);
  const planAt = clean(body.planAt, 32);
  if (!owners.has(ownerName) || !flows.has(flow) || !workDate || !customer || !subcontractor || !jobCode || !planAt) {
    return Response.json({ error: "Owner, date, flow, customer, subcontractor, job and plan time are required" }, { status: 400 });
  }
  const actualAt = clean(body.actualAt, 32);
  const equipmentType = clean(body.equipmentType, 40);
  const containerNo = clean(body.containerNo, 80);
  const issues: string[] = [];
  if (/FCL/i.test(equipmentType) && !containerNo) issues.push("Missing container for FCL");
  const plan = new Date(planAt);
  const actual = actualAt ? new Date(actualAt) : null;
  if (Number.isNaN(plan.getTime())) issues.push("Invalid plan time");
  if (actual && Number.isNaN(actual.getTime())) issues.push("Invalid actual time");
  const validationStatus = issues.length ? "Needs review" : actual ? "Ready" : "In progress";
  const otdStatus = !actual || issues.length ? "Not Assessable" : actual <= plan ? "On Time" : "Late";
  const now = new Date().toISOString();
  const id = crypto.randomUUID();
  const period = workDate.slice(0, 7);
  await database.prepare(`INSERT INTO operation_entries (
    id, owner_name, work_date, reporting_period, flow, customer, subcontractor, job_code,
    container_no, equipment_type, plan_at, actual_at, operation_status, validation_status,
    otd_status, remark, submitted_by, submitted_at, updated_at
  ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .bind(id, ownerName, workDate, period, flow, customer, subcontractor, jobCode,
      containerNo || null, equipmentType || null, planAt, actualAt || null,
      clean(body.operationStatus, 40) || "Planned", validationStatus, otdStatus,
      clean(body.remark, 500) || null, user.email, now, now).run();
  return Response.json({ id, validationStatus, otdStatus, issues }, { status: 201 });
}
