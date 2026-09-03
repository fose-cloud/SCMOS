"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

type RegisterRow = {
  id: number;
  sequenceNo: string;
  courseCustomer: string;
  firstName: string;
  lastName: string;
  company: string;
  driverLicenseNo: string;
  licenseType: string;
  effectiveDate: string;
  expiryDate: string;
  daysLeft: number | null;
  status: string;
  statusTh: string;
};

type RegisterSummary = {
  total: number;
  valid: number;
  nearExpiry: number;
  expired: number;
  invalidDate: number;
};

type ImportRow = Omit<RegisterRow, "id" | "daysLeft" | "status" | "statusTh">;

type RegisterReply = {
  rows: RegisterRow[];
  summary: RegisterSummary;
  alertBeforeDays: number;
};

const EMPTY_SUMMARY: RegisterSummary = {
  total: 0, valid: 0, nearExpiry: 0, expired: 0, invalidDate: 0,
};

const COLUMNS: Record<keyof ImportRow, string[]> = {
  sequenceNo: ["ลำดับ", "no", "no.", "sequence", "sequence no"],
  courseCustomer: ["ชื่อหลักสูตร/ลูกค้า", "ชื่อหลักสูตร / ลูกค้า", "course/customer", "course customer"],
  firstName: ["ชื่อ", "first name", "firstname"],
  lastName: ["นามสกุล", "last name", "lastname", "surname"],
  company: ["บริษัท", "company"],
  driverLicenseNo: ["เลขที่ใบขับขี่", "เลขใบขับขี่", "driver license no", "license no", "licence no"],
  licenseType: ["ประเภทใบขับขี่", "license type", "licence type"],
  effectiveDate: ["effective date", "effectivedate", "วันที่เริ่มมีผล", "วันที่อบรม"],
  expiryDate: ["expire date", "expiry date", "expiredate", "expirydate", "วันหมดอายุ", "วันที่หมดอายุ"],
};

const norm = (value: string) => value.toLowerCase().replace(/[-\s._/()]/g, "");

function asDate(value: unknown): string {
  if (value instanceof Date && !Number.isNaN(value.valueOf())) {
    const pad = (part: number) => String(part).padStart(2, "0");
    return `${pad(value.getDate())}/${pad(value.getMonth() + 1)}/${value.getFullYear()}`;
  }
  return String(value ?? "").trim();
}

function statusTone(status: string) {
  if (status === "EXPIRED") return { bg: "#FEF0EE", text: "#B42318", border: "#F3C9C4" };
  if (status === "EXPIRING_SOON") return { bg: "#FFF8F0", text: "#B45309", border: "#F0D8B8" };
  if (status === "INVALID_DATE") return { bg: "#F1F5F9", text: "#64748B", border: "#D7E0E8" };
  return { bg: "#EDF7F1", text: "#16794C", border: "#BFE0CD" };
}

