"use client";

import { useMemo, useState } from "react";
import { bandForDiesel, priceFor, vehiclesIn, type RateBook, type RateLane } from "../rates";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

/**
 * Transportation rates, as the subcontractors actually quoted them.
 *
 * Built around one question — what does this lane cost today — rather than
 * around the form's shape. Every carrier prices in diesel bands, and no two
 * carriers agree on where the bands sit: the LESCHACO form steps at 36.30 and
 * DGT's per-customer contracts step at 36.00. Showing twenty-four band columns
 * would be honest and useless. Showing the price at one diesel price is honest
 * and answers the question, with the band that produced it named beside it so
 * the number can be traced back to the contract.
 */

type Props = {
  book: RateBook | null;
  error: string;
  /** Shared with Truck Booking — one diesel price decides every rate in the app. */
  diesel: number;
  onDiesel: (value: number) => void;
  onToast: (message: string) => void;
};

const PER_PAGE = 40;

/**
 * The source picker's wording.
 *
 * Thai on screen, the API's word underneath — the two are joined here rather
 * than by a second copy of the mapping inside the handler.
 */
const SOURCE_LABEL = {
  All: "ทั้งหมด",
  carrier: "จากใบเสนอราคาผู้รับเหมา",
  quotation: "จาก Rate Quotation",
} as const;

type Row = {
  lane: RateLane;
  vehicle: string;
  price: number;
  band: string;
};

