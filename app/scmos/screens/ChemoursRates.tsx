"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";
import { DIESEL, DIESEL_DEFAULT } from "../diesel";
import { cell, paginate } from "../util";
import { DataTable, type TableModel } from "../DataTable";
import {
  bandForDiesel, chemoursLaneKey, chemoursLayout, chemoursMargin, chemoursSellIndex,
  parseChemoursSellSheet, parseChemoursSheet, priceFor, reconcileChemoursBands,
  type FuelBand, type RateIssue, type RateLane, type UnpricedLine,
} from "../rates";

/**
 * This account's own transport prices, and nowhere else.
 *
 * Deliberately not in the Rates screen. That book exists to compare eighteen
 * subcontractors so a job can go to the cheapest of them, and a price
 * negotiated for one customer's distribution runs has no business being offered
 * on another customer's job. Here it sits beside the work it prices.
 *
 * The card is read from the workbook in the browser and held for as long as the
 * screen is open. Nothing is uploaded and nothing is stored: a rate is a
 * contract term, changing the book needs approval, and a screen that quietly
 * wrote prices somewhere would be doing the one thing it must not.
 */
/**
 * A condition written on a card, and which sheet of it wrote them.
 *
 * The sheet is kept because it is half the meaning. THAI KOT's return-load
 * term is on its SCGJWD sheets and not on its Unithai ones, and a list of
 * conditions that did not say which is which would read as though the term
 * covered the whole card.
 */
export type CardNote = { carrier: string; sheet: string; text: string };

export type RateCard = {
  file: string;
  bands: FuelBand[];
  lanes: RateLane[];
  issues: RateIssue[];
  /** Conditions off the sheets. Empty for a card read back from the register,
   *  which stores prices and not the words around them. */
  notes: CardNote[];
  /** Lines the customer asked to have priced and that carry no price. */
  unpriced: UnpricedLine[];
};

/**
 * Whose card this is has to be told, because the workbook does not say.
 *
 * The tab names — "Unithai (4W)", "SCGJWD (4W)" — are the warehouse the run
 * leaves from, the same two names the job sheets carry in their W/H column, and
 * reading them as the hauler filed a whole card under two companies that have
 * never quoted anything. The file name is the only place the hauler appears,
 * and only sometimes, so it is asked for rather than guessed.
 */
export async function readRateCard(file: File, carrier: string): Promise<RateCard> {
  const book = XLSX.read(await file.arrayBuffer(), { cellDates: true });
  const bands: FuelBand[] = [];
  const lanes: RateLane[] = [];
  const issues: RateIssue[] = [];
  const notes: CardNote[] = [];

  // Read every sheet first, then agree the fuel clause across them, then parse.
  //
  // The clause is one contract term and all the card's sheets should carry it.
  // Where they do not, the odd one out is a typing slip: SSL's 10-wheel sheet
  // reads 36.31-29.94, and its 4-wheel and 10-wheel sheets stop at 48.01-50.00
  // where the other four run to 48.35-53.18. Parsing sheet by sheet would take
  // each at face value and leave two lorries priced against a clause nobody
  // agreed to.
  const sheets = book.SheetNames.map((sheetName) => {
    const rows = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], {
      header: 1, blankrows: false, defval: "",
    });
    const { bandRow } = rows.length ? chemoursLayout(rows) : { bandRow: -1 };
    const labels = bandRow < 0
      ? []
      : (rows[bandRow] as unknown[]).slice(4).map((cell) => String(cell ?? "").trim());
    return { sheetName, rows, bandRow, labels };
  }).filter((sheet) => sheet.rows.length > 0);

  const agreed = reconcileChemoursBands(
    sheets.filter((sheet) => sheet.bandRow >= 0).map((sheet) => ({ sheetName: sheet.sheetName, labels: sheet.labels })),
    issues, file.name,
  );

  for (const sheet of sheets) {
    // The agreed clause is written back into the rows the parser will read, so
    // there is still exactly one place that decides what a band is.
    if (sheet.bandRow >= 0) {
      const row = sheet.rows[sheet.bandRow] as unknown[];
      agreed.forEach((label, position) => {
        if (label) row[4 + position] = label;
      });
    }
    const parsed = parseChemoursSheet(
      { carrier, fileName: file.name, sheetName: sheet.sheetName, rows: sheet.rows }, bands, issues);
    if (!parsed) continue;
    lanes.push(...parsed.lanes);
    for (const text of parsed.source.notes ?? []) {
      // The same line appears on all three of a warehouse's sheets, being one
      // term of one agreement. Listed once per carrier and wording, with the
      // sheets it was found on folded together below.
      if (!notes.some((note) => note.carrier === carrier && note.text === text)) {
        notes.push({ carrier, sheet: sheet.sheetName, text });
      }
    }
  }

  // The haulier card quotes one truck size a sheet, so a size it does not
  // cover has no row to be unpriced on — the absence is the missing sheet
  // itself, which the reader cannot tell from a size nobody asked about.
  return { file: file.name, bands, lanes, issues, notes, unpriced: [] };
}

