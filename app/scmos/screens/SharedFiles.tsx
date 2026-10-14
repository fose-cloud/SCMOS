"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import { stamp } from "./WorkflowPanel";
import { SharedFileEditor } from "./SharedFileEditor";

/**
 * The shared file space (14 Oct 2026, its own menu): folders made and filled by the department, with a trash a
 * supervisor restores from. Every rule — who may do what, the 32 MB ceiling, which files are taken — is the API's
 * (Rules/SharedFiles.cs, Services/SharedFilesService.cs); this screen draws what the API answers, including the limits,
 * so there is no second copy of them here to drift.
 */

type FolderRow = { id: number; name: string; createdBy: string; createdAt: string; items: number; canChange: boolean };
type FileRow = { id: number; name: string; sizeBytes: number; uploadedBy: string; uploadedAt: string; canChange: boolean; viewable: boolean; online: boolean; revision: number; editedBy?: string; editedAt?: string | null };
type Listing = {
  folder: { id: number; name: string } | null;
  path: { id: number; name: string }[];
  folders: FolderRow[];
  files: FileRow[];
  canUpload: boolean;
  canCreateFolder: boolean;
  canRestore: boolean;
  storageReady: boolean;
  limits: { maxBytes: number; maxFolderName: number; maxFileName: number; extensions: string[] };
};
type TrashItem = { kind: "folder" | "file"; id: number; name: string; location: string; sizeBytes: number | null; deletedAt: string; deletedBy: string };
type Trash = { items: TrashItem[]; canRestore: boolean };
type Naming = { kind: "new" } | { kind: "folder" | "file"; id: number };

const BASE = "/api/shared-files";