export function Rates({ book, error, diesel, onDiesel, onToast }: Props) {
  const [carrier, setCarrier] = useState("All");
  const [service, setService] = useState("All");
  const [vehicle, setVehicle] = useState("All");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [compare, setCompare] = useState(false);
  /*
   * Which prices to show.
   *
   * A price a carrier signed and a price somebody typed this morning are both
   * rates, and both belong here — but only one of them is a contract. They are
   * shown together by default because the question the screen answers is "what
   * does this journey cost", and separable because the question underneath it
   * is sometimes "what have we actually agreed".
   */
  const [source, setSource] = useState<"All" | "carrier" | "quotation">("All");

  const carriers = useMemo(
    () => [...new Set(book?.lanes.map((l) => l.carrier) ?? [])].sort(),
    [book],
  );
  const services = useMemo(
    () => [...new Set(book?.lanes.map((l) => l.service) ?? [])].sort(),
    [book],
  );
  const vehicles = useMemo(() => (book ? vehiclesIn(book.lanes) : []), [book]);

  /** One row per lane and vehicle, priced at the diesel price on screen. */
  const rows = useMemo<Row[]>(() => {
    if (!book) return [];
    const out: Row[] = [];
    const wanted = query.trim().toLowerCase();

    for (const lane of book.lanes) {
      // A row with no source is a carrier row: the browser's own reader builds
      // the book that way, and every one of those came off a signed form.
      if (source !== "All" && (lane.source ?? "carrier") !== source) continue;
      if (carrier !== "All" && lane.carrier !== carrier) continue;
      if (service !== "All" && lane.service !== service) continue;
      if (wanted && ![lane.customer, lane.from, lane.to, lane.county, lane.carrier]
        .some((field) => field.toLowerCase().includes(wanted))) continue;

      for (const type of Object.keys(lane.prices)) {
        if (vehicle !== "All" && type !== vehicle) continue;
        const price = priceFor(lane, type, book.bands, diesel);
        if (price === null) continue;
        const index = bandForDiesel(book.bands, diesel);
        out.push({ lane, vehicle: type, price, band: book.bands[index]?.label ?? "—" });
      }
    }
    return out.sort((a, b) => a.lane.customer.localeCompare(b.lane.customer) || a.price - b.price);
  }, [book, carrier, service, vehicle, query, diesel, source]);

  /**
   * The same lane quoted by more than one carrier. This is the reason the
   * screen exists: it is where a cheaper carrier for work already running shows
   * up. Lanes only one carrier quotes are left out — there is nothing to choose.
   *
   * Matching on the destination alone would put every lane that ends at LCB
   * PORT in one group and call a 9,000 baht run from Chonburi dearer than a
   * 2,500 baht one from the yard next door. The end points have to agree.
   *
   * They are compared as an unordered pair with the customer, because the
   * carriers do not agree on which column is which: TNB write the customer in
   * Customer and the pickup in From, SANGJA write them the other way round, and
   * the two files describe the same journey.
   */
  const contested = useMemo(() => {
    const norm = (value: string) => value.toLowerCase().replace(/[\s.,()\-/]+/g, "");
    const groups = new Map<string, Row[]>();
    for (const row of rows) {
      const ends = [norm(row.lane.customer), norm(row.lane.from)].sort().join(">");
      const key = `${ends}|${norm(row.lane.to)}|${row.vehicle}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(row);
    }
    return [...groups.entries()]
      .map(([key, group]) => {
        const carriersHere = new Set(group.map((r) => r.lane.carrier));
        return { key, group: group.slice().sort((a, b) => a.price - b.price), carriers: carriersHere.size };
      })
      .filter((entry) => entry.carriers > 1)
      .sort((a, b) => {
        const spreadA = a.group[a.group.length - 1].price - a.group[0].price;
        const spreadB = b.group[b.group.length - 1].price - b.group[0].price;
        return spreadB - spreadA;
      });
  }, [rows]);

  if (error) {
    return (
      <div style={css("background:#fff;border:1px solid #F3C9C4;border-left:3px solid #B42318;border-radius:5px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B42318;margin-bottom:5px")}>โหลดตารางราคาไม่สำเร็จ</div>
        <div style={css("font-size:12.5px;color:#5A6B7D")}>{error}</div>
        <div style={css("font-size:12px;color:#94A3B8;margin-top:8px")}>
          สร้างไฟล์ใหม่ด้วย <code>node migration/build-rates.mjs</code>
        </div>
      </div>
    );
  }

  if (!book) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        กำลังโหลดตารางราคา…
      </div>
    );
  }

  const shown = compare ? contested.flatMap((entry) => entry.group) : rows;
  const pages = Math.max(1, Math.ceil(shown.length / PER_PAGE));
  const safePage = Math.min(page, pages);
  const slice = shown.slice((safePage - 1) * PER_PAGE, safePage * PER_PAGE);
  const cheapest = new Set(contested.map((entry) => entry.group[0]));

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <Summary book={book} rows={rows} contested={contested.length} diesel={diesel} />

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:4px")}>
          <span style={LABEL}>ราคาดีเซล (บาท/ลิตร)</span>
          <input
            type="number"
            step="0.01"
            min="0"
            value={diesel}
            onChange={(e) => { onDiesel(Number(e.target.value) || 0); setPage(1); }}
            style={css("width:110px;height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:13px;font-family:ui-monospace,monospace;font-weight:600;color:#0A2240")}
          />
        </label>

        <Picker label="ที่มาของราคา" value={SOURCE_LABEL[source]}
          options={Object.values(SOURCE_LABEL)}
          onChange={(v) => {
            const key = (Object.keys(SOURCE_LABEL) as (keyof typeof SOURCE_LABEL)[])
              .find((one) => SOURCE_LABEL[one] === v) ?? "All";
            setSource(key);
            setPage(1);
          }} />
        <Picker label="ผู้รับเหมา" value={carrier} options={["All", ...carriers]} onChange={(v) => { setCarrier(v); setPage(1); }} />
        <Picker label="บริการ" value={service} options={["All", ...services]} onChange={(v) => { setService(v); setPage(1); }} />
        <Picker label="ประเภทรถ/ตู้" value={vehicle} options={["All", ...vehicles]} onChange={(v) => { setVehicle(v); setPage(1); }} />

        <label style={css("display:flex;flex-direction:column;gap:4px;flex:1;min-width:180px")}>
          <span style={LABEL}>ค้นหา ลูกค้า / ต้นทาง / ปลายทาง</span>
          <input
            value={query}
            onChange={(e) => { setQuery(e.target.value); setPage(1); }}
            placeholder="เช่น ALLNEX, แหลมฉบัง, ระยอง"
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:13px")}
          />
        </label>

        <button
          onClick={() => { setCompare((v) => !v); setPage(1); }}
          style={css(
            "height:32px;padding:0 14px;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;border:1px solid " +
            (compare ? "#0A2240" : "#C9D6E2") + ";background:" + (compare ? "#0A2240" : "#fff") +
            ";color:" + (compare ? "#fff" : "#0A2240"),
          )}
        >
          {compare ? "✓ เฉพาะเส้นทางที่มีหลายเจ้า" : "เทียบราคาหลายเจ้า"}
        </button>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
          <span style={css("font-size:12.5px;color:#465A6E")}>
            <b style={css("color:#0A2240")}>{shown.length.toLocaleString()}</b> ราคา
            {compare && <> · {contested.length} เส้นทางที่เทียบได้</>}
            {" "}· ที่ดีเซล <b style={css("font-family:ui-monospace,monospace;color:#0A2240")}>{diesel.toFixed(2)}</b> บาท
          </span>
          <span style={css("font-size:11.5px;color:#94A3B8")}>
            ราคาถูกสุดของเส้นทางมีเครื่องหมาย ● เขียว
          </span>
        </div>

        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead>
              <tr>
                {["ผู้รับเหมา", "บริการ", "ลูกค้า", "ต้นทาง", "ปลายทาง", "จังหวัด", "ประเภท", "ราคา (บาท)", "ช่วงน้ำมันที่ใช้"].map((h, i) => (
                  <th key={h} style={css(
                    "position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:" + (i === 7 ? "right" : "left") +
                    ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap",
                  )}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {slice.map((row, i) => {
                const best = cheapest.has(row);
                return (
                  <tr
                    key={row.lane.id + row.vehicle + i}
                    onClick={() => onToast(`${row.lane.carrier} · ${row.lane.to || row.lane.customer} · ${row.vehicle} = ${row.price.toLocaleString()} บาท`)}
                    style={css("cursor:pointer;border-bottom:1px solid #F1F5F9")}
                  >
                    <td style={CELL}>
                      <b style={css("color:#0A2240")}>{row.lane.carrier || "—"}</b>
                      {/*
                        Said on the row, not only in the filter. Somebody
                        reading a price off this table is about to quote it, and
                        "a carrier signed this" and "we worked it out from one
                        figure in the rate sheet" are not the same promise.
                      */}
                      {row.lane.source === "quotation" && (
                        <span
                          title="ราคาจาก Rate Quotation — กรอกไว้ช่องเดียว แล้วไล่ขึ้นตามช่วงราคาน้ำมัน +3% ปัดขึ้น"
                          style={css("margin-left:6px;font-size:9.5px;font-weight:700;padding:1px 5px;"
                            + "border-radius:3px;background:#FDF2E3;color:#B45309;white-space:nowrap")}>
                          QUOTE
                        </span>
                      )}
                    </td>
                    <td style={CELL}>
                      <span style={css("font-size:10.5px;font-weight:600;padding:2px 7px;border-radius:3px;background:#E7F0FA;color:#1D5FA8")}>{row.lane.service}</span>
                    </td>
                    <td style={CELL}>{row.lane.customer || "—"}</td>
                    <td style={css(CELL_RAW + ";color:#7B8CA0")}>{row.lane.from || "—"}</td>
                    <td style={CELL}>{row.lane.to || "—"}</td>
                    <td style={css(CELL_RAW + ";color:#7B8CA0")}>{row.lane.county || "—"}</td>
                    <td style={css(CELL_RAW + ";font-family:ui-monospace,monospace")}>{row.vehicle}</td>
                    <td style={css(CELL_RAW + ";text-align:right;font-family:ui-monospace,monospace;font-weight:600;color:" + (best ? "#16794C" : "#16232F"))}>
                      {best && "● "}{row.price.toLocaleString()}
                    </td>
                    <td style={css(CELL_RAW + ";font-family:ui-monospace,monospace;font-size:11.5px;color:#94A3B8")}>{row.band}</td>
                  </tr>
                );
              })}
              {!slice.length && (
                <tr><td colSpan={9} style={css("padding:30px;text-align:center;color:#94A3B8;font-size:12.5px")}>
                  ไม่พบราคาที่ตรงกับเงื่อนไข
                </td></tr>
              )}
            </tbody>
          </table>
        </ZoomBox>

        {pages > 1 && (
          <div style={css("padding:10px 16px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;background:#FBFCFD")}>
            <span style={css("font-size:12px;color:#7B8CA0")}>หน้า {safePage} / {pages}</span>
            <span style={css("display:flex;gap:6px")}>
              <Pager label="‹ ก่อนหน้า" disabled={safePage <= 1} onClick={() => setPage(safePage - 1)} />
              <Pager label="ถัดไป ›" disabled={safePage >= pages} onClick={() => setPage(safePage + 1)} />
            </span>
          </div>
        )}
      </div>

      <Surcharges book={book} />
      <Coverage book={book} />
    </div>
  );
}

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const CELL_RAW = "padding:8px 12px;vertical-align:top";
const CELL = css(CELL_RAW);

function Picker({ label, value, options, onChange }: {
  label: string; value: string; options: string[]; onChange: (v: string) => void;
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:4px")}>
      <span style={LABEL}>{label}</span>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;min-width:130px")}
      >
        {options.map((option) => <option key={option} value={option}>{option === "All" ? "ทั้งหมด" : option}</option>)}
      </select>
    </label>
  );
}

function Pager({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      style={css(
        "height:28px;padding:0 12px;border:1px solid #D8E0E8;border-radius:4px;font-size:12px;background:#fff;color:" +
        (disabled ? "#C3CFDB" : "#0A2240") + ";cursor:" + (disabled ? "default" : "pointer"),
      )}
    >{label}</button>
  );
}

function Summary({ book, rows, contested, diesel }: {
  book: RateBook; rows: Row[]; contested: number; diesel: number;
}) {
  const carriers = new Set(book.lanes.map((l) => l.carrier)).size;
  const band = book.bands[bandForDiesel(book.bands, diesel)];
  const prices = rows.map((r) => r.price);
  const average = prices.length ? Math.round(prices.reduce((a, b) => a + b, 0) / prices.length) : 0;

  const tiles: [string, string, string, string][] = [
    ["เส้นทางทั้งหมด", book.lanes.length.toLocaleString(), `${carriers} ผู้รับเหมา`, "#0A2240"],
    ["ราคาที่ใช้ได้ตอนนี้", rows.length.toLocaleString(), band ? `ช่วง ${band.label}` : "นอกช่วงที่เสนอ", "#16794C"],
    ["เทียบได้หลายเจ้า", contested.toLocaleString(), "เส้นทาง+ประเภทเดียวกัน", "#1D5FA8"],
    ["ราคาเฉลี่ย", average ? average.toLocaleString() : "—", "บาท/เที่ยว", "#B45309"],
  ];

  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:11px")}>
      {tiles.map(([label, value, note, colour]) => (
        <div key={label} style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:12px 15px 14px`)}>
          <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
          <div style={css(`font-family:ui-monospace,monospace;font-size:25px;font-weight:600;line-height:1.25;margin-top:3px;color:${colour}`)}>{value}</div>
          <div style={css("font-size:12px;color:#7B8CA0")}>{note}</div>
        </div>
      ))}
    </div>
  );
}

