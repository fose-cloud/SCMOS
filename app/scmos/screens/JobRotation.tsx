"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  RotationEdit, RotationOptions, RotationOwner, RotationRow, RotationSupplierOption,
} from "../rotation";
import {
  deleteRotation, loadRotation, loadRotationOptions, loadRotationOwners,
  replaceRotation, saveRotation,
} from "../rotation";
import { parseRotationWorkbook } from "../rotationExcel";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

/**
 * Who is responsible for which customer.
 *
 * The register has always said which operator a job belongs to and never what
 * that ought to be — a job arrives carrying a name off a plan sheet and nothing
 * checked it. The team keeps the answer in a rotation workbook; this is that
 * workbook, read back beside the jobs it is about.
 *
 * Which makes one column worth more than the rest: how many of a customer's
 * jobs are sitting with somebody the rotation does not name. Zero is the normal
 * answer, and anything else is either a handover nobody recorded or a job that
 * went to the wrong person. The screen does not guess which — it says the
 * number and leaves the judgement to whoever knows what happened that week.
 */

export function JobRotation({ me, canManage, onToast }: {
  /** The signed-in operator's directory id, so their own page opens first. */
  me: string;
  /** AssignJobs is granted to Operation Supervisor and every role above it. */
  canManage: boolean;
  onToast: (message: string) => void;
}) {
  const [owners, setOwners] = useState<RotationOwner[] | null>(null);
  const [rows, setRows] = useState<RotationRow[] | null>(null);
  const [options, setOptions] = useState<RotationOptions | null>(null);
  const [owner, setOwner] = useState(me);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [draft, setDraft] = useState<RotationEdit | null>(null);
  const [deleteId, setDeleteId] = useState<number | null>(null);
  const file = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    const [list, people] = await Promise.all([
      loadRotation(owner === "ALL" ? {} : { ownerId: owner }),
      loadRotationOwners(),
    ]);
    if (list) setRows(list);
    if (people) setOwners(people);
    if (!list) onToast("อ่านตารางความรับผิดชอบไม่สำเร็จ");
  }, [owner, onToast]);

  // Every setState in `load` is after an await; see the same note in
  // Administration and Operational Issues.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  // The masters do not change when somebody switches owner tabs. Fetch them
  // once for editors instead of adding two database reads to every tab click.
  useEffect(() => {
    if (!canManage) return;
    let cancelled = false;
    void loadRotationOptions().then((choices) => {
      if (!cancelled && choices) setOptions(choices);
      if (!cancelled && !choices) onToast("อ่าน Staff/Subcontractor Master ไม่สำเร็จ");
    });
    return () => { cancelled = true; };
  }, [canManage, onToast]);

  const shown = useMemo(() => {
    const wanted = query.trim().toLowerCase();
    if (!wanted || !rows) return rows ?? [];
    return rows.filter((row) => [row.customer, row.subFcl, row.subLcl, row.csLcb,
      row.primaryName, row.backupName, row.backup2Name]
      .some((field) => (field ?? "").toLowerCase().includes(wanted)));
  }, [rows, query]);

  const misassigned = useMemo(
    () => shown.reduce((sum, row) => sum + row.elsewhere, 0), [shown]);
  const heldJobs = useMemo(
    () => shown.reduce((sum, row) => sum + row.jobs, 0), [shown]);

  async function readFile(chosen: FileList | null) {
    const picked = chosen?.[0];
    if (!picked) return;
    setBusy(true);
    try {
      const parsed = await parseRotationWorkbook(picked);
      if (!parsed.rows.length) {
        onToast("ไม่พบตารางความรับผิดชอบในไฟล์นี้");
        return;
      }
      const result = await replaceRotation(parsed.rows);
      if (!result.ok) { onToast("นำเข้าไม่สำเร็จ — " + result.message); return; }
      onToast(`นำเข้า ${result.added} รายการจาก ${parsed.sheets.length} ชีท`
        + (result.replaced > 0 ? ` · แทนที่ของเดิม ${result.replaced} รายการ` : ""));
      void load();
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ — " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setBusy(false);
    }
  }

  function beginAdd() {
    setEditingId(null);
    setDeleteId(null);
    setDraft({
      customer: "",
      import: false, export: false, fcl: false, lcl: false, domestic: false,
      primaryId: me, backupId: "", backup2Id: "",
      subFclSupplierIds: [], subLclSupplierIds: [], csLcb: "",
    });
  }

  function beginEdit(row: RotationRow) {
    setEditingId(row.id);
    setDeleteId(null);
    setDraft({
      customer: row.customer,
      import: row.import, export: row.export, fcl: row.fcl, lcl: row.lcl,
      domestic: row.domestic,
      primaryId: row.primaryId, backupId: row.backupId, backup2Id: row.backup2Id,
      subFclSupplierIds: row.subFclSupplierIds,
      subLclSupplierIds: row.subLclSupplierIds,
      csLcb: row.csLcb,
    });
  }

  async function commit(edit: RotationEdit) {
    if (busy) return;
    setBusy(true);
    try {
      const result = await saveRotation(edit, editingId ?? undefined);
      onToast(result.ok ? result.message : "บันทึกไม่สำเร็จ — " + result.message);
      if (!result.ok) return;
      setDraft(null);
      setEditingId(null);
      setOwner(edit.primaryId || "ALL");
      await load();
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: number) {
    if (busy) return;
    setBusy(true);
    try {
      const result = await deleteRotation(id);
      onToast(result.ok ? result.message : "ลบไม่สำเร็จ — " + result.message);
      if (!result.ok) return;
      setDeleteId(null);
      if (editingId === id) { setEditingId(null); setDraft(null); }
      await load();
    } finally {
      setBusy(false);
    }
  }

  const person = owners?.find((o) => o.id === owner);

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      {owners && owners.length > 0 && (
        <div style={css("display:flex;gap:9px;flex-wrap:wrap")}>
          {owners.map((each) => (
            <button key={each.email} onClick={() => setOwner(each.id || "ALL")}
              style={css("background:" + (each.id === owner ? "#0A2240" : "#fff")
                + ";color:" + (each.id === owner ? "#fff" : "#31465C")
                + ";border:1px solid " + (each.id === owner ? "#0A2240" : "#D8E0E8")
                + ";border-radius:6px;padding:9px 13px;cursor:pointer;font-family:inherit;text-align:left;display:flex;flex-direction:column;gap:2px;min-width:150px")}>
              <span style={css("font-size:12.5px;font-weight:600")}>{each.name}</span>
              <span style={css("font-size:10.5px;opacity:.75")}>
                {each.customers} ลูกค้าหลัก{each.asBackup > 0 ? ` · สำรอง ${each.asBackup}` : ""}
              </span>
              {!each.id && (
                <span style={css("font-size:10px;color:" + (each.id === owner ? "#F0C36D" : "#B08A5A"))}>
                  ไม่มีในทะเบียนผู้ใช้
                </span>
              )}
            </button>
          ))}
          <button onClick={() => setOwner("ALL")}
            style={css("background:" + (owner === "ALL" ? "#0A2240" : "#fff")
              + ";color:" + (owner === "ALL" ? "#fff" : "#31465C")
              + ";border:1px solid " + (owner === "ALL" ? "#0A2240" : "#D8E0E8")
              + ";border-radius:6px;padding:9px 13px;cursor:pointer;font-family:inherit;font-size:12.5px;font-weight:600")}>
            ทุกคน
          </button>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px;min-width:240px;flex:1")}>
          <span style={LABEL}>ค้นหา</span>
          <input value={query} onChange={(e) => setQuery(e.target.value)}
            placeholder="ลูกค้า · ผู้ขนส่ง · ผู้รับผิดชอบ"
            style={css("height:30px;border:1px solid #D3DBE3;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit")} />
        </label>
        <Tile label="ลูกค้าในมุมมองนี้" value={String(shown.length)} />
        <Tile label="งานในทะเบียน" value={String(heldJobs)} />
        <Tile label="งานที่อยู่กับคนนอกตาราง" value={String(misassigned)}
          tone={misassigned > 0 ? "#B3261E" : undefined}
          note={misassigned > 0 ? "ตรวจว่าเป็นการรับช่วงหรือมอบหมายผิด" : "ตรงกับตารางทั้งหมด"} />
        <input ref={file} type="file" accept=".xlsx,.xlsm,.xls" style={css("display:none")}
          onChange={(e) => { void readFile(e.target.files); e.target.value = ""; }} />
        {canManage ? (
          <>
            <button onClick={beginAdd} disabled={busy} style={BTN_PRIMARY}>+ เพิ่มลูกค้า</button>
            <button onClick={() => file.current?.click()} disabled={busy} style={BTN_SECONDARY}>
              {busy ? "กำลังทำรายการ…" : "นำเข้าตารางจาก Excel"}
            </button>
          </>
        ) : (
          <span style={css("font-size:11px;color:#7B8CA0")}>
            การเพิ่ม แก้ไข และลบ สำหรับ Supervisor ขึ้นไป
          </span>
        )}
      </div>

      {draft && (
        options ? (
          <RotationEditor
            value={draft}
            people={options.people}
            suppliers={options.suppliers}
            busy={busy}
            editing={editingId !== null}
            onChange={setDraft}
            onSave={() => { void commit(draft); }}
            onCancel={() => { setDraft(null); setEditingId(null); }}
          />
        ) : (
          <Note>กำลังอ่านรายชื่อจาก Staff และ Subcontractor Master…</Note>
        )
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        {rows === null ? (
          <Note>กำลังอ่านตารางความรับผิดชอบ…</Note>
        ) : shown.length === 0 ? (
          <Note>
            {rows.length === 0 && owner !== "ALL" && person
              ? `ตารางไม่ได้ระบุลูกค้าให้ ${person.name}`
              : rows.length === 0
                ? "ยังไม่ได้นำเข้าตารางความรับผิดชอบ — กดนำเข้าจากไฟล์ ROTATE ที่ทีมใช้อยู่"
                : "ไม่มีลูกค้าที่ตรงกับคำค้น"}
          </Note>
        ) : (
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["ลูกค้า", "ประเภทงาน", "ผู้รับผิดชอบหลัก", "สำรอง 1", "สำรอง 2",
                    "ผู้ขนส่ง FCL", "ผู้ขนส่ง LCL", "CS LCB", "งานในทะเบียน",
                    ...(canManage ? ["จัดการ"] : [])].map((head) => (
                    <th key={head} style={TH}>{head}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {shown.map((row) => (
                  <tr key={row.id} className="row-hover">
                    <td style={css(TD + ";font-weight:600;color:#0A2240;min-width:180px")}>
                      {row.customer}
                      <div style={css("font-weight:400;color:#94A3B8;font-size:10.5px")}>{row.sheet}</div>
                    </td>
                    <td style={css(TD + ";white-space:nowrap")}><Modes row={row} /></td>
                    <td style={css(TD + ";min-width:160px")}><Person name={row.primaryName} contact={row.primaryContact} email={row.primaryEmail} /></td>
                    <td style={css(TD + ";min-width:160px")}><Person name={row.backupName} contact={row.backupContact} email={row.backupEmail} /></td>
                    <td style={css(TD + ";min-width:160px")}><Person name={row.backup2Name} contact={row.backup2Contact} email={row.backup2Email} /></td>
                    <td style={css(TD + ";min-width:130px")}>{row.subFcl || "—"}</td>
                    <td style={css(TD + ";min-width:110px")}>{row.subLcl || "—"}</td>
                    <td style={css(TD + ";min-width:170px;color:#5C7285")}>{row.csLcb || "—"}</td>
                    <td style={css(TD + ";white-space:nowrap;text-align:right;font-family:'IBM Plex Mono',monospace")}>
                      {row.jobs || "—"}
                      {row.elsewhere > 0 && (
                        <div style={css("color:#B3261E;font-weight:600;font-family:inherit;font-size:10.5px")}>
                          {row.elsewhere} อยู่กับคนนอกตาราง
                        </div>
                      )}
                    </td>
                    {canManage && (
                      <td style={css(TD + ";white-space:nowrap")}>
                        {deleteId === row.id ? (
                          <span style={css("display:flex;gap:5px")}>
                            <button disabled={busy} onClick={() => { void remove(row.id); }}
                              style={BTN_DANGER}>ยืนยันลบ</button>
                            <button disabled={busy} onClick={() => setDeleteId(null)}
                              style={BTN_TINY}>ยกเลิก</button>
                          </span>
                        ) : (
                          <span style={css("display:flex;gap:5px")}>
                            <button disabled={busy} onClick={() => beginEdit(row)}
                              style={BTN_TINY}>แก้ไข</button>
                            <button disabled={busy} onClick={() => setDeleteId(row.id)}
                              style={BTN_DANGER_OUTLINE}>ลบ</button>
                          </span>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </ZoomBox>
        )}
      </div>

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.7")}>
        ตารางนี้มาจากไฟล์ ROTATE ที่ทีมใช้อยู่ — นำเข้าครั้งใหม่จะแทนที่ทั้งตาราง ไม่ใช่รวมกัน
        เพราะเป็นเอกสารที่ออกใหม่ทั้งฉบับเมื่อมีการสับเปลี่ยน ถ้ารวมกันจะเหลือลูกค้าที่ยังผูกกับคนที่ไม่ได้ดูแลแล้ว ·
        ช่อง &ldquo;งานในทะเบียน&rdquo; นับจากงานจริงของลูกค้ารายนั้น ส่วนตัวเลขสีแดงคืองานที่อยู่กับคนที่ตารางไม่ได้ระบุไว้
        ซึ่งอาจเป็นการรับช่วงที่ไม่ได้บันทึก หรือมอบหมายผิด — ระบบไม่เดาให้ว่าเป็นอย่างไหน
      </div>
    </div>
  );
}

function Modes({ row }: { row: RotationRow }) {
  const modes = [
    ["IMPORT", row.import], ["EXPORT", row.export],
    ["FCL", row.fcl], ["LCL", row.lcl], ["DOM", row.domestic],
  ].filter(([, on]) => on).map(([name]) => name as string);
  if (!modes.length) return <span style={css("color:#C4CDD8")}>—</span>;
  return (
    <span style={css("display:flex;gap:4px;flex-wrap:wrap")}>
      {modes.map((mode) => (
        <span key={mode} style={css("background:#EEF3F8;color:#31465C;border-radius:3px;padding:1px 6px;font-size:10px;font-weight:600")}>
          {mode}
        </span>
      ))}
    </span>
  );
}

/**
 * A person as the rotation names them.
 *
 * The name comes from the staff directory when the email matches one; when it
 * does not, the email itself is shown rather than a blank. A rotation naming
 * somebody the directory has never heard of is worth seeing — it usually means
 * a leaver, or an address typed wrong — and a blank cell hides it.
 */
function Person({ name, contact, email }: { name: string; contact: string; email: string }) {
  if (!contact && !email) return <span style={css("color:#C4CDD8")}>—</span>;
  const phone = contact.replace(email, "").replace(/^[\s,;]+/, "").trim();
  return (
    <div>
      <div style={css("font-weight:600;color:" + (name ? "#0A2240" : "#B08A5A"))}>
        {name || email || contact}
      </div>
      {phone && <div style={css("color:#7B8CA0;font-size:10.5px")}>{phone}</div>}
      {!name && email && (
        <div style={css("color:#B08A5A;font-size:10px")}>ไม่มีในทะเบียนผู้ใช้</div>
      )}
    </div>
  );
}

function RotationEditor({ value, people, suppliers, busy, editing, onChange, onSave, onCancel }: {
  value: RotationEdit;
  people: RotationOptions["people"];
  suppliers: RotationSupplierOption[];
  busy: boolean;
  editing: boolean;
  onChange: (next: RotationEdit) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const set = <K extends keyof RotationEdit>(field: K, next: RotationEdit[K]) =>
    onChange({ ...value, [field]: next });

  return (
    <div style={css("background:#fff;border:1px solid #B8CBE0;border-radius:6px;padding:15px 16px;box-shadow:0 5px 16px rgba(10,34,64,.08)")}>
      <div style={css("display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:12px")}>
        <div>
          <div style={css("font-size:13px;font-weight:700;color:#0A2240")}>
            {editing ? "แก้ไข Job Rotation" : "เพิ่มลูกค้าและผู้รับผิดชอบ"}
          </div>
          <div style={css("font-size:10.5px;color:#7B8CA0;margin-top:2px")}>
            ผู้รับผิดชอบมาจาก Staff Directory · ผู้ขนส่งมาจาก Subcontractor Master
          </div>
        </div>
        <button type="button" onClick={onCancel} disabled={busy} style={BTN_TINY}>ปิด</button>
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px")}>
        <Field label="ลูกค้า">
          <input value={value.customer} onChange={(event) => set("customer", event.target.value)}
            placeholder="ชื่อลูกค้า" style={INPUT} />
        </Field>
        <PersonSelect label="ผู้รับผิดชอบหลัก" value={value.primaryId}
          people={people} required onChange={(id) => set("primaryId", id)} />
        <PersonSelect label="สำรอง 1" value={value.backupId}
          people={people} onChange={(id) => set("backupId", id)} />
        <PersonSelect label="สำรอง 2" value={value.backup2Id}
          people={people} onChange={(id) => set("backup2Id", id)} />
        <Field label="CS LCB">
          <input value={value.csLcb} onChange={(event) => set("csLcb", event.target.value)}
            placeholder="ชื่อหรือข้อมูลติดต่อ" style={INPUT} />
        </Field>
      </div>

      <div style={css("display:flex;gap:13px;align-items:center;flex-wrap:wrap;margin-top:12px;padding:10px 11px;background:#F7F9FB;border-radius:4px")}>
        <span style={LABEL}>ประเภทงาน</span>
        {([
          ["import", "IMPORT"], ["export", "EXPORT"], ["fcl", "FCL"],
          ["lcl", "LCL"], ["domestic", "DOMESTIC"],
        ] as [keyof Pick<RotationEdit, "import" | "export" | "fcl" | "lcl" | "domestic">, string][])
          .map(([field, label]) => (
            <label key={field} style={CHECK}>
              <input type="checkbox" checked={value[field]}
                onChange={(event) => set(field, event.target.checked)} />
              {label}
            </label>
          ))}
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:10px;margin-top:12px")}>
        <SupplierPicker label="ผู้ขนส่ง FCL" options={suppliers.filter((item) => item.fcl)}
          selected={value.subFclSupplierIds}
          onChange={(ids) => set("subFclSupplierIds", ids)} />
        <SupplierPicker label="ผู้ขนส่ง LCL" options={suppliers.filter((item) => item.lcl)}
          selected={value.subLclSupplierIds}
          onChange={(ids) => set("subLclSupplierIds", ids)} />
      </div>

      <div style={css("display:flex;gap:8px;justify-content:flex-end;margin-top:14px;padding-top:12px;border-top:1px solid #EEF3F8")}>
        <button type="button" onClick={onCancel} disabled={busy} style={BTN_SECONDARY}>ยกเลิก</button>
        <button type="button" onClick={onSave} disabled={busy} style={BTN_PRIMARY}>
          {busy ? "กำลังบันทึก…" : editing ? "บันทึกการแก้ไข" : "เพิ่มรายการ"}
        </button>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={css("display:flex;flex-direction:column;gap:4px")}>
      <span style={LABEL}>{label}</span>
      {children}
    </div>
  );
}

function PersonSelect({ label, value, people, required = false, onChange }: {
  label: string;
  value: string;
  people: RotationOptions["people"];
  required?: boolean;
  onChange: (id: string) => void;
}) {
  return (
    <Field label={label}>
      <select value={value} onChange={(event) => onChange(event.target.value)} style={INPUT}>
        <option value="">{required ? "— เลือกผู้รับผิดชอบ —" : "— ไม่มี —"}</option>
        {people.map((person) => (
          <option key={person.id} value={person.id} disabled={!person.active}>
            {person.name} · {person.id}{person.active ? "" : " (ปิดใช้งาน)"}
          </option>
        ))}
      </select>
    </Field>
  );
}

function SupplierPicker({ label, options, selected, onChange }: {
  label: string;
  options: RotationSupplierOption[];
  selected: number[];
  onChange: (ids: number[]) => void;
}) {
  const chosen = options.filter((option) => selected.includes(option.id));
  function toggle(id: number, checked: boolean) {
    onChange(checked
      ? [...new Set([...selected, id])]
      : selected.filter((held) => held !== id));
  }

  return (
    <Field label={label}>
      <details style={css("position:relative;border:1px solid #C9D6E2;border-radius:4px;background:#fff")}>
        <summary style={css("min-height:31px;padding:7px 30px 7px 9px;font-size:12px;color:#31465C;cursor:pointer;list-style-position:inside")}>
          {chosen.length > 0
            ? chosen.map((option) => option.code || option.name).join(", ")
            : "— เลือกจาก Subcontractor Master —"}
        </summary>
        <div style={css("position:absolute;z-index:8;left:-1px;right:-1px;top:100%;max-height:230px;overflow:auto;background:#fff;border:1px solid #B8CBE0;border-radius:0 0 4px 4px;box-shadow:0 7px 18px rgba(10,34,64,.14);padding:6px")}>
          {options.length === 0 ? (
            <div style={css("padding:8px;font-size:11px;color:#94A3B8")}>
              ยังไม่มีผู้ขนส่งประเภทนี้ใน Subcontractor Master
            </div>
          ) : options.map((option) => (
            <label key={option.id}
              style={css("display:flex;gap:8px;align-items:flex-start;padding:6px 7px;border-radius:3px;cursor:pointer;font-size:11.5px;color:#31465C")}>
              <input type="checkbox" checked={selected.includes(option.id)}
                onChange={(event) => toggle(option.id, event.target.checked)} />
              <span>
                <b>{option.code || option.name}</b>
                {option.code && option.name !== option.code ? " · " + option.name : ""}
                {option.serviceType && (
                  <span style={css("display:block;font-size:10px;color:#94A3B8;margin-top:1px")}>
                    {option.serviceType}
                  </span>
                )}
              </span>
            </label>
          ))}
        </div>
      </details>
    </Field>
  );
}

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const TH = css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap");
const TD = "padding:8px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top";
const BTN_SECONDARY = css("height:32px;padding:0 14px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit");
const BTN_PRIMARY = css("height:32px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit");
const BTN_TINY = css("height:25px;padding:0 9px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:10.5px;font-weight:600;cursor:pointer;font-family:inherit");
const BTN_DANGER = css("height:25px;padding:0 9px;border:1px solid #B42318;background:#B42318;color:#fff;border-radius:4px;font-size:10.5px;font-weight:600;cursor:pointer;font-family:inherit");
const BTN_DANGER_OUTLINE = css("height:25px;padding:0 9px;border:1px solid #D9A8A4;background:#fff;color:#B42318;border-radius:4px;font-size:10.5px;font-weight:600;cursor:pointer;font-family:inherit");
const INPUT = css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;background:#fff;color:#1F3347;font-size:12px;font-family:inherit");
const CHECK = css("display:inline-flex;gap:5px;align-items:center;font-size:11.5px;color:#31465C;cursor:pointer");

function Tile({ label, value, tone, note }: { label: string; value: string; tone?: string; note?: string }) {
  return (
    <div style={css("display:flex;flex-direction:column;gap:2px;min-width:130px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:18px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${tone ?? "#0A2240"}`)}>{value}</span>
      {note && <span style={css("font-size:10px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}

function Note({ children }: { children: React.ReactNode }) {
  return (
    <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>{children}</div>
  );
}
