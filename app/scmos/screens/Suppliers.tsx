"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import { REQUIREMENTS, STATE_TONE, stateLabel } from "../supplierCompliance";

/**
 * The supplier register.
 *
 * One row per company, with every spelling the register and the rate cards use
 * pointing at it. That reconciliation is the point: TATIYAPOL's jobs and
 * TATIYAPON's jobs can only be counted together once somebody has said the two
 * spellings mean one firm, and this screen is where they say it.
 */

type Summary = {
  id: number; code: string; name: string; status: string;
  serviceType: string; serviceArea: string;
  jobs: number; lanes: number; trucks: number; drivers: number;
  lastScore: number | null; lastEvaluatedPeriod: string;
  aliases: string[]; expiringDocuments: number;
  vendorNo: string; taxId: string; address: string;
  dgCapable: boolean; reeferCapable: boolean; isoTankCapable: boolean; gpsEquipped: boolean;
  /* The company's record on the ASL/BSL list procurement keeps in ABS. */
  absNo: string; listType: string; legalName: string;
  contactPerson: string; telephone: string; fax: string; email: string; website: string;
  creditTerm: string; servicesRequired: string; mainSpType: string; typeOfService: string;
  /** Whether this company moves cargo by road. 560 of the 641 do not. */
  isCarrier: boolean;
  /* The five required documents, always all five, in REQUIREMENTS order. */
  compliance: {
    code: string; documentId: number | null; fileName: string;
    expiryDate: string; state: string; daysLeft: number | null;
  }[];
  /** The worst of the five. */
  complianceStatus: string;
  /**
   * Everything hanging off the row — jobs, rates, documents, evaluations,
   * contacts, lorries, drivers, capacity.
   *
   * The API refuses to remove a row holding any of it, and counts them itself.
   * This is that same count, sent so the button can be greyed before somebody
   * clicks it rather than after.
   */
  attached: number;
};

/** Everything a supplier row stores. Absent fields are left as they are. */
type Edit = Partial<{
  code: string; name: string; status: string;
  vendorNo: string; taxId: string; address: string;
  serviceArea: string; serviceType: string;
  dgCapable: boolean; reeferCapable: boolean; isoTankCapable: boolean; gpsEquipped: boolean;
}>;

const STATUS_TONE: Record<string, string> = {
  approved: "#16794C", draft: "#7B8CA0", "pending-audit": "#B45309",
  suspended: "#B42318", rejected: "#B42318",
};
/** One side of a duplicate pair, as the API reports it. */
type Side = { id: number; code: string; name: string; aliases: number; jobs: number; attached: number };
type Duplicate = { name: string; keep: Side; fold: Side[] };

/** One row of a duplicate pair, with what it is holding. */
function Row({ side, tone }: { side: Side; tone: "keep" | "fold" }) {
  const keep = tone === "keep";
  return (
    <span style={css("display:inline-flex;gap:6px;align-items:baseline;border:1px solid " + (keep ? "#A7D3BC" : "#E3D3D3")
      + ";background:" + (keep ? "#F1F9F5" : "#FBF6F6") + ";border-radius:4px;padding:3px 9px")}>
      <b style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:" + (keep ? "#16794C" : "#8A5A5A"))}>{side.code}</b>
      <span style={css("font-size:11px;color:#7B8CA0")}>
        {keep ? "เก็บไว้" : "รวมเข้า"} · งาน {side.jobs.toLocaleString()} · ชื่อย่อ {side.aliases} · อื่นๆ {side.attached}
      </span>
    </span>
  );
}

const STATUS_TH: Record<string, string> = {
  approved: "อนุมัติแล้ว", draft: "ร่าง", "pending-audit": "รอตรวจ",
  suspended: "ระงับ", rejected: "ไม่ผ่าน",
};

