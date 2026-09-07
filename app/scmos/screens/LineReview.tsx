"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import {
  confidenceLabel, describe, isActionable, palette, summarise, whenLabel,
} from "../lineReview";

/**
 * LINE — the messages hauliers send, and what SCMOS proposes to do with them.
 *
 * Two halves, in the order somebody meets them. The queue is the work: every
 * message the worker could not finish on its own, with what it understood and
 * what it would do. Underneath is the mapping from a LINE group to a haulier,
 * because until a room is mapped every message from it is refused, and the
 * refusal is not the vendor's fault.
 *
 * <b>Nothing here is applied automatically.</b> A message in a chat room is not
 * an approval, so even a message the rule is certain about waits for somebody to
 * press the button. What that button does is worked out fresh by the API at the
 * moment it is pressed — the verdict on the row is what to look at, never what
 * gets written. See LineReviewEndpoints.
 */

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const CELL = "padding:6px 10px;border-bottom:1px solid #EDF1F5;font-size:12.5px;vertical-align:top";
const HEAD = "padding:7px 10px;background:#F4F7FA;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;text-align:left;white-space:nowrap";
const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px";
const BUTTON = "height:28px;padding:0 12px;border-radius:4px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid";

type Event = {
  id: number;
  receivedAt: string;
  rawText: string;
  jobNumber: string;
  parsedStatus: string;
  confidence: number;
  processingStatus: string;
  errorCode: string;
  errorMessage: string;
  jobKey: string;
  retryCount: number;
  group: string;
};

type Option = {
  key: string; cat: string; customer: string; container: string;
  status: string; workDate: string;
  move: { result: string; detail: string; ok: boolean };
};

type Options = {
  outcome: string; detail: string; canApply: boolean;
  from: string; to: string; options: Option[]; stored: string;
};

type Group = {
  id: number; lineGroupId: string; groupName: string;
  supplierId: number; groupType: string; isActive: boolean; supplier: string;
};

type Supplier = { id: number; name: string; code?: string };

/** The filters, in the order somebody wants them. */
const VIEWS: { value: string; label: string }[] = [
  { value: "NEED_REVIEW", label: "รอตรวจสอบ" },
  { value: "PROCESSED", label: "อัปเดตแล้ว" },
  { value: "IGNORED", label: "ปิดแล้ว" },
  { value: "FAILED", label: "ล้มเหลว" },
  { value: "ALL", label: "ทั้งหมด" },
];