/**
 * What LESCHACO bills the customer, read from the RFP answer.
 *
 * No hauler is asked for, because there is not one: this card is ours. Every
 * sheet is a warehouse, every lane carries its own truck sizes, and the whole
 * file is one card — which is why it replaces what was loaded rather than
 * joining it the way a second hauler's cost card does.
 *
 * A workbook without a Truck type column is a cost card, and is refused here
 * rather than parsed as best it can be. Reading one as the other would file
 * what we charge as what we pay, and the margin column would be wrong in the
 * comforting direction.
 */
export async function readSellingCard(file: File): Promise<RateCard> {
  const book = XLSX.read(await file.arrayBuffer(), { cellDates: true });
  const bands: FuelBand[] = [];
  const lanes: RateLane[] = [];
  const issues: RateIssue[] = [];
  const unpriced: UnpricedLine[] = [];

  for (const sheetName of book.SheetNames) {
    const rows = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], {
      header: 1, blankrows: false, defval: "",
    });
    if (!rows.length) continue;
    const parsed = parseChemoursSellSheet(
      { carrier: SELLER, fileName: file.name, sheetName, rows }, bands, issues);
    if (!parsed) continue;
    lanes.push(...parsed.lanes);
    unpriced.push(...parsed.unpriced);
  }

  // No notes: this card keeps its conditions in a Note column beside each lane
  // rather than in a line at the foot of the sheet, and the lanes carry them.
  return { file: file.name, bands, lanes, issues, notes: [], unpriced };
}

/** Us, on the selling side of the card. Not a hauler and never filtered as one. */
const SELLER = "LESCHACO";


/**
 * The three truck sizes on one row, which is how the card is read.
 *
 * The workbook quotes one size per sheet, so the same lane arrives three times.
 * Somebody deciding which truck to send wants those three numbers side by side,
 * not on three rows forty apart.
 */
type LaneRow = { carrier: string; from: string; to: string; zip: string; lanes: RateLane[] };

function laneRows(card: RateCard, carrier: string): LaneRow[] {
  const rows = new Map<string, LaneRow>();
  for (const lane of card.lanes) {
    if (carrier !== "ALL" && lane.carrier !== carrier) continue;
    const key = `${lane.carrier}|${lane.from}|${lane.to}|${lane.county}`;
    const held = rows.get(key);
    if (held) held.lanes.push(lane);
    else rows.set(key, { carrier: lane.carrier, from: lane.from, to: lane.to, zip: lane.county, lanes: [lane] });
  }
  return [...rows.values()].sort((a, b) =>
    a.carrier.localeCompare(b.carrier) || a.to.localeCompare(b.to));
}

const VEHICLES = ["4W", "6W", "10W"];

/**
 * Rows to a page.
 *
 * A rate card runs to a few hundred lanes at most, so this is one page for
 * nearly every card and a guard for the one that is not.
 */
const RATE_PER = 300;

