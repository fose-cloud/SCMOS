"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { css } from "./theme";

/**
 * The scroll box and the zoom that My Job's grid has, around any table.
 *
 * Only three screens are built on `DataTable`, because it takes a table apart
 * into cells and rebuilds it. The other twenty-five write their own markup, and
 * a wide one on a laptop could only be read by scrolling the whole page — no
 * zoom, and the columns past the right edge simply out of reach.
 *
 * What those screens were missing is not the grid, though. It is the shell
 * around it: something that scrolls sideways on its own, and a control that
 * makes the type smaller so more of it fits. Neither needs to know what is
 * inside, so this wraps a table rather than replacing it, and a screen adopts
 * it in one line instead of being rewritten.
 *
 * The zoom itself lives in `useTableZoom` and is shared with `DataTable` rather
 * than copied — the same slider, the same limits, and the same remembered
 * value, so moving between screens does not change the size of the type.
 */

/** Where the zoom is kept. Session, not local: it is forgotten when the tab is. */
const REMEMBERED = "scmos.table.zoom";

const LIMIT = { min: 50, max: 150, step: 5 };

/**
 * The table zoom, as a percentage, shared by every screen that shows one.
 *
 * Read a tick after mounting rather than during it: the server renders this too
 * and has no sessionStorage, so reading it inline would make the first paint
 * disagree with the second.
 */
export function useTableZoom(): [number, (next: number) => void] {
  const [zoom, setZoom] = useState(100);

  useEffect(() => {
    const read = window.setTimeout(() => {
      try {
        const held = Number(window.sessionStorage.getItem(REMEMBERED));
        if (held >= LIMIT.min && held <= LIMIT.max) setZoom(held);
      } catch { /* Storage may be blocked; the zoom still works for this page. */ }
    }, 0);
    return () => window.clearTimeout(read);
  }, []);

  const change = (next: number) => {
    const value = Math.max(LIMIT.min, Math.min(LIMIT.max, Math.round(next / LIMIT.step) * LIMIT.step));
    setZoom(value);
    try { window.sessionStorage.setItem(REMEMBERED, String(value)); } catch { /* optional */ }
  };

  return [zoom, change];
}

/**
 * Keeps a scrolling box short enough that whatever sits under it stays on screen.
 *
 * A fixed `calc(100vh - 300px)` was the first attempt and it is a guess about
 * something that differs per screen: Subcontractor Master puts four tiles and a
 * search box above its table, My Job puts a toolbar and four rows of filter
 * chips, Document Register puts almost nothing. Guessed too tall, the zoom and
 * the sideways scrollbar sit below the fold — which is the state this was meant
 * to fix.
 *
 * So it is measured. The box asks where it actually starts and takes the rest of
 * the window, less the bar beneath it. Written straight to the node rather than
 * held in state: it is a measurement of the layout, and feeding it back through
 * a render to change the layout is how a loop starts.
 */
function useFitted(height?: string) {
  const box = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (height) return;
    const fit = () => {
      const node = box.current;
      if (!node) return;
      const room = window.innerHeight - node.getBoundingClientRect().top - BENEATH;
      // Never so short that it is not worth scrolling; never taller than fits.
      node.style.maxHeight = `${Math.max(FLOOR, Math.round(room))}px`;
    };
    fit();

    window.addEventListener("resize", fit);
    // Everything above a table can change height after it is drawn — figures
    // arrive, a filter bar wraps onto a second line, a panel is expanded.
    const watch = new ResizeObserver(fit);
    watch.observe(document.body);
    return () => {
      window.removeEventListener("resize", fit);
      watch.disconnect();
    };
  }, [height]);

  return box;
}

/** The zoom bar's own height, plus enough air to see the edge of the box. */
const BENEATH = 58;

/** Shorter than this and scrolling inside the box is worse than scrolling the page. */
const FLOOR = 220;

/** The − slider + control, identical wherever it appears because it is one component. */
export function ZoomBar({ zoom, onZoom }: { zoom: number; onZoom: (next: number) => void }) {
  const step = (by: number) => onZoom(zoom + by);
  const button = (on: boolean) =>
    css("width:24px;height:24px;border:0;background:transparent;color:"
      + (on ? "#64748B" : "#CBD5E1") + ";font-size:17px;cursor:" + (on ? "pointer" : "default"));

  return (
    <div style={css("display:flex;gap:6px;align-items:center")}>
      <button type="button" aria-label="ย่อตาราง" title="ย่อตาราง"
        disabled={zoom <= LIMIT.min} onClick={() => step(-LIMIT.step)} style={button(zoom > LIMIT.min)}>
        −
      </button>
      <input type="range" min={LIMIT.min} max={LIMIT.max} step={LIMIT.step} value={zoom}
        onChange={(e) => onZoom(Number(e.target.value))}
        aria-label="ขนาดตาราง" title={`ขนาดตาราง ${zoom}%`}
        style={css("width:105px;height:4px;accent-color:#2E7DD1;cursor:pointer")} />
      <button type="button" aria-label="ขยายตาราง" title="ขยายตาราง"
        disabled={zoom >= LIMIT.max} onClick={() => step(LIMIT.step)} style={button(zoom < LIMIT.max)}>
        +
      </button>
      <span style={css("width:38px;text-align:right;font-size:11.5px;color:#64748B;font-family:'IBM Plex Mono',monospace")}>
        {zoom}%
      </span>
    </div>
  );
}