export function LineReview({
  canApprove, canMap, onToast,
}: {
  canApprove: boolean;
  canMap: boolean;
  onToast: (message: string) => void;
}) {
  const [view, setView] = useState("NEED_REVIEW");
  const [events, setEvents] = useState<Event[] | null>(null);
  const [groups, setGroups] = useState<Group[] | null>(null);
  const [open, setOpen] = useState<number | null>(null);
  const [options, setOptions] = useState<Options | null>(null);
  const [busy, setBusy] = useState(false);

  /*
   * Fetch and store are kept apart.
   *
   * Both a first render and the refresh button want the same request, and only
   * the first has to be abandoned when the screen changes underneath it. So
   * these return the rows and let each caller decide whether it still wants
   * them — which is also what keeps setState out of an effect body.
   *
   * Null means the request failed, and is not the same as an empty list: the
   * previous rows are kept, because a blank table and a broken request look
   * identical and only one of them means there is nothing to do.
   */
  const fetchEvents = useCallback(async (): Promise<Event[] | null> => {
    const response = await apiFetch(
      `/api/integrations/line/events?status=${encodeURIComponent(view)}&limit=200`,
      { headers: { accept: "application/json" } },
    );
    if (!response.ok) return null;
    const body = await response.json() as { events: Event[] };
    return body.events ?? [];
  }, [view]);

  const fetchGroups = useCallback(async (): Promise<Group[] | null> => {
    const response = await apiFetch("/api/integrations/line/groups",
      { headers: { accept: "application/json" } });
    if (!response.ok) return null;
    const body = await response.json() as { groups: Group[] };
    return body.groups ?? [];
  }, []);

  const loadEvents = useCallback(async () => {
    const rows = await fetchEvents();
    if (rows) setEvents(rows);
  }, [fetchEvents]);

  const loadGroups = useCallback(async () => {
    const rows = await fetchGroups();
    if (rows) setGroups(rows);
  }, [fetchGroups]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const rows = await fetchEvents();
      // Changing the filter starts a new request; the old one must not land on
      // top of it.
      if (!cancelled && rows) setEvents(rows);
    })();
    return () => { cancelled = true; };
  }, [fetchEvents]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const rows = await fetchGroups();
      if (!cancelled && rows) setGroups(rows);
    })();
    return () => { cancelled = true; };
  }, [fetchGroups]);

  // Opening a row asks the API what it would do *now*, rather than reading the
  // verdict stored on the row. The two can differ, and the one that matters is
  // the one the approval will use.
  const openRow = useCallback(async (id: number) => {
    if (open === id) { setOpen(null); setOptions(null); return; }
    setOpen(id);
    setOptions(null);
    const response = await apiFetch(`/api/integrations/line/events/${id}/options`,
      { headers: { accept: "application/json" } });
    if (!response.ok) return;
    setOptions(await response.json() as Options);
  }, [open]);

  const act = useCallback(async (id: number, what: "apply" | "dismiss", body: unknown) => {
    setBusy(true);
    try {
      const response = await apiFetch(`/api/integrations/line/events/${id}/${what}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const answer = await response.json() as { message?: string; error?: string };
      onToast(answer.message ?? answer.error ?? "ไม่สำเร็จ");
      if (response.ok) { setOpen(null); setOptions(null); await loadEvents(); }
    } finally {
      setBusy(false);
    }
  }, [loadEvents, onToast]);

  const bands = useMemo(
    () => summarise((events ?? []).map((one) => one.errorCode)),
    [events],
  );

  const unmapped = useMemo(
    () => (events ?? []).filter((one) => one.errorCode === "unknown-group").length,
    [events],
  );

  return (
    <div style={css("display:grid;gap:14px")}>
      <Summary bands={bands} total={events?.length ?? 0} unmapped={unmapped} />

      <div style={css(`${CARD};overflow:hidden`)}>
        <div style={css("display:flex;align-items:center;gap:10px;padding:10px 12px;border-bottom:1px solid #E6EBF0;flex-wrap:wrap")}>
          <span style={css(LABEL)}>ข้อความจากกลุ่ม LINE</span>
          <select value={view} onChange={(e) => setView(e.target.value)} style={css(CONTROL)}>
            {VIEWS.map((one) => <option key={one.value} value={one.value}>{one.label}</option>)}
          </select>
          <button onClick={() => void loadEvents()} style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>
            รีเฟรช
          </button>
        </div>

        {events === null ? (
          <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>
        ) : events.length === 0 ? (
          <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ไม่มีข้อความในหมวดนี้
          </div>
        ) : (
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;min-width:920px")}>
              <thead>
                <tr>
                  <th style={css(HEAD)}>เวลา</th>
                  <th style={css(HEAD)}>กลุ่ม</th>
                  <th style={css(HEAD)}>ข้อความ</th>
                  <th style={css(HEAD)}>เลขงาน</th>
                  <th style={css(HEAD)}>อ่านได้</th>
                  <th style={css(HEAD)}>ผลการตรวจ</th>
                  <th style={css(HEAD)}></th>
                </tr>
              </thead>
              <tbody>
                {events.map((one) => (
                  <Row
                    key={one.id} event={one}
                    open={open === one.id} options={open === one.id ? options : null}
                    busy={busy} canApprove={canApprove}
                    onOpen={() => void openRow(one.id)}
                    onAct={(what, body) => void act(one.id, what, body)}
                  />
                ))}
              </tbody>
            </table>
          </ZoomBox>
        )}
      </div>

      <Groups groups={groups} canMap={canMap} onToast={onToast}
        onSaved={() => { void loadGroups(); void loadEvents(); }} />
    </div>
  );
}

/**
 * How much is waiting, split by who has to do something about it.
 *
 * Unmapped rooms get their own line even though they are counted in the bands.
 * It is the one problem on this screen that makes every message from a whole
 * haulier fail, and it is fixed in the panel directly below.
 */
function Summary({
  bands, total, unmapped,
}: { bands: ReturnType<typeof summarise>; total: number; unmapped: number }) {
  return (
    <div style={css("display:grid;gap:8px")}>
      <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
        {bands.length === 0 ? (
          <div style={css(`${CARD};padding:10px 14px;font-size:12.5px;color:#64748B`)}>
            ไม่มีข้อความค้างอยู่
          </div>
        ) : bands.map((band) => {
          const skin = palette(band.tone);
          return (
            <div key={band.tone}
              style={css(`border:1px solid ${skin.line};background:${skin.fill};border-radius:5px;padding:8px 14px;min-width:104px`)}>
              <div style={css(`font-size:19px;font-weight:600;color:${skin.ink};line-height:1.2`)}>{band.count}</div>
              <div style={css(`font-size:11px;color:${skin.ink}`)}>{band.label}</div>
            </div>
          );
        })}
        {total > 0 && (
          <div style={css(`${CARD};padding:8px 14px;min-width:96px`)}>
            <div style={css("font-size:19px;font-weight:600;color:#334155;line-height:1.2")}>{total}</div>
            <div style={css("font-size:11px;color:#7B8CA0")}>ทั้งหมดที่แสดง</div>
          </div>
        )}
      </div>
      {unmapped > 0 && (
        <div style={css("border:1px solid #D97706;background:#FFFBEB;border-radius:5px;padding:9px 13px;font-size:12.5px;color:#92400E")}>
          มี {unmapped} ข้อความมาจากกลุ่มที่ยังไม่ได้ผูกกับผู้ขนส่ง — ผูกกลุ่มในตารางด้านล่างแล้วข้อความจะถูกอ่านใหม่
        </div>
      )}
    </div>
  );
}

