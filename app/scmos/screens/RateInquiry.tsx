"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { badge, css } from "../theme";
import { readWorkbook, type ImportRead } from "../rateInquiryImport";

/**
 * Asking carriers what a journey would cost.
 *
 * The paper version is a workbook with a sheet per month — Date, No., Requestor,
 * Customer, then a row per lane and twenty-four price columns across. Fifty-nine
 * inquiries were raised in August alone, and an inquiry routinely covers several
 * lanes: one customer asks about eleven journeys and they are one conversation,
 * not eleven.
 *
 * So the form keeps that shape. What it does not keep is the twenty-four
 * columns: a lane is priced for three or four vehicles and the rest of the row
 * is empty, so the boxes appear as they are asked for — tick FCL and the
 * container prices arrive, tick DG and the dangerous-goods ones do. Everything
 * is still reachable behind one more tick, for the lane that really does need
 * a flatbed and an ISO tank quoted together.
 *
 * The vehicle list, the fuel bands and the carriers all come from the API. A
 * form that offered a vehicle the API refuses would be the same rule written
 * twice, which in this codebase has never once stayed in agreement.
 */

type Vehicle = { code: string; label: string; group: string; dg: boolean; reefer: boolean };
type Group = { key: string; label: string };
type Band = { label: string; position: number };

type FormData = {
  vehicles: Vehicle[];
  groups: Group[];
  bands: Band[];
  carriers: string[];
  customers: string[];
};

type LaneView = {
  id: number; fromPlace: string; toPlace: string; county: string; carriers: string;
  fcl: boolean; lcl: boolean; remark: string; prices: Record<string, number>;
};

type InquiryView = {
  id: number; number: number; inquiredOn: string; requestor: string; requestorId: string;
  customer: string; fuelBand: string; status: string; lanes: LaneView[];
};

/** One lane as the form holds it, before it is worth sending anywhere. */
type Lane = {
  key: string;
  fromPlace: string; toPlace: string; county: string;
  carriers: string[];
  fcl: boolean; lcl: boolean;
  dg: boolean; reefer: boolean;
  /** Reveals every vehicle rather than the ones the ticks imply. */
  showAll: boolean;
  remark: string;
  prices: Record<string, string>;
};

const BOX = "height:32px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12.5px;padding:0 9px;outline:none;font-family:inherit;width:100%";
const LABEL = "font-size:11px;color:#64748B;margin-bottom:4px;display:block";

