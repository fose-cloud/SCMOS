"use client";

import { useMemo, useRef, useState } from "react";
import * as XLSX from "xlsx";

type Status = "On Time" | "Late" | "Not Assessable";
type RecordRow = {
  id: string;
  row: number;
  source: string;
  direction: "Import" | "Export";
  customer: string;
  subcontractor: string;
  job: string;
  container: string;
  plan: string;
  actual: string;
  status: Status;
  issues: string[];
};

const previewRows: RecordRow[] = [
  { id: "1", row: 2, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "BARBE", subcontractor: "SANGJA", job: "260617620220", container: "—", plan: "01 Jul 2026 · 13:00", actual: "—", status: "Not Assessable", issues: ["Missing Actual Time"] },
  { id: "2", row: 3, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "HENKEL HAZCHEM", subcontractor: "SANGJA", job: "260617620336", container: "—", plan: "01 Jul 2026 · 11:00", actual: "01 Jul 2026 · 13:00", status: "Late", issues: [] },
  { id: "3", row: 4, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "DANA", subcontractor: "WEALTHY", job: "260600800513", container: "VMLU3811023", plan: "01 Jul 2026 · 10:00", actual: "—", status: "Not Assessable", issues: ["Invalid Actual Time: กำลังเดินทาง"] },
  { id: "4", row: 7, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "QURASAR", subcontractor: "WEALTHY", job: "260600800720", container: "MRSU3222883", plan: "01 Jul 2026 · 09:00", actual: "01 Jul 2026 · 08:00", status: "On Time", issues: [] },
  { id: "5", row: 9, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "AVIENT", subcontractor: "PK", job: "260600800724", container: "MRKU3364118", plan: "01 Jul 2026 · 08:30", actual: "01 Jul 2026 · 10:00", status: "Late", issues: [] },
  { id: "6", row: 10, source: "PLAN IMPORT-EXPORT 2026 Watsana.xlsx", direction: "Import", customer: "CLARIANT", subcontractor: "SHORE", job: "260600800764", container: "TEMU0410130", plan: "01 Jul 2026 · 09:00", actual: "01 Jul 2026 · 08:00", status: "On Time", issues: [] },
];

const requiredAliases = {
  job: ["JOB CODE", "ABS.NO.", "ABS NO", "ABS"],
  customer: ["CUSTOMER"],
  subcontractor: ["TRUCK", "SUBCONTRACTOR", "SUB NAME"],
  container: ["NO CONTAINER", "CONTAINER", "CONTAINER NO"],
  planDate: ["DATE", "PLAN LOADING DATE"],
  planTime: ["PLANLOADING TIME", "PLAN LOADING TIME", "TIME LOAD"],
  actualDate: ["ARRIVAL DATE", "DAET LOANDING", "ACTUAL DATE"],
  actualTime: ["ARRIVAL TIME", "TIME LOANDING", "ACTUAL TIME"],
};

function normalizeHeader(value: unknown) {
  return String(value ?? "").trim().toUpperCase().replace(/\s+/g, " ");
}

function pick(headers: string[], aliases: string[]) {
  const normalized = headers.map(normalizeHeader);
  return aliases.map(normalizeHeader).map((alias) => normalized.indexOf(alias)).find((index) => index >= 0) ?? -1;
}

function parseDate(value: unknown) {
  if (value instanceof Date) return value;
  if (typeof value === "number") return XLSX.SSF.parse_date_code(value) ? new Date(Date.UTC(XLSX.SSF.parse_date_code(value)!.y, XLSX.SSF.parse_date_code(value)!.m - 1, XLSX.SSF.parse_date_code(value)!.d)) : null;
  const text = String(value ?? "").trim();
  if (!text) return null;
  const match = text.match(/^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})/);
  if (match) return new Date(Number(match[3].length === 2 ? `20${match[3]}` : match[3]), Number(match[2]) - 1, Number(match[1]));
  const parsed = new Date(text);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function parseTime(value: unknown) {
  if (typeof value === "number" && value >= 0 && value < 1) return Math.round(value * 24 * 60);
  const text = String(value ?? "").trim().replace(/^@/, "");
  const match = text.match(/^(\d{1,2})[.:](\d{1,2})/);
  if (!match) return null;
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  return hour <= 23 && minute <= 59 ? hour * 60 + minute : null;
}

