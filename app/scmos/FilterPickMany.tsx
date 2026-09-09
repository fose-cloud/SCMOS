"use client";

import { useEffect, useRef, useState } from "react";
import { PICK_SEP, chosenIn, pickLabel } from "./filterChoices";
import { SEARCH_FROM, pickMatches } from "./pickSearch";
import { css } from "./theme";

/**
 * The two bars this control is drawn for.
 *
 * It was written for My Job's navy bar and hard-coded to it, which is why the
 * dashboard's row went navy when the pickers moved into it rather than the
 * other way round. The department asked for that row in white, so the control
 * gained the second set rather than being written a second time.
 *
 * Navy stays the default: My Job and the rate sheet are unchanged, and a screen
 * that adopts the control without thinking about it gets the bar it has always
 * had.
 *
 * The two are the same shapes in different inks. Every value here has a partner
 * on the other side, so a colour added to one is a colour missing from the
 * other rather than a hole nobody notices until a white word lands on white.
 */
const TONES = {
  dark: {
    label: "#CFE2F7",
    buttonBorder: "#24476E", buttonBorderSet: "#4E9BE8",
    buttonBg: "#0A2240", buttonBgSet: "#16406E",
    buttonInk: "#fff",
    panelBg: "#0A2240", panelBorder: "#4E7BA8", panelShadow: "0 10px 28px rgba(0,0,0,.4)",
    rowInk: "#fff", rowOn: "#16406E",
    rule: "#1B3B60",
    inputBorder: "#4E7BA8", inputBg: "#06182E", inputInk: "#fff",
    quiet: "#8FB4DC",
    tick: "#4E9BE8",
  },
  light: {
    label: "#8496A8",
    buttonBorder: "#D8E0E8", buttonBorderSet: "#BBD5EE",
    buttonBg: "#F8FAFC", buttonBgSet: "#F4F8FC",
    buttonInk: "#0A2240",
    panelBg: "#fff", panelBorder: "#D8E0E8", panelShadow: "0 10px 28px rgba(10,34,64,.16)",
    rowInk: "#16232F", rowOn: "#F4F8FC",
    rule: "#E9EFF5",
    inputBorder: "#D8E0E8", inputBg: "#fff", inputInk: "#16232F",
    quiet: "#8496A8",
    tick: "#1D5FA8",
  },
} as const;

/**
 * A filter that takes several values at once.
 *
 * A list of checkboxes rather than a native multiple-select, which needs
 * ctrl-click to add a second value and loses the lot on a stray click — a
 * control people avoid rather than learn. The button says what is chosen and
 * the panel stays open while several are ticked, because ticking three
 * customers is the whole point.
 *
 * Written inside My Job, where the bar was built. The rate sheet needed the
 * same bar over its own columns, and the choice was to write the control a
 * second time or to lift it out — and in this codebase a thing written twice
 * is a thing that has already started to differ.
 */