export function SharedFiles({ onToast }: { onToast: (message: string) => void }) {
  const [folderId, setFolderId] = useState<number | null>(null);
  const [view, setView] = useState<"files" | "trash">("files");
  const [listing, setListing] = useState<Listing | null>(null);
  const [trash, setTrash] = useState<Trash | null>(null);
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  const [naming, setNaming] = useState<Naming | null>(null);
  const [draft, setDraft] = useState("");
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null);
  const [dragging, setDragging] = useState(false);
  const [editorId, setEditorId] = useState<number | null>(null);
  const picker = useRef<HTMLInputElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);
  const requestId = useRef(0);
  const busyRef = useRef(false);

  const load = useCallback(async () => {
    const ticket = ++requestId.current;
    try {
      const response = await apiFetch(view === "trash" ? `${BASE}/trash` : folderId ? `${BASE}?folder=${folderId}` : BASE,
        { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => ({})) as { error?: string };
      if (ticket !== requestId.current) return;
      if (!response.ok) {
        // A folder that went to the trash while it was open: back to the top rather than a dead end.
        if (response.status === 404 && view === "files" && folderId) { setFolderId(null); return; }
        setFailure(body.error || `อ่านไม่สำเร็จ (${response.status})`);
        return;
      }
      setFailure("");
      if (view === "trash") setTrash(body as Trash); else setListing(body as Listing);
    } catch (problem) {
      if (ticket !== requestId.current) return;
      setFailure("ติดต่อ API ไม่ได้: " + (problem instanceof Error ? problem.message : String(problem)));
    }
  }, [folderId, view]);

  // Fetching on mount and on every move. Every setState inside is after an await — the same idiom, and the same
  // note, as Administration's.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
    return () => {
      // Invalidate the latest request generation; this numeric ref is not a DOM node.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      requestId.current++;
    };
  }, [load]);
  // The name field takes the cursor when it opens, without the autoFocus attribute the accessibility rule refuses.
  useEffect(() => { nameInput.current?.focus(); }, [naming]);

  /** One JSON change: its answer is the toast, then the page is read again. */
  async function send(path: string, body?: unknown): Promise<boolean> {
    if (busyRef.current) return false;
    busyRef.current = true;
    setBusy(true);
    try {
      const response = await apiFetch(path, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body ?? {}),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
      return response.ok;
    } catch {
      onToast("ติดต่อ API ไม่ได้ — ยังบันทึกไม่สำเร็จ กรุณาลองใหม่");
      return false;
    } finally { busyRef.current = false; setBusy(false); }
  }

  /** Uploads one at a time, into the folder that is open; the API judges each, and the toast sums them up. */
  async function upload(chosen: File[]) {
    if (!listing?.canUpload || !listing.storageReady || busyRef.current || chosen.length === 0) return;
    busyRef.current = true;
    setBusy(true);
    let saved = 0;
    const refused: string[] = [];
    try {
      for (const [index, file] of chosen.entries()) {
        setProgress({ done: index, total: chosen.length });
        const extension = file.name.includes(".") ? file.name.slice(file.name.lastIndexOf(".")).toLowerCase() : "";
        if (file.size > listing.limits.maxBytes) { refused.push(`${file.name}: ใหญ่เกิน ${megabytes(listing.limits.maxBytes)}`); continue; }
        if (!listing.limits.extensions.includes(extension)) { refused.push(`${file.name}: ไม่รับไฟล์ชนิดนี้`); continue; }
        const form = new FormData();
        form.append("file", file);
        if (folderId) form.append("folderId", String(folderId));
        const response = await apiFetch(`${BASE}/upload`, { method: "POST", body: form }).catch(() => null);
        const reply = response ? await response.json().catch(() => ({})) as { error?: string } : { error: "ติดต่อ API ไม่ได้" };
        if (response?.ok) saved++; else refused.push(`${file.name}: ${reply.error ?? `ไม่สำเร็จ (${response?.status})`}`);
      }
    } finally {
      setProgress(null);
      busyRef.current = false;
      setBusy(false);
    }
    onToast(refused.length === 0 ? `อัปโหลดแล้ว ${saved} ไฟล์`
      : `อัปโหลดแล้ว ${saved} ไฟล์ · ไม่สำเร็จ ${refused.length} — ${refused[0]}${refused.length > 1 ? " …" : ""}`);
    await load();
  }

  function begin(next: Naming, value: string) { if (!busyRef.current) { setNaming(next); setDraft(value); } }

  async function commitName() {
    if (!naming) return;
    const name = draft.trim();
    const ok = naming.kind === "new"
      ? await send(`${BASE}/folders`, { name, parentId: folderId })
      : await send(`${BASE}/${naming.kind === "folder" ? "folders/" : ""}${naming.id}/rename`, { name });
    if (ok) setNaming(null);
  }

  function open(id: number | null) {
    if (busyRef.current || (view === "files" && id === folderId)) return;
    requestId.current++;
    setFailure(""); setListing(null); setNaming(null); setFolderId(id); setView("files");
  }

  function toggleTrash() {
    if (busyRef.current) return;
    requestId.current++;
    setFailure(""); setListing(null); setTrash(null); setNaming(null);
    setView(view === "trash" ? "files" : "trash");
  }

  const nameField = (
    <span style={css("display:inline-flex;gap:6px;align-items:center")}>
      <input ref={nameInput} value={draft}
        maxLength={naming?.kind === "file" ? listing?.limits.maxFileName : listing?.limits.maxFolderName}
        aria-label="ชื่อ" onChange={(event) => setDraft(event.target.value)}
        onKeyDown={(event) => { if (event.key === "Enter") void commitName(); if (event.key === "Escape") setNaming(null); }}
        style={css("height:28px;min-width:220px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12.5px")} />
      <button onClick={() => void commitName()} disabled={busy || !draft.trim()} style={css(BUTTON + ";border-color:#16794C;color:#16794C")}>บันทึก</button>
      <button onClick={() => setNaming(null)} disabled={busy} style={css(BUTTON)}>ยกเลิก</button>
    </span>
  );

  const crumbs = listing?.path ?? [];

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
        <nav aria-label="ตำแหน่งโฟลเดอร์" style={css("display:flex;align-items:center;gap:4px;flex-wrap:wrap;font-size:13px;min-width:0")}>
          {view === "trash" ? <strong style={css("color:#0A2240")}>ถังขยะ</strong> : <>
            <button onClick={() => open(null)} style={css(CRUMB + (crumbs.length ? "" : ";color:#0A2240;font-weight:700"))}>ไฟล์ทั้งหมด</button>
            {crumbs.map((crumb, index) => <span key={crumb.id} style={css("display:inline-flex;align-items:center;gap:4px")}>
              <span style={css("color:#94A3B8")}>/</span>
              <button onClick={() => open(crumb.id)}
                style={css(CRUMB + (index === crumbs.length - 1 ? ";color:#0A2240;font-weight:700" : ""))}>{crumb.name}</button>
            </span>)}
          </>}
        </nav>
        <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
          {view === "files" && listing?.canCreateFolder && (
            <button onClick={() => begin({ kind: "new" }, "")} disabled={busy} style={css(BUTTON)}>+ โฟลเดอร์ใหม่</button>
          )}
          {view === "files" && listing?.canUpload && (
            <button onClick={() => picker.current?.click()} disabled={busy || !listing.storageReady}
              title={`ไม่เกิน ${megabytes(listing.limits.maxBytes)} · ${listing.limits.extensions.join(" ")}`}
              style={css(BUTTON + ";border-color:#0A2240;background:#0A2240;color:#fff")}>อัปโหลดไฟล์</button>
          )}
          <button onClick={toggleTrash} disabled={busy} style={css(BUTTON)}>
            {view === "trash" ? "กลับไปที่ไฟล์" : "ถังขยะ"}
          </button>
          <input ref={picker} type="file" multiple hidden aria-label="เลือกไฟล์อัปโหลด"
            accept={listing?.limits.extensions.join(",")}
            onChange={(event) => { const chosen = Array.from(event.target.files ?? []); event.target.value = ""; void upload(chosen); }} />
        </div>
      </div>

      {failure && <div role="alert" style={css(NOTICE + ";border-color:#F3C9C4;color:#B42318")}>
        {failure} <button onClick={() => void load()} style={css(LINK)}>ลองใหม่</button>
      </div>}
      {view === "files" && listing && !listing.storageReady && (
        <div role="alert" style={css(NOTICE + ";border-color:#F0D8B8;color:#8A5A12")}>ยังไม่ได้ตั้งค่าที่เก็บไฟล์ — อัปโหลดและเปิดไฟล์ไม่ได้</div>
      )}
      {progress && <div role="status" style={css("font-size:12px;color:#1D4E80")}>กำลังอัปโหลด {progress.done + 1} / {progress.total}…</div>}

      {view === "files" ? (
        <div
          onDragOver={(event) => { event.preventDefault(); if (listing?.canUpload && listing.storageReady && !busy) setDragging(true); }}
          onDragLeave={() => setDragging(false)}
          onDrop={(event) => {
            event.preventDefault();
            setDragging(false);
            void upload(Array.from(event.dataTransfer.files));
          }}
          data-testid="shared-files-drop"
          style={css(CARD + (dragging ? ";border-color:#1D5FA8;box-shadow:0 0 0 3px rgba(29,95,168,.15)" : ""))}>
          {!listing ? <div style={css(EMPTY)}>กำลังอ่าน…</div> : (
            <ZoomBox capped={false} zoomable={false}>
              <table style={css("width:100%;min-width:720px;border-collapse:collapse;font-size:12.5px")}>
                <thead><tr>{["ชื่อ", "ขนาด", "โดย", "เมื่อ", ""].map((head, index) => (
                  <th key={index} style={css(HEAD + (index === 1 ? ";text-align:right" : ""))}>{head}</th>
                ))}</tr></thead>
                <tbody>
                  {naming?.kind === "new" && <tr style={css(ROW)}><td colSpan={5} style={css(CELL)}>
                    <span style={css("display:inline-flex;gap:8px;align-items:center")}><FolderIcon />{nameField}</span>
                  </td></tr>}
                  {listing.folders.map((folder) => (
                    <tr key={"d" + folder.id} style={css(ROW)}>
                      <td style={css(CELL)}>
                        <span style={css("display:inline-flex;gap:8px;align-items:center")}>
                          <FolderIcon />
                          {naming?.kind === "folder" && naming.id === folder.id ? nameField
                            : <button onClick={() => open(folder.id)} style={css(NAME)}>{folder.name}</button>}
                        </span>
                      </td>
                      <td style={css(CELL + ";text-align:right;color:#7B8CA0;white-space:nowrap")}>{folder.items.toLocaleString()} รายการ</td>
                      <td style={css(CELL + ";color:#5A6B7D")}>{folder.createdBy}</td>
                      <td style={css(CELL + ";color:#7B8CA0;white-space:nowrap")}>{stamp(folder.createdAt)}</td>
                      <td style={css(CELL + ";text-align:right;white-space:nowrap")}>
                        {folder.canChange && <span style={css("display:inline-flex;gap:5px")}>
                          <button onClick={() => begin({ kind: "folder", id: folder.id }, folder.name)} disabled={busy} style={css(BUTTON)}>เปลี่ยนชื่อ</button>
                          <button disabled={busy} style={css(BUTTON + ";color:#B42318")} onClick={() => {
                            if (window.confirm(`ย้ายโฟลเดอร์ "${folder.name}" และทุกอย่างข้างในลงถังขยะ?`))
                              void send(`${BASE}/folders/${folder.id}/trash`);
                          }}>ลบ</button>
                        </span>}
                      </td>
                    </tr>
                  ))}
                  {listing.files.map((file) => (
                    <tr key={"f" + file.id} style={css(ROW)}>
                      <td style={css(CELL)}>
                        <span style={css("display:inline-flex;gap:8px;align-items:center")}>
                          <FileIcon />
                          {naming?.kind === "file" && naming.id === file.id ? nameField
                            : file.online ? <button disabled={busy} onClick={() => setEditorId(file.id)} style={css(NAME)}>{file.name}</button>
                              : <a href={`${BASE}/${file.id}/content`} style={css(NAME + ";text-decoration:none")}>{file.name}</a>}
                          {file.revision > 1 && <small style={css("color:#7B8CA0")}>ฉบับ {file.revision}</small>}
                        </span>
                      </td>
                      <td style={css(CELL + ";text-align:right;color:#5A6B7D;white-space:nowrap;font-family:ui-monospace,monospace")}>{size(file.sizeBytes)}</td>
                      <td style={css(CELL + ";color:#5A6B7D")} title={file.editedAt ? "แก้ไขล่าสุดโดย" : "อัปโหลดโดย"}>{file.editedBy || file.uploadedBy}</td>
                      <td style={css(CELL + ";color:#7B8CA0;white-space:nowrap")}>{stamp(file.editedAt || file.uploadedAt)}</td>
                      <td style={css(CELL + ";text-align:right;white-space:nowrap")}>
                        <span style={css("display:inline-flex;gap:5px")}>
                          {file.online ? <button disabled={busy} style={css(BUTTON)} onClick={() => setEditorId(file.id)}>เปิดออนไลน์</button>
                            : file.viewable && <a href={`${BASE}/${file.id}/content?inline=1`} target="_blank" rel="noreferrer" style={css(BUTTON + ";text-decoration:none;display:inline-flex;align-items:center")}>เปิดดู</a>}
                          <a href={`${BASE}/${file.id}/content`} style={css(BUTTON + ";text-decoration:none;display:inline-flex;align-items:center")}>ดาวน์โหลด</a>
                          {file.canChange && <>
                            <button onClick={() => begin({ kind: "file", id: file.id }, file.name)} disabled={busy} style={css(BUTTON)}>เปลี่ยนชื่อ</button>
                            <button disabled={busy} style={css(BUTTON + ";color:#B42318")} onClick={() => {
                              if (window.confirm(`ย้าย "${file.name}" ลงถังขยะ?`)) void send(`${BASE}/${file.id}/trash`);
                            }}>ลบ</button>
                          </>}
                        </span>
                      </td>
                    </tr>
                  ))}
                  {!listing.folders.length && !listing.files.length && naming?.kind !== "new" && (
                    <tr><td colSpan={5} style={css(EMPTY)}>ยังไม่มีไฟล์ในโฟลเดอร์นี้</td></tr>
                  )}
                </tbody>
              </table>
            </ZoomBox>
          )}
        </div>
      ) : (
        <div style={css(CARD)}>
          {!trash ? <div style={css(EMPTY)}>กำลังอ่าน…</div> : (
            <ZoomBox capped={false} zoomable={false}>
              <table style={css("width:100%;min-width:720px;border-collapse:collapse;font-size:12.5px")}>
                <thead><tr>{["ชื่อ", "อยู่ใน", "ลบโดย", "เมื่อ", ""].map((head, index) => (
                  <th key={index} style={css(HEAD)}>{head}</th>
                ))}</tr></thead>
                <tbody>
                  {trash.items.map((item) => (
                    <tr key={item.kind + item.id} style={css(ROW)}>
                      <td style={css(CELL)}>
                        <span style={css("display:inline-flex;gap:8px;align-items:center;color:#5A6B7D")}>
                          {item.kind === "folder" ? <FolderIcon /> : <FileIcon />}{item.name}
                          {item.sizeBytes !== null && <span style={css("color:#94A3B8;font-size:11px")}>{size(item.sizeBytes)}</span>}
                        </span>
                      </td>
                      <td style={css(CELL + ";color:#7B8CA0")}>{item.location || "ไฟล์ทั้งหมด"}</td>
                      <td style={css(CELL + ";color:#5A6B7D")}>{item.deletedBy}</td>
                      <td style={css(CELL + ";color:#7B8CA0;white-space:nowrap")}>{stamp(item.deletedAt)}</td>
                      <td style={css(CELL + ";text-align:right")}>
                        {trash.canRestore && <button disabled={busy} style={css(BUTTON + ";border-color:#16794C;color:#16794C")}
                          onClick={() => void send(`${BASE}/trash/restore`, { kind: item.kind, id: item.id })}>กู้คืน</button>}
                      </td>
                    </tr>
                  ))}
                  {!trash.items.length && <tr><td colSpan={5} style={css(EMPTY)}>ถังขยะว่าง</td></tr>}
                </tbody>
              </table>
            </ZoomBox>
          )}
        </div>
      )}
      {editorId !== null && <SharedFileEditor key={editorId} id={editorId} onClose={() => setEditorId(null)}
        onSaved={() => { onToast("บันทึกฉบับใหม่และเก็บฉบับเดิมแล้ว"); void load(); }} />}
    </div>
  );
}