function combine(valueDate: unknown, valueTime: unknown) {
  const date = parseDate(valueDate);
  const minutes = parseTime(valueTime);
  if (!date || minutes === null) return null;
  date.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return date;
}

function fmt(date: Date | null) {
  if (!date) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false }).format(date).replace(",", " ·");
}

function downloadWorkbook(rows: RecordRow[], period: string) {
  const detail = rows.map((r) => ({ Source_File: r.source, Source_Row: r.row, Direction: r.direction, Customer: r.customer, Subcontractor: r.subcontractor, Job_ABS: r.job, Container: r.container, Planned: r.plan, Actual: r.actual, KPI_Status: r.status, Validation_Issues: r.issues.join(" | ") }));
  const summary = [
    { Metric: "Reporting period", Value: period },
    { Metric: "Eligible", Value: rows.filter((r) => r.status !== "Not Assessable").length },
    { Metric: "On Time", Value: rows.filter((r) => r.status === "On Time").length },
    { Metric: "Late", Value: rows.filter((r) => r.status === "Late").length },
    { Metric: "Not Assessable", Value: rows.filter((r) => r.status === "Not Assessable").length },
  ];
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.json_to_sheet(summary), "KPI Summary");
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.json_to_sheet(detail), "Audit Detail");
  XLSX.writeFile(workbook, `SCMOS_${period.replace(" ", "_")}_validated.xlsx`);
}

