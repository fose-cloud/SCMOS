"use client";

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { css } from "./theme";
import type { Cell, Col } from "./util";

export type TableRow = {
  key: string;
  go?: () => void;
  title?: string;
  style: string;
  cells: Cell[];
};

export type TableModel = {
  title: string;
  meta: string;
  cols: Col[];
  rows: TableRow[];
  total: number;
  pageCount: number;
  page: number;
  per: number;
  /** Header buttons; pass [] on screens that carry their own controls. */
  tools?: string[];
  /** Suggestion lists shared by every combo cell in the grid, rendered once. */
  datalists?: { id: string; options: string[] }[];
  /** Rendered between the header and the grid — the workspace puts its bulk bar here. */
  banner?: ReactNode;
};

type Props = {
  model: TableModel;
  onPage: (page: number) => void;
  onTool: (label: string) => void;
};

/** One nudge sideways — about a column, the distance Excel's arrows move. */
const STEP = 160;

const ARROW =
  "width:30px;height:24px;border:1px solid #D8E0E8;background:#fff;border-radius:3px;"
  + "font-size:11px;line-height:1;font-family:inherit;display:inline-flex;align-items:center;justify-content:center;";

const TOOL_BTN =
  "height:30px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer";

export function DataTable({ model, onPage, onTool }: Props) {
  /**
   * The scrolling box, so the sideways controls can drive it.
   *
   * A grid twenty-five columns wide is read by moving across it, and the only
   * way to do that was the browser's own bar — which on a laptop trackpad means
   * a diagonal gesture that fights the vertical scroll, and with a mouse means
   * dragging a bar most people never notice at the bottom of the page.
   */
  const box = useRef<HTMLDivElement | null>(null);
  const shell = useRef<HTMLDivElement | null>(null);
  const [native, setNative] = useState(false);
  const [reach, setReach] = useState({ left: false, right: false });

  /** Whether there is anything further to go, either way. */
  const measure = useCallback(() => {
    const el = box.current;
    if (!el) return;
    setReach({
      left: el.scrollLeft > 1,
      right: el.scrollLeft + el.clientWidth < el.scrollWidth - 1,
    });
  }, []);

  useEffect(() => {
    measure();
    const el = box.current;
    if (!el) return;
    const watch = new ResizeObserver(measure);
    watch.observe(el);
    return () => watch.disconnect();
  }, [measure, model.rows.length, model.cols.length]);

  /**
   * One nudge sideways, the size Excel's own arrows move: about a column.
   *
   * Held down it repeats, which is what those arrows do and what anybody who
   * has used a spreadsheet expects when they hold one.
   */
  const nudge = (by: number) => box.current?.scrollBy({ left: by, behavior: "smooth" });
  const held = useRef<ReturnType<typeof setInterval> | null>(null);
  const startNudging = (by: number) => {
    nudge(by);
    stopNudging();
    held.current = setInterval(() => box.current?.scrollBy({ left: by }), 90);
  };
  const stopNudging = () => {
    if (held.current) { clearInterval(held.current); held.current = null; }
  };
  useEffect(() => stopNudging, []);

  /**
   * Full screen: the grid and nothing else.
   *
   * The screen above this table is mostly not the table — a welcome banner, a
   * week of dates, a period bar, four rows of filter chips — and reading a
   * twenty-five column grid through the slot they leave is the whole
   * complaint. So this covers the lot.
   *
   * Two ways, and it always ends up in one of them. The browser's own full
   * screen is asked for first because it takes the tab chrome too, which on a
   * laptop is the difference between bigger and the whole screen. A browser may
   * refuse — it needs a real click, and some do not allow it at all — and then
   * a fixed panel over the page gives the same thing minus the tab bar. What
   * must not happen is a button that appears to do nothing.
   */
  const [overlay, setOverlay] = useState(false);
  const full = native || overlay;

  const toggleFull = () => {
    const el = shell.current;
    if (!el) return;

    if (document.fullscreenElement === el) { void document.exitFullscreen(); return; }
    if (overlay) { setOverlay(false); return; }

    const asked = el.requestFullscreen?.();
    if (!asked) { setOverlay(true); return; }
    asked.catch(() => setOverlay(true));
  };

  useEffect(() => {
    const follow = () => setNative(document.fullscreenElement === shell.current);
    document.addEventListener("fullscreenchange", follow);
    return () => document.removeEventListener("fullscreenchange", follow);
  }, []);

  // Escape closes the fallback the way it closes the real thing, so the two
  // behave alike and nobody has to know which one they got.
  useEffect(() => {
    if (!overlay) return;
    const leave = (e: KeyboardEvent) => { if (e.key === "Escape") setOverlay(false); };
    window.addEventListener("keydown", leave);
    return () => window.removeEventListener("keydown", leave);
  }, [overlay]);

  const from = model.total === 0 ? 0 : (model.page - 1) * model.per + 1;
  const to = Math.min(model.page * model.per, model.total);
  const pages: number[] = [];
  for (let i = 1; i <= Math.min(model.pageCount, 6); i++) pages.push(i);

  return (
    <div ref={shell}
      style={css("background:#fff;display:flex;flex-direction:column;overflow:hidden;"
        + (full
          ? "border:0;border-radius:0;height:100vh;"
            // Over everything the page draws above the grid, which is what
            // "only the table" means here.
            + (overlay ? "position:fixed;inset:0;z-index:120;" : "")
          : "border:1px solid #D8E0E8;border-radius:5px;"))}>
      <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:14px;flex-wrap:wrap")}>
        <div style={css("display:flex;align-items:baseline;gap:10px")}>
          <span style={css("font-size:13.5px;font-weight:600;color:#0A2240")}>{model.title}</span>
          <span style={css("font-size:11.5px;color:#94A3B8")}>{model.meta}</span>
        </div>
        <div style={css("display:flex;gap:7px;align-items:center")}>
          {(model.tools ?? ["Columns", "Sort", "Export Excel"]).map((label) => (
            <button key={label} className="ghost-btn" onClick={() => onTool(label)} style={css(TOOL_BTN)}>
              {label}
            </button>
          ))}
          <button className="ghost-btn" onClick={toggleFull} style={css(TOOL_BTN)}
            title={full ? "ออกจากเต็มจอ (Esc)" : "เต็มจอ — เหลือแค่ตาราง ซ่อนแถบด้านบนทั้งหมด"}>
            {full ? "ออกจากเต็มจอ (Esc)" : "เต็มจอ"}
          </button>
        </div>
      </div>

      {model.banner}

      {/* One list per column, shared by every row that edits that column. */}
      {model.datalists?.map((list) => (
        <datalist key={list.id} id={list.id}>
          {list.options.map((o) => <option key={o} value={o} />)}
        </datalist>
      ))}

      <div ref={box} onScroll={measure}
        style={css("overflow:auto;" + (full ? "flex:1;min-height:0" : "max-height:calc(100vh - 340px)"))}>
        <table style={css("width:100%;border-collapse:separate;border-spacing:0;min-width:100%")}>
          <thead>
            <tr>
              {model.cols.map((c, i) => (
                <th key={c.label + i} onClick={c.sort} style={css(c.style)}>{c.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {model.rows.map((r) => (
              <tr key={r.key} className="row-hover" onClick={r.go} title={r.title} style={css(r.style)}>
                {r.cells.map((c, ci) => (
                  <td
                    key={ci}
                    style={css(c.td + (c.sel ? ";background:#DCEBFB;box-shadow:inset 0 0 0 1px #2E7DD1" : ""))}
                    title={c.title}
                    onClick={c.go}
                    onMouseDown={c.onDown}
                    onMouseEnter={c.onEnter}
                  >
                    {c.kind === "check" ? (
                      <input
                        type="checkbox"
                        checked={!!c.checked}
                        disabled={c.disabled}
                        title={c.title}
                        aria-label={c.title || "Select row"}
                        onClick={(e) => e.stopPropagation()}
                        onChange={() => c.onCheck?.()}
                        style={css("width:15px;height:15px;cursor:" + (c.disabled ? "not-allowed" : "pointer") + ";accent-color:#2E7DD1")}
                      />
                    ) : c.kind === "select" ? (
                      <select value={c.value} onChange={c.onChange} style={css(c.selStyle)}>
                        {(c.options || []).map((o) => <option key={o} value={o}>{o}</option>)}
                      </select>
                    ) : c.kind === "combo" ? (
                      <input
                        value={c.value}
                        list={c.listId}
                        onChange={c.onChange}
                        onBlur={c.onBlur}
                        onKeyDown={c.onKey}
                        // Same reason as the plain editor below: the box only
                        // exists once the cell has been clicked into.
                        // eslint-disable-next-line jsx-a11y/no-autofocus
                        autoFocus
                        style={css(c.inpStyle)}
                      />
                    ) : c.kind === "input" ? (
                      <input
                        value={c.value}
                        onChange={c.onChange}
                        onBlur={c.onBlur}
                        onKeyDown={c.onKey}
                        // The cell only renders as an input once the user has clicked it to
                        // edit, so focus has to follow the click for the editor to be usable.
                        // eslint-disable-next-line jsx-a11y/no-autofocus
                        autoFocus
                        style={css(c.inpStyle)}
                      />
                    ) : (
                      <span style={css(c.sp)}>{c.v}</span>
                    )}
                  </td>
                ))}
              </tr>
            ))}
            {!model.rows.length && (
              <tr>
                <td colSpan={Math.max(1, model.cols.length)} style={css("padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  No records match the current filters.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/*
        Excel's own sideways controls, under the grid where its scrollbar is.

        A step per click and a run while held, the way those arrows behave in a
        spreadsheet. They are drawn only when the grid is actually wider than
        its box, and each greys at the end it cannot go past — so the bar
        answers "is there more over there?", which the browser's thin scrollbar
        never did.
      */}
      {(reach.left || reach.right) && (
        <div style={css("padding:5px 16px;border-top:1px solid #E9EFF5;background:#F6F8FB;display:flex;align-items:center;gap:7px")}>
          <button aria-label="เลื่อนไปทางซ้าย" title="เลื่อนไปทางซ้าย (กดค้างเพื่อเลื่อนต่อเนื่อง)"
            disabled={!reach.left}
            onClick={() => nudge(-STEP)}
            onMouseDown={() => startNudging(-STEP)} onMouseUp={stopNudging} onMouseLeave={stopNudging}
            style={css(ARROW + (reach.left ? "color:#31465C;cursor:pointer" : "color:#C3CFDB;cursor:default"))}>
            ◀
          </button>
          <button aria-label="เลื่อนไปทางขวา" title="เลื่อนไปทางขวา (กดค้างเพื่อเลื่อนต่อเนื่อง)"
            disabled={!reach.right}
            onClick={() => nudge(STEP)}
            onMouseDown={() => startNudging(STEP)} onMouseUp={stopNudging} onMouseLeave={stopNudging}
            style={css(ARROW + (reach.right ? "color:#31465C;cursor:pointer" : "color:#C3CFDB;cursor:default"))}>
            ▶
          </button>
          <span style={css("font-size:11px;color:#94A3B8")}>
            เลื่อนดูคอลัมทางขวา · หรือกด Shift ค้างแล้วหมุนเมาส์
          </span>
          <span style={css("flex:1")} />
          <button onClick={() => box.current?.scrollTo({ left: 0, behavior: "smooth" })}
            disabled={!reach.left}
            style={css("height:24px;padding:0 10px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:11px;font-family:inherit;"
              + (reach.left ? "color:#31465C;cursor:pointer" : "color:#C3CFDB;cursor:default"))}>
            กลับคอลัมแรก
          </button>
        </div>
      )}

      <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;background:#FBFCFD")}>
        <span style={css("font-size:12px;color:#64748B")}>
          Showing {from}–{to} of {model.total} records
        </span>
        <div style={css("display:flex;gap:6px;align-items:center")}>
          <button className="ghost-btn" onClick={() => onPage(Math.max(1, model.page - 1))} style={css("height:29px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer")}>
            ‹ Prev
          </button>
          {pages.map((i) => (
            <button
              key={i}
              onClick={() => onPage(i)}
              style={css(
                "height:29px;min-width:30px;padding:0 8px;border:1px solid " + (i === model.page ? "#0A2240" : "#D8E0E8") +
                ";background:" + (i === model.page ? "#0A2240" : "#fff") +
                ";color:" + (i === model.page ? "#fff" : "#475569") +
                ";border-radius:4px;font-size:12px;cursor:pointer;font-family:'IBM Plex Mono',monospace",
              )}
            >
              {i}
            </button>
          ))}
          <button className="ghost-btn" onClick={() => onPage(Math.min(model.pageCount, model.page + 1))} style={css("height:29px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer")}>
            Next ›
          </button>
        </div>
      </div>
    </div>
  );
}