export function FilterPickMany({ label, value, options, onPick, render = (option) => option, emptyValue = "ALL", tone = "dark" }: {
  label: string;
  value: string;
  options: string[];
  onPick: (v: string) => void;
  render?: (option: string) => string;
  emptyValue?: string;
  /** Which bar this sits on. See TONES. */
  tone?: "light" | "dark";
}) {
  const [open, setOpen] = useState(false);
  /* What has been typed into the panel's box. Cleared when it closes, so
     reopening a picker never shows a list narrowed by something invisible. */
  const [query, setQuery] = useState("");
  const box = useRef<HTMLDivElement | null>(null);
  const chosen = chosenIn(value);
  const set = chosen.length > 0;
  const ink = TONES[tone];

  // Clicking anywhere else closes it, the way every other menu behaves.
  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false);
    };
    window.addEventListener("mousedown", away);
    return () => window.removeEventListener("mousedown", away);
  }, [open]);

  const toggle = (option: string) => {
    const next = chosen.includes(option)
      ? chosen.filter((one) => one !== option)
      : chosen.concat([option]);
    onPick(next.length ? next.join(PICK_SEP) : emptyValue);
  };

  // Anything ticked stays visible whatever is typed, or clearing a tick would
  // mean clearing the search first to find it again.
  const shown = options.filter((one) => chosen.includes(one) || pickMatches(render(one), query));

  return (
    <div ref={box} style={css("position:relative;display:flex;align-items:center;gap:6px")}>
      <span style={css(`font-size:10px;font-weight:700;color:${ink.label};letter-spacing:.06em`)}>{label}</span>
      <button type="button" onClick={() => { setOpen((was) => !was); setQuery(""); }}
        title={set ? label + ": " + chosen.map(render).join(", ") : label + ": ทั้งหมด"}
        style={css("height:27px;max-width:190px;border:1px solid " + (set ? ink.buttonBorderSet : ink.buttonBorder)
          + ";background:" + (set ? ink.buttonBgSet : ink.buttonBg)
          + ";color:" + ink.buttonInk + ";border-radius:4px;font-size:11.5px;font-family:inherit;padding:0 8px;cursor:pointer;"
          + "display:flex;align-items:center;gap:6px;white-space:nowrap;overflow:hidden"
          + (set ? ";font-weight:600" : ""))}>
        <span style={css("overflow:hidden;text-overflow:ellipsis")}>{pickLabel(value, render)}</span>
        <span style={css("opacity:.7;font-size:9px")}>▼</span>
      </button>

      {open && (
        <div style={css("position:absolute;top:30px;left:0;z-index:60;min-width:230px;max-height:320px;"
          + "overflow:auto;background:" + ink.panelBg + ";border:1px solid " + ink.panelBorder + ";border-radius:5px;"
          + "box-shadow:" + ink.panelShadow + ";padding:5px")}>
          <button type="button" onClick={() => { onPick(emptyValue); }}
            style={css("display:block;width:100%;text-align:left;padding:6px 9px;border:none;border-radius:3px;"
              + "background:" + (set ? "transparent" : ink.rowOn) + ";color:" + ink.rowInk
              + ";font-size:11.5px;font-family:inherit;cursor:pointer")}>
            ทั้งหมด{set ? " (ล้างที่เลือก " + chosen.length + " รายการ)" : ""}
          </button>
          <div style={css(`height:1px;background:${ink.rule};margin:4px 2px`)} />
          {options.length >= SEARCH_FROM && (
            <input value={query} onChange={(event) => setQuery(event.target.value)}
              placeholder="พิมพ์เพื่อค้นหา"
              aria-label={"ค้นหาใน " + label}
              // Not autofocused: the panel is often opened just to read what is
              // ticked, and taking the keyboard there stops the arrow keys
              // scrolling the list.
              style={css("width:100%;height:26px;margin:2px 0 5px;padding:0 8px;border-radius:3px;"
                + "border:1px solid " + ink.inputBorder + ";background:" + ink.inputBg
                + ";color:" + ink.inputInk + ";font:inherit;font-size:11.5px")} />
          )}
          {shown.length === 0 && (
            <div style={css(`padding:8px 9px;font-size:11.5px;color:${ink.quiet}`)}>
              ไม่พบ “{query}”
            </div>
          )}
          {shown.map((option) => {
            const on = chosen.includes(option);
            return (
              <label key={option}
                style={css("display:flex;align-items:center;gap:8px;padding:5px 9px;border-radius:3px;cursor:pointer;"
                  + "font-size:11.5px;color:" + ink.rowInk + ";background:" + (on ? ink.rowOn : "transparent"))}>
                <input type="checkbox" checked={on} onChange={() => toggle(option)}
                  style={css(`width:14px;height:14px;accent-color:${ink.tick};cursor:pointer`)} />
                <span style={css("overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{render(option)}</span>
              </label>
            );
          })}
        </div>
      )}
    </div>
  );
}