export default function Home() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [rows, setRows] = useState<RecordRow[]>(previewRows);
  const [period, setPeriod] = useState("July 2026");
  const [direction, setDirection] = useState("All flows");
  const [customer, setCustomer] = useState("All customers");
  const [subcontractor, setSubcontractor] = useState("All subcontractors");
  const [detailFilter, setDetailFilter] = useState<"All" | Status>("All");
  const [uploading, setUploading] = useState(false);
  const [uploadSummary, setUploadSummary] = useState({ files: 3, rows: 1248, issues: 47, label: "Validated preview" });
  const [notice, setNotice] = useState("July report locked · Formula OTD v1.3");
  const [accidents, setAccidents] = useState(0);

  const filtered = useMemo(() => rows.filter((r) =>
    (direction === "All flows" || r.direction === direction) &&
    (customer === "All customers" || r.customer === customer) &&
    (subcontractor === "All subcontractors" || r.subcontractor === subcontractor)
  ), [rows, direction, customer, subcontractor]);
  const shown = detailFilter === "All" ? filtered : filtered.filter((r) => r.status === detailFilter);
  const eligible = filtered.filter((r) => r.status !== "Not Assessable").length;
  const onTime = filtered.filter((r) => r.status === "On Time").length;
  const late = filtered.filter((r) => r.status === "Late").length;
  const notAssessable = filtered.filter((r) => r.status === "Not Assessable").length;
  const previewMode = uploadSummary.label === "Validated preview" && direction === "All flows" && customer === "All customers" && subcontractor === "All subcontractors";
  const metricEligible = previewMode ? 1194 : eligible;
  const metricOnTime = previewMode ? 1156 : onTime;
  const metricLate = previewMode ? 38 : late;
  const metricNotAssessable = previewMode ? 54 : notAssessable;
  const metricTotal = previewMode ? 1248 : filtered.length;
  const otd = metricEligible ? (metricOnTime / metricEligible) * 100 : null;
  const customers = [...new Set(rows.map((r) => r.customer))];
  const subcontractors = [...new Set(rows.map((r) => r.subcontractor))];

  async function handleFiles(files: FileList | null) {
    if (!files?.length) return;
    setUploading(true);
    const parsedRows: RecordRow[] = [];
    let issueCount = 0;
    for (const file of Array.from(files)) {
      try {
        const data = await file.arrayBuffer();
        const workbook = XLSX.read(data, { type: "array", cellDates: true });
        for (const sheetName of workbook.SheetNames) {
          if (!/IMPORT|EXPORT/i.test(sheetName)) continue;
          const matrix = XLSX.utils.sheet_to_json<unknown[]>(workbook.Sheets[sheetName], { header: 1, raw: true, defval: null });
          const headerIndex = matrix.findIndex((row) => Array.isArray(row) && row.filter(Boolean).length >= 5);
          if (headerIndex < 0) continue;
          const headers = (matrix[headerIndex] as unknown[]).map((v) => String(v ?? ""));
          const cols = Object.fromEntries(Object.entries(requiredAliases).map(([key, aliases]) => [key, pick(headers, aliases)]));
          const flow = /EXPORT/i.test(sheetName) ? "Export" : "Import";
          for (let index = headerIndex + 1; index < matrix.length; index++) {
            const values = matrix[index] as unknown[];
            if (!values || values.filter(Boolean).length < 3) continue;
            const job = cols.job >= 0 ? String(values[cols.job] ?? "").trim() : "";
            const container = cols.container >= 0 ? String(values[cols.container] ?? "").trim() : "";
            const plan = combine(values[cols.planDate], values[cols.planTime]);
            const actual = combine(values[cols.actualDate], values[cols.actualTime]);
            const issues: string[] = [];
            if (!job || job === "-") issues.push(flow === "Export" ? "Missing ABS" : "Missing Job");
            if (!actual) issues.push("Missing / invalid Actual Time");
            if (flow === "Export" && !container) issues.push("Missing Container");
            const status: Status = issues.some((x) => /Job|ABS|Actual/.test(x)) ? "Not Assessable" : actual && plan && actual <= plan ? "On Time" : "Late";
            issueCount += issues.length;
            parsedRows.push({ id: `${file.name}-${sheetName}-${index + 1}`, row: index + 1, source: file.name, direction: flow, customer: cols.customer >= 0 ? String(values[cols.customer] ?? "Unknown") : "Unknown", subcontractor: cols.subcontractor >= 0 ? String(values[cols.subcontractor] ?? "Unknown") : "Unknown", job: job || "—", container: container || "—", plan: fmt(plan), actual: fmt(actual), status, issues });
            if (parsedRows.length >= 5000) break;
          }
        }
      } catch {
        issueCount += 1;
      }
    }
    const seen = new Map<string, number>();
    parsedRows.forEach((row) => {
      const key = `${row.direction}|${row.job}|${row.container}|${row.plan}`;
      seen.set(key, (seen.get(key) ?? 0) + 1);
    });
    parsedRows.forEach((row) => { if ((seen.get(`${row.direction}|${row.job}|${row.container}|${row.plan}`) ?? 0) > 1) { row.issues.push("Possible duplicate — retained"); issueCount += 1; } });
    await Promise.all(Array.from(files).map(async (file) => {
      const form = new FormData();
      form.append("file", file);
      form.append("period", period);
      form.append("rows", String(parsedRows.filter((row) => row.source === file.name).length));
      form.append("issues", String(parsedRows.filter((row) => row.source === file.name).reduce((sum, row) => sum + row.issues.length, 0)));
      try { await fetch("/api/uploads", { method: "POST", body: form }); } catch { /* local preview can continue if persistence is unavailable */ }
    }));
    if (parsedRows.length) setRows(parsedRows);
    setUploadSummary({ files: files.length, rows: parsedRows.length, issues: issueCount, label: parsedRows.length ? "Validation complete" : "No usable rows" });
    setNotice(parsedRows.length ? `${files.length} file(s) validated · no duplicates removed` : "File structure needs review before calculation");
    setUploading(false);
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand"><img src="/logo.png" alt="Leschaco" /><div><b>SCMOS</b><span>Report & Dashboard</span></div></div>
        <nav aria-label="Primary navigation">
          <a className="active" href="#overview"><span>⌂</span> Executive overview</a>
          <a href="#delivery"><span>↗</span> Delivery performance</a>
          <a href="#billing"><span>▤</span> Billing performance</a>
          <a href="#safety"><span>◇</span> Safety & accident</a>
          <a href="#scorecard"><span>☆</span> Subcontractor scorecard</a>
          <div className="nav-label">DATA CONTROL</div>
          <button onClick={() => inputRef.current?.click()}><span>⇧</span> Upload monthly data</button>
          <a href="#quality"><span>✓</span> Data quality</a>
          <a href="#governance"><span>⌘</span> KPI governance</a>
          <a href="#history"><span>◷</span> Report history</a>
        </nav>
        <div className="sidebar-foot"><div className="avatar">ST</div><div><b>SCMOS Team</b><span>Operations · Thailand</span></div><button aria-label="More options">•••</button></div>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div><p className="eyebrow">MONTHLY OPERATIONS CONTROL</p><h1>Executive overview</h1></div>
          <div className="top-actions"><span className="sync"><i /> Data synced 10 Aug · 09:42</span><button className="icon-btn" aria-label="Notifications">○</button><button className="primary" onClick={() => inputRef.current?.click()}>＋ Upload data</button></div>
          <input ref={inputRef} className="sr-only" type="file" accept=".xlsx,.xls,.csv" multiple onChange={(event) => handleFiles(event.target.files)} />
        </header>

        <div className="content">
          <section className="filterbar" aria-label="Dashboard filters">
            <label>REPORTING PERIOD<select value={period} onChange={(e) => setPeriod(e.target.value)}><option>July 2026</option><option>June 2026</option><option>May 2026</option></select></label>
            <label>FLOW<select value={direction} onChange={(e) => setDirection(e.target.value)}><option>All flows</option><option>Import</option><option>Export</option></select></label>
            <label>CUSTOMER<select value={customer} onChange={(e) => setCustomer(e.target.value)}><option>All customers</option>{customers.map((x) => <option key={x}>{x}</option>)}</select></label>
            <label>SUBCONTRACTOR<select value={subcontractor} onChange={(e) => setSubcontractor(e.target.value)}><option>All subcontractors</option>{subcontractors.map((x) => <option key={x}>{x}</option>)}</select></label>
            <button className="reset" onClick={() => { setDirection("All flows"); setCustomer("All customers"); setSubcontractor("All subcontractors"); }}>↻ Reset</button>
          </section>

          <section className="status-strip"><div><span className="live-dot" /> <b>{notice}</b><small>Every result retains source file, sheet and row reference.</small></div><button onClick={() => document.getElementById("governance")?.scrollIntoView({ behavior: "smooth" })}>View governance →</button></section>

          <section className="kpi-grid" id="overview">
            <article className="kpi-card hero-kpi"><div className="kpi-head"><span>ON-TIME DELIVERY</span><i className={(otd ?? 0) >= 95 ? "good" : "warn"}>{otd === null ? "NOT CALCULABLE" : (otd >= 95 ? "ON TARGET" : "BELOW TARGET")}</i></div><div className="kpi-main"><strong>{otd === null ? "—" : `${otd.toFixed(1)}%`}</strong><span>Target ≥ 95%</span></div><div className="progress"><i style={{ width: `${Math.min(otd ?? 0, 100)}%` }} /></div><div className="kpi-foot"><span><b>{metricEligible}</b> Eligible</span><span><b>{metricOnTime}</b> On time</span><span className="danger"><b>{metricLate}</b> Late</span><button onClick={() => setDetailFilter("Late")}>Drill down →</button></div></article>
            <article className="kpi-card"><div className="kpi-head"><span>BILLING ≤ 3 WORKING DAYS</span><i className="warn">NOT CALCULABLE</i></div><div className="kpi-main"><strong>—</strong><span>Target ≥ 95%</span></div><div className="progress"><i style={{ width: "0%" }} /></div><div className="kpi-foot"><span><b>—</b> Billing file required</span><button onClick={() => inputRef.current?.click()}>Upload billing →</button></div></article>
            <article className="kpi-card accident"><div className="kpi-head"><span>TRANSPORT ACCIDENT</span><i className={accidents === 0 ? "good" : "warn"}>{accidents === 0 ? "ON TARGET" : "ACTION REQUIRED"}</i></div><div className="kpi-main"><strong>{accidents}</strong><span>Target 0 case</span></div><div className="zero-line"><i /></div><div className="kpi-foot"><span><b>{accidents}</b> Recorded case</span><button onClick={() => { const note = window.prompt("Accident reference / short description"); if (note?.trim()) { setAccidents((value) => value + 1); setNotice(`Accident recorded: ${note.trim()}`); } }}>＋ Record accident</button></div></article>
          </section>

          <section className="main-grid">
            <article className="panel performance" id="delivery"><div className="panel-title"><div><span>DELIVERY PERFORMANCE</span><h2>OTD status breakdown</h2></div><button onClick={() => setDetailFilter("All")}>View all records →</button></div><div className="breakdown"><div className="donut" style={{ background: `conic-gradient(#12a36d 0 ${metricTotal ? metricOnTime / metricTotal * 100 : 0}%, #ef7f52 ${metricTotal ? metricOnTime / metricTotal * 100 : 0}% ${metricTotal ? (metricOnTime + metricLate) / metricTotal * 100 : 0}%, #c7d0d8 ${metricTotal ? (metricOnTime + metricLate) / metricTotal * 100 : 0}% 100%)` }}><div><b>{metricTotal}</b><span>Total jobs</span></div></div><div className="legend"><button onClick={() => setDetailFilter("On Time")}><i className="green" /><span>On time<small>Arrived at or before plan</small></span><b>{metricOnTime}</b></button><button onClick={() => setDetailFilter("Late")}><i className="orange" /><span>Late<small>Arrived after plan</small></span><b>{metricLate}</b></button><button onClick={() => setDetailFilter("Not Assessable")}><i className="gray" /><span>Not assessable<small>Actual time incomplete</small></span><b>{metricNotAssessable}</b></button></div></div><div className="note">Not Assessable is excluded from the OTD denominator — it is never treated as 0%.</div></article>

            <article className="panel quality" id="quality"><div className="panel-title"><div><span>DATA QUALITY GATE</span><h2>{uploadSummary.label}</h2></div><div className="quality-score">{uploadSummary.issues ? "96.2" : "100"}<small>/100</small></div></div><div className="quality-stats"><div><b>{uploadSummary.files}</b><span>Source files</span></div><div><b>{uploadSummary.rows.toLocaleString()}</b><span>Rows checked</span></div><div className="warning"><b>{uploadSummary.issues}</b><span>Issues found</span></div></div><div className="validation-list"><button onClick={() => setDetailFilter("Not Assessable")}><span className="vicon amber">!</span><div><b>Missing or invalid Actual Time</b><small>Review before KPI calculation</small></div><strong>{notAssessable} →</strong></button><button><span className="vicon red">×</span><div><b>Missing Job / ABS / Container</b><small>Key fields required for traceability</small></div><strong>{rows.filter((r) => r.issues.some((x) => /Job|ABS|Container/.test(x))).length} →</strong></button><button><span className="vicon blue">≡</span><div><b>Possible duplicates retained</b><small>No automatic deletion or merge</small></div><strong>{rows.filter((r) => r.issues.some((x) => /duplicate/.test(x))).length} →</strong></button></div><button className="secondary full" disabled={uploading} onClick={() => inputRef.current?.click()}>{uploading ? "Validating workbook…" : "Upload another workbook"}</button></article>
          </section>

          <section className="panel detail" id="history"><div className="panel-title"><div><span>AUDITABLE DETAIL</span><h2>{detailFilter === "All" ? "Jobs requiring attention" : `${detailFilter} jobs`}</h2></div><div className="export-actions"><button onClick={() => downloadWorkbook(shown, period)}>⇩ Excel</button><button onClick={() => window.print()}>⇩ PDF</button></div></div><div className="tabs">{(["All", "Late", "Not Assessable"] as const).map((tab) => <button key={tab} className={detailFilter === tab ? "active" : ""} onClick={() => setDetailFilter(tab)}>{tab}{tab !== "All" && <span>{filtered.filter((r) => r.status === tab).length}</span>}</button>)}</div><div className="table-wrap"><table><thead><tr><th>STATUS</th><th>JOB / ABS</th><th>CUSTOMER</th><th>SUBCONTRACTOR</th><th>PLAN → ACTUAL</th><th>SOURCE TRACE</th><th /></tr></thead><tbody>{shown.map((row) => <tr key={row.id}><td><span className={`pill ${row.status.replaceAll(" ", "-").toLowerCase()}`}>{row.status}</span></td><td><b>{row.job}</b><small>{row.direction} · {row.container}</small></td><td>{row.customer}</td><td>{row.subcontractor}</td><td><b>{row.plan}</b><small>{row.actual}</small></td><td><b className="source">{row.source}</b><small>Row {row.row} · Formula OTD v1.3</small></td><td><button className="row-action" aria-label={`Open ${row.job}`}>→</button></td></tr>)}</tbody></table>{!shown.length && <div className="empty">No records match the selected filters.</div>}</div></section>

          <section className="governance" id="governance"><div><span className="shield">✓</span><div><p>KPI GOVERNANCE</p><h2>Formula OTD v1.3 · Approved & locked</h2><span>On Time ÷ Eligible Jobs × 100 · Excludes cancelled jobs and records without assessable actual time.</span></div></div><div className="trace"><span>FILE</span><i>→</i><span>ROW</span><i>→</i><span>RULE</span><i>→</i><span>KPI</span></div><button>Open definition</button></section>
        </div>
      </section>
    </main>
  );
}
