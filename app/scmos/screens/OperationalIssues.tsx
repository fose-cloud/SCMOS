"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { Issue, IssueForm, IssueSummary, NewIssue } from "../issues";
import {
  importIssues, loadIssueForm, loadIssues, loadIssueSummary, raiseIssue, updateIssue,
} from "../issues";
import { parseIssueWorkbook } from "../issuesExcel";
import type { Job } from "../ops";
import { css } from "../theme";
import { apiFetch } from "../api";

/**
 * What went wrong today, and which job it went wrong on.
 *
 * The team keeps this log in Excel. This is the same log, with one thing the
 * spreadsheet cannot do: an issue names a job reference, and the register knows
 * that reference, so the customer and the carrier come with it. "260617620220"
 * on a line in a spreadsheet is a number somebody has to go and look up; here it
 * is the shipment, with who it is for and who is moving it.
 *
 * Not every issue attaches. Six of the thirty-three rows in the delivered log
 * carry no reference at all, and some name a shipment that never became a job
 * here. Those stay in the log unattached rather than being dropped — an issue
 * against a shipment the register never held is still a real issue, and hiding
 * it would make the log lie about the week.
 */

const SEVERITY_TONE: Record<string, string> = {
  "วิกฤต": "#B3261E",
  "สูง": "#C2610F",
  "ปานกลาง": "#1E6FB8",
  "ต่ำ": "#5C7285",
};

