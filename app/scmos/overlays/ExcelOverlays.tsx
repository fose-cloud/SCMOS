"use client";

import type { ChangeEvent, DragEvent } from "react";
import { badge, css } from "../theme";
import type { DupDecision, DupMatch, ImportPreview } from "../excel";
import { describeView, type SavedView } from "../views";

/* ------------------------------------------------------------ import */

const DECISIONS: [DupDecision, string, string][] = [
  ["skip", "ข้ามงานนี้", "เก็บของเดิมไว้ ไม่เอาแถวนี้เข้า"],
  ["overwrite", "ทับของเดิม", "อัปเดตงานเดิมด้วยค่าจากไฟล์"],
  ["new", "เพิ่มเป็นงานใหม่", "เป็นอีกเที่ยวหนึ่ง คนละงานกัน"],
];

/** Walks forward from `from`, wrapping, to the next row still awaiting a choice. */
function nextPending(
  dups: DupMatch[],
  decisions: Record<string, DupDecision>,
  from: number,
  justDecided: string,
): number {
  for (let step = 1; step <= dups.length; step++) {
    const i = (from + step) % dups.length;
    if (dups[i].key !== justDecided && !decisions[dups[i].key]) return i;
  }
  return -1;
}

/**
 * Duplicates are resolved one row at a time rather than by a blanket rule: the
 * same job code arriving twice can mean an updated status, a second truck on the
 * booking, or simply the same file imported again, and only the operator looking
 * at the two versions can tell which.
 */
