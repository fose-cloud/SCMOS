"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * Customer Training Control.
 *
 * The dashboard counts certificates, not drivers, because that is the unit of
 * work: one driver with two lapsed courses is two things to chase, and a tile
 * that said "1 driver" would understate what the day holds.
 *
 * Nothing here recalculates on a timer. Every status comes from the API, which
 * derives it from the expiry date each time it is asked — a certificate that
 * lapses overnight reads as expired the next morning without a job having run.
 */

type Summary = {
  drivers: number; valid: number; attention: number;
  expiringSoon: number; expired: number; missing: number;
  compliance: number | null;
};

type CourseState = {
  courseId: number; code: string; name: string; mandatory: boolean;
  trainingDate: string; expiryDate: string; certificateNo: string; provider: string;
  daysLeft: number | null; status: string; recordId: number | null;
};

type Driver = { id: number; name: string; driverIdNo: string; phone: string; supplierId: number | null };
type Course = { id: number; code: string; name: string; validMonths: number };
type Requirement = { id: number; customer: string; courseId: number; course: string; code: string; mandatory: boolean };
type Supplier = { id: number; code: string; name: string };

const BLANK = {
  customer: "", supplierId: "", driverName: "", driverIdNo: "", phone: "",
  courseId: "", trainingDate: "", expiryDate: "", certificateNo: "", provider: "", remark: "",
};

const TONE: Record<string, { bg: string; border: string; text: string; th: string }> = {
  VALID: { bg: "#EDF7F1", border: "#BFE0CD", text: "#16794C", th: "ยังใช้ได้" },
  ATTENTION: { bg: "#FFFBEB", border: "#F5E0A3", text: "#8A6D0B", th: "ใกล้ครบกำหนด" },
  EXPIRING_SOON: { bg: "#FFF8F0", border: "#F0D8B8", text: "#B45309", th: "ใกล้หมดอายุ" },
  EXPIRED: { bg: "#FEF0EE", border: "#F3C9C4", text: "#B42318", th: "หมดอายุแล้ว" },
  MISSING: { bg: "#F1F5F9", border: "#E2E8F0", text: "#64748B", th: "ยังไม่เคยอบรม" },
};

