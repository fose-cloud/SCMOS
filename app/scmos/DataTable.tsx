"use client";

import type { ReactNode } from "react";
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

const TOOL_BTN =
  "height:30px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer";

export function DataTable({ model, onPage, onTool }: Props) {
  const from = model.total === 0 ? 0 : (model.page - 1) * model.per + 1;
  const to = Math.min(model.page * model.per, model.total);
  const pages: number[] = [];
  for (let i = 1; i <= Math.min(model.pageCount, 6); i++) pages.push(i);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;display:flex;flex-direction:column;overflow:hidden")}>
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
        </div>
      </div>

      {model.banner}

      {/* One list per column, shared by every row that edits that column. */}
      {model.datalists?.map((list) => (
        <datalist key={list.id} id={list.id}>
          {list.options.map((o) => <option key={o} value={o} />)}
        </datalist>
      ))}

      <div style={css("overflow:auto;max-height:calc(100vh - 340px)")}>
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