function Row({
  event, open, options, busy, canApprove, onOpen, onAct,
}: {
  event: Event;
  open: boolean;
  options: Options | null;
  busy: boolean;
  canApprove: boolean;
  onOpen: () => void;
  onAct: (what: "apply" | "dismiss", body: unknown) => void;
}) {
  const outcome = describe(event.errorCode);
  const skin = palette(outcome.tone);
  const openable = isActionable(event.errorCode) && event.processingStatus === "NEED_REVIEW";

  return (
    <>
      <tr style={css(open ? "background:#F8FAFC" : "")}>
        <td style={css(`${CELL};white-space:nowrap;color:#64748B`)}>{whenLabel(event.receivedAt)}</td>
        <td style={css(`${CELL};white-space:nowrap`)}>{event.group || <em style={css("color:#B45309;font-style:normal")}>ยังไม่ผูก</em>}</td>
        <td style={css(`${CELL};max-width:280px`)}>{event.rawText}</td>
        <td style={css(`${CELL};white-space:nowrap;font-variant-numeric:tabular-nums`)}>{event.jobNumber || "—"}</td>
        <td style={css(`${CELL};white-space:nowrap`)}>
          {event.parsedStatus || "—"}
          {confidenceLabel(event.confidence) && (
            <span style={css("color:#94A3B8;margin-left:6px")}>{confidenceLabel(event.confidence)}</span>
          )}
        </td>
        <td style={css(CELL)}>
          <span style={css(`display:inline-block;border:1px solid ${skin.line};background:${skin.fill};color:${skin.ink};border-radius:3px;padding:1px 7px;font-size:11.5px`)}>
            {outcome.label}
          </span>
          {outcome.next && <div style={css("font-size:11px;color:#7B8CA0;margin-top:3px")}>{outcome.next}</div>}
        </td>
        <td style={css(`${CELL};white-space:nowrap;text-align:right`)}>
          {openable && (
            <button onClick={onOpen} style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>
              {open ? "ปิด" : "ดู"}
            </button>
          )}
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={7} style={css("padding:0;border-bottom:1px solid #E6EBF0;background:#F8FAFC")}>
            <Detail event={event} options={options} busy={busy} canApprove={canApprove} onAct={onAct} />
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * What the API says it would do, right now.
 *
 * Deliberately not built from the row above it. If the register has moved since
 * the worker looked, this is where that shows — and it shows before somebody
 * presses a button that would be refused.
 */
function Detail({
  event, options, busy, canApprove, onAct,
}: {
  event: Event;
  options: Options | null;
  busy: boolean;
  canApprove: boolean;
  onAct: (what: "apply" | "dismiss", body: unknown) => void;
}) {
  const [reason, setReason] = useState("");

  if (options === null) {
    return <div style={css("padding:14px;font-size:12.5px;color:#94A3B8")}>กำลังตรวจสอบสถานะล่าสุด…</div>;
  }

  const now = describe(options.outcome === "ok" ? "ready-to-apply" : options.outcome);
  const skin = palette(now.tone);
  const moved = options.stored !== "" && options.stored !== options.outcome
    && !(options.stored === "ready-to-apply" && options.outcome === "ok");

  return (
    <div style={css("padding:13px 14px;display:grid;gap:11px")}>
      {moved && (
        <div style={css("border:1px solid #D97706;background:#FFFBEB;border-radius:4px;padding:8px 11px;font-size:12px;color:#92400E")}>
          สถานะงานเปลี่ยนไปหลังจากระบบอ่านข้อความนี้ — ตอนนี้ผลคือ “{now.label}”
        </div>
      )}

      <div style={css(`border:1px solid ${skin.line};background:${skin.fill};border-radius:4px;padding:9px 12px`)}>
        <div style={css(`font-size:12.5px;color:${skin.ink};font-weight:600`)}>{now.label}</div>
        {options.detail && <div style={css("font-size:12px;color:#475569;margin-top:2px")}>{options.detail}</div>}
        {options.canApply && (
          <div style={css("font-size:12px;color:#475569;margin-top:2px")}>
            {options.from} → <strong>{options.to}</strong>
          </div>
        )}
      </div>

      {options.options.length > 0 && (
        <div style={css(`${CARD};overflow:hidden`)}>
          {/* Short by nature — the rows one job number covers. Uncapped, so it
              does not measure the room below its own top edge and end up two
              rows tall inside a row that is already expanded. */}
          <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse")}>
            <thead>
              <tr>
                <th style={css(HEAD)}>งาน</th>
                <th style={css(HEAD)}>ลูกค้า</th>
                <th style={css(HEAD)}>ตู้</th>
                <th style={css(HEAD)}>วันที่</th>
                <th style={css(HEAD)}>สถานะตอนนี้</th>
                <th style={css(HEAD)}></th>
              </tr>
            </thead>
            <tbody>
              {options.options.map((one) => (
                <tr key={one.key}>
                  <td style={css(`${CELL};white-space:nowrap`)}>{one.key}</td>
                  <td style={css(CELL)}>{one.customer}</td>
                  <td style={css(`${CELL};white-space:nowrap`)}>{one.container || "—"}</td>
                  <td style={css(`${CELL};white-space:nowrap`)}>{one.workDate}</td>
                  <td style={css(`${CELL};white-space:nowrap`)}>{one.status}</td>
                  <td style={css(`${CELL};white-space:nowrap;text-align:right`)}>
                    {one.move.ok ? (
                      canApprove ? (
                        <button disabled={busy}
                          onClick={() => onAct("apply", { jobKey: one.key, reason })}
                          style={css(`${BUTTON};border-color:#16A34A;background:#16A34A;color:#fff`)}>
                          อนุมัติ → {options.to}
                        </button>
                      ) : (
                        <span style={css("font-size:11.5px;color:#94A3B8")}>ไม่มีสิทธิ์อนุมัติ</span>
                      )
                    ) : (
                      // Says why this particular row cannot take the message,
                      // which is the whole reason the choice is shown per row.
                      <span style={css("font-size:11.5px;color:#B91C1C")}>{one.move.detail || one.move.result}</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          </ZoomBox>
        </div>
      )}

      <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
        <input value={reason} onChange={(e) => setReason(e.target.value)}
          placeholder="เหตุผล (บันทึกลงประวัติการตรวจสอบ)"
          style={css(`${CONTROL};flex:1;min-width:220px`)} />
        {canApprove && (
          <button disabled={busy} onClick={() => onAct("dismiss", { reason })}
            style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#B91C1C`)}>
            ปิดข้อความนี้
          </button>
        )}
      </div>
      <div style={css("font-size:11px;color:#94A3B8")}>
        ข้อความเดิม: {event.rawText}
      </div>
    </div>
  );
}

/**
 * Which haulier each LINE room speaks for.
 *
 * The thing that has to exist before any of the above works, which is why it is
 * on the same screen rather than hidden in settings — the queue's most common
 * complaint is answered here, in view of the complaint.
 */
function Groups({
  groups, canMap, onToast, onSaved,
}: {
  groups: Group[] | null;
  canMap: boolean;
  onToast: (message: string) => void;
  onSaved: () => void;
}) {
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [form, setForm] = useState({ lineGroupId: "", groupName: "", supplierId: "", groupType: "VENDOR" });
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers?status=approved",
        { headers: { accept: "application/json" } });
      if (!response.ok) return;
      const body = await response.json() as Supplier[];
      if (!cancelled) setSuppliers(Array.isArray(body) ? body : []);
    })();
    return () => { cancelled = true; };
  }, []);

  const save = useCallback(async (body: unknown) => {
    setBusy(true);
    try {
      const response = await apiFetch("/api/integrations/line/groups", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const answer = await response.json() as { message?: string; error?: string };
      onToast(answer.message ?? answer.error ?? "ไม่สำเร็จ");
      if (response.ok) onSaved();
    } finally {
      setBusy(false);
    }
  }, [onSaved, onToast]);

  return (
    <div style={css(`${CARD};overflow:hidden`)}>
      <div style={css("padding:10px 12px;border-bottom:1px solid #E6EBF0")}>
        <span style={css(LABEL)}>กลุ่ม LINE</span>
        <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:3px")}>
          รหัสกลุ่มจะปรากฏในข้อความที่เข้ามา เมื่อผูกกับผู้ขนส่งแล้ว ระบบจึงจะรับสถานะจากกลุ่มนั้นได้
        </div>
      </div>

      <ZoomBox capped={false}>
        <table style={css("width:100%;border-collapse:collapse;min-width:720px")}>
          <thead>
            <tr>
              <th style={css(HEAD)}>ชื่อกลุ่ม</th>
              <th style={css(HEAD)}>รหัสกลุ่ม</th>
              <th style={css(HEAD)}>ผู้ขนส่ง</th>
              <th style={css(HEAD)}>ประเภท</th>
              <th style={css(HEAD)}>ใช้งาน</th>
              <th style={css(HEAD)}></th>
            </tr>
          </thead>
          <tbody>
            {(groups ?? []).map((one) => (
              <tr key={one.id}>
                <td style={css(CELL)}>{one.groupName || "—"}</td>
                <td style={css(`${CELL};font-family:ui-monospace,monospace;font-size:11.5px;color:#64748B`)}>{one.lineGroupId}</td>
                <td style={css(CELL)}>
                  {one.supplier || <em style={css("color:#B91C1C;font-style:normal")}>ไม่พบผู้ขนส่ง</em>}
                </td>
                <td style={css(`${CELL};white-space:nowrap`)}>{one.groupType}</td>
                <td style={css(`${CELL};white-space:nowrap`)}>{one.isActive ? "ใช้งาน" : "ปิด"}</td>
                <td style={css(`${CELL};white-space:nowrap;text-align:right`)}>
                  {canMap && (
                    <button disabled={busy}
                      onClick={() => void save({
                        lineGroupId: one.lineGroupId, groupName: one.groupName,
                        supplierId: one.supplierId, groupType: one.groupType,
                        isActive: !one.isActive, reason: "",
                      })}
                      style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>
                      {one.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"}
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {(groups ?? []).length === 0 && (
              <tr>
                <td colSpan={6} style={css("padding:20px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  ยังไม่ได้ผูกกลุ่มใดไว้
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ZoomBox>

      {canMap && (
        <div style={css("display:flex;gap:8px;padding:11px 12px;border-top:1px solid #E6EBF0;flex-wrap:wrap;align-items:center")}>
          <input value={form.lineGroupId} onChange={(e) => setForm({ ...form, lineGroupId: e.target.value })}
            placeholder="รหัสกลุ่ม (Cxxxxxxxx)" style={css(`${CONTROL};min-width:210px`)} />
          <input value={form.groupName} onChange={(e) => setForm({ ...form, groupName: e.target.value })}
            placeholder="ชื่อกลุ่ม" style={css(`${CONTROL};min-width:150px`)} />
          <select value={form.supplierId} onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
            style={css(CONTROL)}>
            <option value="">เลือกผู้ขนส่ง…</option>
            {suppliers.map((one) => <option key={one.id} value={one.id}>{one.name}</option>)}
          </select>
          <select value={form.groupType} onChange={(e) => setForm({ ...form, groupType: e.target.value })}
            style={css(CONTROL)}>
            <option value="VENDOR">VENDOR</option>
            <option value="INTERNAL">INTERNAL</option>
            <option value="CUSTOMER">CUSTOMER</option>
            <option value="OTHER">OTHER</option>
          </select>
          <button
            disabled={busy || form.lineGroupId.trim() === "" || form.supplierId === ""}
            onClick={() => void save({
              lineGroupId: form.lineGroupId.trim(), groupName: form.groupName.trim(),
              supplierId: Number(form.supplierId), groupType: form.groupType,
              isActive: true, reason: "",
            })}
            style={css(`${BUTTON};border-color:#1E5B8F;background:#1E5B8F;color:#fff`)}>
            ผูกกลุ่ม
          </button>
        </div>
      )}
    </div>
  );
}
