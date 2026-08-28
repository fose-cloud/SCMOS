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
  /**
   * Controls drawn on the header row itself, under the title.
   *
   * The workspace puts the period filter here. That row carries a title, a
   * count and two buttons and is half empty; the filter had a full-width panel
   * to itself for six controls.
   */
  controls?: ReactNode;
  /**
   * Fill the space given rather than sitting in it as a card.
   *
   * The workspace locks its page, so the grid is what has to grow to the
   * bottom of the screen and scroll inside itself. Off elsewhere: a table in
   * the middle of a report is still a card.
   */
  fill?: boolean;
  /**
   * Suppress the browser's own text selection, for the length of a drag.
   *
   * Only while dragging: a grid that can never be selected is a grid whose
   * Ctrl+C depends on the browser firing a copy event over nothing, which is
   * not a thing to rely on.
   */
  noSelect?: boolean;
  /**
   * The screen's own buttons — import, export, add — on this header.
   *
   * They belong wherever the grid is, and the grid can be full screen.
   */
  actions?: { label: string; style: string; go: () => void }[];
};

type Props = {
  model: TableModel;
  onPage: (page: number) => void;
  onTool: (label: string) => void;
  /**
   * Full screen, held by whoever draws this table.
   *
   * Kept outside because the grid's key is its layout, and changing the
   * category changes the layout — so the table is a new component and anything
   * it held goes with it. Choosing a filter threw you out of full screen,
   * which is the moment you least want to be thrown out. Absent, the table
   * keeps the flag itself, which is fine on a screen that draws one table.
   */
  full?: boolean;
  onFull?: () => void;
};

/** One nudge sideways — about a column, the distance Excel's arrows move. */
const STEP = 160;

const ARROW =
  "width:30px;height:24px;border:1px solid #D8E0E8;background:#fff;border-radius:3px;"
  + "font-size:11px;line-height:1;font-family:inherit;display:inline-flex;align-items:center;justify-content:center;";

const TOOL_BTN =
  "height:30px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer";

/** The same button on the navy header. */
const TOOL_BTN_DARK =
  "height:28px;padding:0 12px;border:1px solid #4E7BA8;background:transparent;color:#fff;"
  + "border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit";

/** The one action the screen exists to do, still leading on the navy. */
const ACTION_BTN_LEAD =
  "height:28px;padding:0 13px;border:1px solid #4E9BE8;background:#16406E;color:#fff;"
  + "border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit";