function size(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function megabytes(bytes: number): string {
  return `${Math.round(bytes / 1024 / 1024)} MB`;
}

function FolderIcon() {
  return <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" style={css("flex:none")}>
    <path d="M1.5 4a1 1 0 0 1 1-1h3.6l1.5 1.6h5.9a1 1 0 0 1 1 1V12a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1z" fill="#F2C14E" stroke="#C99A2E" strokeWidth=".8" />
  </svg>;
}

function FileIcon() {
  return <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" style={css("flex:none")}>
    <path d="M3.5 1.5h6l3 3v10h-9z" fill="#fff" stroke="#7B8CA0" strokeWidth=".9" /><path d="M9.5 1.5v3h3" fill="none" stroke="#7B8CA0" strokeWidth=".9" />
  </svg>;
}

const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden";
const HEAD = "background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap";
const ROW = "border-bottom:1px solid #F1F5F9";
const CELL = "padding:8px 12px;vertical-align:middle";
const EMPTY = "padding:28px;text-align:center;color:#94A3B8;font-size:12.5px";
const NOTICE = "background:#fff;border:1px solid;border-radius:5px;padding:10px 13px;font-size:12px";
const BUTTON = "height:28px;padding:0 11px;border:1px solid #C3CFDB;background:#fff;color:#0A2240;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit";
const CRUMB = "border:none;background:none;padding:2px 4px;font-family:inherit;font-size:13px;color:#1D5FA8;cursor:pointer";
const NAME = "border:none;background:none;padding:0;font-family:inherit;font-size:12.5px;font-weight:600;color:#0A5FA8;cursor:pointer;text-align:left";
const LINK = "border:0;background:transparent;color:#1E5B8F;text-decoration:underline;cursor:pointer;font-family:inherit";