export function OperationalIssues({ jobs, prefill, onPrefillTaken, onEscalate, onToast }: {
  /** The register, so a new issue can name a job the person is looking at. */
  jobs: Job[];
  /**
   * A job handed over from the workspace drawer.
   *
   * It carries the job key, so the issue attaches to that exact job rather than
   * to whatever a written reference happens to match. The form opens filled in
   * and waiting for the one thing only a person can supply — what went wrong.
   */
  prefill?: NewIssue | null;
  onPrefillTaken?: () => void;
  /**
   * Escalates one issue into a CAR/PAR case.
   *
   * The only way a case gets opened. The procedure is that a problem is logged
   * here first and only the ones that warrant it are escalated, so a case
   * always has an issue behind it — which is what carries what went wrong, when
   * it was found and who found it. A job on its own says none of that.
   */
  onEscalate?: (issue: Issue) => void;
  onToast: (message: string) => void;
}) {
  const [issues, setIssues] = useState<Issue[] | null>(null);
  const [summary, setSummary] = useState<IssueSummary | null>(null);
  const [form, setForm] = useState<IssueForm | null>(null);
  /**
   * The issue a file is being attached to, and the input that picks it.
   *
   * One hidden input for the whole table rather than one per row: a file input
   * per issue is a hundred of them on a busy month, and the browser keeps every
   * one alive.
   */
  const attachTo = useRef<number | null>(null);
  const attachInput = useRef<HTMLInputElement>(null);

  /**
   * Attaches a photograph or a document to an issue that already exists.
   *
   * After the issue is logged rather than while it is being typed: the file has
   * to belong to something, and until the issue is saved there is nothing for
   * it to belong to. Where it is kept is the API's business — the job's own
   * Images folder when the issue names a job, and a folder under the year and
   * the issue code when the reference never matched one.
   */
  async function attach(file: File) {
    const id = attachTo.current;
    if (!id) return;
    try {
      const body = new FormData();
      body.append("issueId", String(id));
      body.append("kind", /^image\//.test(file.type) ? "photo" : "document");
      body.append("file", file);
      const response = await apiFetch("/api/documents", { method: "POST", body });
      const reply = await response.json().catch(() => null) as { message?: string; error?: string } | null;
      onToast(reply?.message ?? reply?.error ?? (response.ok ? "แนบไฟล์แล้ว" : `แนบไฟล์ไม่สำเร็จ (${response.status})`));
    } catch (error) {
      onToast("แนบไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }
  const [status, setStatus] = useState("OUTSTANDING");
  const [severity, setSeverity] = useState("ALL");
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState<NewIssue>({ detail: "" });
  const file = useRef<HTMLInputElement>(null);

  /**
   * Reads the log and the tiles together.
   *
   * Sets nothing before the first await on purpose. `busy` here means "a write
   * is in flight", not "something is loading" — the first read says so itself
   * by leaving `issues` null, and a filter change swaps a list of tens of rows
   * fast enough that a spinner would only flicker.
   */
  const load = useCallback(async () => {
    const [list, tiles] = await Promise.all([
      loadIssues({ status, severity }),
      loadIssueSummary(),
    ]);
    if (list) setIssues(list);
    if (tiles) setSummary(tiles);
    if (!list) onToast("อ่านรายการปัญหาไม่สำเร็จ");
  }, [status, severity, onToast]);

  // Every setState in `load` is after an await, so it runs in a microtask
  // rather than while this body does — the rule cannot see past the await and
  // reads it as a synchronous set. The same idiom, and the same note, as
  // Administration; the genuine version of this (a busy flag set before the
  // fetch) was removed rather than silenced.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { void loadIssueForm().then((f) => f && setForm(f)); }, []);

  /**
   * Opens the form on a job sent over from the workspace drawer.
   *
   * Taken once and then cleared, so returning to this menu later does not
   * reopen a half-written form somebody had already walked away from.
   */
  useEffect(() => {
    if (!prefill) return;
    // A genuine synchronous set, and the right one: a prop arriving once has to
    // become editable state, because the next thing that happens to it is
    // somebody typing into it. The guard above means this runs on the render
    // the job arrives on and no other.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDraft(prefill);
    setAdding(true);
    onPrefillTaken?.();
  }, [prefill, onPrefillTaken]);

  /** Which statuses count as finished — the API's list, not a second one here. */
  const settled = useMemo(() => new Set(form?.settled ?? []), [form]);

  const rows = useMemo(() => {
    const wanted = query.trim().toLowerCase();
    if (!wanted || !issues) return issues ?? [];
    return issues.filter((issue) => [
      issue.code, issue.detail, issue.jobRef, issue.reporter, issue.owner,
      issue.category, issue.jobCustomer, issue.jobTrucker,
    ].some((field) => (field ?? "").toLowerCase().includes(wanted)));
  }, [issues, query]);

  async function changeStatus(issue: Issue, next: string) {
    const result = await updateIssue(issue.id, { status: next });
    if (!result.ok) { onToast("แก้สถานะไม่สำเร็จ — " + result.message); return; }
    onToast(`${issue.code} → ${next}`);
    void load();
  }

  async function save() {
    if (!draft.detail.trim()) { onToast("ต้องกรอกรายละเอียดปัญหา"); return; }
    setBusy(true);
    const result = await raiseIssue(draft);
    setBusy(false);
    if (!result.ok) { onToast("บันทึกไม่สำเร็จ — " + result.message); return; }
    onToast(result.message || "บันทึกแล้ว");
    setAdding(false);
    setDraft({ detail: "" });
    void load();
  }

  async function readFile(chosen: FileList | null) {
    const picked = chosen?.[0];
    if (!picked) return;
    setBusy(true);
    try {
      const parsed = await parseIssueWorkbook(picked);
      if (!parsed.issues.length) {
        onToast(`ไม่พบรายการปัญหาในไฟล์ (อ่านชีท ${parsed.sheet || "—"})`);
        return;
      }
      const result = await importIssues(parsed.issues);
      if (!result.ok) { onToast("นำเข้าไม่สำเร็จ — " + result.message); return; }
      onToast(result.skipped > 0
        ? `นำเข้า ${result.added} รายการ · ข้าม ${result.skipped} รายการที่มีรหัสอยู่แล้ว`
        : `นำเข้า ${result.added} รายการแล้ว`);
      void load();
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ — " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      {summary && (
        <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
          <Tile label="ปัญหาทั้งหมด" value={summary.total} />
          <Tile label="คงค้าง" value={summary.outstanding} tone="#C2610F" />
          <Tile label="วิกฤต" value={summary.critical} tone="#B3261E" />
          <Tile label="เกินเวลาเป้าหมาย" value={summary.overdue} tone="#B3261E"
            note={summary.outstanding > 0
              ? `จากที่ยังค้าง ${summary.outstanding} รายการ`
              : "ไม่มีงานค้าง"} />
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <Picker label="สถานะ" value={status} onChange={setStatus} options={[
          ["OUTSTANDING", "ที่ยังค้าง"],
          ["ALL", "ทั้งหมด"],
          ...(form?.statuses ?? []).map((s) => [s, s] as [string, string]),
        ]} />
        <Picker label="ความรุนแรง" value={severity} onChange={setSeverity} options={[
          ["ALL", "ทุกระดับ"],
          ...(form?.severities ?? []).map((s) => [s, s] as [string, string]),
        ]} />

        <label style={css("display:flex;flex-direction:column;gap:3px;min-width:220px;flex:1")}>
          <span style={LABEL}>ค้นหา</span>
          <input value={query} onChange={(e) => setQuery(e.target.value)}
            placeholder="รหัส · เลขงาน · ลูกค้า · ผู้รับผิดชอบ · รายละเอียด"
            style={css("height:30px;border:1px solid #D3DBE3;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit")} />
        </label>

        <input ref={file} type="file" accept=".xlsx,.xlsm,.xls" style={css("display:none")}
          onChange={(e) => { void readFile(e.target.files); e.target.value = ""; }} />
        <button onClick={() => file.current?.click()} disabled={busy} style={BTN_SECONDARY}>
          นำเข้าจาก Excel
        </button>
        <button onClick={() => setAdding((v) => !v)} style={BTN_PRIMARY}>
          {adding ? "ปิดฟอร์ม" : "+ แจ้งปัญหา"}
        </button>
      </div>

      {adding && form && (
        <AddIssue form={form} jobs={jobs} draft={draft} onField={(k, v) =>
          setDraft((prev) => ({ ...prev, [k]: v }))} onSave={save} busy={busy} />
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        {issues === null ? (
          <Note>กำลังอ่านรายการปัญหา…</Note>
        ) : rows.length === 0 ? (
          <Note>
            {issues.length === 0
              ? "ยังไม่มีปัญหาบันทึกไว้ — นำเข้าจากไฟล์ Excel ที่ทีมใช้อยู่ หรือกด แจ้งปัญหา"
              : "ไม่มีรายการที่ตรงกับตัวกรอง"}
          </Note>
        ) : (
          <div style={css("overflow-x:auto")}>
            {/* One input for the whole table. A file input per row would be a
                hundred of them on a busy month, all kept alive by the browser. */}
            <input
              ref={attachInput}
              type="file"
              accept="image/*,.pdf,.doc,.docx,.xls,.xlsx"
              style={css("display:none")}
              onChange={(e) => {
                const chosen = e.target.files?.[0];
                if (chosen) void attach(chosen);
                e.target.value = "";
              }}
            />
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["รหัส", "วันที่พบ", "แหล่ง", "งานที่เกี่ยวข้อง", "รายละเอียด",
                    "หมวด", "ความรุนแรง", "ผู้รับผิดชอบ", "กำหนดเสร็จ", "สถานะ",
                    "ไฟล์แนบ", ...(onEscalate ? ["CAR / PAR"] : [])].map((head) => (
                    <th key={head} style={TH}>{head}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((issue) => (
                  <tr key={issue.id} className="row-hover">
                    <td style={css(TD + ";font-family:'IBM Plex Mono',monospace;white-space:nowrap")}>{issue.code}</td>
                    <td style={css(TD + ";white-space:nowrap")}>
                      {issue.foundOn || "—"}
                      {issue.foundAt && <span style={css("color:#94A3B8")}> {issue.foundAt}</span>}
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}>{issue.source || "—"}</td>
                    <td style={css(TD + ";min-width:190px")}>
                      <JobLink issue={issue} />
                      {/* The lorry under the shipment it belongs to, rather
                          than three more columns on a table that already has
                          ten. Blank when nobody recorded it, which is honest. */}
                      {(issue.containerNo || issue.licence || issue.driver) && (
                        <div style={css("color:#7B8CA0;margin-top:3px;font-size:11px")}>
                          {[issue.containerNo, issue.licence, issue.driver].filter(Boolean).join(" · ")}
                        </div>
                      )}
                    </td>
                    <td style={css(TD + ";min-width:280px;max-width:420px")}>
                      <div>{issue.detail}</div>
                      {issue.impact && (
                        <div style={css("color:#94A3B8;margin-top:2px")}>ผลกระทบ: {issue.impact}</div>
                      )}
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}>{issue.category || "—"}</td>
                    <td style={css(TD + ";white-space:nowrap")}>
                      <span style={css(`color:${SEVERITY_TONE[issue.severity] ?? "#5C7285"};font-weight:600`)}>
                        {issue.severity || "—"}
                      </span>
                      {issue.slaHours > 0 && (
                        <span style={css("color:#94A3B8")}> · {issue.slaHours} ชม.</span>
                      )}
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}>{issue.owner || "—"}</td>
                    <td style={css(TD + ";white-space:nowrap")}>
                      {issue.dueOn || "—"}
                      {issue.overdue && (
                        <div style={css("color:#B3261E;font-weight:600")}>เกินเวลาเป้าหมาย</div>
                      )}
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}>
                      <select value={issue.status} disabled={busy}
                        onChange={(e) => void changeStatus(issue, e.target.value)}
                        style={css("height:26px;border:1px solid #D3DBE3;border-radius:4px;font-size:11.5px;font-family:inherit;padding:0 6px;background:"
                          + (settled.has(issue.status) ? "#F2F7F2" : "#fff"))}>
                        {(form?.statuses ?? [issue.status]).map((s) => (
                          <option key={s} value={s}>{s}</option>
                        ))}
                      </select>
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}>
                      <button
                        onClick={() => { attachTo.current = issue.id; attachInput.current?.click(); }}
                        className="ghost-btn"
                        style={css("height:26px;padding:0 10px;border:1px solid #D3DBE3;background:#fff;color:#465A6E;border-radius:4px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}
                      >
                        แนบไฟล์
                      </button>
                    </td>
                    {onEscalate && (
                      <td style={css(TD + ";white-space:nowrap")}>
                        {/* Offered on every issue rather than only the severe
                            ones. Which problems warrant a case is a judgement
                            the quality team makes, not a threshold this screen
                            should be enforcing on their behalf. */}
                        <button
                          onClick={() => onEscalate(issue)}
                          className="ghost-btn"
                          style={css("height:26px;padding:0 10px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}
                        >
                          เปิด CAR/PAR
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.7")}>
        เลขงานที่บันทึกไว้จะถูกจับคู่กับทะเบียนงานอัตโนมัติ — ถ้าจับคู่ได้จะแสดงลูกค้าและผู้ขนส่งของงานนั้นให้
        ถ้าจับคู่ไม่ได้ รายการยังอยู่ในบันทึกตามเดิม เพราะปัญหาที่เกิดกับ shipment ที่ไม่เคยเข้าทะเบียนก็ยังเป็นปัญหาจริง ·
        เวลาเป้าหมายมาจากระดับความรุนแรง (วิกฤต 4 ชม. · สูง 8 · ปานกลาง 24 · ต่ำ 48) และนับเฉพาะรายการที่ยังไม่ปิด
      </div>
    </div>
  );
}

/** The job an issue attached to, or the reference it named and could not find. */
function JobLink({ issue }: { issue: Issue }) {
  if (!issue.jobRef && !issue.jobKey) return <span style={css("color:#C4CDD8")}>—</span>;
  if (!issue.jobKey) {
    return (
      <div>
        <div style={css("font-family:'IBM Plex Mono',monospace")}>{issue.jobRef}</div>
        <div style={css("color:#B08A5A")}>ไม่พบงานนี้ในทะเบียน</div>
      </div>
    );
  }
  return (
    <div>
      <div style={css("font-family:'IBM Plex Mono',monospace")}>{issue.jobRef}</div>
      <div style={css("color:#0A2240;font-weight:600")}>{issue.jobCustomer || "—"}</div>
      <div style={css("color:#7B8CA0")}>
        {issue.jobTrucker || "ยังไม่มีผู้ขนส่ง"}{issue.jobDate ? ` · ${issue.jobDate}` : ""}
      </div>
    </div>
  );
}

function AddIssue({ form, jobs, draft, onField, onSave, busy }: {
  form: IssueForm;
  jobs: Job[];
  draft: NewIssue;
  onField: (key: string, value: string) => void;
  onSave: () => void;
  busy: boolean;
}) {
  /**
   * The references a person can pick from, so an issue attaches without
   * anybody copying a number across. Capped at what a datalist can usefully
   * offer — the field takes anything typed, so the cap narrows the suggestions
   * rather than what may be recorded.
   */
  const refs = useMemo(() => {
    const seen = new Set<string>();
    jobs.forEach((job) => {
      [job.jobCode, job.abs, job.container].forEach((value) => {
        const clean = (value ?? "").trim();
        if (clean) seen.add(clean);
      });
    });
    return [...seen].slice(0, 500);
  }, [jobs]);

  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:15px 16px;display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:flex;gap:12px;flex-wrap:wrap")}>
        <Field label="วันที่พบ (วว/ดด/ปปปป)" width="150px">
          <input value={draft.foundOn ?? ""} onChange={(e) => onField("foundOn", e.target.value)}
            placeholder="24/08/2026" style={INPUT} />
        </Field>
        <Field label="เวลา" width="90px">
          <input value={draft.foundAt ?? ""} onChange={(e) => onField("foundAt", e.target.value)}
            placeholder="14:30" style={INPUT} />
        </Field>
        <Field label="แหล่งปัญหา" width="170px">
          <Select value={draft.source ?? ""} onChange={(v) => onField("source", v)} options={form.sources} />
        </Field>
        <Field label="ผู้แจ้ง / บริษัทขนส่ง" width="190px">
          <input value={draft.reporter ?? ""} onChange={(e) => onField("reporter", e.target.value)} style={INPUT} />
        </Field>
        <Field label="เลขงาน / Shipment Ref." width="220px">
          <input list="scmos-issue-refs" value={draft.jobRef ?? ""}
            onChange={(e) => onField("jobRef", e.target.value)} style={INPUT}
            placeholder="เลือกหรือพิมพ์เลขงาน" />
          <datalist id="scmos-issue-refs">
            {refs.map((ref) => <option key={ref} value={ref} />)}
          </datalist>
        </Field>
      </div>

      {/*
        The lorry the problem happened on, beside the shipment reference rather
        than buried under the detail. These are the three things a person on the
        phone can always give you — the driver's name, the box number and the
        plate — and the three a CAR/PAR raised from this issue has to name
        later, by which time nobody remembers the box number.
      */}
      <div style={css("display:flex;gap:12px;flex-wrap:wrap")}>
        <Field label="ชื่อ - สกุล พนักงานขับรถ" width="240px">
          <input value={draft.driver ?? ""} onChange={(e) => onField("driver", e.target.value)}
            style={INPUT} placeholder="เช่น นายสมชาย ใจดี" />
        </Field>
        <Field label="Container No." width="190px">
          <input value={draft.containerNo ?? ""} onChange={(e) => onField("containerNo", e.target.value)}
            style={css(INPUT_RAW + ";font-family:'IBM Plex Mono',monospace;text-transform:uppercase")} />
        </Field>
        <Field label="ทะเบียนรถ" width="160px">
          <input value={draft.licence ?? ""} onChange={(e) => onField("licence", e.target.value)}
            style={css(INPUT_RAW + ";font-family:'IBM Plex Mono',monospace")} />
        </Field>

        {/*
          Only on an accident, because it means nothing on anything else — and
          asked for there because the carrier scorecard weights a major accident
          at 35% against a minor one's 15%. An accident left ungraded is counted
          and reported and kept out of the score: guessing which it was would put
          a third of somebody's mark on a guess.
        */}
        {(draft.category ?? "") === ACCIDENT_CATEGORY && (
          <Field label="ระดับอุบัติเหตุ (KPI)" width="170px">
            <Select value={draft.accidentGrade ?? ""} onChange={(v) => onField("accidentGrade", v)}
              options={["Minor", "Major"]} />
          </Field>
        )}
      </div>

      <Field label="รายละเอียดปัญหา" width="100%">
        <textarea value={draft.detail} onChange={(e) => onField("detail", e.target.value)} rows={2}
          style={css("border:1px solid #C9D6E2;border-radius:4px;padding:7px 10px;font-size:12.5px;font-family:inherit;resize:vertical;width:100%")} />
      </Field>

      <div style={css("display:flex;gap:12px;flex-wrap:wrap")}>
        <Field label="หมวดปัญหา" width="220px">
          <Select value={draft.category ?? ""} onChange={(v) => onField("category", v)} options={form.categories} />
        </Field>
        <Field label="ความรุนแรง" width="140px">
          <Select value={draft.severity ?? ""} onChange={(v) => onField("severity", v)} options={form.severities} />
        </Field>
        <Field label="ช่องทางแจ้ง" width="190px">
          <Select value={draft.channel ?? ""} onChange={(v) => onField("channel", v)} options={form.channels} />
        </Field>
        <Field label="ผู้รับผิดชอบ" width="200px">
          <Select value={draft.owner ?? ""} onChange={(v) => onField("owner", v)} options={form.owners} />
        </Field>
        <Field label="กำหนดเสร็จ" width="170px">
          <input value={draft.dueOn ?? ""} onChange={(e) => onField("dueOn", e.target.value)}
            placeholder="25/08/2026 09:00" style={INPUT} />
        </Field>
      </div>

      <Field label="ผลกระทบต่อการขนส่ง" width="100%">
        <input value={draft.impact ?? ""} onChange={(e) => onField("impact", e.target.value)} style={INPUT} />
      </Field>

      <div style={css("display:flex;gap:10px;align-items:center")}>
        <button onClick={onSave} disabled={busy || !draft.detail.trim()} style={BTN_PRIMARY}>
          {busy ? "กำลังบันทึก…" : "บันทึกปัญหา"}
        </button>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>
          รหัสปัญหาออกให้อัตโนมัติต่อจากเลขล่าสุด · ไม่ระบุผู้รับผิดชอบจะถือว่าเป็นของผู้บันทึก
        </span>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
// The declaration, kept as text so a field that wants one more rule can add
// to it rather than restate the whole thing and drift from it.
/**
 * The category an accident is logged under.
 *
 * Written here and in CarrierScorecard.cs, which is one copy too many —
 * but the list itself comes from the API, and hard-coding the whole list
 * on this side to avoid repeating one string would be the worse trade.
 */
const ACCIDENT_CATEGORY = "ความปลอดภัย/อุบัติเหตุ";

const INPUT_RAW = "height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit;width:100%";
const INPUT = css(INPUT_RAW);
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;font-family:inherit;background:#fff;width:100%");
const TH = css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap");
const TD = "padding:8px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top";
const BTN_PRIMARY = css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit");
const BTN_SECONDARY = css("height:32px;padding:0 14px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit");

function Tile({ label, value, tone, note }: { label: string; value: number; tone?: string; note?: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;min-width:150px;display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:22px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${tone ?? "#0A2240"}`)}>
        {value.toLocaleString("en-US")}
      </span>
      {note && <span style={css("font-size:10.5px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}

function Picker({ label, value, onChange, options }: {
  label: string; value: string; onChange: (v: string) => void; options: [string, string][];
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:3px;min-width:170px")}>
      <span style={LABEL}>{label}</span>
      <select value={value} onChange={(e) => onChange(e.target.value)} style={SELECT}>
        {options.map(([key, text]) => <option key={key} value={key}>{text}</option>)}
      </select>
    </label>
  );
}

function Select({ value, onChange, options }: {
  value: string; onChange: (v: string) => void; options: string[];
}) {
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)} style={SELECT}>
      <option value="">—</option>
      {options.map((option) => <option key={option} value={option}>{option}</option>)}
    </select>
  );
}

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`
      + (width === "100%" ? ";width:100%" : ""))}>
      <span style={LABEL}>{label}</span>
      {children}
    </label>
  );
}

function Note({ children }: { children: React.ReactNode }) {
  return (
    <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
      {children}
    </div>
  );
}
