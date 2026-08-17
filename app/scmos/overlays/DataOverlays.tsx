"use client";

import { badge, css } from "../theme";
import type { CleanupReport, DupGroup } from "../cleanup";

/* ------------------------------------------------------- cleanup report */

const KIND_LABEL: Record<string, string> = {
  "date-unambiguous": "วันที่ที่อ่านได้แน่นอน (24/7/26 · 7/14/26 · 2026-07-24)",
  "date-dayfirst": "วันที่กำกวม ตีความเป็น วัน/เดือน ตามที่ยืนยัน",
  "plantime-moved": "ข้อความ “รับตู้ …” ย้ายออกจากช่องเวลานัดโหลด",
  "status-mapped": "สถานะภาษาไทย แปลงเข้าลำดับงาน",
  "note-moved": "ข้อความที่เขียนในช่องวันที่/เวลา ย้ายไปหมายเหตุ",
  "standard-fix": "จัดรูปแบบอื่นตามมาตรฐาน (เวลา เบอร์โทร น้ำหนัก เลขตู้)",
};

export function CleanupReportModal(p: { report: CleanupReport; saving: boolean; onClose: () => void }) {
  const kinds = Object.entries(p.report.byKind).sort((a, b) => b[1] - a[1]);
  const stillBad: Record<string, number> = {};
  p.report.remaining.forEach((r) => { stillBad[r.field] = (stillBad[r.field] || 0) + 1; });

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:68;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:680px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:#0A2240")}>
          <div style={css("font-size:14px;font-weight:600;color:#fff")}>ล้างข้อมูลย้อนหลัง · Cleanup report</div>
          <div style={css("font-size:11px;color:#7FA5CC")}>
            ตรวจ {p.report.scanned} งาน · แก้ไข {p.report.changedJobs} งาน · {p.report.changes.length} ค่า
          </div>
        </div>

        <div style={css("padding:18px 20px;display:flex;flex-direction:column;gap:16px")}>
          <div>
            <div style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em;margin-bottom:9px")}>สิ่งที่แก้ให้</div>
            {kinds.length ? kinds.map(([kind, n]) => (
              <div key={kind} style={css("display:flex;align-items:center;gap:10px;padding:8px 0;border-bottom:1px solid #F1F5F9")}>
                <span style={css("flex:1;font-size:12px;color:#334155")}>{KIND_LABEL[kind] ?? kind}</span>
                <span style={css("font-size:14px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#16794C")}>{n}</span>
              </div>
            )) : (
              <div style={css("font-size:12px;color:#94A3B8")}>ไม่มีอะไรต้องแก้ — ข้อมูลอยู่ในรูปแบบมาตรฐานอยู่แล้ว</div>
            )}
          </div>

          <div>
            <div style={css("font-size:11px;font-weight:700;color:#B45309;letter-spacing:.06em;margin-bottom:9px")}>
              ยังต้องให้คนตัดสิน {p.report.remaining.length} ค่า
            </div>
            {Object.keys(stillBad).length ? (
              <>
                <div style={css("display:flex;gap:8px;flex-wrap:wrap;margin-bottom:9px")}>
                  {Object.entries(stillBad).map(([field, n]) => (
                    <span key={field} style={css(badge(field, "amber"))}>{field} {n}</span>
                  ))}
                </div>
                <div style={css("border:1px solid #E9EFF5;border-radius:4px;max-height:170px;overflow:auto")}>
                  {p.report.remaining.slice(0, 40).map((r, i) => (
                    <div key={r.job + r.field + i} style={css("display:flex;gap:10px;padding:6px 10px;font-size:11.5px;" + (i ? "border-top:1px solid #F1F5F9;" : ""))}>
                      <span style={css("width:110px;flex:none;color:#64748B;font-family:'IBM Plex Mono',monospace")}>{r.job}</span>
                      <span style={css("width:90px;flex:none;color:#94A3B8")}>{r.field}</span>
                      <span style={css("flex:1;color:#B42318;word-break:break-word")}>{r.value}</span>
                    </div>
                  ))}
                </div>
                {p.report.remaining.length > 40 && (
                  <div style={css("font-size:11px;color:#94A3B8;margin-top:6px")}>
                    แสดง 40 จาก {p.report.remaining.length} · ที่เหลือดูได้จาก KPI “รูปแบบข้อมูลผิด” ในหน้า Workspace
                  </div>
                )}
              </>
            ) : (
              <div style={css("font-size:12px;color:#16794C")}>ไม่เหลือค่าที่ต้องตัดสินแล้ว ✓</div>
            )}
          </div>
        </div>

        <div style={css("padding:13px 20px;border-top:1px solid #E9EFF5;background:#FBFCFD;display:flex;justify-content:space-between;align-items:center;gap:12px")}>
          <span style={css("font-size:11.5px;color:#64748B")}>
            {p.saving ? "กำลังบันทึกลงฐานข้อมูล…" : "บันทึกลงฐานข้อมูลแล้ว · ทุกการแก้ถูกบันทึกในประวัติของแต่ละงาน"}
          </span>
          <button onClick={p.onClose} style={css("height:34px;padding:0 20px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
            ปิด
          </button>
        </div>
      </div>
    </div>
  );
}

/* ----------------------------------------------------------- duplicates */

export function DuplicatesModal(p: {
  groups: DupGroup[];
  busy: boolean;
  onMerge: (group: DupGroup) => void;
  onMergeAll: () => void;
  onOpenJob: (key: string) => void;
  onClose: () => void;
}) {
  const extra = p.groups.reduce((sum, g) => sum + g.jobs.length - 1, 0);
  const conflicting = p.groups.filter((g) => g.statuses.length > 1 || g.owners.length > 1).length;
  const reUploaded = p.groups.filter((g) => g.reUploaded);
  const sameLoad = p.groups.length - reUploaded.length;

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:68;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:820px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:#0A2240;display:flex;justify-content:space-between;align-items:center;gap:12px")}>
          <div>
            <div style={css("font-size:14px;font-weight:600;color:#fff")}>งานซ้ำในแผน · Duplicate jobs</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>
              {p.groups.length} กลุ่ม · แถวเกิน {extra} · <b style={css("color:#FFC978")}>อัปซ้ำแน่นอน {reUploaded.length} กลุ่ม</b>
              {sameLoad ? " · มาจากไฟล์เดียวกัน " + sameLoad + " กลุ่ม (อาจเป็นหลายเที่ยวในงานเดียว)" : ""}
              {conflicting ? " · ข้อมูลขัดกัน " + conflicting : ""}
            </div>
          </div>
          <button onClick={p.onClose} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        {!!p.groups.length && (
          <div style={css("padding:11px 20px;background:#FFF7DE;border-bottom:1px solid #EADFC8;display:flex;align-items:center;gap:12px;flex-wrap:wrap")}>
            <span style={css("flex:1;min-width:260px;font-size:11.5px;color:#64748B;line-height:1.5")}>
              “รวมกลุ่ม” จะเก็บงานที่กรอกข้อมูลครบที่สุดไว้หนึ่งงาน แล้วลบตัวซ้ำที่เหลือออกจากฐานข้อมูล · ปุ่มรวมทั้งหมดแตะเฉพาะกลุ่มที่ <b>มาจากคนละไฟล์</b> และข้อมูลไม่ขัดกัน — กลุ่มที่มาจากไฟล์เดียวกันอาจเป็นหลายเที่ยวในงานเดียวจริง ๆ ต้องดูเองทีละกลุ่ม
            </span>
            <button
              onClick={p.onMergeAll}
              disabled={p.busy || !reUploaded.length}
              style={css("height:32px;padding:0 14px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;font-size:12px;font-weight:600;cursor:" + (p.busy || !reUploaded.length ? "not-allowed" : "pointer"))}
            >
              รวมเฉพาะกลุ่ม “อัปซ้ำ” ที่ไม่ขัดกัน
            </button>
          </div>
        )}

        <div style={css("padding:14px 20px;display:flex;flex-direction:column;gap:10px")}>
          {p.groups.length ? p.groups.slice(0, 60).map((g) => {
            const conflict = g.statuses.length > 1 || g.owners.length > 1;
            return (
              <div key={g.key} style={css("border:1px solid " + (conflict ? "#F5D9A8" : "#E9EFF5") + ";background:" + (conflict ? "#FFFBF2" : "#fff") + ";border-radius:5px;padding:11px 13px")}>
                <div style={css("display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:8px")}>
                  <span style={css("font-size:12px;font-weight:600;color:#0A2240;font-family:'IBM Plex Mono',monospace")}>{g.key.replace(/\|/g, " · ")}</span>
                  <span style={css(badge(g.jobs.length + " แถว", conflict ? "amber" : "gray"))}>{g.jobs.length} แถว</span>
                  {g.reUploaded
                    ? <span style={css(badge("อัปซ้ำ", "red"))}>อัปซ้ำ · {g.batches.join(" + ")}</span>
                    : <span style={css(badge("ไฟล์เดียว", "blue"))}>ไฟล์เดียวกัน · อาจเป็นหลายเที่ยว</span>}
                  {conflict && <span style={css(badge("ขัดกัน", "amber"))}>ข้อมูลขัดกัน</span>}
                  <button
                    onClick={() => p.onMerge(g)}
                    disabled={p.busy}
                    style={css("margin-left:auto;height:28px;padding:0 12px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:11.5px;cursor:" + (p.busy ? "not-allowed" : "pointer"))}
                  >
                    รวมกลุ่ม (เก็บตัวที่ข้อมูลครบสุด)
                  </button>
                </div>
                <div style={css("overflow-x:auto")}>
                  <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
                    <thead>
                      <tr>
                        {["Job / ABS", "ลูกค้า", "ผู้ขนส่ง", "สถานะ", "ผู้รับผิดชอบ", "ตู้", ""].map((h) => (
                          <th key={h} style={css("text-align:left;font-size:10px;color:#8496A8;padding:0 10px 5px 0;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {g.jobs.map((j) => (
                        <tr key={j.key}>
                          <td style={css("padding:5px 10px 5px 0;font-family:'IBM Plex Mono',monospace;white-space:nowrap")}>{j.jobCode || j.abs || j.jobNo || "—"}</td>
                          <td style={css("padding:5px 10px 5px 0")}>{j.customer}</td>
                          <td style={css("padding:5px 10px 5px 0")}>{j.trucker || "—"}</td>
                          <td style={css("padding:5px 10px 5px 0;white-space:nowrap")}>{j.status}</td>
                          <td style={css("padding:5px 10px 5px 0;white-space:nowrap")}>{j.op}</td>
                          <td style={css("padding:5px 10px 5px 0;font-family:'IBM Plex Mono',monospace")}>{j.container || "—"}</td>
                          <td style={css("padding:5px 0")}>
                            <button
                              onClick={() => p.onOpenJob(j.key)}
                              style={css("height:24px;padding:0 9px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:11px;cursor:pointer")}
                            >
                              เปิด
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            );
          }) : (
            <div style={css("border:1px solid #E3F4EB;background:#F4FBF7;border-radius:5px;padding:16px;font-size:12.5px;color:#16794C")}>
              ไม่พบงานซ้ำในแผนนี้ ✓
            </div>
          )}
          {p.groups.length > 60 && (
            <div style={css("font-size:11.5px;color:#94A3B8")}>แสดง 60 จาก {p.groups.length} กลุ่ม · รวมกลุ่มไปแล้วจะเห็นกลุ่มถัดไป</div>
          )}
        </div>
      </div>
    </div>
  );
}