/**
 * What one cell of the table is: the trip's cost, what we bill for it, and the
 * share of the invoice that is neither.
 *
 * Held together rather than computed three times in the row builder, because a
 * margin worked out from a different pair of numbers than the two printed
 * beside it is the one bug this table could have that nobody would spot.
 */
type Money = { cost: number | null; sell: number | null; margin: number | null };

/** The card as the shared grid draws it. */
function rateModel(
  rows: LaneRow[],
  moneyAt: (row: LaneRow, vehicle: string) => Money,
  carrier: string,
  selling: boolean,
  page: number,
): TableModel {
  const pg = paginate(rows, page, RATE_PER);
  const head = (label: string, right: boolean) => ({
    label,
    // Sticky so the vehicle columns stay named while a long card is read. Not
    // through `cols()`, which makes every heading a sort button, and nothing
    // here sorts — the card's own order is the order it was negotiated in.
    style: "position:sticky;top:0;z-index:2;background:#F4F7FA;padding:7px 10px;font-size:10px;"
      + "color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap;user-select:none;text-align:"
      + (right ? "right" : "left"),
    sort: () => undefined,
  });

  const money = (value: number | null, mute = false) =>
    // A lane the card does not price is a dash, not a zero: zero is a price and
    // this is the absence of one.
    cell(value == null ? "" : value.toLocaleString("en-US"),
      { mono: true, align: "right", mute: mute || value == null });

  return {
    title: "ค่าขนส่ง",
    meta: `${rows.length} เส้นทาง · ${carrier === "ALL" ? "ทุกผู้ขนส่ง" : carrier}`
      + (selling ? " · ทุน / ขาย / กำไร" : ""),
    cols: [
      head("ผู้ขนส่ง", false), head("ต้นทาง", false), head("ปลายทาง", false), head("ZIP", false),
      ...VEHICLES.flatMap((vehicle) => (selling
        ? [head(`${vehicle} ทุน`, true), head(`${vehicle} ขาย`, true), head(`${vehicle} กำไร %`, true)]
        : [head(vehicle, true)])),
    ],
    rows: pg.slice.map((row, index) => ({
      key: `${row.carrier}|${row.from}|${row.to}|${index}`,
      style: "",
      cells: [
        cell(row.carrier), cell(row.from), cell(row.to), cell(row.zip, { mono: true }),
        ...VEHICLES.flatMap((vehicle) => {
          const at = moneyAt(row, vehicle);
          if (!selling) return [money(at.cost)];
          return [
            // The cost is greyed beside the price we bill. Both are wanted on
            // the row and only one of them is the answer to "what do we charge".
            money(at.cost, true),
            money(at.sell),
            cell(at.margin == null ? "" : at.margin.toFixed(1),
              {
                mono: true, align: "right", mute: at.margin == null,
                // A lane billed under cost is the thing this table exists to
                // find. Red is not decoration here — and it is never the only
                // signal, since the number beside it carries a minus sign.
                color: at.margin != null && at.margin < 0 ? "#B91C1C" : undefined,
              }),
          ];
        }),
      ],
    })),
    total: pg.total,
    pageCount: pg.pageCount,
    page: pg.p,
    per: pg.per,
    tools: [],
  };
}

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";

