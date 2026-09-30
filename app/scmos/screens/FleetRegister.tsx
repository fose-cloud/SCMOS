"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { registerDate } from "../carrierPortal";
import {
  KIND_TH, driverFormProblem, filterDrivers, filterTrucks, paperTone, rowState, truckFormProblem,
  type DriverForm, type Fleet, type FleetPaper, type FleetRequirement, type PaperPick, type TruckForm,
} from "../fleet";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

type Replacing = { owner: "trucks" | "drivers"; id: number; label: string; paper: FleetRequirement };

const BLANK_TRUCK: TruckForm = { plate: "", kind: "head", vehicleType: "", dgCapable: false };
const BLANK_DRIVER: DriverForm = { name: "", phone: "", licenceNo: "" };

/**
 * Capacity Planning's two fleet tables (30 Sep 2026): heads and tails, then drivers, each with its papers.
 *
 * One component for both screens, as the department asked — its Capacity reads every carrier's rows, a
 * carrier's reads its own. Only the carrier's adds: a truck with its registration book and both insurances, a
 * driver with the licence, a newer file over an old one, and a truck or driver retired or brought back.
 */
export function FleetRegister({ carrier, onToast }: { carrier: boolean; onToast: (message: string) => void }) {
  const [fleet, setFleet] = useState<Fleet | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState<"" | "truck" | "driver">("");
  const [truckForm, setTruckForm] = useState<TruckForm>(BLANK_TRUCK);
  const [driverForm, setDriverForm] = useState<DriverForm>(BLANK_DRIVER);
  const [papers, setPapers] = useState<Record<string, PaperPick>>({});
  const [replacing, setReplacing] = useState<Replacing | null>(null);
  const [replacement, setReplacement] = useState<PaperPick>({ file: null, expiry: "" });
  const [search, setSearch] = useState("");
  const [kind, setKind] = useState("");
  const [supplier, setSupplier] = useState("");
  const [showInactive, setShowInactive] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch(carrier ? "/api/carrier/fleet" : "/api/fleet", { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as Fleet & { error?: string } | null;
      if (!response.ok || !body) throw new Error(body?.error ?? `เปิดทะเบียนรถไม่ได้ (${response.status})`);
      setFleet(body); setError("");
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : String(problem));
    }
  }, [carrier]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const suppliers = useMemo(() => [...new Set([...(fleet?.trucks ?? []), ...(fleet?.drivers ?? [])]
    .map((row) => row.supplier).filter(Boolean))].sort(), [fleet]);
  const trucks = useMemo(() => filterTrucks(fleet?.trucks ?? [], search, kind, showInactive)
    .filter((row) => !supplier || row.supplier === supplier), [fleet, search, kind, showInactive, supplier]);
  const drivers = useMemo(() => filterDrivers(fleet?.drivers ?? [], search, showInactive)
    .filter((row) => !supplier || row.supplier === supplier), [fleet, search, showInactive, supplier]);

  async function send(path: string, init: RequestInit): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(path, init);
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ทำรายการไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  function withPapers(body: FormData, required: FleetRequirement[]) {
    for (const need of required) {
      const pick = papers[need.code];
      if (pick?.file) body.append(need.code, pick.file);
      if (need.expires && pick?.expiry) body.append(`${need.code}-expiry`, registerDate(pick.expiry));
    }
    return body;
  }

  async function addTruck() {
    if (!fleet || truckFormProblem(truckForm, papers, fleet.truckPapers)) return;
    const body = new FormData();
    body.append("plate", truckForm.plate);
    body.append("kind", truckForm.kind);
    body.append("vehicleType", truckForm.vehicleType);
    body.append("dgCapable", String(truckForm.dgCapable));
    if (await send("/api/carrier/fleet/trucks", { method: "POST", body: withPapers(body, fleet.truckPapers) })) {
      setTruckForm(BLANK_TRUCK); setPapers({}); setAdding("");
    }
  }

  async function addDriver() {
    if (!fleet || driverFormProblem(driverForm, papers, fleet.driverPapers)) return;
    const body = new FormData();
    body.append("name", driverForm.name);
    body.append("phone", driverForm.phone);
    body.append("licenceNo", driverForm.licenceNo);
    if (await send("/api/carrier/fleet/drivers", { method: "POST", body: withPapers(body, fleet.driverPapers) })) {
      setDriverForm(BLANK_DRIVER); setPapers({}); setAdding("");
    }
  }

  async function replace() {
    if (!replacing || !replacement.file) return;
    const body = new FormData();
    body.append("code", replacing.paper.code);
    body.append("file", replacement.file);
    if (replacing.paper.expires && replacement.expiry) body.append("expiryDate", registerDate(replacement.expiry));
    if (await send(`/api/carrier/fleet/${replacing.owner}/${replacing.id}/documents`, { method: "POST", body })) {
      setReplacing(null); setReplacement({ file: null, expiry: "" });
    }
  }

  async function setActive(owner: "trucks" | "drivers", id: number, label: string, active: boolean) {
    if (!active && !window.confirm(`เลิกใช้ ${label}? จะไม่แสดงในรายการจัดรถ — กลับมาใช้ได้ภายหลัง`)) return;
    await send(`/api/carrier/fleet/${owner}/${id}/active`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ active }),
    });
  }

  function open(nextAdding: "truck" | "driver") {
    setAdding((was) => (was === nextAdding ? "" : nextAdding));
    setPapers({});
    setReplacing(null);
  }

  if (error) return <Notice tone="#B45309">{error}</Notice>;
  if (!fleet) return <Notice tone="#7B8CA0">กำลังโหลดทะเบียนรถ…</Notice>;

  const truckProblem = truckFormProblem(truckForm, papers, fleet.truckPapers);
  const driverProblem = driverFormProblem(driverForm, papers, fleet.driverPapers);
  const activeTrucks = fleet.trucks.filter((row) => row.status === "active");
  const activeDrivers = fleet.drivers.filter((row) => row.status === "active").length;
  const paperInputs = (required: FleetRequirement[]) => required.map((need) => (
    <Field key={need.code} label={need.thai + " *"}>
      <div style={css("display:flex;gap:6px;align-items:center")}>
        <input type="file" aria-label={need.thai} accept="application/pdf,image/*"
          onChange={(event) => { const file = event.target.files?.[0] ?? null; setPapers((was) => ({ ...was, [need.code]: { file, expiry: was[need.code]?.expiry ?? "" } })); }}
          style={css("font-size:11.5px;max-width:210px")} />
        {need.expires && <input type="date" aria-label={`${need.thai} วันหมดอายุ`} title="วันหมดอายุ" value={papers[need.code]?.expiry ?? ""}
          onChange={(event) => { const expiry = event.target.value; setPapers((was) => ({ ...was, [need.code]: { file: was[need.code]?.file ?? null, expiry } })); }}
          style={css(INPUT + "width:140px")} />}
      </div>
    </Field>
  ));

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:10px 14px;display:flex;gap:10px;flex-wrap:wrap;align-items:center")}>
        <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="ค้นหาทะเบียน ชื่อ หรือเลขใบขับขี่"
          aria-label="ค้นหาทะเบียนรถและพนักงานขับรถ" style={css(INPUT + "min-width:230px")} />
        {!carrier && (
          <select aria-label="ผู้ขนส่ง" value={supplier} onChange={(event) => setSupplier(event.target.value)} style={css(INPUT + "min-width:180px")}>
            <option value="">ผู้ขนส่งทั้งหมด</option>
            {suppliers.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        )}
        <label style={css("display:flex;gap:5px;align-items:center;font-size:12px;color:#5A6B7D")}>
          <input type="checkbox" checked={showInactive} onChange={(event) => setShowInactive(event.target.checked)} />แสดงที่เลิกใช้
        </label>
        {carrier && !fleet.storageReady && <span style={css("font-size:12px;color:#B45309")}>ที่เก็บไฟล์ยังไม่พร้อม — ยังอัปโหลดเอกสารไม่ได้</span>}
      </div>

      {/* ------------------------------------------------ heads and tails */}
      <div style={css(PANEL)}>
        <div style={css(PANEL_HEAD)}>
          <span>ทะเบียนรถ หัว - หาง · {activeTrucks.filter((row) => row.kind === "head").length} หัว · {activeTrucks.filter((row) => row.kind === "tail").length} หาง</span>
          <select aria-label="หัวหรือหาง" value={kind} onChange={(event) => setKind(event.target.value)} style={css(INPUT + "height:27px;font-weight:400")}>
            <option value="">หัวและหาง</option><option value="head">หัว</option><option value="tail">หาง</option>
          </select>
          {carrier && <button onClick={() => open("truck")} style={css(ADD)}>{adding === "truck" ? "ปิดฟอร์ม" : "+ เพิ่มรถ"}</button>}
        </div>
        {carrier && adding === "truck" && (
          <div style={css(FORM)}>
            <Field label="ทะเบียนรถ *">
              <input aria-label="ทะเบียนรถ" value={truckForm.plate} maxLength={60} onChange={(event) => setTruckForm({ ...truckForm, plate: event.target.value })}
                placeholder="70-1234 กทม" style={css(INPUT + "width:150px")} />
            </Field>
            <Field label="หัว / หาง *">
              <select aria-label="หัว / หาง" value={truckForm.kind} onChange={(event) => setTruckForm({ ...truckForm, kind: event.target.value })} style={css(INPUT)}>
                <option value="head">หัว</option><option value="tail">หาง</option>
              </select>
            </Field>
            <Field label="ประเภทรถ *">
              <select aria-label="ประเภทรถ" value={truckForm.vehicleType} onChange={(event) => setTruckForm({ ...truckForm, vehicleType: event.target.value })} style={css(INPUT)}>
                <option value="">เลือก</option>
                {fleet.vehicleTypes.map((one) => <option key={one} value={one}>{one}</option>)}
              </select>
            </Field>
            <label style={css("display:flex;gap:5px;align-items:center;font-size:12px;color:#334155;height:31px")}>
              <input type="checkbox" checked={truckForm.dgCapable} onChange={(event) => setTruckForm({ ...truckForm, dgCapable: event.target.checked })} />ขนสินค้าอันตราย (DG)
            </label>
            {paperInputs(fleet.truckPapers)}
            <Submit busy={busy} problem={truckProblem} onClick={() => void addTruck()}>ลงทะเบียนรถ</Submit>
          </div>
        )}
        {replacing?.owner === "trucks" && <ReplaceBar replacing={replacing} pick={replacement} busy={busy}
          onPick={setReplacement} onSave={() => void replace()} onCancel={() => setReplacing(null)} />}
        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:860px")}>
            <thead><tr>
              {[...(carrier ? [] : ["ผู้ขนส่ง"]), "ทะเบียน", "หัว/หาง", "ประเภทรถ", "DG",
                ...fleet.truckPapers.map((need) => need.thai), "เอกสาร", "สถานะ", ...(carrier ? [""] : [])]
                .map((head, index) => <th key={head + index} style={css(HEAD)}>{head}</th>)}
            </tr></thead>
            <tbody>
              {trucks.length === 0 && <tr><td colSpan={20} style={css(EMPTY)}>ยังไม่มีรถในทะเบียน</td></tr>}
              {trucks.map((row) => {
                const overall = rowState(row.state);
                const inactive = row.status !== "active";
                return <tr key={row.id} style={css(inactive ? "opacity:.55" : "")}>
                  {!carrier && <td style={css(CELL + "font-weight:600")}>{row.supplier}</td>}
                  <td style={css(CELL + "font-weight:650;white-space:nowrap")}>{row.plate}</td>
                  <td style={css(CELL)}>{KIND_TH[row.kind] ?? row.kind}</td>
                  <td style={css(CELL + MONO)}>{row.vehicleType}</td>
                  <td style={css(CELL)}>{row.dgCapable ? "DG" : "—"}</td>
                  {row.papers.map((paper) => <td key={paper.code} style={css(CELL)}>
                    <Paper paper={paper} onReplace={carrier && !inactive ? () => { setAdding(""); setReplacement({ file: null, expiry: "" }); setReplacing({ owner: "trucks", id: row.id, label: row.plate, paper }); } : undefined} />
                  </td>)}
                  <td style={css(CELL + "font-weight:650;white-space:nowrap;color:" + overall.colour)}>{overall.text}</td>
                  <td style={css(CELL + "white-space:nowrap")}>{inactive ? "เลิกใช้" : "ใช้งาน"}</td>
                  {carrier && <td style={css(CELL)}>
                    <button disabled={busy} onClick={() => void setActive("trucks", row.id, row.plate, inactive)} style={css(SMALL)}>{inactive ? "กลับมาใช้" : "เลิกใช้"}</button>
                  </td>}
                </tr>;
              })}
            </tbody>
          </table>
        </ZoomBox>
      </div>

      {/* ------------------------------------------------------- drivers */}
      <div style={css(PANEL)}>
        <div style={css(PANEL_HEAD)}>
          <span>พนักงานขับรถ · {activeDrivers} คน</span>
          {carrier && <button onClick={() => open("driver")} style={css(ADD)}>{adding === "driver" ? "ปิดฟอร์ม" : "+ เพิ่มพนักงานขับรถ"}</button>}
        </div>
        {carrier && adding === "driver" && (
          <div style={css(FORM)}>
            <Field label="ชื่อ-นามสกุล *">
              <input aria-label="ชื่อ-นามสกุล" value={driverForm.name} maxLength={120} onChange={(event) => setDriverForm({ ...driverForm, name: event.target.value })} style={css(INPUT + "width:200px")} />
            </Field>
            <Field label="เบอร์โทร">
              <input aria-label="เบอร์โทร" value={driverForm.phone} maxLength={40} onChange={(event) => setDriverForm({ ...driverForm, phone: event.target.value })} style={css(INPUT + "width:130px")} />
            </Field>
            <Field label="เลขที่ใบขับขี่ *">
              <input aria-label="เลขที่ใบขับขี่" value={driverForm.licenceNo} maxLength={60} onChange={(event) => setDriverForm({ ...driverForm, licenceNo: event.target.value })} style={css(INPUT + "width:150px")} />
            </Field>
            {paperInputs(fleet.driverPapers)}
            <Submit busy={busy} problem={driverProblem} onClick={() => void addDriver()}>ลงทะเบียนพนักงานขับรถ</Submit>
          </div>
        )}
        {replacing?.owner === "drivers" && <ReplaceBar replacing={replacing} pick={replacement} busy={busy}
          onPick={setReplacement} onSave={() => void replace()} onCancel={() => setReplacing(null)} />}
        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:700px")}>
            <thead><tr>
              {[...(carrier ? [] : ["ผู้ขนส่ง"]), "ชื่อ-นามสกุล", "เบอร์โทร", "เลขที่ใบขับขี่",
                ...fleet.driverPapers.map((need) => need.thai), "เอกสาร", "สถานะ", ...(carrier ? [""] : [])]
                .map((head, index) => <th key={head + index} style={css(HEAD)}>{head}</th>)}
            </tr></thead>
            <tbody>
              {drivers.length === 0 && <tr><td colSpan={20} style={css(EMPTY)}>ยังไม่มีพนักงานขับรถในทะเบียน</td></tr>}
              {drivers.map((row) => {
                const overall = rowState(row.state);
                const inactive = row.status !== "active";
                return <tr key={row.id} style={css(inactive ? "opacity:.55" : "")}>
                  {!carrier && <td style={css(CELL + "font-weight:600")}>{row.supplier}</td>}
                  <td style={css(CELL + "font-weight:650")}>{row.name}</td>
                  <td style={css(CELL + MONO)}>{row.phone || "—"}</td>
                  <td style={css(CELL + MONO)}>{row.licenceNo}</td>
                  {row.papers.map((paper) => <td key={paper.code} style={css(CELL)}>
                    <Paper paper={paper} onReplace={carrier && !inactive ? () => { setAdding(""); setReplacement({ file: null, expiry: "" }); setReplacing({ owner: "drivers", id: row.id, label: row.name, paper }); } : undefined} />
                  </td>)}
                  <td style={css(CELL + "font-weight:650;white-space:nowrap;color:" + overall.colour)}>{overall.text}</td>
                  <td style={css(CELL + "white-space:nowrap")}>{inactive ? "เลิกใช้" : "ใช้งาน"}</td>
                  {carrier && <td style={css(CELL)}>
                    <button disabled={busy} onClick={() => void setActive("drivers", row.id, row.name, inactive)} style={css(SMALL)}>{inactive ? "กลับมาใช้" : "เลิกใช้"}</button>
                  </td>}
                </tr>;
              })}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}

