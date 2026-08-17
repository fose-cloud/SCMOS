"use client";

import { badge, css, stTone, ALL_STATUS } from "../theme";
import type { Ship } from "../demo";
import type { Account } from "../nav";
import { fdate, ftime, money } from "../util";

export type AuditEntry = {
  ts: string; user: string; job: string; field: string; old: string; neu: string; ip: string;
};

type Props = {
  ship: Ship;
  auth: Account;
  audit: AuditEntry[];
  onStatus: (value: string) => void;
  onEdit: () => void;
};

const STEPS: [string, string][] = [
  ["Booking Received", "รับคำสั่งจอง"], ["Booking Validated", "ตรวจสอบคำสั่ง"], ["Carrier Contacted", "ติดต่อผู้ขนส่ง"],
  ["Truck Confirmed", "ยืนยันรถ"], ["Driver Assigned", "จัดคนขับ"], ["Truck Plate Received", "รับทะเบียนรถ"],
  ["Truck Departed", "รถออกเดินทาง"], ["Arrived Pickup", "ถึงจุดรับ"], ["Loading Started", "เริ่มบรรจุ"],
  ["Loading Completed", "บรรจุเสร็จ"], ["Departed", "ออกจากจุดรับ"], ["Arrived Customer", "ถึงลูกค้า"],
  ["Delivery Started", "เริ่มส่งมอบ"], ["Delivery Completed", "ส่งมอบเสร็จ"], ["Billing Received", "รับเอกสารวางบิล"],
  ["Job Closed", "ปิดงาน"],
];

const STEP_INDEX: Record<string, number> = {
  "Waiting Truck": 2, "Truck Assigned": 4, "Driver Assigned": 5, "Plate Received": 6,
  "Ready for Pickup": 7, "In Transit": 8, "Arrived Customer": 12, "Loading / Delivery": 13,
  Completed: 16, Delayed: 9, Cancelled: 3,
};