function Surcharges({ book }: { book: RateBook }) {
  const [open, setOpen] = useState(false);
  if (!book.surcharges.length) return null;

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <button
        onClick={() => setOpen((v) => !v)}
        style={css("width:100%;padding:12px 16px;background:#fff;border:none;display:flex;justify-content:space-between;align-items:center;cursor:pointer;font-size:13px;font-weight:650;color:#0A2240")}
      >
        <span>{open ? "▾" : "▸"} ค่าใช้จ่ายเพิ่มเติมตามสัญญา ({book.surcharges.length} รายการ)</span>
        <span style={css("font-size:11.5px;font-weight:400;color:#94A3B8")}>จากชีท Remark ของฟอร์มกลาง</span>
      </button>
      {open && (
        <div style={css("border-top:1px solid #E9EFF5")}><ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead>
              <tr>{["บริการ", "รายการ", "สกุล", "อัตรา", "หน่วย"].map((h) => (
                <th key={h} style={css("padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr>
            </thead>
            <tbody>
              {book.surcharges.map((charge, i) => (
                <tr key={charge.service + charge.no + i} style={css("border-bottom:1px solid #F1F5F9")}>
                  <td style={css(CELL_RAW + ";font-size:11px;color:#1D5FA8;font-weight:600")}>{charge.service}</td>
                  <td style={CELL}>{charge.description}</td>
                  <td style={css(CELL_RAW + ";color:#7B8CA0")}>{charge.currency}</td>
                  <td style={css(CELL_RAW + ";font-family:ui-monospace,monospace;font-weight:600")}>{charge.rate}</td>
                  <td style={css(CELL_RAW + ";color:#7B8CA0;font-size:11.5px")}>{charge.unit}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </ZoomBox></div>
      )}
    </div>
  );
}

/** What did not come through, named. A gap you cannot see is a gap you pay for. */
function Coverage({ book }: { book: RateBook }) {
  const [open, setOpen] = useState(false);
  const unread = book.issues.filter((issue) => issue.field === "layout");
  const blank = book.issues.filter((issue) => issue.field === "price");
  if (!unread.length && !blank.length) return null;

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid #B45309;border-radius:5px;overflow:hidden")}>
      <button
        onClick={() => setOpen((v) => !v)}
        style={css("width:100%;padding:12px 16px;background:#fff;border:none;display:flex;justify-content:space-between;align-items:center;cursor:pointer;font-size:13px;font-weight:650;color:#B45309")}
      >
        <span>{open ? "▾" : "▸"} สิ่งที่อ่านไม่ได้ / ยังไม่มีราคา ({unread.length + blank.length})</span>
        <span style={css("font-size:11.5px;font-weight:400;color:#94A3B8")}>ต้องมีคนตาม</span>
      </button>
      {open && (
        <div style={css("padding:12px 16px;border-top:1px solid #E9EFF5;font-size:12.5px;color:#465A6E;display:flex;flex-direction:column;gap:9px")}>
          {unread.length > 0 && (
            <div>
              <div style={css("font-weight:650;color:#0A2240;margin-bottom:4px")}>ชีทที่ใช้ฟอร์มอื่น — ยังไม่ได้อ่าน</div>
              {unread.map((issue, i) => (
                <div key={i} style={css("color:#7B8CA0;font-size:12px")}>· {issue.file} [{issue.sheet}]</div>
              ))}
            </div>
          )}
          {blank.length > 0 && (
            <div>
              <div style={css("font-weight:650;color:#0A2240;margin-bottom:4px")}>เส้นทางที่ระบุไว้แต่ไม่ได้ใส่ราคา ({blank.length})</div>
              {blank.slice(0, 12).map((issue, i) => (
                <div key={i} style={css("color:#7B8CA0;font-size:12px")}>· {issue.file} แถว {issue.row} — {issue.value}</div>
              ))}
              {blank.length > 12 && <div style={css("color:#94A3B8;font-size:12px")}>… อีก {blank.length - 12} รายการ</div>}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