function DuplicatePanel(p: {
  dups: DupMatch[];
  decisions: Record<string, DupDecision>;
  canOverwrite: (dup: DupMatch) => boolean;
  cursor: number;
  onCursor: (index: number) => void;
  onDecide: (keys: string[], decision: DupDecision) => void;
}) {
  const index = Math.min(Math.max(p.cursor, 0), p.dups.length - 1);
  const current = p.dups[index];
  const decided = p.dups.filter((d) => p.decisions[d.key]).length;
  const identical = p.dups.filter((d) => !d.diffs.length).length;
  const rest = p.dups.filter((d) => d.key !== current.key && !p.decisions[d.key]);
  const chosen = p.decisions[current.key];
  const j = current.incoming;
  // Import follows the grid's rule: an Operation User cannot write to a job
  // owned by someone else, so that row can only be skipped or added as new.
  const mayOverwrite = p.canOverwrite(current);

  function decide(decision: DupDecision) {
    p.onDecide([current.key], decision);
    const next = nextPending(p.dups, p.decisions, index, current.key);
    if (next >= 0) p.onCursor(next);
  }

  return (
    <div style={css("border:1px solid #F5D9A8;background:#FFFBF2;border-radius:5px;padding:12px 13px")}>
      <div style={css("display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:10px")}>
        <span style={css("font-size:12px;font-weight:600;color:#B45309")}>
          พบงานซ้ำกับที่มีอยู่ {p.dups.length} รายการ
        </span>
        <span style={css("font-size:11px;color:#64748B")}>
          เลือกแล้ว {decided}/{p.dups.length}
          {identical ? ` · ซ้ำแบบไม่มีอะไรต่าง ${identical} รายการ ตั้งเป็นข้ามให้แล้ว` : ""}
        </span>
      </div>

      <div style={css("border:1px solid #EADFC8;background:#fff;border-radius:4px")}>
        <div style={css("display:flex;align-items:center;gap:8px;padding:8px 11px;border-bottom:1px solid #F1F5F9")}>
          <button
            onClick={() => p.onCursor((index - 1 + p.dups.length) % p.dups.length)}
            aria-label="รายการก่อนหน้า"
            disabled={p.dups.length < 2}
            style={css("width:26px;height:26px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;color:#475569;cursor:pointer;flex:none")}
          >‹</button>
          <span style={css("font-size:11.5px;font-weight:600;color:#0A2240;font-family:'IBM Plex Mono',monospace")}>
            {index + 1} / {p.dups.length}
          </span>
          <button
            onClick={() => p.onCursor((index + 1) % p.dups.length)}
            aria-label="รายการถัดไป"
            disabled={p.dups.length < 2}
            style={css("width:26px;height:26px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;color:#475569;cursor:pointer;flex:none")}
          >›</button>
          <span style={css("flex:1;min-width:0;font-size:11.5px;color:#475569;word-break:break-word")}>
            {[j.cat, j.date, j.customer, j.jobCode || j.abs || j.jobNo, j.container].filter(Boolean).join(" · ")}
          </span>
          {!!chosen && (
            <span style={css(badge(DECISIONS.find((d) => d[0] === chosen)?.[1] ?? chosen, chosen === "skip" ? "gray" : chosen === "overwrite" ? "amber" : "blue"))}>
              {DECISIONS.find((d) => d[0] === chosen)?.[1]}
            </span>
          )}
        </div>

        {current.diffs.length ? (
          <div style={css("max-height:150px;overflow:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["ฟิลด์", "ของเดิม", "ในไฟล์"].map((h) => (
                    <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:6px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {current.diffs.map((d) => (
                  <tr key={d.field}>
                    <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap;color:#475569")}>{d.label}</td>
                    <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;color:#94A3B8;text-decoration:line-through")}>{d.from || "—"}</td>
                    <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;color:#16794C;font-weight:600")}>{d.to}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div style={css("padding:9px 11px;font-size:11.5px;color:#64748B")}>
            ไฟล์ไม่มีค่าไหนต่างจากงานเดิม — ข้ามได้เลย
          </div>
        )}

        <div style={css("padding:10px 11px;border-top:1px solid #F1F5F9;display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
          {DECISIONS.map(([value, label, hint]) => {
            const blocked = value === "overwrite" && !mayOverwrite;
            return (
              <button
                key={value}
                onClick={() => decide(value)}
                disabled={blocked}
                title={blocked ? "งานนี้เป็นของ " + current.existing.op : hint}
                style={css(
                  "height:32px;padding:0 13px;border:1px solid " + (chosen === value ? "#0A2240" : blocked ? "#E6EBF1" : "#D8E0E8") +
                  ";background:" + (chosen === value ? "#0A2240" : blocked ? "#F4F6F9" : "#fff") +
                  ";color:" + (chosen === value ? "#fff" : blocked ? "#A8B4C2" : "#475569") +
                  ";border-radius:4px;font-size:12px;font-weight:600;cursor:" + (blocked ? "not-allowed" : "pointer"),
                )}
              >
                {label}{value === "overwrite" && current.diffs.length ? ` (${current.diffs.length} ฟิลด์)` : ""}
              </button>
            );
          })}
          {!mayOverwrite && (
            <span style={css("font-size:11px;color:#64748B")}>
              งานเดิมเป็นของ {current.existing.op} — ทับไม่ได้ เลือกข้ามหรือเพิ่มเป็นงานใหม่
            </span>
          )}
        </div>

        {!!rest.length && (
          <div style={css("padding:9px 11px;border-top:1px solid #F1F5F9;display:flex;gap:7px;align-items:center;flex-wrap:wrap;background:#FBFCFD;border-radius:0 0 4px 4px")}>
            <span style={css("font-size:11px;color:#64748B")}>ใช้กับที่เหลืออีก {rest.length} รายการ:</span>
            {DECISIONS.map(([value, label]) => {
              // Bulk overwrite covers only the rows this user is allowed to write to.
              const targets = value === "overwrite" ? rest.filter(p.canOverwrite) : rest;
              return (
                <button
                  key={value}
                  className="ghost-btn"
                  onClick={() => p.onDecide(targets.map((d) => d.key), value)}
                  disabled={!targets.length}
                  title={value === "overwrite" && targets.length < rest.length ? `ทับได้ ${targets.length} รายการที่เป็นงานของคุณ` : undefined}
                  style={css(
                    "height:26px;padding:0 10px;border:1px solid " + (targets.length ? "#D8E0E8" : "#E6EBF1") +
                    ";background:#fff;border-radius:4px;font-size:11px;color:" + (targets.length ? "#475569" : "#A8B4C2") +
                    ";cursor:" + (targets.length ? "pointer" : "not-allowed"),
                  )}
                >
                  {label}{value === "overwrite" && targets.length < rest.length ? ` (${targets.length})` : ""}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

export function ImportModal(p: {
  preview: ImportPreview | null;
  busy: boolean;
  saving: boolean;
  registerReady: boolean;
  error: string;
  dragOver: boolean;
  decisions: Record<string, DupDecision>;
  canOverwrite: (dup: DupMatch) => boolean;
  dupCursor: number;
  onDupCursor: (index: number) => void;
  onDecide: (keys: string[], decision: DupDecision) => void;
  onFile: (e: ChangeEvent<HTMLInputElement>) => void;
  onDrop: (e: DragEvent<HTMLLabelElement>) => void;
  onDragOver: (e: DragEvent<HTMLLabelElement>) => void;
  onDragLeave: (e: DragEvent<HTMLLabelElement>) => void;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const errors = p.preview ? p.preview.issues.filter((i) => i.severity === "error").length : 0;
  const dups = p.preview?.dups ?? [];
  const pending = dups.filter((d) => !p.decisions[d.key]).length;
  const counted = (decision: DupDecision) => dups.filter((d) => p.decisions[d.key] === decision).length;
  const overwriting = counted("overwrite");
  const skipping = counted("skip");
  const adding = (p.preview?.jobs.length ?? 0) - dups.length + counted("new");
  const ready = !!p.preview && p.preview.rows > 0 && pending === 0 && p.registerReady && !p.busy && !p.saving;
  const statusMessage = p.saving
    ? "กำลังบันทึกลงฐานข้อมูล… กรุณาอย่าปิดหน้าต่างนี้"
    : !p.registerReady
      ? "กำลังโหลดทะเบียนงาน… เมื่อพร้อมแล้วจึงจะนำเข้าได้"
      : pending
        ? `ยังมีงานซ้ำอีก ${pending} รายการที่ต้องเลือกก่อนนำเข้า`
        : errors
          ? "งานที่รูปแบบผิดจะถูกนำเข้าพร้อมธงเตือน แก้ในตารางได้ทีหลัง"
          : dups.length
            ? `ทับของเดิม ${overwriting} · ข้าม ${skipping} · เพิ่มใหม่ ${adding}`
            : "ข้อมูลจะถูกเพิ่มเข้าไปในงานที่มีอยู่ ไม่ทับของเดิม";
  const confirmLabel = p.saving
    ? "กำลังบันทึกลงฐานข้อมูล…"
    : dups.length
      ? `นำเข้า ${adding} งาน` + (overwriting ? ` · ทับ ${overwriting}` : "")
      : `นำเข้า ${p.preview?.rows ?? 0} งาน`;

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:66;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:760px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:16px 22px;background:#0A2240;display:flex;justify-content:space-between;align-items:center;border-radius:6px 6px 0 0")}>
          <div>
            <div style={css("font-size:14.5px;font-weight:600;color:#fff")}>Import from Excel</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>นำเข้าแผนงานจากไฟล์ Excel · ตรวจรูปแบบให้อัตโนมัติ</div>
          </div>
          <button onClick={p.onClose} disabled={p.saving} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        <div style={css("padding:18px 22px;display:flex;flex-direction:column;gap:14px")}>
          <label
            onDrop={p.onDrop}
            onDragOver={p.onDragOver}
            onDragLeave={p.onDragLeave}
            style={css(
              "border:2px dashed " + (p.dragOver ? "#2E7DD1" : "#C7D6E4") + ";background:" + (p.dragOver ? "#F4F8FC" : "#FBFCFD") +
              ";border-radius:5px;padding:20px 16px;display:flex;flex-direction:column;align-items:center;gap:6px;text-align:center;cursor:pointer",
            )}
          >
            <span style={css("font-size:20px;color:#2E7DD1")}>⬆</span>
            <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
              {p.saving ? "กำลังบันทึกลงฐานข้อมูล…" : p.busy ? "กำลังอ่านไฟล์…" : "วางไฟล์ .xlsx ที่นี่ หรือคลิกเพื่อเลือก"}
            </span>
            <span style={css("font-size:11px;color:#64748B;line-height:1.45")}>
              อ่านทุกชีตในไฟล์ · รู้จัก IMPORT / EXPORT / DELIVERY ทั้งชื่อย่อ ภาษาไทย และรูปแบบคอลัมน์
            </span>
            <input type="file" accept=".xlsx,.xls" disabled={p.busy || p.saving} onChange={p.onFile} style={{ display: "none" }} />
          </label>

          {!!p.error && (
            <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:11px 13px;font-size:12px;color:#B42318")}>
              {p.error}
            </div>
          )}

          {p.preview && (
            <>
              <div style={css("display:grid;grid-template-columns:repeat(5,1fr);gap:10px")}>
                {([
                  ["แถวที่อ่านได้", String(p.preview.rows), "#0A2240"],
                  ["จัดรูปแบบให้", String(p.preview.fixes.length), "#16794C"],
                  ["ต้องแก้เอง", String(errors), errors ? "#B42318" : "#16794C"],
                  ["ซ้ำกับของเดิม", String(dups.length), dups.length ? "#B45309" : "#16794C"],
                  ["ชีต", String(p.preview.sheets.length), "#0A2240"],
                ] as [string, string, string][]).map(([label, value, colour]) => (
                  <div key={label} style={css("border:1px solid #E9EFF5;border-radius:5px;padding:10px 12px")}>
                    <div style={css("font-size:10.5px;color:#64748B")}>{label}</div>
                    <div style={css("font-size:22px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:" + colour)}>{value}</div>
                  </div>
                ))}
              </div>

              <div style={css("font-size:11.5px;color:#475569;line-height:1.6")}>
                <b>ไฟล์:</b> {p.preview.fileName}<br />
                <b>ประเภทงาน:</b> Import {p.preview.categoryCounts.IMPORT} · Export {p.preview.categoryCounts.EXPORT} · Delivery {p.preview.categoryCounts.DELIVERY}<br />
                <b>คอลัมน์ที่จับคู่ได้ {p.preview.mappedHeaders.length}:</b> {p.preview.mappedHeaders.slice(0, 14).join(", ") || "—"}
              </div>

              {p.preview.categoryCounts.EXPORT === 0 && p.preview.jobs.some((job) => !!job.abs || !!job.booking || !!job.closingDate) && (
                <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:10px 12px;font-size:11.5px;color:#B42318")}>
                  พบคอลัมน์ที่ใช้กับงานส่งออก แต่ยังจัดเป็น Export ไม่ได้ — กรุณาตรวจคอลัมน์ Category หรือชื่อชีตก่อนนำเข้า
                </div>
              )}

              {!!p.preview.unmappedHeaders.length && (
                <div style={css("border:1px solid #F5E3C7;background:#FFFAEF;border-radius:5px;padding:10px 12px")}>
                  <div style={css("font-size:11.5px;font-weight:600;color:#B45309;margin-bottom:3px")}>
                    คอลัมน์ที่ไม่รู้จัก {p.preview.unmappedHeaders.length} — จะไม่ถูกนำเข้า
                  </div>
                  <div style={css("font-size:11px;color:#64748B;word-break:break-word")}>
                    {p.preview.unmappedHeaders.slice(0, 20).join(" · ")}
                  </div>
                </div>
              )}

              {!!dups.length && (
                <DuplicatePanel
                  dups={dups}
                  decisions={p.decisions}
                  canOverwrite={p.canOverwrite}
                  cursor={p.dupCursor}
                  onCursor={p.onDupCursor}
                  onDecide={p.onDecide}
                />
              )}

              {p.preview.rows > 0 && (
                <div style={css("border:1px solid #E9EFF5;border-radius:5px;overflow:auto;max-height:220px")}>
                  <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
                    <thead>
                      <tr>
                        {["Cat", "Date", "Customer", "Trucker", "Job / ABS", "Status", "ปัญหา"].map((h) => (
                          <th key={h} style={css("position:sticky;top:0;background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {p.preview.jobs.slice(0, 8).map((j) => (
                        <tr key={j.key}>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap")}>{j.cat}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap;font-family:'IBM Plex Mono',monospace")}>{j.date}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9")}>{j.customer}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9")}>{j.trucker}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;font-family:'IBM Plex Mono',monospace")}>{j.jobCode || j.abs}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap")}>{j.status}</td>
                          <td style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;color:" + (j.issues.length ? "#B42318" : "#94A3B8"))}>
                            {j.issues.length ? j.issues.map((i) => i.label).join(", ") : "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {p.preview.rows === 0 && (
                <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:11px 13px;font-size:12px;color:#B42318")}>
                  อ่านไม่พบแถวงานในไฟล์นี้ — ตรวจว่าแถวหัวตารางมีคอลัมน์อย่าง CUSTOMER, TRUCK, JOB CODE อยู่จริง
                </div>
              )}
            </>
          )}
        </div>

        <div style={css("padding:14px 22px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:12px;background:#FBFCFD;border-radius:0 0 6px 6px")}>
          <span style={css("font-size:11.5px;color:" + (pending && !p.saving ? "#B45309" : "#64748B"))}>
            {statusMessage}
          </span>
          <div style={css("display:flex;gap:9px")}>
            <button className="ghost-btn" onClick={p.onClose} disabled={p.saving} style={css("height:36px;padding:0 18px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:13px;color:#475569;cursor:pointer")}>ยกเลิก</button>
            <button
              onClick={p.onConfirm}
              disabled={!ready}
              style={css(
                "height:36px;padding:0 22px;border:1px solid " + (ready ? "#0A2240" : "#C7D6E4") +
                ";background:" + (ready ? "#0A2240" : "#E6EBF1") + ";color:" + (ready ? "#fff" : "#94A3B8") +
                ";border-radius:4px;font-size:13px;font-weight:600;cursor:" + (ready ? "pointer" : "not-allowed"),
              )}
            >
              {confirmLabel}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

/* -------------------------------------------------------- saved views */

export function SavedViewsModal(p: {
  views: SavedView[];
  current: string;
  name: string;
  onName: (value: string) => void;
  onSave: () => void;
  onApply: (view: SavedView) => void;
  onDelete: (name: string) => void;
  onClose: () => void;
}) {
  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:66;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:520px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:#0A2240;display:flex;justify-content:space-between;align-items:center;border-radius:6px 6px 0 0")}>
          <div>
            <div style={css("font-size:14px;font-weight:600;color:#fff")}>Saved views</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>บันทึกมุมมองที่ใช้ประจำ · เก็บไว้ในเครื่องนี้</div>
          </div>
          <button onClick={p.onClose} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        <div style={css("padding:16px 20px;display:flex;flex-direction:column;gap:14px")}>
          <div style={css("border:1px solid #E9EFF5;border-radius:5px;padding:12px 13px;background:#F8FAFC")}>
            <div style={css("font-size:10.5px;color:#64748B;letter-spacing:.05em;margin-bottom:4px")}>มุมมองปัจจุบัน</div>
            <div style={css("font-size:12px;color:#0A2240;font-weight:600;margin-bottom:10px;word-break:break-word")}>{p.current}</div>
            <div style={css("display:flex;gap:8px")}>
              <input
                value={p.name}
                onChange={(e) => p.onName(e.target.value)}
                placeholder="ตั้งชื่อมุมมอง เช่น งานนำเข้าของฉัน"
                style={css("flex:1;height:34px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12.5px;padding:0 10px;outline:none;font-family:inherit")}
              />
              <button
                className="dark-btn"
                onClick={p.onSave}
                style={css("height:34px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
              >
                บันทึก
              </button>
            </div>
          </div>

          {p.views.length ? p.views.map((v) => (
            <div key={v.name} style={css("display:flex;align-items:center;gap:10px;padding:10px 12px;border:1px solid #E9EFF5;border-radius:4px")}>
              <div style={css("flex:1;min-width:0")}>
                <div style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{v.name}</div>
                <div style={css("font-size:11px;color:#64748B;word-break:break-word")}>{describeView(v.state)}</div>
              </div>
              <button className="ghost-btn" onClick={() => p.onApply(v)} style={css("height:29px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer;flex:none")}>ใช้</button>
              <button onClick={() => p.onDelete(v.name)} aria-label={"ลบ " + v.name} style={css("width:29px;height:29px;border:1px solid #E9EFF5;background:#fff;border-radius:4px;color:#94A3B8;cursor:pointer;flex:none")}>✕</button>
            </div>
          )) : (
            <div style={css("font-size:12px;color:#94A3B8;text-align:center;padding:10px 0")}>
              ยังไม่มีมุมมองที่บันทึกไว้ <span style={css(badge("ว่าง", "gray"))}>ว่าง</span>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