export function Training({ onToast }: { onToast: (message: string) => void }) {
  const [tab, setTab] = useState<"dashboard" | "drivers" | "requirements">("dashboard");
  const [summary, setSummary] = useState<Summary | null>(null);
  const [drivers, setDrivers] = useState<Driver[]>([]);
  const [courses, setCourses] = useState<Course[]>([]);
  const [requirements, setRequirements] = useState<Requirement[]>([]);
  const [openDriver, setOpenDriver] = useState<number | null>(null);
  const [profile, setProfile] = useState<
    { name: string; photoDocumentId: number | null; courses: CourseState[] } | null>(null);
  const [customer, setCustomer] = useState("");
  const [failure, setFailure] = useState("");
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ ...BLANK });
  const [photo, setPhoto] = useState<File | null>(null);
  const [certificate, setCertificate] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const [s, d, c, r, v] = await Promise.all([
        apiFetch(`/api/training/summary?customer=${encodeURIComponent(customer)}`),
        apiFetch("/api/training/drivers"),
        apiFetch("/api/training/courses"),
        apiFetch("/api/training/requirements"),
        apiFetch("/api/suppliers"),
      ]);
      if (!s.ok) { setFailure(`API ตอบ ${s.status}`); return; }
      setSummary(await s.json() as Summary);
      setDrivers(d.ok ? await d.json() as Driver[] : []);
      setCourses(c.ok ? await c.json() as Course[] : []);
      setRequirements(r.ok ? await r.json() as Requirement[] : []);
      setSuppliers(v.ok ? await v.json() as Supplier[] : []);
      setFailure("");
    } catch (error) {
      setFailure(error instanceof Error ? error.message : String(error));
    }
  }, [customer]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (openDriver === null) { setProfile(null); return; }
    void (async () => {
      const response = await apiFetch(
        `/api/training/drivers/${openDriver}?customer=${encodeURIComponent(customer)}`);
      setProfile(response.ok
        ? await response.json() as { name: string; photoDocumentId: number | null; courses: CourseState[] }
        : null);
    })();
  }, [openDriver, customer]);

  /**
   * One request for the driver, the certificate and the photograph.
   *
   * Sent as a form rather than JSON because two of the three are files, and
   * splitting it into three calls would be three ways to end up half-entered:
   * a driver with no training, or a photograph filed against a driver the next
   * call failed to create.
   */
  async function save() {
    if (busy) return;
    setBusy(true);
    try {
      const body = new FormData();
      Object.entries(form).forEach(([key, value]) => { if (value) body.append(key, value); });
      if (photo) body.append("photo", photo);
      if (certificate) body.append("certificate", certificate);

      const response = await apiFetch("/api/training/entry", { method: "POST", body });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      if (response.ok) {
        // Customer, carrier and course stay behind: the next certificate is
        // usually the next driver on the same course for the same customer.
        setForm((was) => ({
          ...BLANK, customer: was.customer, supplierId: was.supplierId, courseId: was.courseId,
        }));
        setPhoto(null);
        setCertificate(null);
        setAdding(false);
        await load();
      }
    } finally { setBusy(false); }
  }

  if (failure) {
    return (
      <div style={css("background:#fff;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B45309;margin-bottom:4px")}>เปิดข้อมูลการอบรมไม่ได้</div>
        <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.7")}>{failure}</div>
        <button onClick={() => void load()}
          style={css("margin-top:13px;height:32px;padding:0 15px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
          ลองใหม่
        </button>
      </div>
    );
  }

  const customers = [...new Set(requirements.map((item) => item.customer))].sort();

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:4px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
            วัดตามข้อกำหนดของลูกค้า
          </span>
          <select value={customer} onChange={(event) => setCustomer(event.target.value)}
            style={css("height:31px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff;min-width:200px")}>
            <option value="">ทุกหลักสูตรในระบบ</option>
            {customers.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <div style={css("flex:1;min-width:200px;font-size:11.5px;color:#7B8CA0;line-height:1.6")}>
          เลือกลูกค้าเพื่อดูว่าคนขับผ่านข้อกำหนดของลูกค้ารายนั้นหรือยัง
          <br />
          สถานะคำนวณจากวันหมดอายุทุกครั้งที่เปิดหน้า ไม่มีงานเบื้องหลังที่ต้องรัน
        </div>
      </div>

      <div style={css("display:flex;gap:9px;align-items:center;flex-wrap:wrap")}>
        <button onClick={() => setAdding((v) => !v)}
          style={css("height:33px;padding:0 16px;border:1px solid #16794C;background:" +
            (adding ? "#fff" : "#16794C") + ";color:" + (adding ? "#16794C" : "#fff") +
            ";border-radius:5px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
          {adding ? "ยกเลิก" : "+ บันทึกการอบรม"}
        </button>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>
          กรอกครั้งเดียว — สร้างคนขับใหม่ให้เองถ้ายังไม่มีในทะเบียน
        </span>
      </div>

      {adding && (
        <div style={css("background:#F8FAFC;border:1px solid #D3DBE3;border-radius:6px;padding:15px 17px")}>
          <div style={css("display:flex;gap:9px;flex-wrap:wrap")}>
            <Field label="ชื่อลูกค้า" width="190px">
              <select value={form.customer} onChange={(e) => setForm({ ...form, customer: e.target.value })} style={INPUT}>
                <option value="">— เลือกลูกค้า —</option>
                {customers.map((name) => <option key={name} value={name}>{name}</option>)}
              </select>
            </Field>
            <Field label="บริษัทขนส่ง" width="190px">
              <select value={form.supplierId} onChange={(e) => setForm({ ...form, supplierId: e.target.value })} style={INPUT}>
                <option value="">— ไม่ระบุ —</option>
                {suppliers.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </Field>
            <Field label="ชื่อ-สกุลคนขับรถ *" width="210px">
              <input value={form.driverName} onChange={(e) => setForm({ ...form, driverName: e.target.value })}
                placeholder="นายสมชาย ใจดี" style={INPUT} />
            </Field>
            <Field label="เลขบัตร / ใบขับขี่" width="170px">
              <input value={form.driverIdNo} onChange={(e) => setForm({ ...form, driverIdNo: e.target.value })}
                placeholder="ใช้จับคู่คนเดิม" style={INPUT} />
            </Field>
            <Field label="เบอร์โทร" width="140px">
              <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} style={INPUT} />
            </Field>
            <Field label="หลักสูตร *" width="200px">
              <select value={form.courseId} onChange={(e) => setForm({ ...form, courseId: e.target.value })} style={INPUT}>
                <option value="">— เลือกหลักสูตร —</option>
                {courses.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </Field>
            <Field label="วันที่อบรม *" width="150px">
              <input value={form.trainingDate} onChange={(e) => setForm({ ...form, trainingDate: e.target.value })}
                placeholder="วว/ดด/ปปปป" style={INPUT} />
            </Field>
            <Field label="วันที่หมดอายุ" width="150px">
              <input value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })}
                placeholder="เว้นว่างให้คิดให้" style={INPUT} />
            </Field>
            <Field label="เลขใบรับรอง" width="160px">
              <input value={form.certificateNo} onChange={(e) => setForm({ ...form, certificateNo: e.target.value })} style={INPUT} />
            </Field>
            <Field label="ผู้จัดอบรม" width="180px">
              <input value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value })} style={INPUT} />
            </Field>
            <Field label="หมายเหตุ" width="220px">
              <input value={form.remark} onChange={(e) => setForm({ ...form, remark: e.target.value })} style={INPUT} />
            </Field>
            <Field label="รูปพนักงานขับรถ" width="200px">
              <input type="file" accept="image/*"
                onChange={(e) => setPhoto(e.target.files?.[0] ?? null)}
                style={css("font-size:11.5px;font-family:inherit;width:100%")} />
            </Field>
            <Field label="ไฟล์ใบรับรอง" width="200px">
              <input type="file" accept="image/*,application/pdf"
                onChange={(e) => setCertificate(e.target.files?.[0] ?? null)}
                style={css("font-size:11.5px;font-family:inherit;width:100%")} />
            </Field>
          </div>

          <div style={css("display:flex;gap:9px;align-items:center;margin-top:13px;flex-wrap:wrap")}>
            <button onClick={() => void save()}
              disabled={busy || !form.driverName.trim() || !form.courseId || !form.trainingDate.trim()}
              style={css("height:33px;padding:0 17px;border:1px solid #16794C;background:" +
                (busy || !form.driverName.trim() || !form.courseId || !form.trainingDate.trim()
                  ? "#C3CFDB" : "#16794C") +
                ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
              บันทึก
            </button>
            <span style={css("font-size:11.5px;color:#7B8CA0;line-height:1.6")}>
              เว้นวันหมดอายุไว้ ระบบจะคิดจากอายุหลักสูตรให้ · เลขบัตรคือสิ่งที่กันคนเดิมถูกเพิ่มซ้ำ
            </span>
          </div>
        </div>
      )}

      <div style={css("display:flex;gap:7px;flex-wrap:wrap")}>
        {([["dashboard", "ภาพรวม"], ["drivers", `คนขับ ${drivers.length}`],
           ["requirements", `ข้อกำหนดลูกค้า ${requirements.length}`]] as const).map(([id, label]) => {
          const on = tab === id;
          return (
            <button key={id} onClick={() => setTab(id)}
              style={css("height:33px;padding:0 15px;border:1px solid " + (on ? "#0A2240" : "#D3DBE3") +
                ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#3F5265") +
                ";border-radius:5px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
              {label}
            </button>
          );
        })}
      </div>

      {tab === "dashboard" && summary && (
        <>
          <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px")}>
            <Tile label="คนขับที่ใช้งานอยู่" value={summary.drivers} tone="#0F2B46" />
            <Tile label="ยังใช้ได้" value={summary.valid} tone={TONE.VALID.text} />
            <Tile label="ใกล้ครบกำหนด" value={summary.attention} tone={TONE.ATTENTION.text} />
            <Tile label="ใกล้หมดอายุ ≤30 วัน" value={summary.expiringSoon} tone={TONE.EXPIRING_SOON.text} />
            <Tile label="หมดอายุแล้ว" value={summary.expired} tone={TONE.EXPIRED.text} />
            <Tile label="ยังไม่เคยอบรม" value={summary.missing} tone={TONE.MISSING.text} />
          </div>

          <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:16px 18px")}>
            <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
              Training Compliance
            </div>
            {/* Null rather than 100: nothing required is not perfect
                compliance, it is nothing to measure — the same rule the KPI
                screen follows for a carrier with too few jobs. */}
            {summary.compliance === null ? (
              <div style={css("font-size:13px;color:#7B8CA0;margin-top:6px;line-height:1.6")}>
                ยังวัดไม่ได้ — ยังไม่มีข้อกำหนดของลูกค้าให้เทียบ
              </div>
            ) : (
              <>
                <div style={css("font-size:30px;font-weight:700;color:#0F2B46;margin-top:2px")}>
                  {summary.compliance}%
                </div>
                <div style={css("height:8px;background:#EEF2F6;border-radius:4px;overflow:hidden;margin-top:9px")}>
                  <div style={css("height:100%;width:" + Math.min(100, summary.compliance) + "%;background:" +
                    (summary.compliance >= 90 ? "#16794C" : summary.compliance >= 70 ? "#B45309" : "#B42318"))} />
                </div>
                <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:7px;line-height:1.6")}>
                  หลักสูตรบังคับที่ยังใช้ได้ ÷ หลักสูตรบังคับทั้งหมด — คนที่ยังไม่เคยอบรมนับเป็นไม่ผ่าน
                </div>
              </>
            )}
          </div>
        </>
      )}

      {tab === "drivers" && (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 16px")}>
          {drivers.length === 0 ? (
            <div style={css("padding:22px;text-align:center;color:#7B8CA0;font-size:12.5px;line-height:1.8")}>
              ยังไม่มีคนขับในทะเบียน
              <br />
              เพิ่มได้จากพอร์ทัลผู้รับเหมา หรือทีมที่ดูแลผู้รับเหมาเพิ่มให้
            </div>
          ) : (
            <div style={css("display:flex;flex-direction:column;gap:7px")}>
              {drivers.map((driver) => (
                <button key={driver.id}
                  onClick={() => setOpenDriver(openDriver === driver.id ? null : driver.id)}
                  style={css("width:100%;text-align:left;font-family:inherit;cursor:pointer;background:" +
                    (openDriver === driver.id ? "#F4F8FC" : "#fff") +
                    ";border:1px solid #E3E8EE;border-radius:5px;padding:10px 13px")}>
                  <div style={css("font-size:13px;font-weight:600;color:#0F2B46")}>{driver.name}</div>
                  <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px;font-family:'IBM Plex Mono',monospace")}>
                    {driver.driverIdNo || "—"} · {driver.phone || "ไม่มีเบอร์"}
                  </div>
                </button>
              ))}
            </div>
          )}

          {profile && (
            <div style={css("margin-top:14px;padding-top:14px;border-top:1px solid #E9EFF5")}>
              <div style={css("display:flex;gap:12px;align-items:center;margin-bottom:11px")}>
                {/* Served through the document endpoint, never the blob URL —
                    the container is private and stays that way. */}
                {profile.photoDocumentId && (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img src={`/api/documents/${profile.photoDocumentId}/content`} alt=""
                    style={css("width:52px;height:52px;border-radius:5px;object-fit:cover;border:1px solid #E3E8EE;flex:none")} />
                )}
                <div style={css("font-size:13px;font-weight:650;color:#0F2B46")}>
                  {profile.name} — {customer || "ทุกหลักสูตร"}
                </div>
              </div>
              <div style={css("display:flex;flex-direction:column;gap:6px")}>
                {profile.courses.map((state) => {
                  const tone = TONE[state.status] ?? TONE.MISSING;
                  return (
                    <div key={state.courseId}
                      style={css("display:flex;gap:12px;align-items:center;flex-wrap:wrap;background:" + tone.bg +
                        ";border:1px solid " + tone.border + ";border-radius:5px;padding:9px 12px")}>
                      <div style={css("flex:1;min-width:170px")}>
                        <div style={css("font-size:12.5px;font-weight:600;color:#0F2B46")}>
                          {state.name}{state.mandatory ? "" : " (ไม่บังคับ)"}
                        </div>
                        <div style={css("font-size:11px;color:#7B8CA0;margin-top:2px")}>
                          {state.expiryDate ? `หมดอายุ ${state.expiryDate}` : "ยังไม่มีใบรับรอง"}
                          {state.certificateNo ? ` · ${state.certificateNo}` : ""}
                        </div>
                      </div>
                      <span style={css("font-size:11px;font-weight:700;color:" + tone.text)}>
                        {tone.th}
                        {state.daysLeft !== null && (
                          <span style={css("font-weight:400")}>
                            {state.daysLeft >= 0 ? ` · เหลือ ${state.daysLeft} วัน` : ` · เกินมา ${-state.daysLeft} วัน`}
                          </span>
                        )}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}

      {tab === "requirements" && (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 16px")}>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-bottom:10px;line-height:1.6")}>
            หลักสูตรที่ลูกค้าแต่ละรายกำหนด — เฉพาะข้อบังคับเท่านั้นที่ทำให้คนขับรับงานไม่ได้
          </div>
          {requirements.length === 0 ? (
            <div style={css("padding:20px;text-align:center;color:#7B8CA0;font-size:12.5px")}>
              ยังไม่ได้กำหนดข้อบังคับของลูกค้ารายใด
            </div>
          ) : (
            customers.map((name) => (
              <div key={name} style={css("margin-bottom:12px")}>
                <div style={css("font-size:12.5px;font-weight:650;color:#0F2B46;margin-bottom:5px")}>{name}</div>
                <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
                  {requirements.filter((item) => item.customer === name).map((item) => (
                    <span key={item.id}
                      style={css("font-size:11.5px;padding:4px 10px;border-radius:4px;border:1px solid " +
                        (item.mandatory ? "#BBD5EE" : "#E2E8F0") +
                        ";background:" + (item.mandatory ? "#E7F0FA" : "#F8FAFC") +
                        ";color:" + (item.mandatory ? "#1D4E80" : "#64748B"))}>
                      {item.course}{item.mandatory ? "" : " (ไม่บังคับ)"}
                    </span>
                  ))}
                </div>
              </div>
            ))
          )}

          <div style={css("margin-top:12px;padding-top:12px;border-top:1px solid #E9EFF5;font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
            หลักสูตรในระบบ: {courses.map((course) => course.name).join(" · ") || "ยังไม่มี"}
          </div>
        </div>
      )}
    </div>
  );
}

const INPUT = css("height:31px;width:100%;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff");

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:4px;width:" + width)}>
      <span style={css("font-size:10px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
        {label}
      </span>
      {children}
    </label>
  );
}

function Tile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 15px")}>
      <div style={css("font-size:10px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;line-height:1.4")}>
        {label}
      </div>
      <div style={css("font-size:24px;font-weight:700;margin-top:4px;color:" + tone)}>{value}</div>
    </div>
  );
}