export function ChemoursRates({ card, sell, haulers, onLoad, onLoadSell, onSave, canSave, saving, onToast }: {
  card: RateCard | null;
  /**
   * What we bill the customer, when it has been loaded.
   *
   * Kept apart from `card` rather than merged into it as another carrier. They
   * are two sides of one lane, not two quotes for it, and a selling price
   * sitting in the carrier dropdown would eventually be compared against a
   * haulier and chosen as the cheaper of the two.
   */
  sell: RateCard | null;
  /** Hauliers the register already knows, so the name is not typed twice. */
  haulers: string[];
  onLoad: (file: File, hauler: string) => void;
  onLoadSell: (file: File) => void;
  /** Writes one haulier's part of the card to the register. */
  onSave: (hauler: string) => void;
  /** False for an account that may read the card but not change it. */
  canSave: boolean;
  saving: boolean;
  onToast: (message: string) => void;
}) {
  const [carrier, setCarrier] = useState("ALL");
  /** Whose card is about to be opened. Nothing loads until this is filled. */
  const [loading, setLoading] = useState("");
  /**
   * The diesel price the whole card is read at.
   *
   * Every price on this card is one of eleven, chosen by this number, so it is
   * a control rather than a constant — the contract's fuel clause moves the
   * rate about 3% each time diesel crosses a band.
   */
  const [diesel, setDiesel] = useState(DIESEL_DEFAULT);

  const price = Number(diesel.replace(/,/g, ""));
  const readable = Number.isFinite(price) && price > 0;
  const band = card && readable ? bandForDiesel(card.bands, price) : -1;

  const [page, setPage] = useState(1);
  /** The card filling the screen with everything else hidden, as on My Job. */
  const [full, setFull] = useState(false);
  /** Whether the lanes behind the unpriced summary are listed out. */
  const [showGaps, setShowGaps] = useState(false);

  const rows = useMemo(() => (card ? laneRows(card, carrier) : []), [card, carrier]);

  // Picking another haulier is a different card, and page four of the last one
  // is not a place to land. Read during render on the value changing rather
  // than in an effect, which would draw the wrong page for a frame first.
  const [shown, setShown] = useState(carrier);
  if (shown !== carrier) { setShown(carrier); setPage(1); }
  const carriers = useMemo(
    () => [...new Set((card?.lanes ?? []).map((lane) => lane.carrier))].sort(),
    [card],
  );

  /**
   * The selling lanes by origin and destination postcode.
   *
   * Built once for the card rather than searched per cell — fifty lanes times
   * three trucks is a hundred and fifty scans of the same list otherwise, on
   * every keystroke in the diesel box.
   */
  const selling = useMemo(() => (sell ? chemoursSellIndex(sell.lanes) : null), [sell]);

  const moneyAt = (row: LaneRow, vehicle: string) => {
    const costLane = row.lanes.find((l) => l.prices[vehicle]);
    const cost = card && band >= 0 && costLane
      ? priceFor(costLane, vehicle, card.bands, price) : null;

    // Read against the selling card's own fuel clause, not the haulier's. They
    // agree today — both run 28.01 to 50.00 in eleven steps — and the day they
    // do not, each side must still be the price its own contract names.
    const sellLane = selling?.get(chemoursLaneKey(row.from, row.zip));
    const sellPrice = sell && sellLane && readable
      ? priceFor(sellLane, vehicle, sell.bands, price) : null;

    return { cost, sell: sellPrice, margin: chemoursMargin(cost, sellPrice) };
  };

  /**
   * The lines the customer asked about and nobody priced, gathered by vehicle.
   *
   * Whether a haulier has quoted the same vehicle is worked out from the cost
   * card rather than assumed. On this account nobody has: the cost card is six
   * sheets of 4W, 6W and 10W and there is no container sheet at all. That
   * changes what the gap means — it is not a selling price waiting to be set
   * over a known cost, it is a quote nobody has asked the haulier for.
   */
  const gaps = useMemo(() => {
    const byVehicle = new Map<string, { vehicle: string; note: string; lines: UnpricedLine[] }>();
    for (const line of sell?.unpriced ?? []) {
      const held = byVehicle.get(line.vehicle);
      if (held) held.lines.push(line);
      else byVehicle.set(line.vehicle, { vehicle: line.vehicle, note: line.note, lines: [line] });
    }
    return [...byVehicle.values()].map((group) => ({
      ...group,
      costed: (card?.lanes ?? []).some((lane) => lane.prices[group.vehicle]),
    }));
  }, [sell, card]);

  /**
   * Cost lanes on screen with no selling price behind them.
   *
   * Said out loud rather than left as a column of dashes. The cost card carries
   * lanes the RFP answer does not, and a reader scanning for margins needs to
   * know that a blank is a lane we never quoted the customer for — not one
   * where the join failed.
   */
  const unmatched = useMemo(() => (selling
    ? rows.filter((row) => !selling.get(chemoursLaneKey(row.from, row.zip))).length
    : 0), [rows, selling]);

  function exportCard() {
    if (!card || !rows.length) { onToast("ยังไม่มีการ์ดราคาให้ส่งออก"); return; }
    const head = ["Carrier", "Origin", "Destination", "ZIP",
      ...VEHICLES.flatMap((vehicle) => (sell
        ? [`${vehicle} cost`, `${vehicle} sell`, `${vehicle} margin %`]
        : [vehicle])),
    ];
    const body = rows.map((row) => [
      row.carrier, row.from, row.to, row.zip,
      ...VEHICLES.flatMap((vehicle) => {
        const at = moneyAt(row, vehicle);
        const show = (value: number | null) => (value == null ? "" : value);
        // Numbers, not text. This workbook gets pivoted and summed the moment
        // it lands, and a column of strings that look like money does not add
        // up — quietly, with a total of zero at the bottom.
        return sell
          ? [show(at.cost), show(at.sell), at.margin == null ? "" : Number(at.margin.toFixed(1))]
          : [show(at.cost)];
      }),
    ]);
    // The diesel price is printed above the table because the numbers under it
    // are meaningless without it — the same lane is eleven different prices.
    const sheet = XLSX.utils.aoa_to_sheet([
      ["Customer", ":", "", "CHEMOURS"],
      ["Diesel", ":", "", diesel + " บาท" + (band >= 0 ? "  (" + card.bands[band].label + ")" : "")],
      // Said on the sheet, because a margin column with no definition beside it
      // gets read as a mark-up on cost by whoever opens it next.
      ...(sell ? [["Margin", ":", "", "กำไรขั้นต้น = (ขาย − ทุน) ÷ ขาย"], ["Selling card", ":", "", sell.file]] : []),
      [],
      head, ...body,
    ]);
    sheet["!cols"] = head.map((h) => ({ wch: Math.max(11, h.length + 6) }));
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, sheet, "Rates");

    // The gaps go out as their own sheet, in the shape of the question rather
    // than of an answer. Somebody has to take this list to the haulier before
    // any of it can be priced, and a list they can send is the useful form of
    // it — one row per lane, with what is missing named.
    if (gaps.length) {
      const gapRows = gaps.flatMap((gap) => gap.lines.map((line) => [
        line.from, line.to, line.county, line.vehicle, line.note,
        gap.costed ? "มีราคาทุนแล้ว" : "ยังไม่มีราคาทุน",
      ]));
      const gapHead = ["Origin", "Destination", "ZIP", "Truck type", "Note", "Cost"];
      const gapSheet = XLSX.utils.aoa_to_sheet([
        ["Customer", ":", "", "CHEMOURS"],
        ["", "", "", "ลูกค้าขอราคาไว้แต่ยังไม่ได้เสนอ"],
        [],
        gapHead, ...gapRows,
      ]);
      gapSheet["!cols"] = [{ wch: 26 }, { wch: 34 }, { wch: 9 }, { wch: 13 }, { wch: 20 }, { wch: 16 }];
      XLSX.utils.book_append_sheet(workbook, gapSheet, "Not quoted");
    }

    XLSX.writeFile(workbook, `Chemours_rates_${carrier === "ALL" ? "ALL" : carrier}.xlsx`);
    const missing = gaps.reduce((sum, gap) => sum + gap.lines.length, 0);
    onToast(`ส่งออก ${rows.length} เส้นทางแล้ว`
      + (missing ? ` · แนบรายการที่ยังไม่ได้เสนอราคา ${missing} รายการไว้ในชีต Not quoted` : ""));
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>การ์ดนี้เป็นของผู้ขนส่ง</span>
          <input
            list="chemours-haulers"
            value={loading}
            onChange={(e) => setLoading(e.target.value)}
            placeholder="เช่น SSL, THAIKOT"
            style={css(CONTROL + ";min-width:190px")}
          />
          <datalist id="chemours-haulers">
            {haulers.map((name) => <option key={name} value={name} />)}
          </datalist>
        </label>

        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ไฟล์การ์ดราคา</span>
          <input
            type="file"
            accept=".xlsx,.xlsm,.xls"
            disabled={!loading.trim()}
            title={loading.trim() ? "" : "ใส่ชื่อผู้ขนส่งก่อน — ในไฟล์ไม่ได้บอกไว้"}
            onChange={(e) => {
              const chosen = e.target.files?.[0];
              if (chosen) onLoad(chosen, loading.trim());
              e.target.value = "";
            }}
            style={css("font-size:12px;font-family:inherit;max-width:250px" + (loading.trim() ? "" : ";opacity:.45"))}
          />
        </label>

        {/* Its own picker, and no hauler box beside it. This card is ours, so
            there is nobody to name — and asking for one would invite somebody
            to file the selling prices under a haulier. */}
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ไฟล์ราคาขาย (วางบิลลูกค้า)</span>
          <input
            type="file"
            accept=".xlsx,.xlsm,.xls"
            onChange={(e) => {
              const chosen = e.target.files?.[0];
              if (chosen) onLoadSell(chosen);
              e.target.value = "";
            }}
            style={css("font-size:12px;font-family:inherit;max-width:250px")}
          />
        </label>

        {card && (
          <>
            <label style={css("display:flex;flex-direction:column;gap:3px")}>
              <span style={css(LABEL)}>ผู้ขนส่ง</span>
              <select value={carrier} onChange={(e) => setCarrier(e.target.value)} style={css(CONTROL)}>
                <option value="ALL">ทั้งหมด · {card.lanes.length} แถว</option>
                {carriers.map((name) => <option key={name} value={name}>{name}</option>)}
              </select>
            </label>

            <label style={css("display:flex;flex-direction:column;gap:3px")}>
              <span style={css(LABEL)}>ราคาน้ำมันดีเซล</span>
              <input
                value={diesel}
                onChange={(e) => setDiesel(e.target.value)}
                style={css(CONTROL + ";width:98px;font-family:'IBM Plex Mono',monospace")}
              />
            </label>

            <div style={css("display:flex;flex-direction:column;gap:2px")}>
              <span style={css(LABEL)}>ช่วงราคาที่ใช้</span>
              {/* Where the default came from and how old it is. The box is the
                  one control on this screen that changes every number under it,
                  and a figure with no date on it invites somebody to trust a
                  stale one. */}
              {/* One clause per haulier, so with more than one on screen there
                  is no single band to name. Each row is still priced against
                  its own haulier's clause; naming one of them here would be a
                  claim about rows it does not cover. */}
              <span style={css("font-size:13.5px;font-weight:600;color:" + (band >= 0 ? "#0A2240" : "#B45309"))}>
                {!readable ? "อ่านราคาน้ำมันไม่ออก"
                  : carriers.length > 1 && carrier === "ALL" ? "แต่ละรายใช้ช่วงของตัวเอง"
                  : band >= 0 ? card.bands[band].label
                  : "เกินช่วงสูงสุดที่การ์ดนี้ระบุไว้"}
              </span>
              <span style={css("font-size:10.5px;color:#94A3B8")}>
                ค่าตั้งต้น {DIESEL.price} · {DIESEL.effective} · {DIESEL.source}
              </span>
            </div>

            <div style={css("display:flex;gap:8px;margin-left:auto")}>
              {/* Whatever is on screen is what gets saved, one haulier at a
                  time underneath. It says which, so nobody has to work out what
                  the button is about to write. */}
              <button
                onClick={() => onSave(carrier)}
                disabled={saving || !canSave}
                title={canSave ? "" : "ต้องใช้บัญชีระดับ Assistant Manager ขึ้นไปจึงจะบันทึกราคาได้"}
                style={css("height:32px;padding:0 15px;border-radius:4px;font-size:12.5px;font-weight:600;font-family:inherit;"
                  + (saving || !canSave
                    ? "border:1px solid #E7ECF2;background:#FAFBFC;color:#B4C0CC;cursor:default"
                    : "border:1px solid #0A6E8A;background:#fff;color:#0A6E8A;cursor:pointer"))}
              >
                {!canSave ? "บันทึกได้เฉพาะผู้มีสิทธิ์แก้ราคา"
                  : saving ? "กำลังบันทึก…"
                  : carrier === "ALL"
                    ? `บันทึกเข้าระบบ (${carriers.length} ราย)`
                    : `บันทึกเข้าระบบ (${carrier})`}
              </button>
              <button
                onClick={exportCard}
                style={css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}
              >
                Export Excel
              </button>
            </div>
          </>
        )}
      </div>

      {!card ? (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:22px 20px;font-size:12px;color:#7B8CA0;line-height:1.8;max-width:72ch")}>
          เลือกไฟล์สรุปงานของลูกค้าด้านบน ระบบจะอ่านชีตราคาในไฟล์นั้นเอง — หนึ่งชีตต่อหนึ่งขนาดรถ
          โดยดูขนาดรถจากหัวข้อเหนือช่องราคา ไม่ใช่จากชื่อแท็บ ถ้าสองอย่างไม่ตรงกัน ชีตนั้นจะถูกปฏิเสธ
          และรายงานขึ้นมา แทนที่จะเดาว่าเป็นรถกี่ล้อ
          <div style={css("margin-top:14px;color:#94A3B8")}>
            ราคาที่เปิดตรงนี้อยู่ในเครื่องคุณเท่านั้น ไม่ได้ส่งขึ้นเซิร์ฟเวอร์ ไม่ได้เก็บลงฐานข้อมูล
            และไม่ได้นำไปรวมกับตารางราคาของผู้ขนส่งรายอื่น — มันเป็นราคาของลูกค้ารายนี้รายเดียว
            ไม่ควรไปโผล่ในงานของลูกค้ารายอื่น
          </div>
        </div>
      ) : (
        <>
          {/* Lines the customer asked to have priced, that carry none.
              These used to be counted and dropped: the 20'/40'GP row on every
              lane of the RFP, priced at none of the eleven bands. A count told
              nobody which lanes, and dropping them meant the question the
              customer asked disappeared along with the rows. */}
          {!!gaps.length && (
            <div style={css("background:#FFFBEB;border:1px solid #FDE68A;border-radius:6px;padding:12px 15px;font-size:12px;color:#7C4A03;line-height:1.75")}>
              <b>ลูกค้าขอราคาไว้แต่ยังไม่ได้เสนอ {gaps.reduce((sum, gap) => sum + gap.lines.length, 0)} รายการ</b>
              {gaps.map((gap) => (
                <div key={gap.vehicle}>
                  {gap.vehicle} · {gap.lines.length} เส้นทาง{gap.note ? ` · ${gap.note}` : ""}
                  {" — "}
                  {gap.costed
                    ? "มีราคาทุนของผู้ขนส่งแล้ว ตั้งราคาขายได้เลย"
                    : "ยังไม่มีราคาทุนของรถประเภทนี้ในการ์ดผู้ขนส่งด้วย ต้องขอราคาจากผู้ขนส่งก่อน"}
                </div>
              ))}
              <button
                onClick={() => setShowGaps((on) => !on)}
                style={css("margin-top:7px;height:26px;padding:0 11px;border:1px solid #D9A441;background:#fff;color:#7C4A03;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}
              >
                {showGaps ? "ซ่อนรายชื่อเส้นทาง" : "ดูว่าเป็นเส้นทางไหนบ้าง"}
              </button>
              {showGaps && (
                <div style={css("margin-top:8px;max-height:220px;overflow:auto;font-size:11.5px;font-family:'IBM Plex Mono',monospace;line-height:1.85")}>
                  {gaps.flatMap((gap) => gap.lines).map((line, index) => (
                    <div key={index}>
                      {line.from} → {line.to} · {line.county} · {line.vehicle}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Conditions off the card, under the prices they qualify. These are
              contract terms that were being dropped on the floor: the
              return-load line is worth half a trip's rate on every backhaul and
              lived only in a workbook nobody reopens. Shown with the haulier
              and the sheet, because THAI KOT carries it on SCGJWD and not on
              Unithai and a list that flattened that would be misleading. */}
          {!!card.notes.length && (
            <div style={css("background:#F1F7FB;border:1px solid #CBE0EE;border-radius:6px;padding:12px 15px;font-size:12px;color:#12496B;line-height:1.75")}>
              <b>เงื่อนไขที่เขียนไว้ในการ์ด {card.notes.length} ข้อ</b>
              {card.notes
                .filter((note) => carrier === "ALL" || note.carrier === carrier)
                .map((note, index) => (
                  <div key={index}>
                    <span style={css("color:#5B7A91")}>{note.carrier} · {note.sheet}</span>{" — "}{note.text}
                  </div>
                ))}
              <div style={css("margin-top:7px;color:#5B7A91;font-size:11.5px")}>
                ระบบไม่ได้คิดเงื่อนไขเหล่านี้ให้เอง ยกเว้นงานรับกลับ ซึ่งติ๊กได้ในตาราง งาน Domestic
                แล้วจะคิดครึ่งราคาของเที่ยวนั้นให้ · งานรับกลับที่เป็น Finished goods คิด 80%
                ตามที่ทีมบัญชีกำหนด ไม่ได้เขียนไว้ในการ์ดใบนี้
              </div>
            </div>
          )}

          {!!card.issues.length && (
            <div style={css("background:#FFF7ED;border:1px solid #FED7AA;border-radius:6px;padding:12px 15px;font-size:11.5px;color:#9A3412;line-height:1.7")}>
              <b>อ่านไม่ได้ {card.issues.length} ชีต</b>
              {card.issues.map((issue, index) => (
                <div key={index}>{issue.sheet}: {issue.message}{issue.value ? ` (${issue.value})` : ""}</div>
              ))}
            </div>
          )}

          <div style={css("font-size:11px;color:#7B8CA0;line-height:1.6")}>
            {card.file} · {rows.length} เส้นทาง · {card.lanes.length} แถวราคา · {card.bands.length} ช่วงราคาน้ำมัน ·
            ราคาที่แสดงคือราคาที่ช่วงน้ำมันด้านบน เปลี่ยนตัวเลขแล้วทั้งตารางเปลี่ยนตาม
            {sell && (
              <>
                <br />
                ราคาขาย: {sell.file} · {sell.lanes.length} เส้นทาง · จับคู่กับต้นทุนด้วยต้นทางและรหัสไปรษณีย์ปลายทาง ·{" "}
                <b>กำไรขั้นต้น = (ขาย − ทุน) ÷ ขาย</b> ไม่ใช่บวกเพิ่มจากทุน
                {unmatched > 0 && (
                  <>
                    {" · "}
                    <span style={css("color:#B45309")}>
                      {unmatched} เส้นทางในตารางนี้ยังไม่มีราคาขาย
                    </span>
                  </>
                )}
              </>
            )}
          </div>

          {rows.length === 0 ? (
            <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
              ไม่มีเส้นทางของผู้ขนส่งที่เลือก
            </div>
          ) : (
            /* The same grid My Job and the truck report draw: the heading holds
               while a long card is read, the zoom fits more lanes on a screen,
               and full screen is there for comparing two hauliers. Not `fill`,
               because the diesel price and the file pickers above it are part
               of reading the card and a locked page would squeeze them. */
            <DataTable
              model={rateModel(rows, moneyAt, carrier, !!sell, page)}
              full={full}
              onFull={() => setFull((on) => !on)}
              onPage={setPage}
              onTool={() => undefined} />
          )}
        </>
      )}
    </div>
  );
}
