"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

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
  async function upload(supplierId: number, file: File, folder: string, expiryDate: string) {
    if (busy) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append("supplierId", String(supplierId));
      body.append("folder", folder);
      body.append("kind", folder);
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
  async function merge(group: Duplicate, fold: Duplicate["fold"][number]) {
    const moving = fold.aliases + fold.attached;
    const warning = moving > 0
      ? `\n\nจะย้ายข้อมูล ${moving} รายการมาที่ ${group.keep.code} ก่อนลบ`
        + (fold.jobs > 0 ? ` · งาน ${fold.jobs.toLocaleString()} รายการจะตามชื่อย่อไปด้วย` : "")
      : "\n\nรายการที่ถูกลบไม่มีข้อมูลผูกอยู่เลย";
    if (!window.confirm(`รวม ${fold.code} (${fold.name}) เข้ากับ ${group.keep.code} หรือไม่?${warning}`)) return;

    setBusy(true);
    try {
      const response = await apiFetch("/api/suppliers/merge", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ keepId: group.keep.id, foldId: fold.id, reason: "รวมรายการซ้ำในทะเบียน" }),
      });
      const reply = await response.json().catch(() => null) as { message?: string } | null;
      onToast(reply?.message ?? (response.ok ? "รวมรายการแล้ว" : `รวมไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
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
                        <button onClick={() => void merge(group, fold)} disabled={busy}
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
              <button onClick={() => setShowDirectory((open) => !open)}
                style={css("height:30px;padding:0 12px;border:1px solid #0A2240;background:" + (showDirectory ? "#0A2240" : "#fff")
                  + ";color:" + (showDirectory ? "#fff" : "#0A2240") + ";border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
                นำเข้าทะเบียนผู้ขนส่ง
              </button>
            )}
          </div>
        </div>

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
        <div style={css("overflow-x:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["รหัส", "ชื่อ", "สถานะ", "งาน", "เส้นทางราคา", "คะแนนล่าสุด", "ชื่อที่สะกดต่างกัน"].map((h, i) => (
              <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:" + (i >= 3 && i <= 5 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {shown.map((row) => (
                <tr key={row.id} onClick={() => setPicked(row.id === picked ? null : row.id)}
                  style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (row.id === picked ? "#F2F7FC" : "#fff"))}>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{row.code}</td>
                  <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.name}</td>
                  <td style={CELL_S}>
                    <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${STATUS_TONE[row.status] ?? "#7B8CA0"}`)}>
                      {STATUS_TH[row.status] ?? row.status}
                    </span>
                  </td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.jobs.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;color:" + (row.jobs > 0 && row.lanes === 0 ? "#B45309" : "#7B8CA0"))}>{row.lanes.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.lastScore ?? "—"}</td>
                  <td style={css(CELL + ";font-size:11px;color:#7B8CA0")}>{row.aliases.join(" · ")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {picked !== null && canManage && (
        <Manage supplier={rows.find((r) => r.id === picked)!} busy={busy}
          onEdit={(fields) => void post(`${picked}/edit`, fields)}
          onRemove={() => void remove(rows.find((r) => r.id === picked)!)}
          onAlias={(alias) => void post(`${picked}/alias`, { alias })}
          onStatus={(status) => void post(`${picked}/status`, { status })}
          onEvaluate={(period, safety, documents, note) =>
            void post(`${picked}/evaluate`, { period, safety, documents, note })}
          onUpload={(file, folder, expiryDate) => void upload(picked, file, folder, expiryDate)} />
      )}
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:top";
const CELL_S = css(CELL);

/** The folders a supplier's paperwork goes in, matching BlobPaths.SupplierFolders. */
const SUPPLIER_FOLDERS: [string, string][] = [
  ["Insurance", "ประกันภัย"], ["License", "ใบอนุญาต"], ["Audit", "ผลตรวจประเมิน"],
  ["Training", "อบรม"], ["Contract", "สัญญา"], ["Other", "อื่นๆ"],
];

function Manage({ supplier, busy, onEdit, onRemove, onAlias, onStatus, onEvaluate, onUpload }: {
  supplier: Summary; busy: boolean;
  onEdit: (fields: Edit) => void;
  onRemove: () => void;
  onAlias: (alias: string) => void;
  onStatus: (status: string) => void;
  onEvaluate: (period: string, safety: number | null, documents: number | null, note: string) => void;
  onUpload: (file: File, folder: string, expiryDate: string) => void;
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
      <Details supplier={supplier} busy={busy} onEdit={onEdit} onRemove={onRemove} />
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
          <select value={folder} onChange={(e) => setFolder(e.target.value)}
            style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;background:#fff")}>
            {SUPPLIER_FOLDERS.map(([id, label]) => <option key={id} value={id}>{label}</option>)}
          </select>
          <input value={expiry} onChange={(e) => setExpiry(e.target.value)} placeholder="หมดอายุ DD/MM/YYYY"
            style={css("width:150px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <label style={css(`height:29px;padding:0 13px;border:1px solid #0A2240;background:${busy ? "#C3CFDB" : "#0A2240"};color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center`)}>
            แนบไฟล์
            <input type="file" disabled={busy} style={css("display:none")}
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = "";
                if (file) onUpload(file, folder, expiry);
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
function Details({ supplier, busy, onEdit, onRemove }: {
  supplier: Summary; busy: boolean; onEdit: (fields: Edit) => void; onRemove: () => void;
}) {
  // Seeded from the row and re-seeded when a different supplier is picked, so
  // an edit box never carries one company's text over onto another.
  const [draft, setDraft] = useState<Edit>({});
  const [seeded, setSeeded] = useState(supplier.id);
  if (seeded !== supplier.id) { setSeeded(supplier.id); setDraft({}); }

  const value = <K extends keyof Summary & keyof Edit>(field: K) =>
    (draft[field] ?? supplier[field]) as Summary[K];
  const set = (field: keyof Edit, next: string | boolean) =>
    setDraft((held) => ({ ...held, [field]: next }));

  const dirty = Object.keys(draft).length > 0;
  const attached = supplier.attached;

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
          ลบไม่ได้เพราะยังมีข้อมูลผูกอยู่ — บริษัทที่เคยมีงานจะไม่ถูกลบ แต่ใช้ &ldquo;รวมรายการซ้ำ&rdquo; แทน
        </div>
      )}
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