/** One paper in its cell: the file, how its date stands, and — on the carrier's screen — a newer upload. */
function Paper({ paper, onReplace }: { paper: FleetPaper; onReplace?: () => void }) {
  const tone = paperTone(paper.state, paper.daysLeft);
  return <div style={css("display:flex;flex-direction:column;gap:3px;min-width:130px")}>
    {paper.file && <a href={`/api/documents/${paper.file.id}/content${paper.file.canShow ? "?inline=1" : ""}`} target="_blank" rel="noreferrer"
      title={paper.file.fileName}
      style={css("color:#0A5C97;font-size:11.5px;max-width:170px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;display:block")}>{paper.file.fileName}</a>}
    <span style={css(`font-size:10.5px;font-weight:650;color:${tone.colour};background:${tone.background};border-radius:999px;padding:2px 7px;align-self:flex-start;white-space:nowrap`)}>
      {tone.text}{paper.file?.expiryDate ? ` · ${paper.file.expiryDate}` : ""}
    </span>
    {onReplace && <button onClick={onReplace} style={css(SMALL + "align-self:flex-start")}>{paper.file ? "อัปโหลดใหม่" : "อัปโหลด"}</button>}
  </div>;
}

function ReplaceBar({ replacing, pick, busy, onPick, onSave, onCancel }: {
  replacing: Replacing; pick: PaperPick; busy: boolean;
  onPick: (pick: PaperPick) => void; onSave: () => void; onCancel: () => void;
}) {
  return <div style={css(FORM + "background:#F4F8FC")}>
    <span style={css("font-size:12.5px;font-weight:650;color:#0F2B46;align-self:center")}>{replacing.paper.thai} · {replacing.label}</span>
    <input type="file" aria-label={`ไฟล์${replacing.paper.thai}`} accept="application/pdf,image/*"
      onChange={(event) => onPick({ ...pick, file: event.target.files?.[0] ?? null })} style={css("font-size:11.5px;align-self:center")} />
    {replacing.paper.expires && <input type="date" aria-label="วันหมดอายุ" title="วันหมดอายุ" value={pick.expiry}
      onChange={(event) => onPick({ ...pick, expiry: event.target.value })} style={css(INPUT + "width:140px")} />}
    <button disabled={busy || !pick.file} onClick={onSave} style={css(SAVE + "opacity:" + (busy || !pick.file ? ".55" : "1"))}>อัปโหลด</button>
    <button disabled={busy} onClick={onCancel} style={css(SMALL + "height:31px")}>ยกเลิก</button>
  </div>;
}

