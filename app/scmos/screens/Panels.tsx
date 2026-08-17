"use client";

import { badge, css } from "../theme";
import type { Db, Ship } from "../demo";
import { money, pad, rng } from "../util";


/* ------------------------------------------------------------- import/export */

const IMPORT_STEPS: [string, string][] = [
  ["Port / Terminal Arrival", "ถึงท่าเรือ"], ["Customs Release", "ผ่านพิธีการ"], ["E-Card Issued", "ออก E-Card"],
  ["Container Pickup", "รับตู้สินค้า"], ["Warehouse Delivery", "ส่งคลังสินค้า"], ["Empty Container Return", "คืนตู้เปล่า"],
];
const EXPORT_STEPS: [string, string][] = [
  ["Empty Container Pickup", "รับตู้เปล่า"], ["Customer Loading", "บรรจุที่ลูกค้า"], ["Loaded Container Pickup", "รับตู้บรรจุแล้ว"],
  ["Port Return", "ส่งกลับท่าเรือ"], ["Terminal Gate-In", "ผ่านประตูท่าเรือ"],
];

export function Flow({ direction, filtered }: { direction: "import" | "export"; filtered: Ship[] }) {
  const steps = direction === "import" ? IMPORT_STEPS : EXPORT_STEPS;
  const set = filtered.filter((s) => s.dir === (direction === "import" ? "Import" : "Export"));

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:18px 20px")}>
      <h3 style={css("margin:0 0 16px;font-size:14px;font-weight:600;color:#0A2240")}>
        {direction === "import"
          ? "Import Process Progress · ความคืบหน้ากระบวนการนำเข้า"
          : "Export Process Progress · ความคืบหน้ากระบวนการส่งออก"}
      </h3>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px")}>
        {steps.map((st, i) => {
          const done = Math.max(0, set.length - i * Math.ceil(set.length / (steps.length + 3)));
          const pending = set.length - done;
          const pct = set.length ? (done / set.length) * 100 : 0;
          const pill = pending > 12 ? "Attention" : "On track";
          return (
            <div key={st[0]} style={css("border:1px solid #E2E8F0;border-radius:5px;padding:14px 15px;background:#F8FAFC")}>
              <div style={css("display:flex;justify-content:space-between;align-items:center;margin-bottom:9px")}>
                <span style={css("width:22px;height:22px;border-radius:3px;background:#0A2240;color:#fff;font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center;font-family:'IBM Plex Mono',monospace")}>
                  {pad(i + 1)}
                </span>
                <span style={css(badge(pill, pending > 12 ? "amber" : "green"))}>{pill}</span>
              </div>
              <div style={css("font-size:12.5px;font-weight:600;color:#0A2240;line-height:1.3")}>{st[0]}</div>
              <div style={css("font-size:10.5px;color:#94A3B8;margin-bottom:10px")}>{st[1]}</div>
              <div style={css("height:6px;background:#E2E8F0;border-radius:3px;overflow:hidden")}>
                <div style={css("height:100%;width:" + pct + "%;background:#2E7DD1")} />
              </div>
              <div style={css("display:flex;justify-content:space-between;margin-top:6px")}>
                <span style={css("font-size:10.5px;color:#64748B")}>{done} cleared</span>
                <span style={css("font-size:10.5px;color:#64748B")}>{pending} pending</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------- capacity */

export function Capacity({ db }: { db: Db }) {
  const days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  const R = rng(777);
  let avail = 0;
  let booked = 0;

  const capRows = db.suppliers.map((sp) => {
    const cells = days.map(() => {
      const a = 3 + Math.floor(R() * 7);
      const b = Math.min(a + (R() < 0.2 ? 2 : 0), Math.floor(R() * (a + 2)));
      avail += a;
      booked += b;
      const rem = a - b;
      const tone = rem < 0 ? ["#B42318", "#FCE9E7"] : rem === 0 ? ["#B45309", "#FDF2DF"] : ["#16794C", "#E3F4EB"];
      return {
        txt: b + "/" + a,
        sub: rem < 0 ? "short " + Math.abs(rem) : rem + " free",
        style: "text-align:center;padding:7px 4px;border-radius:3px;background:" + tone[1] + ";color:" + tone[0] + ";border:1px solid " + tone[1],
      };
    });
    return { name: sp.name, types: sp.service, cells };
  });

  const capKpis: [string, string, string][] = [
    ["Required Capacity", "กำลังรถที่ต้องการ", String(booked + 24)],
    ["Confirmed Capacity", "กำลังรถที่ยืนยัน", String(booked)],
    ["Available Trucks", "รถที่พร้อมใช้งาน", String(avail)],
    ["Shortage", "ขาดแคลน", "24"],
  ];

  return (
    <div style={css("display:flex;flex-direction:column;gap:16px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px")}>
        {capKpis.map((k, i) => (
          <div key={k[0]} style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid " + (i === 3 ? "#B42318" : "#2E7DD1") + ";border-radius:5px;padding:14px 16px")}>
            <div style={css("font-size:11.5px;color:#475569;font-weight:600")}>{k[0]}</div>
            <div style={css("font-size:10.5px;color:#94A3B8")}>{k[1]}</div>
            <div style={css("font-size:28px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240;margin-top:8px")}>{k[2]}</div>
          </div>
        ))}
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:18px 20px;overflow-x:auto")}>
        <h3 style={css("margin:0 0 4px;font-size:14px;font-weight:600;color:#0A2240")}>Weekly Capacity Planning Board</h3>
        <div style={css("font-size:11.5px;color:#94A3B8;margin-bottom:16px")}>
          Available / Booked per subcontractor · กระดานวางแผนกำลังรถรายสัปดาห์
        </div>
        <div style={css("min-width:900px;display:flex;flex-direction:column;gap:4px")}>
          <div style={css("display:grid;grid-template-columns:190px repeat(7,1fr);gap:4px")}>
            <div />
            {days.map((d, i) => (
              <div key={d} style={css("text-align:center;padding:7px 0;background:#0A2240;color:#fff;border-radius:3px")}>
                <div style={css("font-size:11px;font-weight:600")}>{d}</div>
                <div style={css("font-size:10px;color:#9DBBD6;font-family:'IBM Plex Mono',monospace")}>{pad(10 + i)}/08</div>
              </div>
            ))}
          </div>
          {capRows.map((r) => (
            <div key={r.name} style={css("display:grid;grid-template-columns:190px repeat(7,1fr);gap:4px")}>
              <div style={css("display:flex;flex-direction:column;justify-content:center;padding:6px 10px;background:#F8FAFC;border:1px solid #E9EFF5;border-radius:3px")}>
                <span style={css("font-size:12px;font-weight:600;color:#0A2240")}>{r.name}</span>
                <span style={css("font-size:10.5px;color:#94A3B8")}>{r.types}</span>
              </div>
              {r.cells.map((c, i) => (
                <div key={i} style={css(c.style)}>
                  <div style={css("font-size:13px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>{c.txt}</div>
                  <div style={css("font-size:9.5px;opacity:.7;margin-top:2px")}>{c.sub}</div>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* --------------------------------------------------------- billing aging */

export function BillingAging({ filtered }: { filtered: Ship[] }) {
  const done = filtered.filter((s) => s.status === "Completed");
  const bucket = (lo: number, hi: number) =>
    done.filter((s) => s.invDays !== null && s.invDays >= lo && s.invDays <= hi);

  const defs: [string, string, Ship[], string, "green" | "amber" | "red" | "blue"][] = [
    ["0–2 Days", "ภายใน 2 วัน", bucket(0, 2), "Within KPI", "green"],
    ["3 Days", "3 วัน", bucket(3, 3), "Due Soon", "amber"],
    ["4 Days", "4 วัน", bucket(4, 4), "KPI Limit", "amber"],
    ["> 4 Days", "เกิน 4 วัน", bucket(5, 99), "Overdue", "red"],
    ["Advance Receipts", "รับล่วงหน้า", done.slice(0, 9), "Received", "blue"],
  ];

  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px")}>
      {defs.map((d) => (
        <div
          key={d[0]}
          style={css(
            "background:#fff;border-top:3px solid " +
            (d[4] === "red" ? "#B42318" : d[4] === "amber" ? "#D89614" : d[4] === "green" ? "#16794C" : "#2E7DD1") +
            ";border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8" +
            ";border-radius:5px;padding:14px 16px",
          )}
        >
          <div style={css("display:flex;justify-content:space-between;align-items:baseline")}>
            <span style={css("font-size:12px;font-weight:600;color:#0A2240")}>{d[0]}</span>
            <span style={css(badge(d[3], d[4]))}>{d[3]}</span>
          </div>
          <div style={css("font-size:10.5px;color:#94A3B8")}>{d[1]}</div>
          <div style={css("display:flex;align-items:baseline;gap:8px;margin-top:10px")}>
            <span style={css("font-size:28px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{d[2].length}</span>
            <span style={css("font-size:12px;color:#64748B")}>{money(d[2].reduce((sum, s) => sum + s.cost, 0))}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

/* --------------------------------------------------------------- reports */

const REPORT_DEFS: [string, string, string, string][] = [
  ["Shipment Volume Report", "รายงานปริมาณงาน", "Total trips by day, week and month with import/export split.", "Operations"],
  ["Import Volume", "ปริมาณงานนำเข้า", "Import trips by port, terminal and customer.", "Operations"],
  ["Export Volume", "ปริมาณงานส่งออก", "Export trips by origin plant and destination terminal.", "Operations"],
  ["Trips by Supplier", "เที่ยวตามผู้ขนส่ง", "Allocation share per subcontractor with trend.", "Supplier"],
  ["Trips by Truck Type", "เที่ยวตามประเภทรถ", "Fleet mix utilisation across the period.", "Operations"],
  ["Trips by Customer", "เที่ยวตามลูกค้า", "Customer ranking by volume and direction.", "Commercial"],
  ["Transportation Cost", "ต้นทุนค่าขนส่ง", "Total cost with fuel adjustment breakdown.", "Finance"],
  ["Cost by Customer", "ต้นทุนตามลูกค้า", "Cost, selling rate and margin per customer.", "Finance"],
  ["Cost by Supplier", "ต้นทุนตามผู้ขนส่ง", "Spend concentration and rate variance.", "Finance"],
  ["Supplier Performance", "ผลงานผู้ขนส่ง", "Scorecard across ten weighted KPIs.", "Supplier"],
  ["Delay Analysis", "วิเคราะห์ความล่าช้า", "Delay reasons, frequency and responsible party.", "Operations"],
  ["Billing KPI", "รายงาน KPI การวางบิล", "4-day compliance and aging distribution.", "Finance"],
  ["Safety Report", "รายงานความปลอดภัย", "Checklist compliance, incidents and PPE.", "Safety"],
  ["CAR / PAR Report", "รายงาน CAR/PAR", "Case aging, closure rate and repeat findings.", "Quality"],
];

const REPORT_TONES: Record<string, "blue" | "green" | "teal" | "amber" | "red" | "gray"> = {
  Operations: "blue", Finance: "green", Supplier: "teal", Safety: "amber", Quality: "red", Commercial: "gray",
};

export function Reports({ toast }: { toast: (message: string) => void }) {
  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px")}>
      {REPORT_DEFS.map((d) => (
        <div key={d[0]} className="card-hover" style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:16px 17px;display:flex;flex-direction:column;gap:10px")}>
          <div style={css("display:flex;justify-content:space-between;align-items:flex-start;gap:10px")}>
            <div>
              <div style={css("font-size:13px;font-weight:600;color:#0A2240;line-height:1.3")}>{d[0]}</div>
              <div style={css("font-size:10.5px;color:#94A3B8;margin-top:2px")}>{d[1]}</div>
            </div>
            <span style={css(badge(d[3], REPORT_TONES[d[3]]))}>{d[3]}</span>
          </div>
          <div style={css("font-size:11.5px;color:#64748B;line-height:1.5;text-wrap:pretty")}>{d[2]}</div>
          <div style={css("display:flex;gap:6px;margin-top:auto;padding-top:6px;border-top:1px solid #F1F5F9")}>
            <button className="dark-btn" onClick={() => toast("Generating " + d[0] + "…")} style={css("flex:1;height:30px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:11.5px;font-weight:500;cursor:pointer")}>Run</button>
            <button className="ghost-btn" onClick={() => toast(d[0] + " → Excel")} style={css("height:30px;padding:0 11px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:11.5px;cursor:pointer")}>Excel</button>
            <button className="ghost-btn" onClick={() => toast(d[0] + " → PDF")} style={css("height:30px;padding:0 11px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:11.5px;cursor:pointer")}>PDF</button>
          </div>
        </div>
      ))}
    </div>
  );
}

/* ------------------------------------------------------- supplier profile */

export function SupplierProfile({ db, name }: { db: Db; name: string }) {
  const sp = db.suppliers.find((x) => x.name === name) || db.suppliers[0];
  const panels: [string, string, [string, string][]][] = [
    ["Company Information", "ข้อมูลบริษัท", [["Supplier Code", sp.code], ["Company Name", sp.name], ["Vendor Number", sp.vendor], ["Service Type", sp.service], ["Service Area", sp.area], ["Status", sp.status]]],
    ["Contacts", "ผู้ติดต่อ", [["Contact Person", sp.contact], ["Phone", sp.phone], ["Email", sp.email], ["Billing Contact", sp.contact], ["Billing Email", sp.billEmail]]],
    ["Fleet & Drivers", "รถและคนขับ", [["Fleet Size", sp.fleet + " units"], ["Truck Types", sp.trucks + " types"], ["Registered Drivers", String(sp.drivers)], ["DG Capability", sp.dg], ["Reefer", sp.reefer], ["ISO Tank", sp.iso], ["GPS", sp.gps]]],
    ["Insurance & Licence", "ประกันและใบอนุญาต", [["Cargo Insurance", sp.ins], ["Insurance Expiry", sp.insExp], ["Transport Licence", sp.lic], ["Licence Expiry", sp.licExp], ["Safety Training", sp.safety]]],
    ["Performance", "ผลการดำเนินงาน", [["Overall Score", String(sp.score)], ["Total Trips", String(sp.trips)], ["Delayed Trips", String(sp.late)], ["On-Time Rate", (sp.trips ? Math.round((1 - sp.late / sp.trips) * 100) : 0) + "%"], ["Open CAR/PAR", String(db.carpar.filter((c) => c.sub === sp.name && c.status !== "Closed").length)]]],
    ["Billing & Documents", "การวางบิลและเอกสาร", [["Invoices this month", String(12 + (sp.trips % 9))], ["Avg. days to invoice", (2 + (sp.trips % 3)) + " days"], ["Billing Compliance", 88 + (sp.trips % 9) + "%"], ["Attachments", "14 files"], ["Last Audit", "18 Mar 2026"]]],
  ];

  return (
    <div style={css("display:grid;grid-template-columns:1fr 1fr 1fr;gap:14px;align-items:start")}>
      {panels.map((p) => (
        <div key={p[0]} style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;background:#F8FAFC;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:baseline")}>
            <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{p[0]}</span>
            <span style={css("font-size:10.5px;color:#94A3B8")}>{p[1]}</span>
          </div>
          <div style={css("padding:6px 16px 12px")}>
            {p[2].map((r) => (
              <div key={r[0]} style={css("display:flex;justify-content:space-between;gap:14px;padding:7px 0;border-bottom:1px solid #F5F8FA")}>
                <span style={css("font-size:11.5px;color:#64748B;flex:none")}>{r[0]}</span>
                <span style={css(
                  r[0] === "Status"
                    ? badge(r[1], r[1] === "Approved" ? "green" : r[1] === "Suspended" ? "red" : "amber")
                    : "font-size:12px;color:#16232F;font-weight:500;text-align:right",
                )}>
                  {r[1]}
                </span>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
