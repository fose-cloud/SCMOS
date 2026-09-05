"use client";

import { css } from "../theme";
import { QuoteCalculator } from "./QuoteCalculator";
import { QuoteTerms } from "./QuoteTerms";
import { RateSheet } from "./RateSheet";

/**
 * What a journey costs, in the three ways the team asks it.
 *
 * The register, laid out as the workbook lays it out and typed into; the
 * calculator, for a journey nobody has a price for yet; and the conditions,
 * which are the part of the price that is not the rate — waiting time, X-ray,
 * a cancelled booking, a night with the chassis still hooked up.
 *
 * There were two more. "Rate Inquiry" was a form for raising a new request and
 * "Rate Comparison" asked the API which carriers had priced a lane — both
 * built, neither used: the team raises an inquiry by email and reads the answer
 * off the sheet. They are gone rather than left as tabs that teach people the
 * screen has parts that do nothing. The workbook importer that lived on the
 * first of them was the one thing there anybody used, and it moved onto the
 * sheet, which is what it fills.
 */

type View = "calculate" | "sheet" | "terms";

export function Quotation({ view, onView, canEditRates, onToast }: {
  /**
   * Which half is showing, owned above this screen.
   *
   * The sheet is worked the way My Job is — the page held still and the grid
   * scrolling inside it — and only the app knows how to stop the page
   * scrolling. The calculator is a form and wants the page back, so the tab has
   * to be visible from up there rather than kept in here.
   */
  view: View;
  onView: (next: View) => void;
  /**
   * Whether this account may change a rate.
   *
   * Shown rather than enforced: the API refuses a write from an account without
   * it, and this only decides whether the sheet offers a cursor and a box. A
   * screen that hid the refusal would be the whole of the protection, which is
   * the arrangement this codebase has had to unpick before.
   */
  canEditRates: boolean;
  onToast: (m: string) => void;
}) {
  return (
    <div style={css("display:flex;flex-direction:column;gap:"
      // No gap under the tabs on the sheet: it fills what is left of the
      // window, and fourteen pixels of grey between the tabs and the grid is
      // fourteen pixels of rows.
      + (view === "sheet" ? "8px;flex:1;min-height:0" : "14px"))}>
      <div style={css("display:flex;gap:7px;flex-wrap:wrap;flex:none")}>
        {([
          ["calculate", "คำนวณราคา", "Rate Calculator"],
          ["sheet", "ตารางอัตรา", "Rate Sheet"],
          ["terms", "เงื่อนไขเพิ่มเติม", "Surcharges & Terms"],
        ] as [View, string, string][]).map(([key, th, en]) => {
          const on = view === key;
          return (
            <button key={key} onClick={() => onView(key)}
              style={css("height:34px;padding:0 15px;border:1px solid " + (on ? "#0A2240" : "#E2E8F0") +
                ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#64748B") +
                ";border-radius:4px;font-size:12.5px;cursor:pointer;font-family:inherit;font-weight:" +
                (on ? "600" : "400"))}>
              {th} <span style={css("opacity:.7;font-size:11px")}>· {en}</span>
            </button>
          );
        })}
      </div>

      {view === "calculate" && <QuoteCalculator canEditRates={canEditRates}
        onOpenSheet={() => onView("sheet")} onToast={onToast} />}

      {/* The register in the workbook's own shape, typed into like My Job. */}
      {view === "sheet" && <RateSheet canEdit={canEditRates} onToast={onToast} />}

      {/* What is charged on top of the rate — a page to read, not to type in. */}
      {view === "terms" && <QuoteTerms onToast={onToast} />}
    </div>
  );
}