function Submit({ busy, problem, onClick, children }: { busy: boolean; problem: string; onClick: () => void; children: React.ReactNode }) {
  return <div style={css("display:flex;gap:8px;align-items:center")}>
    <button disabled={busy || problem !== ""} onClick={onClick} style={css(SAVE + "opacity:" + (busy || problem ? ".55" : "1"))}>
      {busy ? "กำลังบันทึก…" : children}</button>
    {problem && <span style={css("font-size:11.5px;color:#B45309")}>{problem}</span>}
  </div>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}>
    <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>{children}
  </label>;
}

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:16px 18px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}

const PANEL = "background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden";
const PANEL_HEAD = "padding:10px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240;display:flex;gap:10px;align-items:center;flex-wrap:wrap";
const FORM = "padding:12px 16px;border-bottom:1px solid #E9EFF5;background:#FAFCFE;display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end;";
const INPUT = "height:31px;padding:0 8px;border:1px solid #C9D6E2;border-radius:4px;background:#fff;font-size:12.5px;font-family:inherit;";
const HEAD = "text-align:left;padding:8px 10px;background:#F8FAFC;font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap";
const CELL = "padding:8px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top;color:#0F2B46;";
const MONO = "font-family:ui-monospace,monospace;font-size:11.5px;";
const EMPTY = "padding:22px;text-align:center;color:#94A3B8;font-size:12.5px";
const ADD = "margin-left:auto;height:28px;padding:0 13px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer";
const SAVE = "height:31px;padding:0 15px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;";
const SMALL = "height:25px;padding:0 9px;border:1px solid #9CC2E8;background:#F4F8FC;color:#0A5C97;border-radius:4px;font-size:11px;font-weight:600;cursor:pointer;";
