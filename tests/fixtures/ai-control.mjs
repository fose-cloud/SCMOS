// Synthetic test fixtures only. Never imported by the application.
export const status = {
  enabled: true, chatEnabled: true, providerConfigured: true, mock: false, configurationValid: true,
  liveToolsReady: true, writeToolsReady: false, auditReady: true,
  agents: [
    { id: "operations-agent", name: "Operations Agent", enabled: true, connected: true },
    { id: "vendor-agent", name: "Vendor Agent", enabled: false, connected: false },
  ],
};
const figure = (id, value, note = "FIXTURE ONLY · ข้อมูลทดสอบ ไม่ใช่ข้อมูลงานจริง") =>
  ({ id, english: id, thai: id, value, base: 12, unit: "งาน", note });
export const today = {
  date: "07/09/2026", computedAt: "2026-09-07T03:00:00Z",
  volume: [figure("total", 12), figure("inTransit", 4), figure("delay", 0)],
  performance: [], attention: [figure("openCarPar", null, "ยังไม่มีเคสที่ใช้วัดได้ · FIXTURE ONLY")],
};
export const brief = {
  today: "07/09/2026", quiet: "",
  findings: [
    { urgency: "Now", kind: "overdue", headline: "FIXTURE · 2 งานผ่านเวลาแผนแล้วยังไม่ลงถึงหน้างาน", detail: "ข้อมูลจำลองสำหรับตรวจการเปิดหน้าต้นทาง ไม่มีการอ่าน Azure SQL", count: 2, screen: "monitoring" },
    { urgency: "Soon", kind: "today", headline: "FIXTURE · ตรวจความพร้อมก่อนเริ่มงาน", detail: "ยังไม่ระบุคนขับหรือทะเบียน", count: 1, screen: "monitoring" },
    { urgency: "Records", kind: "unmeasurable", headline: "FIXTURE · ตรวจวันที่ในทะเบียน", detail: "มีงานที่อ่านวันแผนไม่ได้ ข้อจำกัดนี้อาจทำให้ความเสี่ยงแสดงไม่ครบ", count: 1, screen: "myjob" },
  ],
};
export const reply = {
  runId: "a".repeat(32), code: "ok", summary: "FIXTURE ONLY · พบ 2 งานตามขอบเขต แสดงตัวอย่าง 1 งานสำหรับทดสอบหน้าจอ",
  agentId: "operations-agent", mock: false, usage: { inputTokens: 10, outputTokens: 3 },
  evidence: {
    view: "risk_today", asOfDate: "07/09/2026", timeZone: "Asia/Bangkok", window: "overdue through 09/09/2026",
    total: 2, returned: 1, truncated: true, undatedActive: 1, invalidRows: 0,
    retrievedAt: "2026-09-07T03:01:00Z", sourceUpdatedAt: null, basis: "FIXTURE ONLY · ใช้ทดสอบการแสดงหลักฐาน ไม่มีข้อมูลจริง",
    rows: [{
      key: "TEST-ONLY-001", category: "IMPORT", jobCode: "TEST-JOB-001", container: "TEST CONTAINER",
      customer: "TEST CUSTOMER", trucker: "TEST TRUCKER", date: "07/09/2026", planTime: "08:00",
      status: "WAIT", hasOwner: true, hasDriver: false, hasPlate: false, arrivalRecorded: false,
      risk: "Overdue", explanation: "ผ่านเวลาแผนและยังไม่มีข้อมูลถึงหน้างาน",
      suggestedAction: "ตรวจวันแผนและติดตามการลงเวลา", source: "operation_jobs",
    }],
  },
};
/** The Data Agent's reply (Phase 2): the figure and its provenance, no Operations evidence. */
export const kpiReply = {
  runId: "c".repeat(32), code: "ok", summary: "FIXTURE ONLY · งวด 09/2026: งานทั้งหมด 10 · ตรงเวลา 4 จาก 6 งานที่วัดได้ (67%)",
  agentId: "data-agent", mock: false, usage: { inputTokens: 12, outputTokens: 6 }, evidence: null,
  correlationId: "fixture-corr-kpi", contextUsed: false,
  kpi: {
    view: "kpi", period: "2026-09", periodLabel: "09/2026", filters: { customer: "TEST CUSTOMER", trucker: "", owner: "" },
    total: 10, measured: 6, onTime: 4, onTimePercent: 67, notAssessable: 4, undated: 1, formatErrors: 2, actionRequired: 2,
    byCategory: [{ label: "IMPORT", value: 7 }, { label: "EXPORT", value: 3 }],
    carriers: [{ carrier: "TEST TRUCKER", total: 5, measured: 3, onTime: 2, percent: 67 }, { carrier: "TEST TRUCKER 2", total: 3, measured: 2, onTime: 1, percent: 50 }],
    carriersTotal: 3, returned: 2, truncated: true,
    rule: { id: "arrival.on_time", version: "2", source: "Rules/JobRules.cs:JobRules.IsOnTime", meaning: "FIXTURE ONLY", missingData: "FIXTURE ONLY" },
    customerContract: "unknown", retrievedAt: "2026-09-20T05:00:00Z", sourceUpdatedAt: "2026-09-20T04:00:00Z",
    basis: "FIXTURE ONLY · SCMOS KpiService", source: "operation_jobs",
  },
};
/** The Communication Agent's reply (Phase 4): the messages as the ledger holds them, no Operations evidence. */
export const messagesReply = {
  runId: "d".repeat(32), code: "ok", summary: "FIXTURE ONLY · งาน TEST-JOB-001: 2 ข้อความ แสดง 2 · ไม่มีการส่งหรือแก้ไขใด ๆ",
  agentId: "communication-agent", mock: false, usage: { inputTokens: 12, outputTokens: 6 }, evidence: null,
  correlationId: "fixture-corr-msg", contextUsed: false,
  messages: {
    view: "job", asOfDate: "21/09/2026", timeZone: "Asia/Bangkok", window: "all_dates_for_the_job",
    total: 2, returned: 2, truncated: false, waiting: 1, applied: 1, unmatched: 0, ignored: 0, mails: 1,
    jobs: [{ key: "TEST-ONLY-001", jobCode: "TEST-JOB-001", customer: "TEST CUSTOMER", trucker: "TEST TRUCKER", ownerId: "fixture-op", category: "IMPORT", date: "21/09/2026", status: "IN_TRANSIT" }],
    retrievedAt: "2026-09-21T05:00:00Z", basis: "FIXTURE ONLY · nothing sent, nothing applied",
    rows: [
      { id: "line:1", channel: "line", at: "2026-09-21T04:30:00Z", group: "TEST ROOM", jobKey: "TEST-ONLY-001", jobCode: "TEST-JOB-001", customer: "TEST CUSTOMER", trucker: "TEST TRUCKER",
        status: "DELIVERED", arrival: "21/09/2026 11:20", eta: null, plate: "70-1234", container: "TEST1234567", seal: null, delayed: false, delayCategory: null, question: false,
        state: "applied", detail: "นำเข้าตารางงานแล้ว", excerpt: "FIXTURE ONLY ถึงโรงงาน 11:20", source: "line_events" },
      { id: "mail:11", channel: "mail", at: "2026-09-21T02:00:00Z", group: "Test Ops", jobKey: "TEST-ONLY-001", jobCode: "TEST-JOB-001", customer: "TEST CUSTOMER", trucker: "TEST TRUCKER",
        status: null, arrival: null, eta: null, plate: null, container: null, seal: null, delayed: false, delayCategory: null, question: false,
        state: "linked", detail: "จับคู่จาก container (98%)", excerpt: "FIXTURE ONLY arrival notice", source: "emails" },
    ],
  },
};
export const run = {
  runId: reply.runId, userId: "fixture-user", role: "Operation Supervisor", agentId: "operations-agent", model: "fixture-provider",
  scope: { team: true, operatorId: null }, status: "succeeded",
  startedAt: "2026-09-07T03:01:00Z", completedAt: "2026-09-07T03:01:01Z",
  toolCallId: "fixture-call", tool: "get_shipments", toolStatus: "succeeded", view: "risk_today", limit: 1,
  risk: "low", approvalStatus: "not_required", source: "operation_jobs", sourceKeys: ["TEST-ONLY-001"],
  total: 2, returned: 1, usage: reply.usage,
  events: ["run_started", "tool_started", "tool_completed", "run_completed"].map(event => ({
    event, status: event.endsWith("started") ? "running" : "succeeded", at: "2026-09-07T03:01:00Z",
    total: event === "tool_completed" ? 2 : null, returned: event === "tool_completed" ? 1 : null,
    step: event === "run_started" ? null : 1, tool: event === "run_started" ? null : "get_shipments",
  })),
  correlationId: "fixture-corr-0001", steps: 1,
};
export const audit = { runs: [run, { ...run, runId: "b".repeat(32), status: "incomplete", completedAt: null, events: run.events.slice(0, 2) }], nextBeforeId: 3 };
export const job = {
  id: "TEST-ONLY-001", cat: "IMPORT", op: "FIXTURE ONLY", opId: "fixture-op",
  date: "07/09/2026", customer: "TEST CUSTOMER", trucker: "TEST TRUCKER",
  jobCode: "TEST-JOB-001", abs: "", destination: "TEST DESTINATION", planTime: "08:00", type: "1X20",
  status: "WAIT", container: "TEST CONTAINER", product: "NON DG",
};