export function Detail({ ship: s, auth, audit, onStatus, onEdit }: Props) {
  const mine = s.op === auth.name;
  const editable = auth.role !== "Operation User" || mine;
  const doneN = STEP_INDEX[s.status] || 3;
  const t0 = s.plan.getTime() - 6 * 3600000;

  const timeline = STEPS.map((st, i) => {
    const done = i < doneN;
    const cur = i === doneN;
    const pt = new Date(t0 + i * 55 * 60000);
    const at = new Date(pt.getTime() + (i === 7 && s.status === "Delayed" ? 165 : Math.round(((i * 7) % 5) - 2) * 6) * 60000);
    const diff = Math.round((at.getTime() - pt.getTime()) / 60000);
    return {
      label: st[0], th: st[1],
      plan: fdate(pt) + " " + ftime(pt),
      actual: done ? fdate(at) + " " + ftime(at) : cur ? "in progress" : "—",
      variance: done ? (diff > 0 ? "+" + diff + " min" : diff + " min") : "—",
      dotStyle: "width:11px;height:11px;border-radius:50%;z-index:1;background:" +
        (done ? "#16794C" : cur ? "#D89614" : "#CBD5E1") + ";box-shadow:0 0 0 3px " +
        (done ? "#E3F4EB" : cur ? "#FDF2DF" : "#F1F5F9"),
      lineStyle: "position:absolute;top:50%;bottom:-22px;width:2px;background:" + (done ? "#BFE0CE" : "#E2E8F0"),
      labelStyle: "font-size:12.5px;font-weight:" + (cur ? "600" : "500") + ";color:" + (done || cur ? "#0A2240" : "#94A3B8"),
      actualStyle: "font-size:12px;font-family:'IBM Plex Mono',monospace;color:" + (done ? "#16232F" : "#94A3B8"),
      varStyle: "font-size:11.5px;font-family:'IBM Plex Mono',monospace;text-align:right;color:" +
        (!done ? "#CBD5E1" : diff > 30 ? "#B42318" : diff > 5 ? "#B45309" : "#16794C"),
    };
  });

  const comms = [
    { who: "K. Wanida → " + s.sub, when: fdate(s.plan) + " 07:12", chan: "LINE", text: "Requested " + s.truck + " for " + s.abs + ", pickup at " + s.from + ". Please confirm within 2 hours." },
    { who: s.sub + " → SCMOS", when: fdate(s.plan) + " 08:40", chan: "Phone", text: "Confirmed 1 unit. Driver " + s.driver + ", plate " + s.plate + "." },
    { who: "System", when: fdate(s.plan) + " 08:41", chan: "Auto", text: "Truck confirmation SLA met (1h 28m). Booking moved to Ready for Operation." },
    { who: "K. Somsak → Customer", when: fdate(s.plan) + " 14:05", chan: "Email", text: s.status === "Delayed" ? "Notified customer of delay — reason: " + s.delay + ". Revised ETA shared." : "Shared ETA and driver contact with customer service." },
  ];

  const panels: { title: string; th: string; rows: [string, string][] }[] = [
    { title: "Shipment Information", th: "ข้อมูลงานขนส่ง", rows: [["ABS No.", s.abs], ["Job No.", s.job], ["Booking No.", s.bkg], ["Direction", s.dir], ["Shipment Type", s.sType], ["FCL / LCL", s.fcl], ["DG", s.dg], ["Status", s.status]] },
    { title: "Customer Information", th: "ข้อมูลลูกค้า", rows: [["Customer", s.cust], ["Pickup", s.from], ["Delivery", s.to], ["Plan Loading", fdate(s.plan) + " " + ftime(s.plan)], ["Next Update", s.next]] },
    { title: "Truck & Driver", th: "ข้อมูลรถและคนขับ", rows: [["Subcontractor", s.sub], ["Truck Type", s.truck], ["Truck Plate", s.plate], ["Trailer Plate", s.trailer], ["Driver", s.driver], ["Contact", s.phone]] },
    { title: "Container", th: "ข้อมูลตู้สินค้า", rows: [["Container No.", s.cont], ["Container Type", s.contType], ["Seal No.", s.fcl === "FCL" ? "SL" + (400000 + s.id * 17) : "—"]] },
    { title: "Billing", th: "การวางบิล", rows: [["Trucking Charge", money(s.cost)], ["Additional Charge", money(s.cost * 0.06)], ["Selling Rate", money(s.sell)], ["Margin", money(s.sell - s.cost)], ["Billing Status", s.bill]] },
    { title: "Delay / Exception & Safety", th: "ความล่าช้าและความปลอดภัย", rows: [["Delay Reason", s.delay], ["Pre-Start Checklist", "Pass"], ["Twist Lock Inspection", s.fcl === "FCL" ? "Pass" : "N/A"], ["PPE Verified", "Pass"], ["GPS Signal", "Active"], ["Documents", "4 attached"]] },
  ];

  const valueStyle = (k: string, v: string) => {
    if (k === "Status") return badge(v, stTone(v));
    if (k === "Billing Status") return badge(v, v === "Overdue" ? "red" : v === "Due Soon" ? "amber" : "green");
    return "font-size:12px;color:#16232F;font-weight:500;text-align:right;font-family:" +
      (/^[฿0-9A-Z\-\s.,:]+$/.test(v) ? "'IBM Plex Mono',monospace" : "inherit");
  };

  const sysAudit = [
    { ts: fdate(s.plan) + " 08:15", user: "System", act: "Booking Received", field: "—", old: "—", neu: s.bkg },
    { ts: fdate(s.plan) + " 08:32", user: s.assignedBy, act: "Assigned to " + s.op, field: "Assigned Operator", old: "Unassigned", neu: s.op },
    { ts: fdate(s.plan) + " 09:10", user: s.op, act: "Truck Confirmed", field: "Current Status", old: "Waiting Truck", neu: "Truck Assigned" },
    { ts: fdate(s.plan) + " 09:25", user: s.op, act: "Driver Information Added", field: "Driver Name", old: "—", neu: s.driver },
    { ts: fdate(s.plan) + " 09:41", user: s.op, act: "Truck Plate Received", field: "Truck Plate", old: "—", neu: s.plate },
    { ts: fdate(s.plan) + " 13:42", user: s.op, act: "Arrived Customer", field: "Current Status", old: "In Transit", neu: "Arrived Customer" },
  ];
  const liveAudit = audit
    .filter((x) => x.job === s.abs)
    .map((x) => ({ ts: x.ts, user: x.user, act: "Field updated", field: x.field, old: x.old, neu: x.neu }));
  const auditRows = liveAudit.concat(sysAudit.slice().reverse());

  const AUDIT_GRID = "display:grid;grid-template-columns:18px 138px 108px 1fr 150px 150px;gap:10px";

  return (
    <>
      <div style={css(
        "display:flex;align-items:center;gap:14px;padding:12px 16px;border-radius:5px;border:1px solid " +
        (mine ? "#BBD5EE" : "#E2E8F0") + ";background:" + (mine ? "#F4F8FC" : "#F8FAFC") +
        ";border-left:4px solid " + (mine ? "#2E7DD1" : "#94A3B8"),
      )}>
        <span style={css(badge(mine ? "MY JOB" : "VIEW ONLY", mine ? "blue" : "gray"))}>{mine ? "MY JOB" : "VIEW ONLY"}</span>
        <div style={css("display:flex;flex-direction:column;line-height:1.35;min-width:0")}>
          <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
            {mine
              ? "You own this job — status and operational fields are editable."
              : "This job is currently handled by " + s.op + ". " + (editable ? "Your role (" + auth.role + ") allows supervisory editing." : "Editing is disabled for your role.")}
          </span>
          <span style={css("font-size:11px;color:#64748B")}>
            Assigned Operator: {s.op} ({s.opId}) · Assigned {s.assignedDate} by {s.assignedBy}
          </span>
        </div>
        <div style={css("margin-left:auto;display:flex;align-items:center;gap:10px")}>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css("font-size:10px;color:#8496A8;letter-spacing:.05em;font-weight:600")}>QUICK STATUS UPDATE</span>
            <select
              value={s.status}
              onChange={(e) => onStatus(e.target.value)}
              style={css(
                "height:34px;min-width:190px;border:1px solid " + (editable ? "#BBD5EE" : "#E2E8F0") +
                ";border-radius:4px;background:" + (editable ? "#F4F8FC" : "#F1F5F9") +
                ";font-size:12.5px;font-weight:600;color:" + (editable ? "#0A2240" : "#94A3B8") +
                ";padding:0 9px;cursor:" + (editable ? "pointer" : "not-allowed"),
              )}
            >
              {ALL_STATUS.indexOf(s.status) < 0 && <option value={s.status}>{s.status}</option>}
              {ALL_STATUS.map((o) => <option key={o} value={o}>{o}</option>)}
            </select>
          </label>
          <button
            onClick={onEdit}
            style={css(
              "height:34px;padding:0 18px;border:1px solid " + (editable ? "#0A2240" : "#D8E0E8") +
              ";background:" + (editable ? "#0A2240" : "#EDF1F5") + ";color:" + (editable ? "#fff" : "#94A3B8") +
              ";border-radius:4px;font-size:12.5px;font-weight:600;cursor:" + (editable ? "pointer" : "not-allowed"),
            )}
          >
            {editable ? "EDIT JOB" : "VIEW ONLY"}
          </button>
        </div>
      </div>

      <div style={css("display:grid;grid-template-columns:1fr 400px;gap:16px;align-items:start")}>
        <div style={css("display:flex;flex-direction:column;gap:16px")}>
          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:18px 20px")}>
            <div style={css("display:flex;justify-content:space-between;align-items:center;margin-bottom:18px")}>
              <h3 style={css("margin:0;font-size:14px;font-weight:600;color:#0A2240")}>Operational Timeline · ไทม์ไลน์การปฏิบัติงาน</h3>
              <span style={css("font-size:11.5px;color:#64748B")}>Planned vs Actual · {doneN} of 16 steps complete</span>
            </div>
            <div style={css("display:flex;flex-direction:column")}>
              {timeline.map((st) => (
                <div key={st.label} style={css("display:grid;grid-template-columns:26px 1fr 130px 130px 92px;gap:12px;align-items:center;padding:9px 0;border-bottom:1px solid #F1F5F9")}>
                  <div style={css("position:relative;display:flex;justify-content:center")}>
                    <span style={css(st.dotStyle)} />
                    <span style={css(st.lineStyle)} />
                  </div>
                  <div style={css("display:flex;flex-direction:column;line-height:1.3")}>
                    <span style={css(st.labelStyle)}>{st.label}</span>
                    <span style={css("font-size:10.5px;color:#94A3B8")}>{st.th}</span>
                  </div>
                  <span style={css("font-size:12px;color:#64748B;font-family:'IBM Plex Mono',monospace")}>{st.plan}</span>
                  <span style={css(st.actualStyle)}>{st.actual}</span>
                  <span style={css(st.varStyle)}>{st.variance}</span>
                </div>
              ))}
            </div>
          </div>

          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:18px 20px")}>
            <h3 style={css("margin:0 0 14px;font-size:14px;font-weight:600;color:#0A2240")}>Audit Trail · บันทึกการเปลี่ยนแปลง</h3>
            <div style={css("display:flex;flex-direction:column;margin-bottom:22px")}>
              <div style={css(AUDIT_GRID + ";padding:0 0 7px;font-size:10px;font-weight:600;color:#8496A8;letter-spacing:.05em;border-bottom:1px solid #E9EFF5")}>
                <span /><span>DATE / TIME</span><span>USER</span><span>ACTION / FIELD</span><span>OLD VALUE</span><span>NEW VALUE</span>
              </div>
              {auditRows.map((r, i) => (
                <div key={i} style={css(AUDIT_GRID + ";align-items:center;padding:8px 0;border-bottom:1px solid #F5F8FA")}>
                  <span style={css("width:9px;height:9px;border-radius:50%;flex:none;background:" + (i === 0 ? "#2E7DD1" : "#CBD5E1"))} />
                  <span style={css("font-size:11.5px;font-family:'IBM Plex Mono',monospace;color:#475569")}>{r.ts}</span>
                  <span style={css("font-size:11.5px;font-weight:600;color:#0A2240")}>{r.user}</span>
                  <span style={css("font-size:11.5px;color:#334155")}>{r.act} · {r.field}</span>
                  <span style={css("font-size:11.5px;font-family:'IBM Plex Mono',monospace;color:#94A3B8")}>{r.old}</span>
                  <span style={css("font-size:11.5px;font-family:'IBM Plex Mono',monospace;color:#16794C;font-weight:600")}>{r.neu}</span>
                </div>
              ))}
            </div>

            <h3 style={css("margin:0 0 14px;font-size:14px;font-weight:600;color:#0A2240")}>Communication History · ประวัติการติดต่อ</h3>
            <div style={css("display:flex;flex-direction:column;gap:12px")}>
              {comms.map((c, i) => (
                <div key={i} style={css("display:flex;gap:12px;padding:11px 13px;background:#F8FAFC;border:1px solid #E9EFF5;border-left:3px solid #2E7DD1;border-radius:4px")}>
                  <div style={css("flex:1;min-width:0")}>
                    <div style={css("display:flex;gap:8px;align-items:baseline")}>
                      <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{c.who}</span>
                      <span style={css("font-size:11px;color:#94A3B8;font-family:'IBM Plex Mono',monospace")}>{c.when}</span>
                      <span style={css(badge(c.chan, c.chan === "Auto" ? "gray" : "blue"))}>{c.chan}</span>
                    </div>
                    <div style={css("font-size:12.5px;color:#334155;margin-top:4px;text-wrap:pretty")}>{c.text}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div style={css("display:flex;flex-direction:column;gap:14px")}>
          {panels.map((p) => (
            <div key={p.title} style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
              <div style={css("padding:11px 16px;background:#F8FAFC;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:baseline")}>
                <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{p.title}</span>
                <span style={css("font-size:10.5px;color:#94A3B8")}>{p.th}</span>
              </div>
              <div style={css("padding:6px 16px 12px")}>
                {p.rows.map((r) => (
                  <div key={r[0]} style={css("display:flex;justify-content:space-between;gap:14px;padding:7px 0;border-bottom:1px solid #F5F8FA")}>
                    <span style={css("font-size:11.5px;color:#64748B;flex:none")}>{r[0]}</span>
                    <span style={css(valueStyle(r[0], String(r[1])))}>{r[1]}</span>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </>
  );
}