export function CustomerTrainingRegister({ onToast }: { onToast: (message: string) => void }) {
  const [rows, setRows] = useState<RegisterRow[]>([]);
  const [summary, setSummary] = useState<RegisterSummary>(EMPTY_SUMMARY);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("ALL");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");
  const [preview, setPreview] = useState<{
    fileName: string;
    ok: ImportRow[];
    bad: { row: number; why: string }[];
  } | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/training/register");
      if (!response.ok) {
        setFailure(`API ตอบ ${response.status}`);
        return;
      }
      const reply = await response.json() as RegisterReply;
      setRows(reply.rows ?? []);
      setSummary(reply.summary ?? EMPTY_SUMMARY);
      setFailure("");
    } catch (error) {
      setFailure(error instanceof Error ? error.message : String(error));
    }
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const shown = useMemo(() => {
    const wanted = search.trim().toLowerCase();
    const rank: Record<string, number> = { EXPIRED: 0, EXPIRING_SOON: 1, INVALID_DATE: 2, VALID: 3 };
    return rows.filter((row) => {
      if (status !== "ALL" && row.status !== status) return false;
      if (!wanted) return true;
      return [row.sequenceNo, row.courseCustomer, row.firstName, row.lastName,
        row.company, row.driverLicenseNo, row.licenseType]
        .some((value) => value.toLowerCase().includes(wanted));
    }).sort((left, right) =>
      (rank[left.status] ?? 9) - (rank[right.status] ?? 9)
      || (left.daysLeft ?? Number.MAX_SAFE_INTEGER) - (right.daysLeft ?? Number.MAX_SAFE_INTEGER)
      || left.id - right.id);
  }, [rows, search, status]);

  async function readFile(file: File) {
    try {
      const book = XLSX.read(await file.arrayBuffer(), { type: "array", cellDates: true });
      const sheet = book.Sheets[book.SheetNames[0]];
      const source = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet, { defval: "" });
      const ok: ImportRow[] = [];
      const bad: { row: number; why: string }[] = [];

      source.forEach((raw, index) => {
        const pick = (field: keyof ImportRow) => {
          const wanted = COLUMNS[field].map(norm);
          const key = Object.keys(raw).find((header) => wanted.includes(norm(header)));
          return key ? raw[key] : "";
        };
        const row: ImportRow = {
          sequenceNo: String(pick("sequenceNo") ?? "").trim(),
          courseCustomer: String(pick("courseCustomer") ?? "").trim(),
          firstName: String(pick("firstName") ?? "").trim(),
          lastName: String(pick("lastName") ?? "").trim(),
          company: String(pick("company") ?? "").trim(),
          driverLicenseNo: String(pick("driverLicenseNo") ?? "").trim(),
          licenseType: String(pick("licenseType") ?? "").trim(),
          effectiveDate: asDate(pick("effectiveDate")),
          expiryDate: asDate(pick("expiryDate")),
        };
        const missing: string[] = [];
        if (!row.courseCustomer) missing.push("ชื่อหลักสูตร/ลูกค้า");
        if (!row.firstName && !row.lastName) missing.push("ชื่อหรือนามสกุล");
        if (!row.effectiveDate) missing.push("Effective date");
        if (!row.expiryDate) missing.push("Expire date");
        if (missing.length) bad.push({ row: index + 1, why: `ไม่มี ${missing.join(", ")}` });
        else ok.push(row);
      });

      setPreview({ fileName: file.name, ok, bad });
      if (ok.length === 0 && bad.length === 0) onToast("ไฟล์นี้ยังไม่มีแถวข้อมูลสำหรับนำเข้า");
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  async function importRows() {
    if (!preview || preview.ok.length === 0 || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/training/register/import", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rows: preview.ok }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "นำเข้าไม่สำเร็จ");
      if (response.ok) {
        setPreview(null);
        await load();
      }
    } finally {
      setBusy(false);
    }
  }

  if (failure) {
    return (
      <div style={css("background:#fff;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:18px") }>
        <div style={css("font-size:13px;font-weight:650;color:#B45309")}>เปิดทะเบียนอบรมไม่ได้</div>
        <div style={css("font-size:12px;color:#5A6B7D;margin-top:4px")}>{failure}</div>
        <button onClick={() => void load()} style={css("margin-top:10px;height:31px;padding:0 14px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;cursor:pointer")}>ลองใหม่</button>
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px") }>
      {(summary.nearExpiry > 0 || summary.expired > 0) && (
        <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-left:4px solid #B45309;border-radius:6px;padding:11px 14px;color:#7A4210;font-size:12.5px;line-height:1.6") }>
          แจ้งเตือนการอบรม: ใกล้หมดอายุภายในน้อยกว่า 60 วัน {summary.nearExpiry} รายการ
          {summary.expired > 0 ? ` · หมดอายุแล้ว ${summary.expired} รายการ` : ""}
        </div>
      )}

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(135px,1fr));gap:9px") }>
        <RegisterTile label="ทั้งหมด" value={summary.total} tone="#0F2B46" />
        <RegisterTile label="ยังใช้ได้" value={summary.valid} tone="#16794C" />
        <RegisterTile label="ใกล้หมดอายุ < 60 วัน" value={summary.nearExpiry} tone="#B45309" />
        <RegisterTile label="หมดอายุแล้ว" value={summary.expired} tone="#B42318" />
        <RegisterTile label="วันที่ไม่ถูกต้อง" value={summary.invalidDate} tone="#64748B" />
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 14px;display:flex;gap:9px;align-items:end;flex-wrap:wrap") }>
        <label style={css("display:flex;flex-direction:column;gap:4px;min-width:260px;flex:1") }>
          <span style={css("font-size:10px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค้นหา</span>
          <input value={search} onChange={(event) => setSearch(event.target.value)}
            placeholder="หลักสูตร/ลูกค้า, ชื่อ, บริษัท หรือเลขใบขับขี่"
            style={css("height:32px;padding:0 10px;border:1px solid #D3DBE3;border-radius:4px;font:12.5px inherit") } />
        </label>
        <label style={css("display:flex;flex-direction:column;gap:4px;min-width:185px") }>
          <span style={css("font-size:10px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>สถานะ</span>
          <select value={status} onChange={(event) => setStatus(event.target.value)}
            style={css("height:32px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font:12.5px inherit") }>
            <option value="ALL">ทั้งหมด</option>
            <option value="EXPIRING_SOON">ใกล้หมดอายุ &lt; 60 วัน</option>
            <option value="EXPIRED">หมดอายุแล้ว</option>
            <option value="VALID">ยังใช้ได้</option>
            <option value="INVALID_DATE">วันที่ไม่ถูกต้อง</option>
          </select>
        </label>
        <label style={css("height:32px;padding:0 15px;border:1px solid #1D4E80;background:#1D4E80;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center") }>
          นำเข้า Excel ตามแบบฟอร์ม
          <input type="file" accept=".xlsx,.xls,.csv" style={css("display:none")}
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = "";
              if (file) void readFile(file);
            }} />
        </label>
      </div>

      {preview && (
        <div style={css("background:#fff;border:1px solid #BBD5EE;border-left:3px solid #1D4E80;border-radius:6px;padding:13px 15px") }>
          <div style={css("font-size:13px;font-weight:650;color:#0F2B46")}>{preview.fileName} — พร้อมนำเข้า {preview.ok.length} รายการ</div>
          {preview.bad.length > 0 && (
            <div style={css("margin-top:8px;background:#FFF8F0;border-radius:4px;padding:8px 10px;font-size:11.5px;color:#8A5A12;max-height:120px;overflow:auto") }>
              {preview.bad.slice(0, 10).map((item) => <div key={item.row}>รายการ {item.row}: {item.why}</div>)}
              {preview.bad.length > 10 ? <div>… อีก {preview.bad.length - 10} รายการ</div> : null}
            </div>
          )}
          <div style={css("display:flex;gap:8px;align-items:center;margin-top:10px;flex-wrap:wrap") }>
            <button disabled={busy || preview.ok.length === 0} onClick={() => void importRows()}
              style={css("height:32px;padding:0 15px;border:0;background:" + (busy || preview.ok.length === 0 ? "#C3CFDB" : "#16794C") + ";color:#fff;border-radius:4px;font-weight:600;cursor:pointer") }>
              {busy ? "กำลังนำเข้า…" : `ยืนยันนำเข้า ${preview.ok.length} รายการ`}
            </button>
            <button disabled={busy} onClick={() => setPreview(null)} style={css("height:32px;padding:0 13px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;cursor:pointer")}>ยกเลิก</button>
            <span style={css("font-size:11.5px;color:#7B8CA0")}>ระบบจะข้ามรายการซ้ำ และแจ้งแถวที่วันที่ไม่ถูกต้อง</span>
          </div>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden") }>
        <ZoomBox height="62vh">
          <table style={css("width:100%;min-width:1330px;border-collapse:separate;border-spacing:0;font-size:12px") }>
            <thead style={css("position:sticky;top:0;z-index:1;background:#0A2240;color:#fff") }>
              <tr>
                {[
                  "ลำดับ", "ชื่อหลักสูตร/ลูกค้า", "ชื่อ", "นามสกุล", "บริษัท",
                  "เลขที่ใบขับขี่", "ประเภทใบขับขี่", "Effective date", "Expire date", "สถานะ",
                ].map((header) => <th key={header} style={css("padding:10px 11px;text-align:left;white-space:nowrap;border-right:1px solid #29445F;font-weight:650")}>{header}</th>)}
              </tr>
            </thead>
            <tbody>
              {shown.map((row, index) => {
                const tone = statusTone(row.status);
                return (
                  <tr key={row.id} style={css("background:" + (row.status === "VALID" ? (index % 2 ? "#FBFCFE" : "#fff") : tone.bg))}>
                    <Cell>{row.sequenceNo || String(index + 1)}</Cell>
                    <Cell strong>{row.courseCustomer}</Cell>
                    <Cell>{row.firstName}</Cell>
                    <Cell>{row.lastName}</Cell>
                    <Cell>{row.company}</Cell>
                    <Cell mono>{row.driverLicenseNo || "—"}</Cell>
                    <Cell>{row.licenseType || "—"}</Cell>
                    <Cell mono>{row.effectiveDate}</Cell>
                    <Cell mono>{row.expiryDate}</Cell>
                    <td style={css("padding:8px 11px;border-bottom:1px solid #E7ECF1;white-space:nowrap") }>
                      <span style={css("display:inline-flex;padding:3px 8px;border:1px solid " + tone.border + ";border-radius:999px;background:" + tone.bg + ";color:" + tone.text + ";font-size:11px;font-weight:700") }>
                        {row.statusTh}
                        {row.daysLeft !== null
                          ? row.daysLeft > 0
                            ? ` · เหลือ ${row.daysLeft} วัน`
                            : row.daysLeft === 0
                              ? " · หมดอายุวันนี้"
                              : ` · เกิน ${Math.abs(row.daysLeft)} วัน`
                          : ""}
                      </span>
                    </td>
                  </tr>
                );
              })}
              {shown.length === 0 && (
                <tr><td colSpan={10} style={css("padding:32px;text-align:center;color:#7B8CA0")}>ยังไม่มีข้อมูลที่ตรงกับตัวกรอง</td></tr>
              )}
            </tbody>
          </table>
        </ZoomBox>
        <div style={css("padding:8px 12px;background:#F8FAFC;border-top:1px solid #E3E8EE;color:#7B8CA0;font-size:11px")}>แสดง {shown.length} จาก {rows.length} รายการ · สถานะคำนวณใหม่จาก Expire date ทุกครั้งที่เปิดหน้า</div>
      </div>
    </div>
  );
}

function Cell({ children, strong, mono }: { children: React.ReactNode; strong?: boolean; mono?: boolean }) {
  return <td style={css("padding:8px 11px;border-bottom:1px solid #E7ECF1;white-space:nowrap;color:#253A4D;font-weight:" + (strong ? "650" : "400") + ";font-family:" + (mono ? "'IBM Plex Mono',monospace" : "inherit"))}>{children}</td>;
}

function RegisterTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:11px 13px") }>
      <div style={css("font-size:10px;letter-spacing:.04em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css("font-size:22px;font-weight:700;color:" + tone + ";margin-top:3px")}>{value}</div>
    </div>
  );
}
