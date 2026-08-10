"use client";

import { FormEvent, useEffect, useRef, useState } from "react";

const owners = ["Maliwan", "Ananya", "Jiratchaya", "Uthai", "Watsana"] as const;
type Owner = typeof owners[number];

type OperationRecord = {
  id: string; owner_name: string; work_date: string; flow: string; customer: string;
  subcontractor: string; job_code: string; container_no: string | null;
  equipment_type: string | null; plan_at: string; actual_at: string | null;
  operation_status: string; validation_status: string; otd_status: string; submitted_by: string;
};

const initialForm = { workDate: new Date().toISOString().slice(0, 10), flow: "Import", customer: "", subcontractor: "", jobCode: "", containerNo: "", equipmentType: "FCL 20'", planAt: "", actualAt: "", operationStatus: "Planned", remark: "" };

function displayDate(value: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" }).format(date);
}

export function OperationWorkspace() {
  const [owner, setOwner] = useState<Owner>("Maliwan");
  const [records, setRecords] = useState<OperationRecord[]>([]);
  const [form, setForm] = useState(initialForm);
  const [message, setMessage] = useState("Select a coordinator, then record a job or upload their workbook.");
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(false);
  const [showForm, setShowForm] = useState(true);
  const fileRef = useRef<HTMLInputElement>(null);

  async function loadRecords(selectedOwner = owner) {
    setLoading(true);
    try {
      const response = await fetch(`/api/operations?owner=${encodeURIComponent(selectedOwner)}`);
      const data = await response.json() as { records?: OperationRecord[]; error?: string };
      if (!response.ok) throw new Error(data.error || "Unable to load records");
      setRecords(data.records ?? []);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to load operation records"); }
    finally { setLoading(false); }
  }

  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/operations?owner=${encodeURIComponent(owner)}`, { signal: controller.signal })
      .then(async (response) => {
        const data = await response.json() as { records?: OperationRecord[]; error?: string };
        if (!response.ok) throw new Error(data.error || "Unable to load records");
        return data.records ?? [];
      })
      .then(setRecords)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setMessage(error instanceof Error ? error.message : "Unable to load operation records");
      });
    return () => controller.abort();
  }, [owner]);

  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true);
    try {
      const response = await fetch("/api/operations", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ ...form, ownerName: owner }) });
      const result = await response.json() as { error?: string; validationStatus?: string; otdStatus?: string };
      if (!response.ok) throw new Error(result.error || "Unable to save job");
      setMessage(`${form.jobCode} saved under ${owner} · ${result.validationStatus} · ${result.otdStatus}`);
      setForm({ ...initialForm, workDate: form.workDate, flow: form.flow });
      await loadRecords(owner);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to save job"); }
    finally { setSaving(false); }
  }

  async function upload(file: File | undefined) {
    if (!file) return;
    setSaving(true);
    const data = new FormData();
    data.append("file", file); data.append("period", form.workDate.slice(0, 7)); data.append("owner", owner); data.append("flow", form.flow); data.append("rows", "0"); data.append("issues", "0");
    try {
      const response = await fetch("/api/uploads", { method: "POST", body: data });
      const result = await response.json() as { error?: string };
      if (!response.ok) throw new Error(result.error || "Upload failed");
      setMessage(`${file.name} uploaded to ${owner}'s workspace · waiting for validation`);
    } catch (error) { setMessage(error instanceof Error ? error.message : "Upload failed"); }
    finally { setSaving(false); if (fileRef.current) fileRef.current.value = ""; }
  }

  const ready = records.filter((record) => record.validation_status === "Ready").length;
  const attention = records.filter((record) => record.validation_status === "Needs review").length;
  const inProgress = records.filter((record) => record.validation_status === "In progress").length;

  return <div className="ops-workspace">
    <section className="ops-hero"><div><p>OPERATION DATA ENTRY · พื้นที่ลงข้อมูลงาน</p><h2>พื้นที่ทำงานแยกตามผู้รับผิดชอบ</h2><span>เลือกชื่อผู้รับผิดชอบ แล้วบันทึกงาน Import/Export รายเที่ยว หรืออัปโหลดไฟล์ประจำวันได้ทันที</span></div><div className="ops-actions"><button className="secondary" onClick={() => fileRef.current?.click()}>Upload {owner}&apos;s workbook</button><button className="primary" onClick={() => setShowForm((value) => !value)}>{showForm ? "Hide form" : "+ New job"}</button></div><input ref={fileRef} className="sr-only" type="file" accept=".xlsx,.xls,.csv" onChange={(event) => upload(event.target.files?.[0])}/></section>

    <section className="owner-switcher" aria-label="Coordinator workspace">{owners.map((name) => <button key={name} className={owner === name ? "active" : ""} onClick={() => setOwner(name)}><span>{name.slice(0, 2).toUpperCase()}</span><b>{name}</b><small>{owner === name ? `${records.length} current records` : "Open workspace"}</small></button>)}</section>
    <section className="ops-message"><span className="live-dot"/><b>{message}</b><small>Signed-in user and submission time are recorded automatically.</small></section>

    {showForm && <form className="ops-form panel" onSubmit={submit}>
      <div className="ops-form-title"><div><span>NEW OPERATION RECORD</span><h2>{owner}&apos;s workspace</h2></div><i>Required fields *</i></div>
      <div className="ops-fields">
        <label>WORK DATE *<input type="date" required value={form.workDate} onChange={(e) => setForm({ ...form, workDate: e.target.value })}/></label>
        <label>FLOW *<select value={form.flow} onChange={(e) => setForm({ ...form, flow: e.target.value })}><option>Import</option><option>Export</option></select></label>
        <label>STATUS<select value={form.operationStatus} onChange={(e) => setForm({ ...form, operationStatus: e.target.value })}><option>Planned</option><option>Dispatched</option><option>Arrived</option><option>Completed</option><option>Cancelled</option></select></label>
        <label>JOB / ABS *<input required placeholder="e.g. 260617620220" value={form.jobCode} onChange={(e) => setForm({ ...form, jobCode: e.target.value })}/></label>
        <label>CUSTOMER *<input required placeholder="Customer name" value={form.customer} onChange={(e) => setForm({ ...form, customer: e.target.value })}/></label>
        <label>SUBCONTRACTOR *<input required placeholder="Transport company" value={form.subcontractor} onChange={(e) => setForm({ ...form, subcontractor: e.target.value })}/></label>
        <label>EQUIPMENT<select value={form.equipmentType} onChange={(e) => setForm({ ...form, equipmentType: e.target.value })}><option>FCL 20&apos;</option><option>FCL 40&apos;</option><option>FCL 20&apos;RF</option><option>FCL 40&apos;RF</option><option>LCL 4WH</option><option>LCL 6WH</option><option>LCL 10WH</option></select></label>
        <label>CONTAINER NO.<input placeholder="Required for FCL" value={form.containerNo} onChange={(e) => setForm({ ...form, containerNo: e.target.value })}/></label>
        <label>PLAN DATE & TIME *<input type="datetime-local" required value={form.planAt} onChange={(e) => setForm({ ...form, planAt: e.target.value })}/></label>
        <label>ACTUAL DATE & TIME<input type="datetime-local" value={form.actualAt} onChange={(e) => setForm({ ...form, actualAt: e.target.value })}/><small>Leave blank while the job is in progress.</small></label>
        <label className="span-2">REMARK / DELAY REASON<textarea rows={2} placeholder="Optional operational note" value={form.remark} onChange={(e) => setForm({ ...form, remark: e.target.value })}/></label>
      </div>
      <div className="ops-form-foot"><span>Incomplete Actual Time is saved as In progress and excluded from OTD.</span><button className="primary" disabled={saving}>{saving ? "Saving…" : `Save under ${owner}`}</button></div>
    </form>}

    <section className="ops-stat-row"><div><span>TOTAL RECORDS</span><b>{records.length}</b><small>{owner}</small></div><div><span>READY FOR KPI</span><b>{ready}</b><small>Validated</small></div><div><span>IN PROGRESS</span><b>{inProgress}</b><small>Actual pending</small></div><div><span>NEEDS REVIEW</span><b>{attention}</b><small>Validation issue</small></div></section>

    <section className="panel ops-history"><div className="panel-title"><div><span>OWNER-SPECIFIC HISTORY</span><h2>{owner}&apos;s operation records</h2></div><button onClick={() => loadRecords(owner)}>{loading ? "Refreshing…" : "Refresh ↻"}</button></div><div className="table-wrap"><table><thead><tr><th>WORK DATE</th><th>FLOW / JOB</th><th>CUSTOMER</th><th>SUBCONTRACTOR</th><th>PLAN / ACTUAL</th><th>OPERATION</th><th>VALIDATION</th><th>OTD</th></tr></thead><tbody>{records.map((record) => <tr key={record.id}><td><b>{record.work_date}</b><small>{record.owner_name}</small></td><td><b>{record.job_code}</b><small>{record.flow} · {record.container_no || "No container"}</small></td><td>{record.customer}</td><td>{record.subcontractor}<small>{record.equipment_type}</small></td><td><b>{displayDate(record.plan_at)}</b><small>{displayDate(record.actual_at)}</small></td><td><span className="ops-status">{record.operation_status}</span></td><td><span className={`pill ${record.validation_status.toLowerCase().replaceAll(" ", "-")}`}>{record.validation_status}</span></td><td><span className={`pill ${record.otd_status.toLowerCase().replaceAll(" ", "-")}`}>{record.otd_status}</span></td></tr>)}</tbody></table>{!loading && !records.length && <div className="empty">No records yet for {owner}. Use the form above to record the first job.</div>}</div></section>
  </div>;
}
