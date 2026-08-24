"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { RotationOwner, RotationRow } from "../rotation";
import { loadRotation, loadRotationOwners, replaceRotation } from "../rotation";
import { parseRotationWorkbook } from "../rotationExcel";
import { css } from "../theme";

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

export function JobRotation({ me, onToast }: {
  /** The signed-in operator's directory id, so their own page opens first. */
  me: string;
  onToast: (message: string) => void;
}) {
  const [owners, setOwners] = useState<RotationOwner[] | null>(null);
  const [rows, setRows] = useState<RotationRow[] | null>(null);
  const [owner, setOwner] = useState(me);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
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
        <button onClick={() => file.current?.click()} disabled={busy} style={BTN_SECONDARY}>
          {busy ? "กำลังนำเข้า…" : "นำเข้าตารางจาก Excel"}
        </button>
      </div>

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
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["ลูกค้า", "ประเภทงาน", "ผู้รับผิดชอบหลัก", "สำรอง 1", "สำรอง 2",
                    "ผู้ขนส่ง FCL", "ผู้ขนส่ง LCL", "CS LCB", "งานในทะเบียน"].map((head) => (
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
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const TH = css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap");
const TD = "padding:8px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top";
const BTN_SECONDARY = css("height:32px;padding:0 14px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit");

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
