"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";
import {
  bandForDiesel, parseChemoursSheet, priceFor,
  type FuelBand, type RateIssue, type RateLane,
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
export type RateCard = { file: string; bands: FuelBand[]; lanes: RateLane[]; issues: RateIssue[] };

export async function readRateCard(file: File): Promise<RateCard> {
  const book = XLSX.read(await file.arrayBuffer(), { cellDates: true });
  const bands: FuelBand[] = [];
  const lanes: RateLane[] = [];
  const issues: RateIssue[] = [];

  for (const sheetName of book.SheetNames) {
    const rows = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], {
      header: 1, blankrows: false, defval: "",
    });
    if (!rows.length) continue;
    const parsed = parseChemoursSheet({ carrier: "", fileName: file.name, sheetName, rows }, bands, issues);
    if (parsed) lanes.push(...parsed.lanes);
  }

  return { file: file.name, bands, lanes, issues };
}

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

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const CELL = "padding:7px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap";

export function ChemoursRates({ card, onLoad, onToast }: {
  card: RateCard | null;
  onLoad: (file: File) => void;
  onToast: (message: string) => void;
}) {
  const [carrier, setCarrier] = useState("ALL");
  /**
   * The diesel price the whole card is read at.
   *
   * Every price on this card is one of eleven, chosen by this number, so it is
   * a control rather than a constant — the contract's fuel clause moves the
   * rate about 3% each time diesel crosses a band.
   */
  const [diesel, setDiesel] = useState("32.94");

  const price = Number(diesel.replace(/,/g, ""));
  const readable = Number.isFinite(price) && price > 0;
  const band = card && readable ? bandForDiesel(card.bands, price) : -1;

  const rows = useMemo(() => (card ? laneRows(card, carrier) : []), [card, carrier]);
  const carriers = useMemo(
    () => [...new Set((card?.lanes ?? []).map((lane) => lane.carrier))].sort(),
    [card],
  );

  const priceAt = (row: LaneRow, vehicle: string) => {
    if (!card || band < 0) return null;
    const lane = row.lanes.find((l) => l.prices[vehicle]);
    return lane ? priceFor(lane, vehicle, card.bands, price) : null;
  };

  function exportCard() {
    if (!card || !rows.length) { onToast("ยังไม่มีการ์ดราคาให้ส่งออก"); return; }
    const head = ["Carrier", "Origin", "Destination", "ZIP", ...VEHICLES];
    const body = rows.map((row) => [
      row.carrier, row.from, row.to, row.zip,
      ...VEHICLES.map((vehicle) => {
        const value = priceAt(row, vehicle);
        return value == null ? "" : String(value);
      }),
    ]);
    // The diesel price is printed above the table because the numbers under it
    // are meaningless without it — the same lane is eleven different prices.
    const sheet = XLSX.utils.aoa_to_sheet([
      ["Customer", ":", "", "CHEMOURS"],
      ["Diesel", ":", "", diesel + " บาท" + (band >= 0 ? "  (" + card.bands[band].label + ")" : "")],
      [],
      head, ...body,
    ]);
    sheet["!cols"] = head.map((h) => ({ wch: Math.max(11, h.length + 6) }));
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, sheet, "Rates");
    XLSX.writeFile(workbook, `Chemours_rates_${carrier === "ALL" ? "ALL" : carrier}.xlsx`);
    onToast(`ส่งออก ${rows.length} เส้นทางแล้ว`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ไฟล์การ์ดราคา</span>
          <input
            type="file"
            accept=".xlsx,.xlsm,.xls"
            onChange={(e) => { const chosen = e.target.files?.[0]; if (chosen) onLoad(chosen); e.target.value = ""; }}
            style={css("font-size:12px;font-family:inherit;max-width:260px")}
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
              <span style={css("font-size:13.5px;font-weight:600;color:" + (band >= 0 ? "#0A2240" : "#B45309"))}>
                {band >= 0
                  ? card.bands[band].label
                  : readable
                    ? "เกินช่วงสูงสุดที่การ์ดนี้ระบุไว้"
                    : "อ่านราคาน้ำมันไม่ออก"}
              </span>
            </div>

            <button
              onClick={exportCard}
              style={css("height:32px;padding:0 16px;margin-left:auto;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}
            >
              Export Excel
            </button>
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
          </div>

          <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
            {rows.length === 0 ? (
              <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                ไม่มีเส้นทางของผู้ขนส่งที่เลือก
              </div>
            ) : (
              <div style={css("overflow-x:auto")}>
                <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
                  <thead>
                    <tr>
                      {["ผู้ขนส่ง", "ต้นทาง", "ปลายทาง", "ZIP", ...VEHICLES].map((head) => (
                        <th
                          key={head}
                          style={css("background:#F4F7FA;padding:7px 10px;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap;text-align:"
                            + (VEHICLES.indexOf(head) >= 0 ? "right" : "left"))}
                        >
                          {head}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((row, index) => (
                      <tr key={index} className="row-hover">
                        <td style={css(CELL)}>{row.carrier}</td>
                        <td style={css(CELL)}>{row.from}</td>
                        <td style={css(CELL)}>{row.to}</td>
                        <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace")}>{row.zip || "—"}</td>
                        {VEHICLES.map((vehicle) => {
                          const value = priceAt(row, vehicle);
                          return (
                            <td
                              key={vehicle}
                              style={css(CELL + ";text-align:right;font-family:'IBM Plex Mono',monospace"
                                + (value == null ? ";color:#B4C0CC" : ""))}
                            >
                              {value == null ? "—" : value.toLocaleString("en-US")}
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