function today(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${pad(now.getDate())}/${pad(now.getMonth() + 1)}/${now.getFullYear()}`;
}

function blankLane(): Lane {
  return {
    key: "L" + Date.now() + "-" + Math.round(Math.random() * 9999),
    fromPlace: "", toPlace: "", county: "", carriers: [],
    fcl: false, lcl: true, dg: false, reefer: false, showAll: false,
    remark: "", prices: {},
  };
}

/**
 * Which price boxes this lane is asking for.
 *
 * The workbook's own grouping: a truck price is an LCL price and a container or
 * tank price is an FCL one, and inside each block the dangerous-goods and
 * reefer columns are separate. Reading the ticks rather than showing everything
 * is the difference between three boxes and twenty-four.
 */
function vehiclesFor(lane: Lane, all: Vehicle[]): Vehicle[] {
  if (lane.showAll) return all;
  return all.filter((vehicle) => {
    if (vehicle.group === "TRUCK" && !lane.lcl) return false;
    if ((vehicle.group === "CONTAINER" || vehicle.group === "TANK") && !lane.fcl) return false;
    // A special is never implied by a tick; it is asked for by name.
    if (vehicle.group === "SPECIAL") return false;
    return vehicle.dg === lane.dg && vehicle.reefer === lane.reefer;
  });
}

export function RateInquiry({ onToast }: { onToast: (message: string) => void }) {
  const [form, setForm] = useState<FormData | null>(null);
  const [failure, setFailure] = useState("");
  const [recent, setRecent] = useState<InquiryView[]>([]);
  const [busy, setBusy] = useState(false);

  const [date, setDate] = useState(today());
  const [customer, setCustomer] = useState("");
  const [band, setBand] = useState("");
  const [lanes, setLanes] = useState<Lane[]>([blankLane()]);

  const load = useCallback(async () => {
    try {
      const [f, list] = await Promise.all([
        apiFetch("/api/rate-inquiries/form", { headers: { accept: "application/json" } }),
        apiFetch("/api/rate-inquiries?take=25", { headers: { accept: "application/json" } }),
      ]);
      if (!f.ok) {
        const body = await f.json().catch(() => ({})) as { error?: string };
        setFailure(body.error || `เปิดฟอร์มไม่ได้ (${f.status})`);
        return;
      }
      const data = await f.json() as FormData;
      setForm(data);
      setBand((current) => current || data.bands[0]?.label || "");
      setFailure("");
      if (list.ok) setRecent(((await list.json()) as { inquiries: InquiryView[] }).inquiries);
    } catch (error) {
      setFailure("ติดต่อ API ไม่ได้: " + (error instanceof Error ? error.message : String(error)));
    }
  }, []);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const patch = (key: string, change: Partial<Lane>) =>
    setLanes((prev) => prev.map((lane) => (lane.key === key ? { ...lane, ...change } : lane)));

  const priced = (lane: Lane) =>
    Object.values(lane.prices).filter((value) => value.trim().length > 0).length;

  const laneReady = (lane: Lane) =>
    lane.fromPlace.trim().length > 0 && lane.toPlace.trim().length > 0 && (lane.fcl || lane.lcl);

  const ready = customer.trim().length > 0
    && /^\d{2}\/\d{2}\/\d{4}$/.test(date.trim())
    && lanes.length > 0
    && lanes.every(laneReady);

  async function create() {
    if (busy || !ready) return;
    setBusy(true);
    try {
      const body = {
        inquiredOn: date.trim(),
        customer: customer.trim(),
        fuelBand: band,
        lanes: lanes.map((lane) => ({
          fromPlace: lane.fromPlace.trim(),
          toPlace: lane.toPlace.trim(),
          county: lane.county.trim(),
          carriers: lane.carriers.join(","),
          fcl: lane.fcl,
          lcl: lane.lcl,
          remark: lane.remark.trim(),
          prices: Object.fromEntries(
            Object.entries(lane.prices)
              .filter(([, value]) => value.trim().length > 0 && Number.isFinite(Number(value)))
              .map(([code, value]) => [code, Math.round(Number(value))])),
        })),
      };
      const response = await apiFetch("/api/rate-inquiries", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      if (response.ok) {
        setCustomer("");
        setLanes([blankLane()]);
        await load();
      }
    } finally { setBusy(false); }
  }

  if (failure) {
    return (
      <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:14px 16px;display:flex;flex-direction:column;gap:9px")}>
        <span style={css("font-size:12.5px;color:#B42318")}>{failure}</span>
        <button onClick={() => { setFailure(""); void load(); }}
          style={css("align-self:flex-start;height:30px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;cursor:pointer")}>
          ลองใหม่
        </button>
      </div>
    );
  }

  if (!form) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        กำลังเปิดฟอร์ม…
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <ImportWorkbook onToast={onToast} onDone={() => void load()} />

      {/* ---------------------------------------------------------- header */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("font-size:13px;font-weight:600;color:#0A2240;margin-bottom:3px")}>ใบขอราคาใหม่ · Rate Inquiry</div>
        <div style={css("font-size:11px;color:#64748B;margin-bottom:12px")}>
          หนึ่งใบขอราคา = ลูกค้าหนึ่งราย ใส่ได้หลายเส้นทาง · เลขที่ใบจะออกให้อัตโนมัติตามเดือน
        </div>

        <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px")}>
          <div>
            <span style={css(LABEL)}>วันที่ขอราคา · วว/ดด/ปปปป</span>
            <input value={date} onChange={(e) => setDate(e.target.value)} placeholder="20/08/2026"
              style={css(BOX + ";font-family:'IBM Plex Mono',monospace")} />
          </div>
          <div>
            <span style={css(LABEL)}>ลูกค้า · จำเป็น</span>
            <input list="rate-customers" value={customer} onChange={(e) => setCustomer(e.target.value)}
              placeholder="เช่น SHPP / SABIC" style={css(BOX)} />
            <datalist id="rate-customers">
              {form.customers.map((name) => <option key={name} value={name} />)}
            </datalist>
          </div>
          <div>
            <span style={css(LABEL)}>ราคาอ้างอิงราคาน้ำมัน</span>
            <select value={band} onChange={(e) => setBand(e.target.value)} style={css(BOX)}>
              {form.bands.length === 0 && <option value="">— ยังไม่ได้ตั้งช่วงราคาน้ำมัน —</option>}
              {form.bands.map((b) => <option key={b.label} value={b.label}>{b.label}</option>)}
            </select>
          </div>
        </div>
        <div style={css("font-size:10.5px;color:#94A3B8;margin-top:8px")}>
          ผู้ขอราคาบันทึกจากบัญชีที่เข้าสู่ระบบ ไม่ต้องกรอก — ใบขอราคาคือคำถามที่มีคนถาม ถ้ากรอกเองได้ก็ตอบไม่ได้ว่าใครถาม
        </div>
      </div>

      {/* ----------------------------------------------------------- lanes */}
      {lanes.map((lane, index) => {
        const shown = vehiclesFor(lane, form.vehicles);
        const groups = form.groups.filter((g) => shown.some((v) => v.group === g.key));
        return (
          <div key={lane.key} style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px;display:flex;flex-direction:column;gap:11px")}>
            <div style={css("display:flex;align-items:center;gap:9px")}>
              <span style={css(badge("เส้นทางที่ " + (index + 1), "blue"))}>เส้นทางที่ {index + 1}</span>
              {priced(lane) > 0 && (
                <span style={css("font-size:11px;color:#16794C")}>ใส่ราคาแล้ว {priced(lane)} ประเภท</span>
              )}
              <div style={css("margin-left:auto;display:flex;gap:7px")}>
                {lanes.length > 1 && (
                  <button onClick={() => setLanes((prev) => prev.filter((l) => l.key !== lane.key))}
                    style={css("height:28px;padding:0 11px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:11.5px;cursor:pointer;font-family:inherit")}>
                    ลบเส้นทางนี้
                  </button>
                )}
              </div>
            </div>

            <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px")}>
              <div>
                <span style={css(LABEL)}>ต้นทาง · From</span>
                <input value={lane.fromPlace} onChange={(e) => patch(lane.key, { fromPlace: e.target.value })}
                  placeholder="เช่น Pluakdaeng Rayong 21140" style={css(BOX)} />
              </div>
              <div>
                <span style={css(LABEL)}>ปลายทาง · To</span>
                <input value={lane.toPlace} onChange={(e) => patch(lane.key, { toPlace: e.target.value })}
                  placeholder="เช่น LCB Port" style={css(BOX)} />
              </div>
              <div>
                <span style={css(LABEL)}>จังหวัด · County</span>
                <input value={lane.county} onChange={(e) => patch(lane.key, { county: e.target.value })}
                  placeholder="เช่น Rayong" style={css(BOX)} />
              </div>
            </div>

            <div>
              <span style={css(LABEL)}>ผู้ขนส่งที่ขอราคา · เลือกได้หลายเจ้า</span>
              <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
                {form.carriers.map((name) => {
                  const on = lane.carriers.includes(name);
                  return (
                    <button key={name}
                      onClick={() => patch(lane.key, {
                        carriers: on ? lane.carriers.filter((c) => c !== name) : [...lane.carriers, name],
                      })}
                      style={css("height:26px;padding:0 10px;border:1px solid " + (on ? "#0A2240" : "#E2E8F0") +
                        ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#64748B") +
                        ";border-radius:3px;font-size:11px;cursor:pointer;font-family:inherit")}>
                      {name}
                    </button>
                  );
                })}
                {form.carriers.length === 0 && (
                  <span style={css("font-size:11px;color:#94A3B8")}>ยังไม่มีผู้ขนส่งที่อนุมัติในทะเบียน</span>
                )}
              </div>
            </div>

            <div style={css("display:flex;gap:14px;flex-wrap:wrap;align-items:center;padding-top:3px;border-top:1px solid #F1F5F9")}>
              {([
                ["FCL", lane.fcl, (v: boolean) => patch(lane.key, { fcl: v })],
                ["LCL", lane.lcl, (v: boolean) => patch(lane.key, { lcl: v })],
                ["DG", lane.dg, (v: boolean) => patch(lane.key, { dg: v })],
                ["Reefer", lane.reefer, (v: boolean) => patch(lane.key, { reefer: v })],
              ] as [string, boolean, (v: boolean) => void][]).map(([label, on, set]) => (
                <label key={label} style={css("display:flex;align-items:center;gap:5px;font-size:12px;color:#0F2B46;cursor:pointer")}>
                  <input type="checkbox" checked={on} onChange={(e) => set(e.target.checked)} />
                  {label}
                </label>
              ))}
              <label style={css("display:flex;align-items:center;gap:5px;font-size:11.5px;color:#64748B;cursor:pointer;margin-left:auto")}>
                <input type="checkbox" checked={lane.showAll}
                  onChange={(e) => patch(lane.key, { showAll: e.target.checked })} />
                แสดงราคาทุกประเภท ({form.vehicles.length})
              </label>
            </div>

            {shown.length === 0 ? (
              <div style={css("font-size:11.5px;color:#B45309;background:#FFFAEF;border:1px solid #F5E3C7;border-radius:4px;padding:9px 11px")}>
                เลือก FCL หรือ LCL เพื่อให้ช่องกรอกราคาขึ้นมา — หรือติ๊ก “แสดงราคาทุกประเภท”
              </div>
            ) : (
              groups.map((group) => (
                <div key={group.key}>
                  <span style={css(LABEL)}>{group.label}</span>
                  <div style={css("display:grid;grid-template-columns:repeat(auto-fill,minmax(148px,1fr));gap:8px")}>
                    {shown.filter((v) => v.group === group.key).map((vehicle) => (
                      <div key={vehicle.code}>
                        <span style={css("font-size:10.5px;color:#94A3B8;display:block;margin-bottom:2px")}>{vehicle.label}</span>
                        <input
                          value={lane.prices[vehicle.code] ?? ""}
                          onChange={(e) => patch(lane.key, {
                            prices: { ...lane.prices, [vehicle.code]: e.target.value },
                          })}
                          inputMode="numeric" placeholder="—"
                          style={css(BOX + ";text-align:right;font-family:'IBM Plex Mono',monospace")} />
                      </div>
                    ))}
                  </div>
                </div>
              ))
            )}

            <div>
              <span style={css(LABEL)}>หมายเหตุ</span>
              <input value={lane.remark} onChange={(e) => patch(lane.key, { remark: e.target.value })}
                placeholder="เช่น รวมค่าผ่านท่า / รอยืนยันจากลูกค้า" style={css(BOX)} />
            </div>
          </div>
        );
      })}

      {/* ---------------------------------------------------------- actions */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;align-items:center;gap:10px;flex-wrap:wrap")}>
        <button onClick={() => setLanes((prev) => [...prev, blankLane()])}
          style={css("height:34px;padding:0 15px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer;font-family:inherit")}>
          + เพิ่มเส้นทาง
        </button>
        <span style={css("font-size:11.5px;color:" + (ready ? "#64748B" : "#B45309"))}>
          {ready
            ? `${lanes.length} เส้นทาง พร้อมบันทึก`
            : "กรอกลูกค้า วันที่ และต้นทาง–ปลายทางของทุกเส้นทางให้ครบก่อน"}
        </span>
        <button
          onClick={() => void create()}
          disabled={!ready || busy}
          style={css("margin-left:auto;height:36px;padding:0 22px;border:1px solid " + (ready && !busy ? "#0A2240" : "#D8E0E8") +
            ";background:" + (ready && !busy ? "#0A2240" : "#EDF1F5") + ";color:" + (ready && !busy ? "#fff" : "#94A3B8") +
            ";border-radius:4px;font-size:13px;font-weight:600;cursor:" + (ready && !busy ? "pointer" : "not-allowed") +
            ";font-family:inherit")}
        >
          {busy ? "กำลังบันทึก…" : "สร้างใบขอราคา"}
        </button>
      </div>

      {/* ----------------------------------------------------------- recent */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:600;color:#0A2240")}>
          ใบขอราคาล่าสุด {recent.length ? `· ${recent.length} ใบ` : ""}
        </div>
        {recent.length === 0 ? (
          <div style={css("padding:22px 16px;text-align:center;font-size:12px;color:#94A3B8")}>
            ยังไม่มีใบขอราคาในระบบ — ใบแรกที่สร้างจะขึ้นตรงนี้
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["เลขที่", "วันที่", "ลูกค้า", "ผู้ขอ", "เส้นทาง", "ราคาที่ได้", "สถานะ"].map((h) => (
                    <th key={h} style={css("background:#F4F7FA;padding:8px 12px;text-align:left;font-size:10.5px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {recent.map((inquiry) => {
                  const quoted = inquiry.lanes.reduce(
                    (total, lane) => total + Object.keys(lane.prices ?? {}).length, 0);
                  return (
                    <tr key={inquiry.id}>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9;font-family:'IBM Plex Mono',monospace")}>#{inquiry.number}</td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9;font-family:'IBM Plex Mono',monospace;white-space:nowrap")}>{inquiry.inquiredOn}</td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9")}>{inquiry.customer}</td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9")}>{inquiry.requestor}</td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9")}>{inquiry.lanes.length}</td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9;color:" + (quoted ? "#16794C" : "#94A3B8"))}>
                        {quoted || "—"}
                      </td>
                      <td style={css("padding:8px 12px;border-bottom:1px solid #F1F5F9")}>
                        <span style={css(badge(inquiry.status, inquiry.status === "Quoted" ? "green" : inquiry.status === "Closed" ? "gray" : "amber"))}>
                          {inquiry.status}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * The historical workbook, brought in.
 *
 * The team has kept every quote request in one file since August 2025, a sheet
 * per month, and the register was modelled on it and stood empty. This reads
 * the file the person picks and sends it in batches.
 *
 * What it will not do is pretend. Columns it cannot place, rows it had to date
 * from the sheet's name rather than from the row, and prices two columns
 * disagreed about are all counted and shown before anything is sent — so the
 * decision to import is made knowing what is imperfect about it.
 */
function ImportWorkbook({ onToast, onDone }: { onToast: (m: string) => void; onDone: () => void }) {
  const [read, setRead] = useState<ImportRead | null>(null);
  const [file, setFile] = useState("");
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState(0);

  async function pick(chosen: File) {
    try {
      const XLSX = await import("xlsx");
      const book = XLSX.read(await chosen.arrayBuffer(), { type: "array" });
      // Only the month sheets. The file also holds Remarks, a trucker list and
      // two customers' own rate cards, none of which are inquiries.
      const sheets = book.SheetNames
        .filter((name) => /\d{4}\s*$/.test(name))
        .map((name) => ({
          name,
          rows: XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[name],
            { header: 1, raw: true, defval: null }),
        }));
      setFile(chosen.name);
      setRead(readWorkbook(sheets));
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  async function send() {
    if (!read) return;
    setSending(true);
    setDone(0);
    let added = 0;
    // The endpoint lists at most twenty refusals a batch so the reply stays
    // small, and says separately how many there really were. Counting the list
    // reported 272 of 1,454 — an answer that quietly contradicted its own
    // "added 1290 of 2744".
    let refusedCount = 0;
    const examples: string[] = [];
    try {
      // Two hundred at a time: one body for three thousand inquiries is several
      // megabytes, and a link that drops takes the lot with it.
      for (let at = 0; at < read.inquiries.length; at += 200) {
        const batch = read.inquiries.slice(at, at + 200).map((one) => ({
          inquiredOn: one.inquiredOn,
          customer: one.customer,
          fuelBand: one.fuelBand,
          lanes: one.lanes.map((lane) => ({
            fromPlace: lane.fromPlace, toPlace: lane.toPlace, county: lane.county,
            carriers: lane.carriers, fcl: lane.fcl, lcl: lane.lcl, remark: lane.remark,
            prices: lane.prices,
          })),
        }));
        const response = await apiFetch("/api/rate-inquiries/import", {
          method: "POST",
          headers: { "content-type": "application/json", accept: "application/json" },
          body: JSON.stringify(batch),
        });
        const answer = await response.json().catch(() => null) as
          { added?: number; refused?: string[]; refusedTotal?: number; error?: string } | null;
        if (!response.ok) throw new Error(answer?.error ?? `นำเข้าไม่สำเร็จ (${response.status})`);
        added += answer?.added ?? 0;
        refusedCount += answer?.refusedTotal ?? answer?.refused?.length ?? 0;
        if (answer?.refused) examples.push(...answer.refused);
        setDone(Math.min(at + 200, read.inquiries.length));
      }
      onToast(`นำเข้าแล้ว ${added} จาก ${read.inquiries.length} ใบ`
        + (refusedCount ? ` · ปฏิเสธ ${refusedCount} — ${examples[0]}` : ""));
      setRead(null);
      setFile("");
      onDone();
    } catch (error) {
      onToast(error instanceof Error ? error.message : String(error));
    } finally {
      setSending(false);
    }
  }

  const lanes = read?.inquiries.reduce((n, one) => n + one.lanes.length, 0) ?? 0;
  const prices = read?.inquiries.reduce(
    (n, one) => n + one.lanes.reduce((m, lane) => m + Object.keys(lane.prices).length, 0), 0) ?? 0;
  const fromSheet = read?.inquiries.filter((one) => one.datedFromSheet).length ?? 0;
  // The register will not hold a quote with nobody to quote to. Naming these
  // here — sheet and number — lets the blank be filled in the file, which is
  // where it is wrong; inventing a customer to get them in would not.
  const nameless = read?.inquiries.filter((one) => !one.customer.trim()) ?? [];

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;flex-direction:column;gap:10px")}>
      <div style={css("display:flex;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>นำเข้าจากไฟล์ Rate Inquiry</span>
        <span style={css("font-size:11px;color:#7B8CA0;flex:1;min-width:200px")}>
          อ่านเฉพาะชีทที่เป็นเดือน — ชีท Remarks, Trucker และการ์ดราคาลูกค้าไม่ถูกอ่าน
        </span>
        <input type="file" accept=".xlsx,.xls" disabled={sending}
          onChange={(event) => { const one = event.target.files?.[0]; if (one) void pick(one); }}
          style={css("font-size:11.5px")} />
      </div>

      {read && (
        <div style={css("border:1px solid #E3E8EE;background:#F8FAFC;border-radius:4px;padding:11px 13px;display:flex;flex-direction:column;gap:7px")}>
          <div style={css("font-size:12px;color:#0A2240")}>
            <b>{file}</b> — {read.inquiries.length.toLocaleString()} ใบ ·
            {" "}{lanes.toLocaleString()} เส้นทาง · {prices.toLocaleString()} ราคา
          </div>

          {/* Everything imperfect, before anything is sent. */}
          <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
            {fromSheet > 0 && (
              <div style={css("color:#B45309")}>
                {fromSheet.toLocaleString()} ใบไม่มีวันที่ในไฟล์ — ใช้วันที่ 1 ของเดือนตามชื่อชีท
              </div>
            )}
            {read.unmapped.length > 0 && (
              <div style={css("color:#B42318")}>
                {read.unmapped.length} คอลัมน์ราคาที่ระบบยังไม่รู้จัก จะไม่ถูกนำเข้า: {read.unmapped.join(" · ")}
              </div>
            )}
            {read.conflicts.length > 0 && (
              <div style={css("color:#B45309")}>
                {read.conflicts.length} แถวที่สองคอลัมน์รวมเป็นรถคันเดียวแล้วราคาไม่ตรงกัน — เก็บค่าแรกไว้ ·
                {" "}เช่น {read.conflicts[0]}
              </div>
            )}
            {nameless.length > 0 && (
              <div style={css("color:#B42318")}>
                {nameless.length} ใบไม่มีชื่อลูกค้าในไฟล์ — ระบบจะปฏิเสธ ต้องเติมในไฟล์แล้วนำเข้าใหม่:
                {" "}{nameless.slice(0, 8).map((one) => `${one.sheet} no.${one.number}`).join(" · ")}
                {nameless.length > 8 && ` … อีก ${nameless.length - 8} ใบ`}
              </div>
            )}
            {read.skipped > 0 && <div>{read.skipped} แถวว่างหรือแถวรวม ไม่ถูกนำเข้า</div>}
            {read.unmapped.length === 0 && read.conflicts.length === 0 && fromSheet === 0
              && nameless.length === 0 && (
              <div style={css("color:#16794C")}>อ่านได้ครบทุกคอลัมน์ ไม่มีอะไรตกหล่น</div>
            )}
          </div>

          <div style={css("display:flex;gap:9px;align-items:center")}>
            <button onClick={() => void send()} disabled={sending || read.inquiries.length === 0}
              style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:"
                + (sending ? "#8FA3B8" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
              {sending ? `กำลังนำเข้า ${done.toLocaleString()}/${read.inquiries.length.toLocaleString()}…` : "นำเข้าเข้าระบบ"}
            </button>
            <button onClick={() => { setRead(null); setFile(""); }} disabled={sending}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
              ยกเลิก
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