export function DataTable(p: Props) {
  const { model, onPage, onTool } = p;
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
  const activeCell = model.rows
    .flatMap((row) => row.cells.map((cell, column) => cell.active ? row.key + ":" + column : ""))
    .find(Boolean) ?? "";

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

  // Arrow navigation can move beyond the part of a wide grid that is visible.
  // Keep its active cell in view just as a spreadsheet does; the surrounding
  // page stays put because "nearest" scrolls only as far as that cell needs.
  useEffect(() => {
    if (!activeCell) return;
    const cell = box.current?.querySelector<HTMLElement>('td[data-grid-active="true"]');
    if (!cell) return;
    cell.scrollIntoView({ block: "nearest", inline: "nearest" });
    measure();
  }, [activeCell, measure]);

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
  const [ownOverlay, setOwnOverlay] = useState(false);
  const overlay = p.full ?? ownOverlay;
  const full = native || overlay;

  const toggleFull = () => {
    // Deliberately not the browser's own full screen.
    //
    // That one takes the whole display, and the whole display includes the
    // Windows taskbar — LESCHACO wants the clock, the mail icon and the rest of
    // it still there while they work. This fills the browser window instead,
    // which is everything above the taskbar and nothing below it.
    //
    // What it costs is the browser's tab bar, which stays. That is the trade
    // that was asked for.
    if (document.fullscreenElement === shell.current) { void document.exitFullscreen(); return; }
    if (p.onFull) { p.onFull(); return; }
    setOwnOverlay((on) => !on);
  };

  useEffect(() => {
    const follow = () => setNative(document.fullscreenElement === shell.current);
    document.addEventListener("fullscreenchange", follow);
    return () => document.removeEventListener("fullscreenchange", follow);
  }, []);

  // Escape closes the fallback the way it closes the real thing, so the two
  // behave alike and nobody has to know which one they got.
  const onFull = p.onFull;
  useEffect(() => {
    if (!overlay) return;
    const leave = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      if (onFull) onFull(); else setOwnOverlay(false);
    };
    window.addEventListener("keydown", leave);
    return () => window.removeEventListener("keydown", leave);
  }, [overlay, onFull]);

  const from = model.total === 0 ? 0 : (model.page - 1) * model.per + 1;
  const to = Math.min(model.page * model.per, model.total);
  const pages: number[] = [];
  for (let i = 1; i <= Math.min(model.pageCount, 6); i++) pages.push(i);

  return (
    <div ref={shell}
      style={css("background:#fff;display:flex;flex-direction:column;overflow:hidden;"
        + (full
          ? "border:0;border-radius:0;"
            // Over everything the page draws above the grid, and no further:
            // inset:0 is the browser window, which stops above the taskbar.
            + (overlay ? "position:fixed;inset:0;z-index:120;" : "height:100vh;")
          : model.fill
            // No border and no radius: it meets the edges of the screen.
            ? "border:0;border-radius:0;flex:1;min-height:0;"
            : "border:1px solid #D8E0E8;border-radius:5px;"))}>
      {/*
        One header, one colour.

        This was a white title row above a navy bar above a second navy bar in
        a different navy above a pale period row — four backgrounds and three
        borders to say one thing. Filling the screen it is all the one navy and
        the rows inside it are just rows.
      */}
      <div style={css("display:flex;justify-content:space-between;align-items:center;gap:14px;flex-wrap:wrap;"
        + (model.fill ? "padding:7px 16px;background:#0A2240" : "padding:12px 16px;border-bottom:1px solid #E9EFF5"))}>
        <div style={css("display:flex;align-items:baseline;gap:10px")}>
          <span style={css("font-size:13.5px;font-weight:600;color:" + (model.fill ? "#fff" : "#0A2240"))}>{model.title}</span>
          <span style={css("font-size:11.5px;color:" + (model.fill ? "#CFE2F7" : "#94A3B8"))}>{model.meta}</span>
        </div>
        <div style={css("display:flex;gap:7px;align-items:center")}>
          {/*
            The screen's buttons, restyled for the navy they now sit on.

            They were built for a white heading: white boxes with grey text,
            which on navy read as blank tiles, and a primary button that is navy
            on navy — "+ ADD JOB" was white lettering on nothing at all. On this
            header they are outlined and white, and the primary one keeps its
            fill so the action you came to do still leads.
          */}
          {(model.actions ?? []).map((a) => (
            <button key={a.label} onClick={a.go}
              style={css(model.fill
                ? (a.style.includes("background:#0A2240") ? ACTION_BTN_LEAD : TOOL_BTN_DARK)
                : a.style)}>
              {a.label}
            </button>
          ))}
          {(model.tools ?? ["Columns", "Sort", "Export Excel"]).map((label) => (
            <button key={label} className="ghost-btn" onClick={() => onTool(label)}
              style={css(model.fill ? TOOL_BTN_DARK : TOOL_BTN)}>
              {label}
            </button>
          ))}
          <button className="ghost-btn" onClick={toggleFull} style={css(model.fill ? TOOL_BTN_DARK : TOOL_BTN)}
            title={full ? "ออกจากเต็มจอ (Esc)" : "เต็มจอ — เหลือแค่ตาราง ซ่อนแถบด้านบนทั้งหมด"}>
            {full ? "ออกจากเต็มจอ (Esc)" : "เต็มจอ"}
          </button>
        </div>
      </div>

      {model.controls && (
        <div style={css("padding:0 16px 11px;"
          + (model.fill ? "background:#0A2240" : "border-bottom:1px solid #E9EFF5;background:#FBFCFD"))}>
          {model.controls}
        </div>
      )}

      {model.banner}

      {/* One list per column, shared by every row that edits that column. */}
      {model.datalists?.map((list) => (
        <datalist key={list.id} id={list.id}>
          {list.options.map((o) => <option key={o} value={o} />)}
        </datalist>
      ))}

      <div ref={box} onScroll={measure}
        style={css("overflow:auto;"
          + (full || model.fill ? "flex:1;min-height:0" : "max-height:calc(100vh - 340px)"))}>
        {/*
          While a rectangle is being dragged, the browser's own text selection
          is off. Dragging used to drag both at once, so the whole row lit up
          blue on top of the rectangle and neither could be read.

          It comes straight back when the mouse does, because Ctrl+C on this
          grid is the browser's copy event, and a page with nothing selectable
          is a poor place to depend on one.
        */}
        <table style={css("width:100%;border-collapse:separate;border-spacing:0;min-width:100%"
          + (model.noSelect ? ";user-select:none;-webkit-user-select:none" : ""))}>
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
                    data-grid-active={c.active ? "true" : undefined}
                    onClick={c.go}
                    onDoubleClick={c.onDouble}
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
