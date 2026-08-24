"use client";

import { useState, type ChangeEvent, type DragEvent } from "react";
import { badge, css, opTone } from "../theme";
import { isCancelled, MOVED_BY, wasMoved, type Job, type Masters } from "../ops";

/* ---------------------------------------------------------- job drawer */

export function JobDrawer(p: {
  job: Job;
  mine: boolean;
  canEdit: boolean;
  onClose: () => void;
  onEdit: () => void;
  onDuplicate: () => void;
  onReassign: () => void;
  onMove: () => void;
  onCancelJob: () => void;
  onDelete: () => void;
}) {
  const { job: j } = p;
  const rows: [string, string | undefined][] =
    j.cat === "DELIVERY"
      ? [["Warehouse", j.wh], ["Job No.", j.jobNo], ["SID No.", j.sid], ["Pickup Date", j.date],
        ["Customer", j.customer], ["Province / ZIP", (j.province || "") + " " + (j.zip || "")],
        ["Pallet / KG", (j.pallet || "") + " / " + (j.kgs || "")], ["Vehicles", j.type],
        ["Transport Cost", j.cost ? "฿" + Number(j.cost).toLocaleString("en-US") : "—"], ["Assigned Operator", j.op]]
      : [["Job / ABS", j.jobCode || j.abs], ["Booking", j.booking], ["Customer", j.customer],
        ["Trucking Company", j.trucker], ["Type", j.type], ["CY Yard", j.cyYard],
        [j.cat === "EXPORT" ? "Plant Loading" : "Destination", j.plant || j.destination],
        ["Return / Empty", j.returnLoc || j.emptyReturn], ["Plan Date / Time", j.date + " " + j.planTime],
        ["Closing", j.closingDate ? j.closingDate + " " + j.closingTime : "—"], ["Container", j.container],
        ["Seal", j.seal], ["Weight / Tare", j.weight || j.tare], ["Licence", j.licence], ["Driver", j.driver],
        ["Driver Contact", j.contact], ["Arrival", (j.arrDate || "") + " " + (j.arrTime || "")],
        ["Pickup Plan Date", j.pickupPlan], ["Pickup Plan Time", j.pickupTime], ["CS", j.cs], ["Reason / Delay", j.reason], ["Remark", j.remark],
        ["Assigned Operator", j.op]];

  const hist = (j.hist || []).slice().reverse().concat([{
    ts: "08:10", user: j.op, field: "Job created from " + j.cat + " plan", old: "—", neu: j.jobCode || j.jobNo || "",
  }]);

  return (
    <aside style={css("position:fixed;top:60px;right:0;bottom:0;width:440px;background:#fff;border-left:1px solid #D8E0E8;box-shadow:-8px 0 28px rgba(10,34,64,.12);z-index:55;display:flex;flex-direction:column;animation:tin .16s ease")}>
      <div style={css("padding:14px 18px;background:#0A2240;display:flex;justify-content:space-between;align-items:flex-start;gap:10px")}>
        <div style={css("display:flex;flex-direction:column;gap:4px;min-width:0")}>
          <span style={css("font-size:14px;font-weight:600;color:#fff")}>{j.customer}</span>
          <span style={css("font-size:11px;color:#7FA5CC;font-family:'IBM Plex Mono',monospace")}>
            {(j.cat === "DELIVERY" ? j.jobNo : j.jobCode || j.abs)} · {j.cat}
          </span>
        </div>
        <button onClick={p.onClose} aria-label="Close job" style={css("width:28px;height:28px;flex:none;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer;font-size:14px")}>✕</button>
      </div>

      <div style={css("padding:12px 18px;border-bottom:1px solid #E9EFF5;display:flex;align-items:center;gap:9px;flex-wrap:wrap;background:#F8FAFC")}>
        <span style={css(badge(p.mine ? "MY JOB" : "VIEW ONLY", p.mine ? "blue" : "gray"))}>{p.mine ? "MY JOB" : "VIEW ONLY"}</span>
        <span style={css(badge(j.status, opTone(j.status)))}>{j.status}</span>
        <span style={css("font-size:11px;color:#64748B;flex-basis:100%")}>
          {p.mine ? "You own this job." : "Handled by " + j.op + (p.canEdit ? " — your role allows supervisory editing." : " — view only.")}
        </span>
      </div>

      <div style={css("flex:1;overflow-y:auto;padding:14px 18px;display:flex;flex-direction:column;gap:16px")}>
        {(isCancelled(j) || wasMoved(j)) && (
          <div style={css("border:1px solid " + (isCancelled(j) ? "#F3C3BE" : "#F5E3C7") +
            ";background:" + (isCancelled(j) ? "#FDF6F5" : "#FFFAEF") +
            ";border-radius:5px;padding:11px 13px;display:flex;flex-direction:column;gap:5px")}>
            <span style={css("font-size:11.5px;font-weight:600;color:" + (isCancelled(j) ? "#B42318" : "#B45309"))}>
              {isCancelled(j) ? "งานนี้ถูกยกเลิก" : "งานนี้ถูกเลื่อนวัน"}
            </span>
            {isCancelled(j)
              ? <span style={css("font-size:11.5px;color:#475569")}>{j.cancelReason || "ไม่ได้ระบุเหตุผล"}</span>
              : (
                <>
                  <span style={css("font-size:11.5px;color:#475569")}>
                    เดิม <b>{j.origDate}</b> → ปัจจุบัน <b>{j.date}</b>
                    {j.moveBy ? " · ผู้ขอเลื่อน: " + j.moveBy : ""}
                  </span>
                  <span style={css("font-size:11.5px;color:#475569")}>{j.moveReason || "ไม่ได้ระบุเหตุผล"}</span>
                </>
              )}
            <span style={css("font-size:10.5px;color:#94A3B8")}>
              ทุกครั้งที่เลื่อนถูกบันทึกไว้ในประวัติด้านล่าง · วันเดิมคือวันแรกที่วางแผนไว้ ไม่ใช่ครั้งก่อนหน้า
            </span>
          </div>
        )}

        {!!j.issues.length && (
          <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:11px 13px;display:flex;flex-direction:column;gap:9px")}>
            <div style={css("display:flex;align-items:center;gap:8px")}>
              <span style={css(badge("รูปแบบข้อมูลผิด", "red"))}>รูปแบบข้อมูลผิด</span>
              <span style={css("font-size:11px;color:#64748B")}>{j.issues.length} รายการ · งานนี้ไม่ถูกนับใน KPI จนกว่าจะแก้</span>
            </div>
            {j.issues.map((issue, i) => (
              <div key={issue.field + i} style={css("display:flex;flex-direction:column;gap:2px;padding-top:7px;border-top:1px solid #F6E3E1")}>
                <span style={css("font-size:11.5px;font-weight:600;color:#0A2240")}>{issue.label}</span>
                <span style={css("font-size:11px;color:#B42318;word-break:break-all")}>
                  ค่าปัจจุบัน: {issue.value ? "“" + issue.value + "”" : "(ว่าง)"} — {issue.message}
                </span>
                <span style={css("font-size:11px;color:#475569")}>
                  ต้องเป็น: {issue.expected} · เช่น <span style={css("font-family:'IBM Plex Mono',monospace")}>{issue.example}</span>
                </span>
              </div>
            ))}
          </div>
        )}

        {!!j.fixes.length && (
          <div style={css("border:1px solid #CFE3D6;background:#F5FBF8;border-radius:5px;padding:10px 12px;display:flex;flex-direction:column;gap:5px")}>
            <span style={css("font-size:11.5px;font-weight:600;color:#16794C")}>จัดรูปแบบให้อัตโนมัติ {j.fixes.length} รายการ</span>
            {j.fixes.map((fix, i) => (
              <span key={fix.field + i} style={css("font-size:11px;color:#475569")}>
                {fix.label}: <span style={css("font-family:'IBM Plex Mono',monospace")}>{fix.from}</span> → <span style={css("font-family:'IBM Plex Mono',monospace;color:#16794C")}>{fix.to}</span>
              </span>
            ))}
          </div>
        )}

        {!!j.flags.length && (
          <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
            {j.flags.map((f) => <span key={f} style={css(badge(f, "amber"))}>{f}</span>)}
          </div>
        )}

        <div style={css("display:flex;flex-direction:column")}>
          {rows.map((r) => (
            <div key={r[0]} style={css("display:flex;justify-content:space-between;gap:14px;padding:7px 0;border-bottom:1px solid #F5F8FA")}>
              <span style={css("font-size:11.5px;color:#64748B;flex:none")}>{r[0]}</span>
              <span style={css("font-size:12px;color:#16232F;font-weight:500;text-align:right;word-break:break-word")}>
                {r[1] === undefined || String(r[1]).trim() === "" ? "—" : String(r[1])}
              </span>
            </div>
          ))}
        </div>

        <div>
          <div style={css("font-size:12px;font-weight:600;color:#0A2240;margin-bottom:8px")}>Change History · ประวัติการแก้ไข</div>
          {hist.map((h, i) => (
            <div key={i} style={css("display:flex;gap:10px;padding:7px 0;border-bottom:1px solid #F5F8FA")}>
              <span style={css("font-size:11px;font-family:'IBM Plex Mono',monospace;color:#94A3B8;flex:none")}>{h.ts}</span>
              <span style={css("font-size:11px;font-weight:600;color:#0A2240;flex:none;width:74px")}>{h.user}</span>
              <span style={css("font-size:11px;color:#475569;text-wrap:pretty")}>{h.field}: {h.old} → {h.neu}</span>
            </div>
          ))}
        </div>
      </div>

      <div style={css("padding:13px 18px;border-top:1px solid #E9EFF5;background:#FBFCFD;display:flex;gap:8px")}>
        <button
          onClick={p.onEdit}
          style={css(
            "flex:1;height:34px;border:1px solid " + (p.canEdit ? "#0A2240" : "#D8E0E8") +
            ";background:" + (p.canEdit ? "#0A2240" : "#EDF1F5") + ";color:" + (p.canEdit ? "#fff" : "#94A3B8") +
            ";border-radius:4px;font-size:12.5px;font-weight:600;cursor:" + (p.canEdit ? "pointer" : "not-allowed"),
          )}
        >
          {p.canEdit ? "Edit inline in table" : "View only"}
        </button>
        <button className="ghost-btn" onClick={p.onDuplicate} style={css("height:34px;padding:0 13px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer")}>Duplicate</button>
        <button className="ghost-btn" onClick={p.onReassign} style={css("height:34px;padding:0 13px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer")}>Reassign</button>
        {p.canEdit && !isCancelled(j) && (
          <>
            <button className="ghost-btn" onClick={p.onMove}
              style={css("height:34px;padding:0 12px;border:1px solid #F5E3C7;background:#FFFAEF;color:#B45309;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
              เลื่อนวัน
            </button>
            <button className="ghost-btn" onClick={p.onCancelJob}
              style={css("height:34px;padding:0 12px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
              ยกเลิกงาน
            </button>
          </>
        )}
        {p.canEdit && (
          <button
            onClick={p.onDelete}
            style={css("height:34px;padding:0 13px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
          >
            ลบงานนี้
          </button>
        )}
      </div>
    </aside>
  );
}

/* ------------------------------------------------ postpone / cancel modal */

/**
 * Moving a job's date, or calling it off.
 *
 * Both ask for a reason and neither proceeds without one. That is not ceremony:
 * a date that moved with nothing recorded against it is the thing nobody can
 * explain at the end of the month, and the two questions this whole feature
 * exists to answer — who keeps moving the dates, and what is actually being
 * cancelled — cannot be answered out of a blank box.
 *
 * Cancelling is a status change, not a delete. The job stays, its history stays,
 * and it stays countable; it simply stops appearing on the lists that mean
 * "still to do".
 */
export function JobChangeModal(p: {
  job: Job;
  mode: "move" | "cancel";
  onApply: (change: { date: string; moveBy: string; reason: string }) => void;
  onClose: () => void;
}) {
  const moving = p.mode === "move";
  const [date, setDate] = useState(p.job.date);
  const [moveBy, setMoveBy] = useState(MOVED_BY[0]);
  const [reason, setReason] = useState("");

  const dateOk = /^\d{2}\/\d{2}\/\d{4}$/.test(date.trim());
  const moved = dateOk && date.trim() !== p.job.date.trim();
  const ready = reason.trim().length >= 3 && (!moving || moved);

  const box = "height:32px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12.5px;padding:0 9px;outline:none;font-family:inherit;width:100%";
  const label = "font-size:11px;color:#64748B;margin-bottom:4px;display:block";
  const tone = moving ? "#B45309" : "#B42318";

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:65;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:460px;max-width:100%;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:" + tone + ";border-radius:6px 6px 0 0;display:flex;justify-content:space-between;align-items:center;gap:10px")}>
          <div style={css("min-width:0")}>
            <div style={css("font-size:14px;font-weight:600;color:#fff")}>
              {moving ? "เลื่อนวันส่งงาน" : "ยกเลิกงาน"}
            </div>
            <div style={css("font-size:11px;color:#FFE7CC;word-break:break-word")}>
              {p.job.customer} · {p.job.jobCode || p.job.abs || p.job.jobNo || p.job.key}
            </div>
          </div>
          <button onClick={p.onClose} aria-label="Close" style={css("width:28px;height:28px;flex:none;border:1px solid rgba(255,255,255,.35);background:rgba(255,255,255,.14);color:#fff;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        <div style={css("padding:16px 20px;display:flex;flex-direction:column;gap:12px")}>
          {moving && (
            <>
              <div>
                <span style={css(label)}>วันปัจจุบัน</span>
                <div style={css("font-size:13px;font-family:'IBM Plex Mono',monospace;color:#0A2240;font-weight:600")}>
                  {p.job.date || "—"}
                  {p.job.origDate && p.job.origDate !== p.job.date
                    ? "   (วันแรกที่วางแผน " + p.job.origDate + ")" : ""}
                </div>
              </div>
              <div>
                <span style={css(label)}>วันใหม่ · วว/ดด/ปปปป</span>
                <input value={date} onChange={(e) => setDate(e.target.value)} placeholder="24/07/2026"
                  style={css(box + ";font-family:'IBM Plex Mono',monospace")} />
                {date.trim().length > 0 && !dateOk && (
                  <span style={css("font-size:11px;color:#B42318")}>ต้องเป็นรูปแบบ วว/ดด/ปปปป</span>
                )}
              </div>
              <div>
                <span style={css(label)}>ใครขอเลื่อน</span>
                <select value={moveBy} onChange={(e) => setMoveBy(e.target.value)} style={css(box)}>
                  {MOVED_BY.map((who) => <option key={who} value={who}>{who}</option>)}
                </select>
              </div>
            </>
          )}

          <div>
            <span style={css(label)}>{moving ? "เหตุผลที่เลื่อน" : "เหตุผลที่ยกเลิก"} · จำเป็น</span>
            <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={3}
              placeholder={moving ? "เช่น ลูกค้าแจ้งเลื่อนโหลดสินค้า" : "เช่น ลูกค้ายกเลิกการจอง"}
              style={css("width:100%;border:1px solid #D8E0E8;border-radius:4px;padding:8px 9px;font-size:12.5px;outline:none;font-family:inherit;resize:vertical")} />
          </div>

          <span style={css("font-size:11px;color:#94A3B8;line-height:1.5")}>
            {moving
              ? "งานจะย้ายไปวันใหม่ และขึ้นในแท็บ CANCEL / MOVED พร้อมวันเดิม"
              : "งานจะเปลี่ยนสถานะเป็น CANCELLED และหายจาก PENDING · TODAY · TOMORROW แต่ไม่ถูกลบ"}
          </span>
        </div>

        <div style={css("padding:13px 20px;border-top:1px solid #E9EFF5;background:#FBFCFD;display:flex;justify-content:flex-end;gap:9px;border-radius:0 0 6px 6px")}>
          <button className="ghost-btn" onClick={p.onClose} style={css("height:34px;padding:0 16px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer")}>ปิด</button>
          <button
            onClick={() => p.onApply({ date: date.trim(), moveBy, reason: reason.trim() })}
            disabled={!ready}
            style={css("height:34px;padding:0 18px;border:1px solid " + (ready ? tone : "#D8E0E8") +
              ";background:" + (ready ? tone : "#EDF1F5") + ";color:" + (ready ? "#fff" : "#94A3B8") +
              ";border-radius:4px;font-size:12.5px;font-weight:600;cursor:" + (ready ? "pointer" : "not-allowed"))}
          >
            {moving ? "ยืนยันเลื่อนวัน" : "ยืนยันยกเลิก"}
          </button>
        </div>
      </div>
    </div>
  );
}

/* -------------------------------------------------------- reassign modal */

export function AssignModal(p: {
  reference: string;
  current: string;
  operators: string[];
  loads: Record<string, number>;
  onPick: (name: string) => void;
  onClose: () => void;
}) {
  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:64;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:460px;max-width:100%;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:#0A2240;border-radius:6px 6px 0 0;display:flex;justify-content:space-between;align-items:center")}>
          <div>
            <div style={css("font-size:14px;font-weight:600;color:#fff")}>Reassign Job</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>{p.reference}</div>
          </div>
          <button onClick={p.onClose} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>
        <div style={css("padding:16px 20px;display:flex;flex-direction:column;gap:8px")}>
          {p.operators.map((name) => (
            <button
              key={name}
              type="button"
              onClick={() => p.onPick(name)}
              style={css(
                "width:100%;font-family:inherit;text-align:left;" +
                "display:flex;align-items:center;gap:11px;padding:10px 12px;border:1px solid " +
                (p.current === name ? "#2E7DD1" : "#E9EFF5") + ";background:" + (p.current === name ? "#F4F8FC" : "#fff") +
                ";border-radius:4px;cursor:pointer",
              )}
            >
              <span style={css("width:28px;height:28px;border-radius:4px;background:#0A2240;color:#fff;font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center")}>
                {name.slice(0, 2).toUpperCase()}
              </span>
              <span style={css("display:flex;flex-direction:column;line-height:1.25")}>
                <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{name}</span>
                <span style={css("font-size:11px;color:#64748B")}>{p.loads[name] || 0} active jobs</span>
              </span>
            </button>
          ))}
          <span style={css("font-size:11px;color:#94A3B8;line-height:1.5;margin-top:4px")}>
            Assignment history is never overwritten — the previous operator, who changed it and when are appended to the
            job&apos;s change history.
          </span>
        </div>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------- add job modal */

/** [label, form key, select options (null = free text), placeholder] */
type FieldDef = [string, string, string[] | null, string?];

export function addJobFields(cat: string, m: Masters): FieldDef[] {
  const cust = [""].concat(m.customers);
  const truck = [""].concat(m.truckers);
  const cy = [""].concat(m.cyYards);
  const ops = m.operators;

  if (cat === "EXPORT") {
    return [
      ["PLAN DATE *", "date", null, "DD/MM/YYYY"], ["CUSTOMER *", "customer", cust], ["TRUCKING COMPANY *", "trucker", truck],
      ["BOOKING NO.", "booking", null, "Carrier booking"], ["ABS NO.", "abs", null, "e.g. 260800123"],
      ["FCL / LCL", "fclLcl", ["", "FCL", "LCL"]], ["PLANT LOADING", "plant", null, "Loading plant"],
      ["PLAN LOADING TIME", "planTime", null, "08:00"], ["TYPE", "type", null, "1X40"], ["CY YARD", "cyYard", cy],
      ["RETURN", "returnLoc", null, "Return terminal"], ["CLOSING DATE", "closingDate", null, "DD/MM/YYYY"],
      ["CLOSING TIME", "closingTime", null, "16:00"], ["CONTAINER NO.", "container", null, "TCLU1234567"],
      ["SEAL NO.", "seal", null, "Seal"], ["LICENCE", "licence", null, "70-1234"], ["DRIVER", "driver", null, "Driver name"],
      ["DRIVER CONTACT", "contact", null, "081-234-5678"], ["ASSIGNED OPERATOR", "op", ops],
    ];
  }
  if (cat === "DELIVERY") {
    return [
      ["PICKUP DATE *", "date", null, "DD/MM/YYYY"], ["CUSTOMER *", "customer", null, "Consignee name"],
      ["WAREHOUSE *", "wh", [""].concat(m.warehouses)], ["JOB NO.", "jobNo", null, "LSTH_…"],
      ["SID NO.", "sid", null, "SID"], ["PROVINCE", "province", [""].concat(m.provinces)],
      ["ZIP", "zip", null, "10250"], ["PALLET", "pallet", null, "0"], ["WEIGHT KG", "kgs", null, "0"],
      ["4-WHEEL", "v4", null, "0"], ["6-WHEEL", "v6", null, "0"], ["10-WHEEL", "v10", null, "0"],
      ["TRAILER", "vtr", null, "0"], ["TRANSPORT COST", "cost", null, "THB"], ["REMARK", "remark", null, "Note"],
      ["ASSIGNED OPERATOR", "op", ops],
    ];
  }
  return [
    ["PLAN DATE *", "date", null, "DD/MM/YYYY"], ["CUSTOMER *", "customer", cust], ["TRUCKING COMPANY *", "trucker", truck],
    ["JOB CODE", "jobCode", null, "e.g. 260800123"], ["PRODUCT / DG", "product", null, "e.g. DG CLASS 3"],
    ["DESTINATION", "destination", null, "Plant / warehouse"], ["PLAN LOADING TIME", "planTime", null, "08:00"],
    ["TYPE", "type", null, "1X40"], ["CY YARD", "cyYard", cy], ["TOTAL WEIGHT", "weight", null, "kg"],
    ["CONTAINER NO.", "container", null, "MRSU4470590"], ["EMPTY RETURN", "emptyReturn", null, "Return depot"],
    ["LICENCE", "licence", null, "70-1234"], ["DRIVER", "driver", null, "Driver name"],
    ["DRIVER CONTACT", "contact", null, "081-234-5678"], ["ASSIGNED OPERATOR", "op", ops],
  ];
}

const CHOICES: [string, string, string][] = [
  ["IMPORT", "นำเข้า", "Port release, container pickup, delivery, empty return"],
  ["EXPORT", "ส่งออก", "Empty pickup, plant loading, port return, gate-in before closing"],
  ["DELIVERY", "งานกระจายสินค้า", "Warehouse distribution by province with vehicle mix and cost"],
];

export function AddJobModal(p: {
  cat: string;
  masters: Masters;
  form: Record<string, string>;
  aiFields: string[];
  aiBusy: boolean;
  aiMessage: string;
  dragOver: boolean;
  onChoose: (cat: string) => void;
  onField: (key: string, value: string) => void;
  onAiInput: (e: ChangeEvent<HTMLInputElement>) => void;
  onAiDrop: (e: DragEvent<HTMLLabelElement>) => void;
  onDragOver: (e: DragEvent<HTMLLabelElement>) => void;
  onDragLeave: (e: DragEvent<HTMLLabelElement>) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const choosing = p.cat === "CHOOSE";
  const required = p.cat === "DELIVERY" ? ["date", "customer", "wh"] : ["date", "customer", "trucker"];
  const ready = required.every((k) => !!p.form[k]);
  const fields = choosing ? [] : addJobFields(p.cat, p.masters);

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:66;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:900px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:16px 22px;background:#0A2240;display:flex;justify-content:space-between;align-items:center;border-radius:6px 6px 0 0;position:sticky;top:0")}>
          <div>
            <div style={css("font-size:14.5px;font-weight:600;color:#fff")}>
              {choosing ? "Add Job — choose category" : "New " + p.cat + " Job"}
            </div>
            <div style={css("font-size:11px;color:#7FA5CC")}>
              {choosing ? "เพิ่มงานใหม่ · เลือกประเภทงาน" : "คีย์ข้อมูล Booking และรายละเอียดงาน"}
            </div>
          </div>
          <button onClick={p.onClose} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        {choosing ? (
          <div style={css("padding:20px 22px;display:grid;grid-template-columns:repeat(3,1fr);gap:12px")}>
            {CHOICES.map((c) => (
              <button
                key={c[0]}
                type="button"
                className="card-hover"
                onClick={() => p.onChoose(c[0])}
                style={css("font-family:inherit;align-items:flex-start;display:flex;flex-direction:column;gap:4px;padding:14px 16px;border:1px solid #E9EFF5;background:#fff;border-radius:5px;cursor:pointer;text-align:left")}
              >
                <span style={css(badge(c[0], c[0] === "IMPORT" ? "dark" : c[0] === "EXPORT" ? "blue" : "teal"))}>{c[0]}</span>
                <span style={css("font-size:12px;color:#0A2240;font-weight:600;margin-top:4px")}>{c[1]}</span>
                <span style={css("font-size:11.5px;color:#64748B;line-height:1.45;text-wrap:pretty")}>{c[2]}</span>
              </button>
            ))}
          </div>
        ) : (
          <>
            <div style={css("padding:18px 22px 0")}>
              <label
                onDrop={p.onAiDrop}
                onDragOver={p.onDragOver}
                onDragLeave={p.onDragLeave}
                style={css(
                  "border:2px dashed " + (p.aiBusy || p.dragOver ? "#2E7DD1" : "#BBD5EE") +
                  ";background:" + (p.aiBusy ? "#F4F8FC" : "#FBFDFF") +
                  ";border-radius:5px;padding:16px;display:flex;align-items:center;gap:14px;cursor:pointer",
                )}
              >
                <span style={css("width:38px;height:38px;flex:none;border-radius:5px;background:#0A2240;color:#fff;display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700;letter-spacing:.03em")}>AI</span>
                <span style={css("display:flex;flex-direction:column;gap:3px;min-width:0;flex:1")}>
                  <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>Read a document and fill this form automatically</span>
                  <span style={css("font-size:11px;color:#64748B;line-height:1.45")}>
                    Drop a booking confirmation, D/O, B/L, container list or a photo of the paperwork (PDF · JPG · PNG).
                    อ่านไฟล์ PDF และรูปภาพ แล้วกรอกฟอร์มให้อัตโนมัติ
                  </span>
                  {!!p.aiMessage && (
                    <span style={css("font-size:11.5px;line-height:1.45;color:" + (/Could not|not configured/.test(p.aiMessage) ? "#B42318" : "#16794C"))}>
                      {p.aiMessage}
                    </span>
                  )}
                </span>
                <span style={css("font-size:11.5px;color:#2E7DD1;font-weight:600;flex:none")}>{p.aiBusy ? "Reading…" : "Browse"}</span>
                <input type="file" multiple accept=".pdf,image/*" onChange={p.onAiInput} style={{ display: "none" }} />
              </label>
            </div>

            <div style={css("padding:18px 22px;display:grid;grid-template-columns:repeat(4,1fr);gap:13px")}>
              {fields.map((f) => {
                const isAi = p.aiFields.indexOf(f[1]) >= 0;
                const missingRequired = !p.form[f[1]] && ["customer", "trucker", "date"].indexOf(f[1]) >= 0;
                const style =
                  "height:34px;border:1px solid " + (isAi ? "#7FC4D6" : missingRequired ? "#E8B4AE" : "#D8E0E8") +
                  ";border-radius:4px;background:" + (isAi ? "#F2FBFD" : "#F8FAFC") +
                  ";font-size:12.5px;padding:0 9px;outline:none;width:100%";
                return (
                  <label key={f[0]} style={css("display:flex;flex-direction:column;gap:4px")}>
                    <span style={css("display:flex;align-items:center;gap:6px")}>
                      <span style={css("font-size:10px;font-weight:600;color:#475569;letter-spacing:.05em")}>{f[0]}</span>
                      {isAi && <span style={css("font-size:8.5px;font-weight:700;color:#0A6E8A;background:#E2F2F7;border-radius:2px;padding:1px 4px;letter-spacing:.05em")}>AI</span>}
                    </span>
                    {f[2] ? (
                      <select value={p.form[f[1]] || ""} onChange={(e) => p.onField(f[1], e.target.value)} style={css(style)}>
                        {f[2].map((o) => <option key={o} value={o}>{o}</option>)}
                      </select>
                    ) : (
                      <input value={p.form[f[1]] || ""} onChange={(e) => p.onField(f[1], e.target.value)} placeholder={f[3]} style={css(style)} />
                    )}
                  </label>
                );
              })}
            </div>

            <div style={css("padding:14px 22px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:12px;background:#FBFCFD;border-radius:0 0 6px 6px")}>
              <span style={css("font-size:11.5px;color:#64748B")}>
                {ready ? "Ready to save — the job appears immediately in Team Work." : "Required: " + required.join(", ")}
              </span>
              <div style={css("display:flex;gap:9px")}>
                <button className="ghost-btn" onClick={p.onClose} style={css("height:36px;padding:0 18px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:13px;color:#475569;cursor:pointer")}>Cancel</button>
                <button
                  onClick={p.onSave}
                  style={css(
                    "height:36px;padding:0 22px;border:1px solid " + (ready ? "#0A2240" : "#C7D6E4") +
                    ";background:" + (ready ? "#0A2240" : "#E6EBF1") + ";color:" + (ready ? "#fff" : "#94A3B8") +
                    ";border-radius:4px;font-size:13px;font-weight:600;cursor:" + (ready ? "pointer" : "not-allowed"),
                  )}
                >
                  Save Job
                </button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