export function Suppliers({ canManage, onToast }: { canManage: boolean; onToast: (m: string) => void }) {
  const [rows, setRows] = useRemembered<Summary[]>("suppliers");
  /* The Excel import: the file chosen, what the preview said, and whether a
     call is in flight. The file is held so "write it" sends the same one that
     was previewed rather than asking for it twice. */
  const [aslFile, setAslFile] = useState<File | null>(null);
  const [asl, setAsl] = useState<AslOutcome | null>(null);
  const [aslBusy, setAslBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  /** The paste box for the agreed list of haulage companies. */
  const [directory, setDirectory] = useState("");
  const [showDirectory, setShowDirectory] = useState(false);
  /** Hauliers the register holds twice, and which row keeps the history. */
  const [dupes, setDupes] = useState<Duplicate[]>([]);

  const load = useCallback(async () => {
    // Both in flight together: the duplicate check reads the same table the
    // list does, and waiting for one before asking for the other only makes a
    // sleeping database take twice as long to answer.
    const [list, duplicates] = await Promise.all([
      apiFetch("/api/suppliers", { headers: { accept: "application/json" } }),
      apiFetch("/api/suppliers/duplicates", { headers: { accept: "application/json" } }),
    ]);
    const body = list.ok ? await list.json() as Summary[] : null;
    setRows((held) => body ?? held ?? []);
    if (duplicates.ok) setDupes(await duplicates.json() as Duplicate[]);
  }, [setRows]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [list, duplicates] = await Promise.all([
        apiFetch("/api/suppliers", { headers: { accept: "application/json" } }),
        apiFetch("/api/suppliers/duplicates", { headers: { accept: "application/json" } }),
      ]);
      const rows = list.ok ? await list.json() as Summary[] : null;
      const dupes = duplicates.ok ? await duplicates.json() as Duplicate[] : null;
      if (cancelled) return;
      setRows((held) => rows ?? held ?? []);
      if (dupes) setDupes(dupes);
    })();
    return () => { cancelled = true; };
  }, [setRows]);

  async function post(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/suppliers/${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  /**
   * Files a compliance document.
   *
   * The screen sends the supplier and the folder; where the file lands —
   * SCMOS/Supplier/{code}/{folder} — is the API's to decide. The expiry is the
   * point of storing it at all: an insurance certificate with no expiry cannot
   * be watched, and a lapsed one is what the compliance count exists to catch.
   */
  async function upload(supplierId: number, file: File, folder: string, kind: string, expiryDate: string) {
    if (busy) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append("supplierId", String(supplierId));
      body.append("folder", folder);
      // The kind is what decides which column the file lands under, so it is
      // the requirement's code rather than the folder — two of the five share
      // Insurance and two share Contract, and the folder cannot tell them
      // apart. It was the folder here, which is why nothing ever matched.
      body.append("kind", kind);
      body.append("expiryDate", expiryDate);
      body.append("file", file);
      const response = await apiFetch("/api/documents", { method: "POST", body });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "อัปโหลดไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  /**
   * Reads the pasted list.
   *
   * A plain line is a company. A line with an equals sign is a short form and
   * the company it belongs to — "SJ = Sangja Transport Co., Ltd." — which is
   * how SANGJA and SJ stop being counted as two firms.
   */
  function readDirectory(text: string) {
    const names: string[] = [];
    const aliases: { alias: string; company: string }[] = [];

    for (const line of text.split("\n")) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      const equals = trimmed.indexOf("=");
      if (equals > 0) {
        aliases.push({
          alias: trimmed.slice(0, equals).trim(),
          company: trimmed.slice(equals + 1).trim(),
        });
      } else {
        names.push(trimmed);
      }
    }
    return { names, aliases };
  }

  async function importDirectory() {
    const { names, aliases } = readDirectory(directory);
    if (!names.length && !aliases.length) { onToast("ยังไม่มีรายชื่อในกล่อง"); return; }

    setBusy(true);
    try {
      const response = await apiFetch("/api/suppliers/directory", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ names, aliases }),
      });
      const reply = await response.json().catch(() => null) as {
        added?: number; alreadyThere?: number; renamed?: number; aliasesLinked?: number;
        aliasesWithNoCompany?: string[]; message?: string;
      } | null;

      if (!response.ok) { onToast(reply?.message ?? `นำเข้าไม่สำเร็จ (${response.status})`); return; }

      const orphans = reply?.aliasesWithNoCompany ?? [];
      onToast(`เพิ่มใหม่ ${reply?.added ?? 0} ราย · เปลี่ยนเป็นชื่อเต็ม ${reply?.renamed ?? 0} ราย`
        + ` · มีอยู่แล้ว ${reply?.alreadyThere ?? 0} · ผูกชื่อย่อ ${reply?.aliasesLinked ?? 0}`
        + (orphans.length ? ` · ชื่อย่อที่หาบริษัทไม่เจอ ${orphans.length}: ${orphans.join(", ")}` : ""));
      setDirectory("");
      setShowDirectory(false);
      await load();
    } catch (error) {
      onToast("นำเข้าไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setBusy(false); }
  }

  /**
   * Send the workbook and say what it would do, or do it.
   *
   * The same call either way, with `apply` deciding — so what is confirmed is
   * exactly what was previewed, computed by the same code against the same
   * database, rather than a preview from one path and a write from another.
   */
  async function importAsl(file: File, apply: boolean) {
    setAslBusy(true);
    try {
      const form = new FormData();
      form.append("file", file);
      if (apply) form.append("apply", "1");

      const response = await apiFetch("/api/suppliers/import-asl", { method: "POST", body: form });
      const reply = await response.json().catch(() => null) as (AslOutcome & { error?: string }) | null;

      if (!response.ok) {
        onToast(reply?.error ?? `นำเข้าไม่สำเร็จ (${response.status})`);
        return;
      }

      setAsl(reply);
      if (apply) {
        onToast(`นำเข้าแล้ว — เพิ่ม ${reply?.created ?? 0} · ปรับปรุง ${reply?.updated ?? 0}`
          + ` · ผู้ขนส่ง ${reply?.carriers ?? 0}`);
        setAslFile(null);
        await load();
      }
    } catch (error) {
      onToast("นำเข้าไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setAslBusy(false); }
  }

  /** Removes a supplier the register is not using. Refused by the API if it is. */
  async function remove(row: Summary) {
    if (row.attached > 0) {
      // Named rather than totalled, so somebody can see what to go and clear.
      const held = [
        row.jobs ? `งาน ${row.jobs}` : "",
        row.lanes ? `เส้นทางราคา ${row.lanes}` : "",
        row.trucks ? `รถ ${row.trucks}` : "",
        row.drivers ? `พนักงานขับรถ ${row.drivers}` : "",
      ].filter(Boolean).join(" · ");
      onToast(`ลบ ${row.name} ไม่ได้ — ยังมีข้อมูลผูกอยู่ ${row.attached} รายการ${held ? ` (${held})` : ""}`);
      return;
    }
    if (!window.confirm(`ลบ ${row.name} (${row.code}) ออกจากทะเบียนหรือไม่?`
      + "\n\nรายการนี้ไม่มีงาน ราคา หรือเอกสารผูกอยู่")) return;

    setBusy(true);
    try {
      const response = await apiFetch(`/api/suppliers/${row.id}?reason=${encodeURIComponent("ลบรายการที่ไม่ได้ใช้")}`,
        { method: "DELETE" });
      const reply = await response.json().catch(() => null) as { message?: string } | null;
      onToast(reply?.message ?? (response.ok ? "ลบแล้ว" : `ลบไม่สำเร็จ (${response.status})`));
      if (response.ok) { setPicked(null); await load(); }
    } catch (error) {
      onToast("ลบไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setBusy(false); }
  }

  /**
   * Folds a duplicate row into the one holding the history.
   *
   * Confirmed first because it moves paperwork between rows and removes one,
   * and because what the register says about a haulier is not something to
   * change on a mis-click.
   */
  async function merge(
    keep: { id: number; code: string; name: string },
    fold: { id: number; code: string; name: string; jobs: number; others: number },
  ) {
    const moving = [
      fold.jobs ? `งาน ${fold.jobs.toLocaleString()}` : "",
      fold.others ? `ข้อมูลอื่น ${fold.others.toLocaleString()} รายการ` : "",
    ].filter(Boolean).join(" · ");

    if (!window.confirm(
      `รวม ${fold.code} (${fold.name}) เข้ากับ ${keep.code} (${keep.name}) หรือไม่?`
      + (moving ? `\n\nจะย้าย ${moving} มาที่ ${keep.code} แล้วจึงลบ ${fold.code}` : "")
      + `\n\nชื่อย่อ เอกสาร และผลประเมินย้ายมาทั้งหมด — ไม่มีข้อมูลใดหายไป`)) return;

    setBusy(true);
    try {
      const response = await apiFetch("/api/suppliers/merge", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ keepId: keep.id, foldId: fold.id, reason: "รวมรายการซ้ำในทะเบียน" }),
      });
      const reply = await response.json().catch(() => null) as { message?: string } | null;
      onToast(reply?.message ?? (response.ok ? "รวมรายการแล้ว" : `รวมไม่สำเร็จ (${response.status})`));
      if (response.ok) { setPicked(keep.id); await load(); }
    } catch (error) {
      onToast("รวมไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setBusy(false); }
  }

  if (!rows) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const wanted = query.trim().toLowerCase();
  const shown = rows.filter((row) => !wanted
    || row.name.toLowerCase().includes(wanted)
    || row.aliases.some((alias) => alias.toLowerCase().includes(wanted)));

  const multi = rows.filter((row) => row.aliases.length > 1).length;
  const noRates = rows.filter((row) => row.jobs > 0 && row.lanes === 0).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:11px")}>
        <Tile label="ผู้ขนส่งทั้งหมด" value={rows.length} note="จากทะเบียนงานและตารางราคา" colour="#0A2240" />
        <Tile label="รวมชื่อซ้ำแล้ว" value={multi} note="เจ้าที่มีมากกว่า 1 การสะกด" colour="#16794C" />
        <Tile label="มีงานแต่ไม่มีราคา" value={noRates} note="คิดต้นทุนไม่ได้" colour="#B45309" />
        <Tile label="เอกสารใกล้หมดอายุ" value={rows.reduce((n, r) => n + r.expiringDocuments, 0)} note="ภายใน 60 วัน" colour="#B42318" />
      </div>

      {dupes.length > 0 && (
        <div style={css("background:#FFFBEB;border:1px solid #F5D9A6;border-radius:5px;padding:13px 16px")}>
          <div style={css("font-size:12.5px;font-weight:600;color:#92400E;margin-bottom:3px")}>
            มีบริษัทเดียวกันอยู่ในทะเบียนมากกว่าหนึ่งรายการ ({dupes.length} เจ้า)
          </div>
          <div style={css("font-size:11.5px;color:#8A6A3B;line-height:1.7;margin-bottom:9px")}>
            เกิดจากการนำเข้ารอบที่ล้มกลางคัน — รายการที่บันทึกไปก่อนหน้านั้นค้างอยู่ ·
            การรวมจะ<b>ย้าย</b>ชื่อย่อ เอกสาร เส้นทางราคา และผลประเมินไปไว้ที่รายการที่มีประวัติ
            แล้วจึงลบรายการที่ว่างเปล่า — ไม่มีข้อมูลใดหายไป · งานตามไปด้วยเอง เพราะงานอ้างบริษัทจาก<b>การสะกด</b>
            และชื่อย่อทุกอันจะมาอยู่ที่รายการเดียวกัน
          </div>
          <div style={css("display:flex;flex-direction:column;gap:7px")}>
            {dupes.map((group) => (
              <div key={group.keep.id} style={css("background:#fff;border:1px solid #EFE0C4;border-radius:4px;padding:9px 11px")}>
                <div style={css("font-size:12.5px;font-weight:600;color:#0A2240;margin-bottom:5px")}>{group.name}</div>
                <div style={css("display:flex;flex-wrap:wrap;gap:7px;align-items:center")}>
                  <Row side={group.keep} tone="keep" />
                  {group.fold.map((fold) => (
                    <span key={fold.id} style={css("display:flex;gap:6px;align-items:center")}>
                      <Row side={fold} tone="fold" />
                      {canManage && (
                        <button
                          onClick={() => void merge(group.keep,
                            { ...fold, others: fold.attached })}
                          disabled={busy}
                          style={css("height:25px;padding:0 10px;border:1px solid #B45309;background:" + (busy ? "#E5DCCB" : "#fff")
                            + ";color:#B45309;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
                          รวมเข้ากับ {group.keep.code}
                        </button>
                      )}
                    </span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap")}>
          <span style={css("font-size:12.5px;color:#465A6E")}><b style={css("color:#0A2240")}>{shown.length}</b> ราย</span>
          <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
            <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="ค้นหาชื่อหรือชื่อที่สะกดต่างกัน"
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;min-width:240px")} />
            {canManage && (
              <>
                {/* The label is the file picker. A button that opens a hidden
                    input is the only way to have one that looks like the button
                    beside it — a bare file input cannot be styled to match. */}
                <label style={css("height:30px;padding:0 12px;border:1px solid #16794C;background:"
                  + (aslBusy ? "#C3CFDB" : "#16794C") + ";color:#fff;border-radius:4px;font-size:12px;"
                  + "font-weight:600;cursor:" + (aslBusy ? "default" : "pointer")
                  + ";font-family:inherit;display:flex;align-items:center")}>
                  {aslBusy ? "กำลังอ่านไฟล์…" : "นำเข้าจาก Excel (ASL/BSL)"}
                  <input
                    type="file"
                    accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    disabled={aslBusy}
                    style={css("display:none")}
                    onChange={(e) => {
                      const chosen = e.target.files?.[0];
                      // Cleared so choosing the same file twice fires again —
                      // which somebody will do after fixing a column in it.
                      e.target.value = "";
                      if (!chosen) return;
                      setAslFile(chosen);
                      setAsl(null);
                      void importAsl(chosen, false);
                    }}
                  />
                </label>
                <button onClick={() => setShowDirectory((open) => !open)}
                  style={css("height:30px;padding:0 12px;border:1px solid #0A2240;background:" + (showDirectory ? "#0A2240" : "#fff")
                    + ";color:" + (showDirectory ? "#fff" : "#0A2240") + ";border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
                  นำเข้าทะเบียนผู้ขนส่ง
                </button>
              </>
            )}
          </div>
        </div>

        {/*
          What the file would do, before it does it.

          Shown rather than summarised in a toast because the numbers are the
          decision: 642 created and nothing updated means the carriers already
          in the register were not recognised and are about to be duplicated,
          which is the one outcome worth stopping.
        */}
        {asl && canManage && (
          <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;background:"
            + (asl.applied ? "#F1F8F4" : "#F8FAFC"))}>
            <div style={css("display:flex;gap:18px;flex-wrap:wrap;align-items:center;margin-bottom:9px")}>
              <Figure label="อ่านได้" value={asl.read} />
              <Figure label="เพิ่มใหม่" value={asl.created} />
              <Figure label="ปรับปรุง" value={asl.updated} />
              <Figure label="ไม่เปลี่ยน" value={asl.unchanged} />
              <Figure label="เป็นผู้ขนส่ง" value={asl.carriers} tone="#16794C" />
              {asl.skipped > 0 && <Figure label="ข้ามเพราะไม่มีชื่อ" value={asl.skipped} tone="#B45309" />}
            </div>

            {/* The failure worth naming. Everything else is a number to read;
                this one means the register is about to gain a second row for
                every carrier it already has. */}
            {!asl.applied && asl.updated === 0 && asl.created > 0 && rows && rows.length > 0 && (
              <div style={css("font-size:11.5px;color:#B42318;line-height:1.7;margin-bottom:8px")}>
                <b>ไม่มีรายการที่จับคู่กับทะเบียนเดิมได้เลย</b> — ถ้ากดเขียน ผู้ขนส่งที่มีอยู่แล้วจะถูกสร้างซ้ำ
                และงานกับคะแนนจะแยกกันอยู่คนละแถว ตรวจว่าเป็นไฟล์และฐานข้อมูลที่ถูกต้องก่อน
              </div>
            )}

            {asl.matched.length > 0 && (
              <details style={css("font-size:11.5px;color:#5A6B7D;margin-bottom:6px")}>
                <summary style={css("cursor:pointer;color:#0A5FA8")}>
                  จับคู่ผู้ขนส่งเดิมจากชื่อย่อ {asl.matchedByTradingName} ราย — กดดูรายชื่อ
                </summary>
                <div style={css("margin-top:6px;font-family:ui-monospace,monospace;font-size:11px;line-height:1.8;max-height:200px;overflow:auto")}>
                  {asl.matched.map((line, i) => <div key={i}>{line}</div>)}
                </div>
              </details>
            )}

            {asl.unsure.length > 0 && (
              <div style={css("font-size:11.5px;color:#8A5A12;line-height:1.7;margin-bottom:6px")}>
                <b>อาจเป็นบริษัทเดียวกัน</b> แต่ระบบไม่เดาให้ — นำเข้าแยกไว้ ถ้าใช่ให้กดรวมทีหลัง:
                <div style={css("font-family:ui-monospace,monospace;font-size:11px;margin-top:3px")}>
                  {asl.unsure.map((line, i) => <div key={i}>{line}</div>)}
                </div>
              </div>
            )}

            {asl.ambiguous.length > 0 && (
              <div style={css("font-size:11.5px;color:#8A5A12;line-height:1.7;margin-bottom:6px")}>
                ไม่จับคู่เพราะเข้าได้หลายบริษัท:
                <div style={css("font-family:ui-monospace,monospace;font-size:11px;margin-top:3px")}>
                  {asl.ambiguous.map((line, i) => <div key={i}>{line}</div>)}
                </div>
              </div>
            )}

            <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:4px")}>
              {asl.applied ? (
                <span style={css("font-size:12px;color:#16794C;font-weight:600")}>เขียนลงทะเบียนแล้ว</span>
              ) : (
                <>
                  <button disabled={aslBusy || !aslFile}
                    onClick={() => aslFile && void importAsl(aslFile, true)}
                    style={css("height:30px;padding:0 14px;border:1px solid #16794C;background:"
                      + (aslBusy ? "#C3CFDB" : "#16794C") + ";color:#fff;border-radius:4px;font-size:12px;"
                      + "font-weight:600;cursor:pointer;font-family:inherit")}>
                    {aslBusy ? "กำลังเขียน…" : `เขียนลงทะเบียน (${asl.created + asl.updated} รายการ)`}
                  </button>
                  <span style={css("font-size:11.5px;color:#7B8CA0")}>ยังไม่ได้เขียนอะไรลงฐานข้อมูล</span>
                </>
              )}
              <button onClick={() => { setAsl(null); setAslFile(null); }}
                style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
                ปิด
              </button>
            </div>
          </div>
        )}

        {showDirectory && canManage && (
          <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;background:#F8FAFC")}>
            <div style={css("font-size:11.5px;color:#5A6B7D;line-height:1.75;margin-bottom:8px")}>
              วางรายชื่อบริษัทขนส่ง <b>บรรทัดละหนึ่งชื่อ</b> ตามที่จดทะเบียนไว้ ·
              บรรทัดที่มีเครื่องหมาย <b>=</b> คือการผูกชื่อย่อกับบริษัท เช่น{" "}
              <code style={css("background:#EEF3F8;padding:1px 5px;border-radius:3px")}>SJ = Sangja Transport Co., Ltd.</code>
              <div style={css("margin-top:5px;color:#7B8CA0")}>
                ไม่มีการลบหรือทับของเดิม — บริษัทที่มีอยู่แล้วจะคงสถานะ เลขผู้ขาย และเอกสารไว้ทั้งหมด ·
                รายชื่อเก็บในฐานข้อมูล ไม่ได้ฝังไว้ในโค้ดหรือไปกับตัวติดตั้ง
              </div>
            </div>
            <textarea
              value={directory}
              onChange={(e) => setDirectory(e.target.value)}
              rows={8}
              placeholder={"DGT Cross Haul Co., Ltd.\nJTC Logistics Co., Ltd.\n…\n\nSJ = Sangja Transport Co., Ltd."}
              style={css("width:100%;border:1px solid #C9D6E2;border-radius:4px;padding:8px 10px;font-size:12px;font-family:'IBM Plex Mono',monospace;resize:vertical")}
            />
            <div style={css("display:flex;gap:8px;align-items:center;margin-top:8px")}>
              <button onClick={importDirectory} disabled={busy}
                style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:" + (busy ? "#C3CFDB" : "#0A2240")
                  + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
                {busy ? "กำลังนำเข้า…" : "นำเข้า"}
              </button>
              <span style={css("font-size:11.5px;color:#7B8CA0")}>
                {(() => {
                  const { names, aliases } = readDirectory(directory);
                  return names.length || aliases.length
                    ? `อ่านได้ ${names.length} ชื่อบริษัท · ${aliases.length} ชื่อย่อ`
                    : "ยังไม่มีรายชื่อในกล่อง";
                })()}
              </span>
            </div>
          </div>
        )}
        {/*
          The register as procurement keeps it, in their column order.

          Seventeen columns, which is wider than a laptop: the first three are
          pinned so a row stays identifiable while the rest is dragged sideways,
          because a telephone number with no company beside it is not an answer
          to anything. The last three — status, jobs, rate lanes — are
          SCMOS's own, not the list's, and are what says whether this company
          has ever actually worked for us.
        */}
        {/* No zoom. The register is read across, not squinted at: nineteen
            columns with three of them pinned, and a slider that shrinks the
            type does not make the twentieth column reachable. Asked for by the
            department, the same as on KPI. */}
        <ZoomBox zoomable={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px;white-space:nowrap")}>
            <thead><tr>{COLUMNS.map((column, i) => (
              <th key={column.head} style={css("position:sticky;top:0;z-index:" + (i < PINNED ? 3 : 2)
                + ";background:#F8FAFC;padding:8px 12px;text-align:" + (column.right ? "right" : "left")
                + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;"
                + "font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap"
                + (i < PINNED
                  ? `;left:${PIN_AT[i]}px;width:${PIN_WIDTH[i]}px;min-width:${PIN_WIDTH[i]}px`
                    + `;max-width:${PIN_WIDTH[i]}px;box-shadow:${i === PINNED - 1 ? "2px 0 0 #E9EFF5" : "none"}`
                  : ""))}>
                {column.head}
              </th>
            ))}</tr></thead>
            <tbody>
              {shown.map((row) => (
                <tr key={row.id} onClick={() => setPicked(row.id === picked ? null : row.id)}
                  style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (row.id === picked ? "#F2F7FC" : "#fff"))}>
                  {/* Pinned, tinted, and carrying the shadow that says the row
                      continues to the right. */}
                  <td style={css(PIN(0) + ";font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0")}>{row.absNo || "—"}</td>
                  <td style={css(PIN(1))}>
                    {row.listType
                      ? <span style={css("font-size:10px;font-weight:700;padding:2px 6px;border-radius:3px;color:#fff;background:"
                          + (row.listType === "ASL" ? "#16794C" : "#1D5FA8"))}>{row.listType}</span>
                      : <span style={css("color:#C3CFDB")}>—</span>}
                  </td>
                  <td style={css(PIN(2) + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{row.code}</td>

                  {/* The registered name where there is one, the register's own
                      spelling otherwise. A carrier the plan calls 9ISARA is
                      "9 Isara Transport Co., Ltd." on procurement's list, and
                      this is the column where the full name belongs. */}
                  <td style={css(CELL + ";font-weight:600;color:#0A2240;max-width:280px;overflow:hidden;text-overflow:ellipsis")}
                    title={row.legalName && row.legalName !== row.name ? row.name : undefined}>
                    {row.legalName || row.name}
                  </td>
                  <td style={css(CELL + ";max-width:300px;overflow:hidden;text-overflow:ellipsis;color:#475569")} title={row.address}>{row.address || DASH}</td>
                  <td style={css(CELL)}>{row.contactPerson || DASH}</td>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{row.telephone || DASH}</td>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{row.fax || DASH}</td>
                  <td style={css(CELL)}>
                    {row.email
                      ? <a href={`mailto:${row.email}`} onClick={(e) => e.stopPropagation()}
                          style={css("color:#0A5FA8;text-decoration:none")}>{row.email}</a>
                      : DASH}
                  </td>
                  <td style={css(CELL + ";max-width:200px;overflow:hidden;text-overflow:ellipsis")}>
                    {row.website
                      ? <a href={webAddress(row.website)} target="_blank" rel="noreferrer"
                          onClick={(e) => e.stopPropagation()}
                          style={css("color:#0A5FA8;text-decoration:none")}>{row.website}</a>
                      : DASH}
                  </td>
                  <td style={css(CELL + ";text-align:right")}>{row.creditTerm || DASH}</td>
                  <td style={css(CELL)}>{row.servicesRequired || DASH}</td>
                  <td style={css(CELL)}>{row.mainSpType || DASH}</td>
                  <td style={css(CELL + ";max-width:260px;overflow:hidden;text-overflow:ellipsis")} title={row.typeOfService}>
                    {row.typeOfService || DASH}
                  </td>

                  {/*
                    One cell per required document: what it says, when it runs
                    out, and the file itself. A missing one is not blank — a
                    blank cell reads as "nothing to see", and the whole point of
                    listing all five is that the gap is visible.
                  */}
                  {REQUIREMENTS.map((need) => {
                    const held = row.compliance?.find((one) => one.code === need.code);
                    const state = held?.state ?? "missing";
                    const tone = STATE_TONE[state] ?? STATE_TONE.missing;
                    return (
                      <td key={need.code} style={css(CELL + ";white-space:nowrap")}>
                        <div style={css(`display:inline-block;padding:2px 7px;border-radius:3px;font-size:10.5px;`
                          + `font-weight:700;background:${tone.bg};color:${tone.ink}`)}>
                          {need.expires && held?.expiryDate ? held.expiryDate : tone.label}
                        </div>
                        {held?.documentId ? (
                          <div style={css("margin-top:2px")}>
                            <a href={`/api/documents/${held.documentId}/content`} target="_blank" rel="noreferrer"
                              onClick={(e) => e.stopPropagation()}
                              style={css("font-size:11px;color:#0A5FA8;text-decoration:none")}
                              title={held.fileName}>
                              เปิดไฟล์
                            </a>
                            {need.expires && held.expiryDate && (
                              <span style={css(`font-size:10.5px;color:${tone.ink};margin-left:6px`)}>
                                {stateLabel(state, held.daysLeft)}
                              </span>
                            )}
                          </div>
                        ) : (
                          <div style={css("margin-top:2px;font-size:10.5px;color:#C3CFDB")}>ยังไม่แนบไฟล์</div>
                        )}
                      </td>
                    );
                  })}

                  {/* The worst of the five, so a row can be judged without
                      reading across all of them. */}
                  <td style={css(CELL + ";white-space:nowrap")}>
                    {(() => {
                      const tone = STATE_TONE[row.complianceStatus] ?? STATE_TONE.missing;
                      return (
                        <span style={css(`font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:3px;`
                          + `background:${tone.bg};color:${tone.ink}`)}>
                          {tone.label}
                        </span>
                      );
                    })()}
                  </td>

                  <td style={CELL_S}>
                    <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${STATUS_TONE[row.status] ?? "#7B8CA0"}`)}>
                      {STATUS_TH[row.status] ?? row.status}
                    </span>
                  </td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.jobs.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;color:" + (row.jobs > 0 && row.lanes === 0 ? "#B45309" : "#7B8CA0"))}>{row.lanes.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.lastScore ?? DASH}</td>
                  <td style={css(CELL + ";font-size:11px;color:#7B8CA0")}>{row.aliases.join(" · ")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </ZoomBox>
      </div>

      {picked !== null && canManage && (
        <Manage supplier={rows.find((r) => r.id === picked)!} others={rows} busy={busy}
          onMerge={(keep, fold) => void merge(keep,
            // A register row's total counts its jobs; the dialog names those
            // separately, so they come back out of it here.
            { ...fold, others: Math.max(0, fold.attached - fold.jobs) })}
          onEdit={(fields) => void post(`${picked}/edit`, fields)}
          onRemove={() => void remove(rows.find((r) => r.id === picked)!)}
          onAlias={(alias) => void post(`${picked}/alias`, { alias })}
          onStatus={(status) => void post(`${picked}/status`, { status })}
          onEvaluate={(period, safety, documents, note) =>
            void post(`${picked}/evaluate`, { period, safety, documents, note })}
          onUpload={(file, folder, kind, expiryDate) => void upload(picked, file, folder, kind, expiryDate)} />
      )}
    </div>
  );
}

/** One number in the import preview. */
function Figure({ label, value, tone = "#0A2240" }: { label: string; value: number; tone?: string }) {
  return (
    <div>
      <div style={css(`font-size:16px;font-weight:700;font-family:ui-monospace,monospace;color:${tone}`)}>{value.toLocaleString()}</div>
      <div style={css("font-size:10.5px;color:#7B8CA0")}>{label}</div>
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:top";
const CELL_S = css(CELL);

/*
 * The register, in procurement's column order.
 *
 * Their thirteen from the ASL/BSL list, then four of ours: what SCMOS makes of
 * the company. The order is the one the department asked for, which puts the
 * ABS number first because that is the number they look a supplier up by.
 */
const COLUMNS: { head: string; right?: boolean }[] = [
  { head: "ABS No" }, { head: "ASL-BSL" }, { head: "รหัส" },
  { head: "Supplier_Name" }, { head: "Address" }, { head: "Contact_Person" },
  { head: "Telephone" }, { head: "Fax" }, { head: "Email" }, { head: "Website" },
  { head: "Credit Term (Days)", right: true }, { head: "Services Required" },
  { head: "Main SP Type" }, { head: "Type of Service" },
  // The five the department listed, then Status, which is the worst of them.
  ...REQUIREMENTS.map((need) => ({ head: `${need.english}\n(${need.thai})` })),
  { head: "Status" },
  { head: "สถานะ" }, { head: "งาน", right: true }, { head: "เส้นทางราคา", right: true },
  // Kept from before the ASL/BSL columns arrived. Not on the department's list,
  // and not dropped for that: a score and the spellings a company is known by
  // are things the register already answered, and taking them away to make room
  // is a loss nobody asked for.
  { head: "คะแนนล่าสุด", right: true }, { head: "ชื่อที่สะกดต่างกัน" },
];

/*
 * How many of those stay put while the rest is dragged sideways.
 *
 * Three: the ABS number, which list they are on, and our code. Seventeen
 * columns is wider than any laptop, and a telephone number with no company
 * beside it is not an answer to anything — but pin the name as well and half
 * the screen is frozen, so the identifiers are pinned and the name scrolls
 * with the rest.
 */
const PINNED = 3;

/*
 * How wide each pinned column is, and therefore where the next one starts.
 *
 * Declared rather than guessed. The offsets were three numbers written by eye
 * — 0, 92, 168 — against columns the browser sized from their contents, so the
 * third sat 25px left of where the second actually ended and the company name
 * was cut off under it. A sticky column has to be told its width, or the
 * position it sticks to and the width it occupies are two different opinions.
 */
const PIN_WIDTH = [96, 92, 96];

/** Where each pinned column starts: everything to its left, added up. */
const PIN_AT = PIN_WIDTH.map((_, i) =>
  PIN_WIDTH.slice(0, i).reduce((total, width) => total + width, 0));

/** The width has to be on the header and the cell alike, or only one obeys it. */
const PIN = (i: number) =>
  CELL + ";position:sticky;z-index:1;background:inherit;left:" + PIN_AT[i] + "px"
  + ";width:" + PIN_WIDTH[i] + "px;min-width:" + PIN_WIDTH[i] + "px;max-width:" + PIN_WIDTH[i] + "px"
  + ";overflow:hidden;text-overflow:ellipsis"
  + (i === PINNED - 1 ? ";box-shadow:2px 0 0 #F1F5F9" : "");

/**
 * What the ASL/BSL import did, or would do.
 *
 * The same record the command-line importer prints. Read before writing: 642
 * rows land in the table that decides who a job may be given to, and the
 * difference between "created 642" and "updated 30, created 612" is the
 * difference between an import and a duplicate register.
 */
type AslOutcome = {
  read: number; skipped: number;
  created: number; updated: number; unchanged: number;
  carriers: number; repeatedInFile: number; matchedByTradingName: number;
  matched: string[]; unsure: string[]; ambiguous: string[];
  applied: boolean;
};

/** What an empty cell shows. 560 of these companies have no fax and no website. */
const DASH = "—";

/**
 * A website as typed, made into something a browser will follow.
 *
 * The list carries "www.mintercorp.coth" and " http://www.ckline.co.th" in the
 * same column. A bare host with no scheme is read as a relative path and would
 * navigate inside SCMOS, which looks like the app breaking rather than a
 * supplier having typed their own address in without the http.
 */
function webAddress(raw: string): string {
  const text = raw.trim();
  return /^https?:\/\//i.test(text) ? text : "https://" + text;
}


/** The folders a supplier's paperwork goes in, matching BlobPaths.SupplierFolders. */
const SUPPLIER_FOLDERS: [string, string][] = [
  ["Insurance", "ประกันภัย"], ["License", "ใบอนุญาต"], ["Audit", "ผลตรวจประเมิน"],
  ["Training", "อบรม"], ["Contract", "สัญญา"], ["Other", "อื่นๆ"],
];

function Manage({ supplier, others, busy, onEdit, onRemove, onMerge, onAlias, onStatus, onEvaluate, onUpload }: {
  supplier: Summary;
  /** The rest of the register, so two rows for one firm can be joined by hand. */
  others: Summary[];
  busy: boolean;
  onMerge: (keep: Summary, fold: Summary) => void;
  onEdit: (fields: Edit) => void;
  onRemove: () => void;
  onAlias: (alias: string) => void;
  onStatus: (status: string) => void;
  onEvaluate: (period: string, safety: number | null, documents: number | null, note: string) => void;
  onUpload: (file: File, folder: string, kind: string, expiryDate: string) => void;
}) {
  const [alias, setAlias] = useState("");
  const [period, setPeriod] = useState(String(new Date().getFullYear()));
  const [safety, setSafety] = useState("");
  const [documents, setDocuments] = useState("");
  const [note, setNote] = useState("");
  const [folder, setFolder] = useState("Insurance");
  const [expiry, setExpiry] = useState("");

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <Details supplier={supplier} others={others} busy={busy}
        onEdit={onEdit} onRemove={onRemove} onMerge={onMerge} />
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px;display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:18px")}>
      <div>
        <Label>ผูกชื่อที่สะกดต่างกันเข้ากับ {supplier.name}</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
          เช่น TTP กับ TATIYAPON — ระบบไม่เดาให้ เพราะจ่ายผิดเจ้าแย่กว่าไม่มีข้อมูล
        </div>
        <div style={css("display:flex;gap:6px")}>
          <input value={alias} onChange={(e) => setAlias(e.target.value)} placeholder="ชื่อที่จะผูก"
            style={css("flex:1;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <Button label="ผูก" tone="#0A2240" busy={busy || !alias.trim()} onClick={() => onAlias(alias)} />
        </div>
      </div>

      <div>
        <Label>สถานะการอนุมัติ</Label>
        <div style={css("display:flex;gap:5px;flex-wrap:wrap;margin-top:7px")}>
          {["draft", "pending-audit", "approved", "suspended"].map((status) => (
            <button key={status} onClick={() => onStatus(status)} disabled={busy}
              style={css("height:27px;padding:0 10px;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;border:1px solid " +
                (supplier.status === status ? STATUS_TONE[status] : "#C9D6E2") +
                ";background:" + (supplier.status === status ? STATUS_TONE[status] : "#fff") +
                ";color:" + (supplier.status === status ? "#fff" : "#5A6B7D"))}>
              {STATUS_TH[status]}
            </button>
          ))}
        </div>
      </div>

      <div>
        <Label>ประเมินประจำปี</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
          คะแนนตรงเวลา / ตอบยืนยัน / ความล่าช้า ดึงจาก KPI อัตโนมัติ
        </div>
        <div style={css("display:flex;gap:6px;margin-bottom:6px;flex-wrap:wrap")}>
          <input value={period} onChange={(e) => setPeriod(e.target.value)} placeholder="รอบ"
            style={css("width:80px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <input value={safety} onChange={(e) => setSafety(e.target.value.replace(/\D/g, ""))} placeholder="ความปลอดภัย"
            style={css("flex:1;min-width:110px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <input value={documents} onChange={(e) => setDocuments(e.target.value.replace(/\D/g, ""))} placeholder="เอกสาร"
            style={css("flex:1;min-width:90px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
        </div>
        <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="หมายเหตุการประเมิน"
          style={css("width:100%;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:7px")} />
        <Button label="บันทึกการประเมิน" tone="#16794C" busy={busy}
          onClick={() => onEvaluate(period, safety ? Number(safety) : null, documents ? Number(documents) : null, note)} />
      </div>

      <div>
        <Label>เอกสาร</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
          เก็บที่ SCMOS/Supplier/{supplier.code}/{folder} — ระบบเลือกที่เก็บให้เอง ไม่ต้องตั้งชื่อพาธ
        </div>
        <div style={css("display:flex;gap:6px;flex-wrap:wrap;align-items:center")}>
          {/*
            The five required documents first, then the plain folders.

            Choosing one of the five is what puts the file in its column on the
            register: the value carries the requirement's code, and the folder
            follows from it. The loose folders stay for everything that is not
            one of the five — a rate card, a training record — which still has
            to go somewhere.
          */}
          <select value={folder} onChange={(e) => setFolder(e.target.value)}
            style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;background:#fff;max-width:260px")}>
            <optgroup label="เอกสารที่ต้องมี">
              {REQUIREMENTS.map((need) => (
                <option key={need.code} value={need.code}>
                  {need.english} ({need.thai})
                </option>
              ))}
            </optgroup>
            <optgroup label="อื่น ๆ">
              {SUPPLIER_FOLDERS.map(([id, label]) => <option key={id} value={id}>{label}</option>)}
            </optgroup>
          </select>
          <input value={expiry} onChange={(e) => setExpiry(e.target.value)} placeholder="หมดอายุ DD/MM/YYYY"
            style={css("width:150px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <label style={css(`height:29px;padding:0 13px;border:1px solid #0A2240;background:${busy ? "#C3CFDB" : "#0A2240"};color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center`)}>
            แนบไฟล์
            <input type="file" disabled={busy} style={css("display:none")}
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = "";
                if (!file) return;
                // One of the five, or a loose folder. The requirement decides
                // both where the file goes and what it is; a folder can only
                // say where.
                const need = REQUIREMENTS.find((one) => one.code === folder);
                onUpload(file, need ? need.folder : folder, need ? need.code : folder, expiry);
              }} />
          </label>
        </div>
      </div>
      </div>
    </div>
  );
}

/**
 * The company's own details, all of them editable.
 *
 * Everything on this card was typed by somebody, so all of it can be corrected
 * — including the code and the name, which the table shows and nothing else
 * could change. What is deliberately not here is the other half of the table:
 * jobs, rate lanes and the last score are counted from the register, the rate
 * book and the evaluations. A counted figure you can type over stops meaning
 * anything, so those are corrected where they come from and are shown here
 * read-only, with where to go.
 */
function Details({ supplier, others, busy, onEdit, onRemove, onMerge }: {
  supplier: Summary; others: Summary[]; busy: boolean;
  onEdit: (fields: Edit) => void;
  onRemove: () => void;
  onMerge: (keep: Summary, fold: Summary) => void;
}) {
  // Seeded from the row and re-seeded when a different supplier is picked, so
  // an edit box never carries one company's text over onto another.
  const [draft, setDraft] = useState<Edit>({});
  const [seeded, setSeeded] = useState(supplier.id);
  /** The other row this one is the same company as, once somebody says so. */
  const [pair, setPair] = useState("");
  /** Which of the two keeps its code. The busier one by default. */
  const [keepThis, setKeepThis] = useState(true);
  if (seeded !== supplier.id) { setSeeded(supplier.id); setDraft({}); setPair(""); }

  const value = <K extends keyof Summary & keyof Edit>(field: K) =>
    (draft[field] ?? supplier[field]) as Summary[K];
  const set = (field: keyof Edit, next: string | boolean) =>
    setDraft((held) => ({ ...held, [field]: next }));

  const dirty = Object.keys(draft).length > 0;
  const attached = supplier.attached;
  const mate = others.find((row) => String(row.id) === pair && row.id !== supplier.id);

  const text = (field: keyof Edit & keyof Summary, label: string, hint = "") => (
    <label style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</span>
      <input value={String(value(field) ?? "")} onChange={(e) => set(field, e.target.value)}
        placeholder={hint}
        style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;font-family:inherit")} />
    </label>
  );

  const flag = (field: keyof Edit & keyof Summary, label: string) => (
    <label key={field} style={css("display:inline-flex;gap:5px;align-items:center;font-size:12px;color:#465A6E;cursor:pointer")}>
      <input type="checkbox" checked={Boolean(value(field))}
        onChange={(e) => set(field, e.target.checked)} />
      {label}
    </label>
  );

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:10px;flex-wrap:wrap;margin-bottom:11px")}>
        <Label>ข้อมูลบริษัท — แก้ไขได้ทุกช่อง</Label>
        <span style={css("font-size:11.5px;color:#94A3B8")}>
          งาน {supplier.jobs.toLocaleString()} · เส้นทางราคา {supplier.lanes.toLocaleString()} ·
          คะแนนล่าสุด {supplier.lastScore ?? "—"} — สามอย่างนี้นับมาจากทะเบียนงาน ตารางราคา และผลประเมิน จึงแก้ที่ต้นทาง
        </span>
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:11px")}>
        {text("code", "รหัส", "ไม่ซ้ำกับรายอื่น")}
        {text("name", "ชื่อบริษัท")}
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>สถานะ</span>
          <select value={String(value("status"))} onChange={(e) => set("status", e.target.value)}
            style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;background:#fff;font-family:inherit")}>
            {["draft", "pending-audit", "approved", "suspended", "rejected"].map((id) => (
              <option key={id} value={id}>{STATUS_TH[id] ?? id}</option>
            ))}
          </select>
        </label>
        {text("vendorNo", "เลขผู้ขาย")}
        {text("taxId", "เลขประจำตัวผู้เสียภาษี")}
        {text("serviceType", "ประเภทบริการ", "FCL, LCL, ISO TANK")}
        {text("serviceArea", "พื้นที่ให้บริการ")}
        {text("address", "ที่อยู่")}
      </div>

      <div style={css("display:flex;gap:14px;flex-wrap:wrap;margin-top:11px")}>
        {flag("dgCapable", "สินค้าอันตราย")}
        {flag("reeferCapable", "ตู้เย็น")}
        {flag("isoTankCapable", "ไอโซแท็งก์")}
        {flag("gpsEquipped", "มี GPS")}
      </div>

      <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:13px;padding-top:11px;border-top:1px solid #EEF3F8")}>
        <Button label="บันทึกการแก้ไข" tone="#0A2240" busy={busy || !dirty}
          onClick={() => { onEdit(draft); setDraft({}); }} />
        {dirty && (
          <button onClick={() => setDraft({})}
            style={css("height:29px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
            ยกเลิก
          </button>
        )}

        <span style={css("flex:1")} />

        {/* Only for a row nothing is using. A company that has worked is
            merged into the row that holds its history, never deleted — the
            API refuses it either way, and greying it here says why before
            somebody clicks. */}
        <button onClick={onRemove} disabled={busy || attached > 0}
          title={attached > 0
            ? "ลบไม่ได้ — ยังมีงาน ราคา หรือเอกสารผูกอยู่ ถ้าเป็นบริษัทซ้ำให้ใช้ปุ่มรวมรายการ"
            : "ลบรายการนี้ออกจากทะเบียน"}
          style={css("height:29px;padding:0 13px;border-radius:4px;font-size:12px;font-weight:600;font-family:inherit;border:1px solid "
            + (attached > 0 ? "#DCE4EC" : "#B42318")
            + ";background:#fff;color:" + (attached > 0 ? "#B6C2CE" : "#B42318")
            + ";cursor:" + (attached > 0 ? "not-allowed" : "pointer"))}>
          ลบออกจากทะเบียน
        </button>
      </div>

      {attached > 0 && (
        <div style={css("font-size:11px;color:#94A3B8;margin-top:6px;text-align:right")}>
          ลบไม่ได้เพราะยังมีข้อมูลผูกอยู่ — บริษัทที่เคยมีงานจะไม่ถูกลบ แต่ใช้การรวมรายการแทน
        </div>
      )}

      {/*
        Joining two rows by hand.

        The panel above finds the pairs that can be proved from the register
        itself — the names match, or one row's name is a spelling the other
        holds. Some pairs cannot be proved and never will be: TATIYAPOL and
        TATIYAPON differ by one letter and are one company, PK and PKN differ
        by one letter and are two, and no rule reads both of those correctly.
        Somebody who knows says so here instead.
      */}
      <div style={css("margin-top:13px;padding-top:11px;border-top:1px solid #EEF3F8")}>
        <Label>รวมกับอีกรายการ — ถ้าเป็นบริษัทเดียวกัน</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin:4px 0 8px;line-height:1.7")}>
          ใช้เมื่อรู้ว่าสองรายการคือเจ้าเดียวกันแต่ระบบพิสูจน์เองไม่ได้ เช่นสะกดต่างกันหนึ่งตัว ·
          ข้อมูลทั้งหมดจะย้ายมารวมกัน ไม่มีอะไรหายไป
        </div>
        <div style={css("display:flex;gap:7px;align-items:center;flex-wrap:wrap")}>
          <select value={pair} onChange={(e) => { setPair(e.target.value); setKeepThis(true); }}
            style={css("flex:1;min-width:250px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;background:#fff;font-family:inherit")}>
            <option value="">— เลือกรายการที่เป็นบริษัทเดียวกัน —</option>
            {others.filter((row) => row.id !== supplier.id)
              .map((row) => (
                <option key={row.id} value={String(row.id)}>
                  {row.code} · {row.name} — งาน {row.jobs.toLocaleString()} · ราคา {row.lanes.toLocaleString()}
                </option>
              ))}
          </select>
          {mate && (
            <Button label={`รวมเป็นรายการเดียว`} tone="#B45309" busy={busy}
              onClick={() => onMerge(keepThis ? supplier : mate, keepThis ? mate : supplier)} />
          )}
        </div>

        {mate && (
          <div style={css("margin-top:8px;background:#FFFBEB;border:1px solid #F5D9A6;border-radius:4px;padding:9px 11px;font-size:11.5px;color:#8A6A3B;line-height:1.8")}>
            เหลือรายการเดียว รหัส <b style={css("font-family:ui-monospace,monospace")}>{(keepThis ? supplier : mate).code}</b>{" "}
            ชื่อ <b>{[supplier.name, mate.name].sort((a, b) => b.length - a.length)[0]}</b> —
            งาน {(supplier.jobs + mate.jobs).toLocaleString()} · เส้นทางราคา {(supplier.lanes + mate.lanes).toLocaleString()} ·
            ชื่อย่อ {[...new Set([...supplier.aliases, ...mate.aliases])].length}
            <button onClick={() => setKeepThis((held) => !held)}
              style={css("margin-left:8px;height:23px;padding:0 9px;border:1px solid #C9A96A;background:#fff;color:#8A6A3B;border-radius:4px;font-size:11px;cursor:pointer;font-family:inherit")}>
              ใช้รหัส {(keepThis ? mate : supplier).code} แทน
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function Label({ children }: { children: React.ReactNode }) {
  return <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{children}</div>;
}

function Button({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:29px;padding:0 13px;border:1px solid ${tone};background:${busy ? "#C3CFDB" : tone};color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}

function Tile({ label, value, note, colour }: { label: string; value: number; note: string; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
      <div style={css("font-size:12px;color:#7B8CA0")}>{note}</div>
    </div>
  );
}