export function TableFrame({ children, title, meta, actions, note, height }: {
  children: ReactNode;
  /** Shown top left. Omit and the header is left out entirely. */
  title?: string;
  /** The quieter line beside the title — a count, a period. */
  meta?: string;
  /** The screen's own buttons, rendered as given. */
  actions?: ReactNode;
  /** Shown bottom left where a grid puts its record count. */
  note?: ReactNode;
  /**
   * How tall the scrolling area may be.
   *
   * The default leaves room for this app's header, breadcrumb and filter bar.
   * A screen that carries more above its table passes its own.
   */
  height?: string;
}) {
  const [zoom, setZoom] = useTableZoom();
  const box = useFitted(height);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;display:flex;flex-direction:column;overflow:hidden")}>
      {(title || actions) && (
        <div style={css("display:flex;justify-content:space-between;align-items:center;gap:14px;flex-wrap:wrap;padding:12px 16px;border-bottom:1px solid #E9EFF5")}>
          <div style={css("display:flex;align-items:baseline;gap:10px")}>
            {title && <span style={css("font-size:13.5px;font-weight:600;color:#0A2240")}>{title}</span>}
            {meta && <span style={css("font-size:11.5px;color:#94A3B8")}>{meta}</span>}
          </div>
          {actions && <div style={css("display:flex;gap:7px;align-items:center;flex-wrap:wrap")}>{actions}</div>}
        </div>
      )}

      {/*
        The sideways scroll, and the only place it belongs.

        A table wider than the window has to scroll somewhere. Left to the page
        it takes the header, the filters and the toolbar with it, so reading the
        last column means losing sight of which screen you are on.
      */}
      <div ref={box} style={css("overflow:auto;" + (height ? `max-height:${height}` : ""))}>
        {/*
          Zoom on a wrapper rather than on the table, so it applies whatever the
          screen put inside — two tables, a table and a caption, anything.
        */}
        <div style={css("zoom:" + zoom / 100)}>{children}</div>
      </div>

      <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;background:#FBFCFD")}>
        <span style={css("font-size:12px;color:#64748B")}>{note}</span>
        <ZoomBar zoom={zoom} onZoom={setZoom} />
      </div>
    </div>
  );
}

/**
 * A table's own scroll box, with the zoom under it.
 *
 * Drops in where a screen already had `<div style={css("overflow-x:auto")}>`,
 * which is how twenty-two of them were written. Those screens could already
 * scroll sideways; what none of them had was a way to make the type smaller,
 * so a twenty-column report on a laptop could be reached but not read.
 *
 * A fragment rather than a box of its own, so it inherits whatever card the
 * screen already put around its table and the control lands on that card's
 * bottom edge — the same place My Job keeps it.
 */
export function ZoomBox({ children, height }: { children: ReactNode; height?: string }) {
  const [zoom, setZoom] = useTableZoom();
  const box = useFitted(height);

  return (
    <>
      {/*
        Capped, and that cap is most of the point.

        Left to grow, the box is as tall as its table, so on a screen of two
        hundred rows the sideways scrollbar and the zoom sit at the bottom of
        the page — under everything, reachable only by scrolling past the whole
        report, which is the same as not being there. Capped, the rows scroll
        inside the box and both controls stay where they were put. It is what
        My Job's grid has always done; a shorter table is untouched, because a
        maximum only ever takes away.
      */}
      <div ref={box} style={css("overflow:auto;" + (height ? `max-height:${height}` : ""))}>
        {/* On a wrapper, not the table: a screen may have put two in here. */}
        <div style={css("zoom:" + zoom / 100)}>{children}</div>
      </div>
      <div style={css("padding:7px 14px;border-top:1px solid #E9EFF5;display:flex;justify-content:flex-end;background:#FBFCFD")}>
        <ZoomBar zoom={zoom} onZoom={setZoom} />
      </div>
    </>
  );
}
