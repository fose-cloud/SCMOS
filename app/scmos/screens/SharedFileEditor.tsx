"use client";

import { useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { cropPixels, imageExport, pdfExport, pdfInfo, renderImage, type Crop, type PdfNote } from "../sharedEditorLogic";
import { stamp } from "./WorkflowPanel";
import { ZoomBox } from "../TableFrame";
import { SharedPdfPreview } from "./SharedPdfPreview";

type Cell = { address: string; value: string; kind: string; editable: boolean };
type Sheet = { name: string; cells: Cell[]; truncated: boolean; problem: string };
type Info = {
  id: number; name: string; kind: "excel" | "pdf" | "image"; revision: number; version: string; canEdit: boolean; maxBytes: number;
  workbook: { sheets: Sheet[]; problem: string } | null;
  history: { revision: number; name: string; savedBy: string; savedAt: string }[];
};
type Patch = { sheet: string; address: string; kind: string; value: string };
const button = { border: "1px solid #BCCDDD", borderRadius: 5, padding: "7px 12px", background: "#fff", color: "#0A2240", cursor: "pointer" };
const input = { border: "1px solid #BCCDDD", borderRadius: 4, padding: 6, color: "#0A2240", background: "#fff" };
const fullCrop: Crop = { x: 0, y: 0, width: 100, height: 100 };
const initialNote: PdfNote = { page: 1, text: "", x: 5, y: 5, size: 12 };

/** All document bytes stay in the authenticated same-origin app. No Office/Google document URL or public SAS. */
export function SharedFileEditor({ id, onClose, onSaved }: { id: number; onClose: () => void; onSaved: () => void }) {
  const [info, setInfo] = useState<Info | null>(null);
  const [blob, setBlob] = useState<Blob | null>(null);
  const [url, setUrl] = useState("");
  const [failure, setFailure] = useState("");
  const [readOnly, setReadOnly] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [sheetIndex, setSheetIndex] = useState(0);
  const [patches, setPatches] = useState<Record<string, Patch>>({});
  const [turns, setTurns] = useState(0);
  const [crop, setCrop] = useState<Crop>(fullCrop);
  const [note, setNote] = useState<PdfNote>(initialNote);
  const [pages, setPages] = useState(0);
  const [pdfReady, setPdfReady] = useState(false);
  const [prepared, setPrepared] = useState<Blob | null>(null);
  const [history, setHistory] = useState(false);
  const canvas = useRef<HTMLCanvasElement>(null);
  const dialog = useRef<HTMLDivElement>(null);
  const dirty = Object.keys(patches).length > 0 || turns !== 0 || JSON.stringify(crop) !== JSON.stringify(fullCrop) || note.text.length > 0;
  const canEdit = !!info?.canEdit && !readOnly && !info?.workbook?.problem;

  useEffect(() => {
    const controller = new AbortController();
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog.current?.focus();
    const oldOverflow = document.body.style.overflow; document.body.style.overflow = "hidden";
    void (async () => {
      try {
        const response = await apiFetch(`/api/shared-files/${id}/editor`, { signal: controller.signal });
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || "เปิดไฟล์ไม่ได้");
        if (controller.signal.aborted) return;
        setInfo(data as Info);
        if (data.kind !== "excel") {
          const result = await apiFetch(`/api/shared-files/${id}/content?revision=${data.revision}`, { signal: controller.signal });
          if (!result.ok) throw new Error("อ่านข้อมูลไฟล์ไม่ได้ — ลองเปิดฉบับล่าสุดใหม่");
          const bytes = await result.blob();
          if (bytes.size > data.maxBytes) throw new Error("ไฟล์ใหญ่เกินขีดจำกัด editor");
          if (controller.signal.aborted) return;
          setBlob(new Blob([bytes], { type: data.kind === "pdf" ? "application/pdf" : bytes.type }));
          if (data.kind === "pdf") {
            try {
              const total = await pdfInfo(new Uint8Array(await bytes.arrayBuffer()));
              if (!controller.signal.aborted) setPages(total);
            } catch { if (!controller.signal.aborted) setReadOnly("PDF เข้ารหัส/มีลายเซ็น/XFA หรือโครงสร้างไม่รองรับ — เปิดดูหรือดาวน์โหลดเท่านั้น"); }
          }
        }
      } catch (e) { if (!controller.signal.aborted) setFailure(e instanceof Error ? e.message : "เปิดไฟล์ไม่ได้"); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    })();
    return () => { controller.abort(); document.body.style.overflow = oldOverflow; previousFocus?.focus(); };
  }, [id]);

  useEffect(() => {
    if (!blob) return;
    const objectUrl = URL.createObjectURL(prepared ?? blob);
    let live = true;
    queueMicrotask(() => { if (live) setUrl(objectUrl); });
    return () => { live = false; URL.revokeObjectURL(objectUrl); };
  }, [blob, prepared]);

  useEffect(() => {
    let stale = false;
    if (info?.kind !== "image" || !blob) return;
    void renderImage(blob, turns, crop).then(rendered => {
      if (stale || !canvas.current) return;
      const target = canvas.current; target.width = rendered.width; target.height = rendered.height;
      target.getContext("2d")?.drawImage(rendered, 0, 0);
    }).catch(e => { if (!stale) setFailure(e instanceof Error ? e.message : "เปิดรูปไม่ได้"); });
    return () => { stale = true; };
  }, [info?.kind, blob, turns, crop]);

  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => { if (dirty || busyRef.current) event.preventDefault(); };
    window.addEventListener("beforeunload", unload);
    return () => window.removeEventListener("beforeunload", unload);
  }, [dirty]);

  function close() {
    if (busyRef.current) return;
    if (!dirty || window.confirm("มีข้อมูลที่ยังไม่บันทึก — ปิดโดยไม่บันทึก?")) onClose();
  }
  async function preview() {
    if (!info || !blob || busyRef.current) return;
    busyRef.current = true; setBusy(true); setFailure("");
    try { setPrepared(new Blob([new Uint8Array(await pdfExport(new Uint8Array(await blob.arrayBuffer()), note))], { type: "application/pdf" })); }
    catch (e) { setFailure(e instanceof Error ? e.message : "สร้างตัวอย่างไม่ได้"); }
    finally { busyRef.current = false; setBusy(false); }
  }
  async function save() {
    if (!info || busyRef.current || !dirty || !canEdit) return;
    busyRef.current = true; setBusy(true); setFailure("");
    try {
      let response: Response;
      if (info.kind === "excel") {
        response = await apiFetch(`/api/shared-files/${id}/cells`, { method: "POST", headers: { "content-type": "application/json" },
          body: JSON.stringify({ version: info.version, edits: Object.values(patches) }) });
      } else {
        const edited = info.kind === "image" && blob ? await imageExport(blob, info.name, turns, crop) : prepared;
        if (!edited) throw new Error("กด ดูตัวอย่างข้อความ ก่อนบันทึก");
        if (edited.size > info.maxBytes) throw new Error("ไฟล์หลังแก้ไขใหญ่เกิน 32 MB");
        const form = new FormData(); form.append("version", info.version); form.append("file", edited, info.name);
        response = await apiFetch(`/api/shared-files/${id}/edit`, { method: "POST", body: form });
      }
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || "บันทึกไม่สำเร็จ");
      onSaved(); onClose();
    } catch (e) { setFailure(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ"); }
    finally { busyRef.current = false; setBusy(false); }
  }

  const sheet = info?.workbook?.sheets[sheetIndex];
  return <div style={{ position: "fixed", inset: 0, zIndex: 10000, background: "rgba(5,22,40,.65)", padding: "3vh 2vw", display: "flex" }}>
    {/* Dialog-level keyboard handling traps focus and supports Escape; not a clickable non-interactive element. */}
    {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
    <div ref={dialog} role="dialog" aria-modal="true" aria-label="เปิดและแก้ไขไฟล์ออนไลน์" tabIndex={-1}
      onKeyDown={event => {
        if (event.key === "Escape") { event.stopPropagation(); close(); }
        if (event.key === "Tab") {
          const targets = Array.from(dialog.current?.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled]), textarea:not([disabled]), select:not([disabled]), iframe') ?? []);
          const first = targets[0], last = targets.at(-1);
          if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog.current)) { event.preventDefault(); last?.focus(); }
          else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
      }}
      style={{ background: "#fff", borderRadius: 10, width: "100%", display: "flex", flexDirection: "column", color: "#0A2240", minHeight: 0, boxShadow: "0 20px 80px #0005" }}>
      <header style={{ padding: 16, borderBottom: "1px solid #DEE7EF", display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
        <strong style={{ flex: 1, overflowWrap: "anywhere" }}>{info?.name ?? "เปิดออนไลน์"} {info && <small>· ฉบับ {info.revision}</small>}</strong>
        {info && <button style={button} disabled={busy} onClick={() => setHistory(!history)}>ประวัติฉบับ ({info.history.length})</button>}
        {info && <a style={button} href={`/api/shared-files/${id}/content?revision=${info.revision}`}>ดาวน์โหลด</a>}
        <button style={button} disabled={busy} onClick={close}>ปิด ✕</button>
      </header>
      <div style={{ padding: 16, overflow: "auto", flex: 1, minHeight: 0 }}>
        {loading && <p role="status">กำลังเปิดไฟล์…</p>}
        {failure && <p role="alert" style={{ color: "#B42318", background: "#FFF0EE", padding: 10 }}>{failure}</p>}
        {(readOnly || (info && !info.canEdit)) && <p style={{ color: "#8A5A12" }}>{readOnly || "อ่านอย่างเดียว — บัญชีนี้ไม่มีสิทธิ์อัปโหลดเอกสาร"}</p>}
        {history && <section aria-label="ประวัติฉบับ" style={{ padding: 12, border: "1px solid #DEE7EF", marginBottom: 12 }}>
          <p>ฉบับปัจจุบัน: {info?.revision} · ฉบับก่อนแก้ไม่ถูกลบ (ดาวน์โหลดแล้วอัปโหลดเป็นไฟล์ใหม่ได้)</p>
          {info?.history.map(old => <p key={old.revision}>
            <a href={`/api/shared-files/${id}/content?revision=${old.revision}`}>ดาวน์โหลดฉบับ {old.revision}</a> · {old.savedBy} · {stamp(old.savedAt)}
          </p>)}
        </section>}
        {info?.kind === "excel" && <>
          <p style={{ fontSize: 13 }}>แก้ค่าของเซลล์เดิมเท่านั้น · สูตร/เซลล์รวมอ่านอย่างเดียว · รูปแบบเดิมยังอยู่ · วันที่เป็นค่าตัวเลข Excel เดิม</p>
          <p style={{ fontSize: 13, color: "#8A5A12" }}>ไม่มีการคำนวณสูตรในหน้านี้ หลังบันทึกต้องเปิดใน Excel เพื่อคำนวณสูตรใหม่ก่อนใช้ยอดราคา</p>
          {info.workbook?.problem && <p role="status">{info.workbook.problem}</p>}
          <label>แผ่นงาน <select style={input} disabled={busy} value={sheetIndex} onChange={e => setSheetIndex(Number(e.target.value))}>
            {info.workbook?.sheets.map((s, i) => <option key={s.name} value={i}>{s.name}</option>)}
          </select></label>
          {sheet?.problem && <p>{sheet.problem}</p>}
          {sheet?.truncated && <p>แสดงเฉพาะ 200 แถวแรก/60 คอลัมน์แรก ส่วนที่เหลือยังเก็บไว้ในไฟล์เดิม</p>}
          <ZoomBox capped={false} zoomable={false}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13, marginTop: 12 }}>
            <thead><tr>{["เซลล์", "ค่า / สูตร (ไม่มีการคำนวณใหม่)", "ชนิด"].map(label => <th key={label} style={{ textAlign: "left", padding: 8, background: "#EEF4FA" }}>{label}</th>)}</tr></thead>
            <tbody>{sheet?.cells.map(cell => {
              const key = sheet.name + ":" + cell.address, patch = patches[key];
              const editable = canEdit && cell.editable && !busy;
              return <tr key={key} style={{ borderBottom: "1px solid #E6EEF5" }}>
                <td style={{ width: 70, padding: 6 }}>{cell.address}</td>
                <td style={{ padding: 6 }}>{editable ? <input aria-label={`${sheet.name}!${cell.address}`} style={{ ...input, width: "100%", background: patch ? "#FFF9E8" : "#fff" }}
                  maxLength={10000} value={patch?.value ?? cell.value} onChange={e => {
                    const value = e.target.value;
                    setPatches(previous => { const next = { ...previous };
                      if (value === cell.value && (!patch || patch.kind === cell.kind)) delete next[key];
                      else next[key] = { sheet: sheet.name, address: cell.address, kind: patch?.kind ?? cell.kind, value };
                      return next; });
                  }} /> : <span style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{patch?.value ?? cell.value}</span>}</td>
                <td style={{ padding: 6, width: 150 }}>{editable ? <select aria-label={`ชนิด ${cell.address}`} style={input} value={patch?.kind ?? cell.kind} onChange={e => {
                  const kind = e.target.value;
                  setPatches(previous => ({ ...previous, [key]: { sheet: sheet.name, address: cell.address, kind, value: patch?.value ?? cell.value } }));
                }}><option value="text">ข้อความ</option><option value="number">ตัวเลข</option><option value="boolean">Boolean (0/1)</option></select> : cell.kind === "formula" ? "สูตร · อ่านอย่างเดียว" : "อ่านอย่างเดียว"}</td>
              </tr>;
            })}</tbody>
          </table>
          </ZoomBox>
          {!sheet?.cells.length && <p>ไม่มีเซลล์ในพื้นที่ preview</p>}
        </>}
        {info?.kind === "pdf" && <>
          <p style={{ fontSize: 13 }}>เพิ่มข้อความไทย/อังกฤษบนหน้าเดิมเท่านั้น ไม่แก้ข้อความเดิมหรือ OCR · ตรวจตัวอย่างก่อนบันทึก</p>
          <label style={{ display: "block", marginBottom: 12 }}>หน้า (ทั้งหมด {pages || "…"} หน้า)
            <input type="number" aria-label="หน้า PDF" style={{ ...input, marginLeft: 8, width: 90 }} disabled={busy}
              min={1} max={pages || 1} value={note.page} onChange={e => { setNote({ ...note, page: Number(e.target.value) }); setPrepared(null); }} />
          </label>
          {canEdit && !loading && <div style={{ display: "flex", flexWrap: "wrap", gap: 12, alignItems: "start", marginBottom: 12 }}>
            <label>ข้อความ<textarea aria-label="ข้อความ PDF" style={{ ...input, display: "block" }} rows={2} maxLength={1000} disabled={busy} value={note.text}
              onChange={e => { setNote({ ...note, text: e.target.value }); setPrepared(null); }} /></label>
            {([['x', 'จากซ้าย (%)', 0, 100], ['y', 'จากบน (%)', 0, 100], ['size', 'ขนาดตัวอักษร', 8, 48]] as const).map(([key, label, min, max]) =>
              <label key={key}>{label}<input type="number" aria-label={label} style={{ ...input, display: "block", width: 90 }} disabled={busy} min={min} max={max} value={note[key]}
                onChange={e => { setNote({ ...note, [key]: Number(e.target.value) }); setPrepared(null); }} /></label>)}
            <button style={button} disabled={busy || !note.text.trim()} onClick={() => void preview()}>ดูตัวอย่างข้อความ</button>
          </div>}
          {blob && <SharedPdfPreview blob={prepared ?? blob} page={note.page} onPages={setPages} onReady={setPdfReady} />}
          {url && <a href={url} target="_blank" rel="noreferrer">เปิด PDF ในแท็บใหม่</a>}
        </>}
        {info?.kind === "image" && <>
          <p style={{ fontSize: 13 }}>หมุน/ครอปแล้วบันทึกเป็นชนิดเดิม · JPG/WebP เข้ารหัสใหม่ (อาจเสียรายละเอียด) · Metadata/EXIF ไม่เก็บในฉบับแก้ แต่ต้นฉบับยังอยู่</p>
          {canEdit && <div style={{ display: "flex", flexWrap: "wrap", gap: 12, alignItems: "center", marginBottom: 12 }}>
            <button style={button} disabled={busy} onClick={() => { setTurns((turns + 1) % 4); setCrop(fullCrop); }}>หมุน 90°</button>
            {([['x', 'ซ้าย (%)'], ['y', 'บน (%)'], ['width', 'กว้าง (%)'], ['height', 'สูง (%)']] as const).map(([key, label]) =>
              <label key={key}>{label}<input type="number" aria-label={label} style={{ ...input, display: "block", width: 90 }} disabled={busy} min={0} max={100} value={crop[key]}
                onChange={e => { setCrop({ ...crop, [key]: Number(e.target.value) }); setFailure(""); }} /></label>)}
            <button style={button} disabled={busy} onClick={() => { setTurns(0); setCrop(fullCrop); setFailure(""); }}>คืนค่ารูป</button>
          </div>}
          <canvas ref={canvas} aria-label="ตัวอย่างรูปหลังหมุนและครอป" style={{ maxWidth: "100%", maxHeight: "55vh", objectFit: "contain", border: "1px solid #DEE7EF" }} />
        </>}
      </div>
      <footer style={{ padding: 16, borderTop: "1px solid #DEE7EF", display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap" }}>
        <small style={{ flex: 1 }}>ทุกครั้งที่บันทึกเก็บฉบับก่อนแก้ไว้ · ไฟล์ไม่ถูกส่งไปบริการภายนอก</small>
        <button style={button} disabled={busy} onClick={close}>ปิด</button>
        {canEdit && <button style={{ ...button, background: "#0B609F", color: "#fff" }}
          disabled={busy || loading || !dirty || (info?.kind === "pdf" && (!prepared || !pdfReady)) || Object.keys(patches).length > 500}
          onClick={() => {
            try { if (info?.kind === "image") cropPixels(crop, 100, 100); void save(); }
            catch (e) { setFailure(e instanceof Error ? e.message : "ครอปไม่ถูกต้อง"); }
          }}>{busy ? "กำลังทำงาน…" : "บันทึกฉบับใหม่"}</button>}
      </footer>
    </div>
  </div>;
}
