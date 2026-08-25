"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";
import type { Job } from "../ops";
import { monthKey, monthKeyLabel } from "../period";
import { dnum, kilos } from "../util";
import { apiFetch } from "../api";
import { CargoForm, type FormTemplate } from "./CargoForm";
import { ChemoursRates, readRateCard, type RateCard } from "./ChemoursRates";

/**
 * The Chemours delivery details, in the shape the account already receives.
 *
 * Taken column for column from `Del details-CHEM-(DTT)`: the workbook the
 * customer is sent, so what this produces can be checked against last month's
 * file line by line rather than argued about.
 *
 * Unlike the L'OREAL report, every column here has a real source. This is a
 * delivery run — a warehouse, a job number, a pick-up date, pallets and kilos,
 * a count of vehicles by size — and the register already stores all of it,
 * because DELIVERY jobs were modelled on this very sheet. Nothing is blank
 * because nothing is missing.
 *
 * Which jobs belong to the account is the one thing the register cannot answer
 * on its own. The workbook says "Ware house : UNITHAI" in its header and the job
 * numbers run LSTH_U_…, so the warehouse is what identifies them — but that is
 * read off one month's file, not off a rule anybody wrote down. It is a visible
 * control rather than a hidden constant for exactly that reason: if UNITHAI is
 * the wrong answer, it is one dropdown away from the right one instead of a
 * silent filter nobody can see.
 */

type Column = { head: string; sub?: string; read: (job: Job) => string; align?: "right" };

/** Numbers as the workbook writes them: grouped, and blank rather than zero. */
function count(value: string | undefined): string {
  const raw = (value ?? "").trim();
  if (!raw) return "";
  const number = Number(raw.replace(/,/g, ""));
  if (!Number.isFinite(number) || number === 0) return raw === "0" ? "" : raw;
  return number.toLocaleString("en-US");
}

export const COLUMNS: Column[] = [
  { head: "W/H", read: (j) => j.wh ?? "" },
  { head: "JOB NO.", read: (j) => j.jobCode || j.jobNo || "" },
  { head: "Pick-Up Date", read: (j) => j.date },
  { head: "SID NO.", read: (j) => j.sid ?? "" },
  { head: "Customer List", read: (j) => j.customer },
  { head: "Province", read: (j) => j.province ?? "" },
  { head: "ZIP CODE", read: (j) => j.zip ?? "" },
  { head: "QTY", sub: "PALLET", read: (j) => count(j.pallet), align: "right" },
  { head: "QTY", sub: "KGS.", read: (j) => kilos(j.weight || j.kgs), align: "right" },
  { head: "TYPE of Vehicle", sub: "4W", read: (j) => count(j.v4), align: "right" },
  { head: "TYPE of Vehicle", sub: "6W", read: (j) => count(j.v6), align: "right" },
  { head: "TYPE of Vehicle", sub: "10W", read: (j) => count(j.v10), align: "right" },
  { head: "TYPE of Vehicle", sub: "TRAILER", read: (j) => count(j.vtr), align: "right" },
  { head: "Transportation", read: (j) => count(j.cost), align: "right" },
  { head: "Remark", read: (j) => j.remark },
];

/** The tab that holds this account's own transport prices. */
export const RATES_TAB = "ค่าขนส่ง";

/** The tab that shows the account's own summary sheet. */
export const SUMMARY_TAB = "สรุปงาน";

/**
 * The summary sheet, column for column from `สรุปงาน Chemous 2026`.
 *
 * The same jobs as the delivery report and a wider view of them: who drove,
 * the customer's SAP order and delivery note, and the account's own tick that
 * the row has been checked. Their file spells the last one CHACK and it is left
 * that way, because matching their spelling costs nothing and a diff against
 * last month's file stays clean.
 *
 * Two captions are not left that way. Their sheet puts "SID NUMBER" over the
 * LSTH job numbers and "JOB NO." over the D-codes, which is the wrong way round
 * in all 36 rows of the August sheet — the second sheet of the same workbook
 * calls those D-codes DCODE. The columns sit where theirs sit, so the file
 * still lines up; the two captions say what the column underneath actually
 * holds. Handing back a sheet that repeats the mistake would make it permanent.
 */
export const SUMMARY_COLUMNS: Column[] = [
  { head: "TRUCK", read: (j) => j.trucker },
  { head: "W/H", read: (j) => j.wh ?? "" },
  { head: "JOB NO.", read: (j) => j.jobCode || j.jobNo || "" },
  { head: "DCODE", read: (j) => j.dCode ?? "" },
  { head: "Pick-Up Date", read: (j) => j.date },
  { head: "SID NO.", read: (j) => j.sid ?? "" },
  { head: "SAP ORDER", read: (j) => j.sapOrder ?? "" },
  { head: "DELIVER NO.", read: (j) => j.deliverNo ?? "" },
  { head: "Customer List", read: (j) => j.customer },
  { head: "ZIP CODE", read: (j) => j.zip ?? "" },
  { head: "QTY", sub: "PALLET", read: (j) => count(j.pallet), align: "right" },
  { head: "QTY", sub: "KGS.", read: (j) => kilos(j.weight || j.kgs), align: "right" },
  { head: "TYPE of Vehicle", sub: "4W", read: (j) => count(j.v4), align: "right" },
  { head: "TYPE of Vehicle", sub: "6W", read: (j) => count(j.v6), align: "right" },
  { head: "TYPE of Vehicle", sub: "10W", read: (j) => count(j.v10), align: "right" },
  { head: "TYPE of Vehicle", sub: "TAIL LIFT", read: (j) => count(j.vtl), align: "right" },
  { head: "Remark", read: (j) => j.remark },
  { head: "CHACK", read: (j) => j.checked ?? "" },
];

/**
 * The period line their sheet carries under the warehouse, "01/08/2026 -
 * 31/08/2026" — the whole month, whether or not a job fell on either end.
 */
function monthSpan(key: string): string {
  const [year, month] = key.split("-");
  if (!year || !month) return "";
  const last = new Date(Number(year), Number(month), 0).getDate();
  return `01/${month}/${year} - ${last}/${month}/${year}`;
}

/**
 * The card as the API stores it, and the two translations between it and the
 * card this screen works with.
 *
 * They are not the same shape and should not be forced to be. The screen's card
 * is a reading of one workbook — it carries the file it came from and what could
 * not be read out of it. What is stored is the card itself, which has no file
 * and no complaints, only prices.
 */
type StoredCard = {
  customer: string;
  bands: { label: string; min: number; max: number; position: number }[];
  lanes: {
    id: number; carrier: string; from: string; to: string; postalCode: string;
    prices: Record<string, (number | null)[]>;
  }[];
};

function fromStored(stored: StoredCard): RateCard {
  return {
    file: "บันทึกไว้ในระบบ",
    bands: stored.bands.map((band) => ({ label: band.label, min: band.min, max: band.max })),
    lanes: stored.lanes.map((lane) => ({
      id: String(lane.id),
      carrier: lane.carrier,
      service: "DELIVERY",
      customer: stored.customer,
      from: lane.from,
      to: lane.to,
      county: lane.postalCode,
      remark: "",
      prices: lane.prices,
    })),
    issues: [],
  };
}

/** This report groups by the pick-up date, which is the only date it carries. */
const monthOf = (job: Job) => monthKey(job.date);

export function Chemours({ jobs, tab, onToast }: {
  jobs: Job[];
  /** Which of the account's two documents is being looked at. */
  tab: string;
  onToast: (message: string) => void;
}) {
  const [warehouse, setWarehouse] = useState("UNITHAI");
  /**
   * The account's rate card, once somebody has opened the workbook.
   *
   * Held here rather than inside the rates tab so moving between this screen's
   * tabs does not throw it away — reading the card and then looking at the jobs
   * it prices is the obvious thing to do with it.
   */
  const [card, setCard] = useState<RateCard | null>(null);
  const [saving, setSaving] = useState(false);
  /** The receipt shapes already on file, so the picker is filled before anybody opens a folder. */
  const [templates, setTemplates] = useState<FormTemplate[] | null>(null);

  /**
   * What is already stored, fetched once when the screen opens.
   *
   * Both of these used to live only for as long as the tab was open, which
   * meant picking the same files again every morning. They are kept now, so the
   * screen starts with them and the file pickers are for changing them.
   */
  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch("/api/customer-rates?customer=CHEMOURS",
          { headers: { accept: "application/json" } });
        if (!response.ok || !alive) return;
        const stored = await response.json() as StoredCard;
        if (!alive || !stored.lanes?.length) return;
        setCard(fromStored(stored));
      } catch { /* the tab still works from a file; a failed fetch is not worth a toast on arrival */ }
    })();
    (async () => {
      try {
        const response = await apiFetch("/api/cargo-forms", { headers: { accept: "application/json" } });
        if (!response.ok || !alive) return;
        const rows = await response.json() as { customer: string; sourceFile: string; columns: string[] }[];
        if (alive) setTemplates(rows.map((row) => ({ customer: row.customer, file: row.sourceFile, columns: row.columns })));
      } catch { if (alive) setTemplates([]); }
    })();
    return () => { alive = false; };
  }, []);
  // Which of the account's documents is in view. The filters, the totals and
  // the export are the same machinery either way — only the columns differ, so
  // only the columns are chosen here.
  const summary = tab === SUMMARY_TAB;
  const columns = summary ? SUMMARY_COLUMNS : COLUMNS;
  const [month, setMonth] = useState("ALL");

  const deliveries = useMemo(() => jobs.filter((job) => job.cat === "DELIVERY"), [jobs]);
  const haulerNames = useMemo(
    () => [...new Set(jobs.map((job) => job.trucker).filter(Boolean))].sort(),
    [jobs],
  );

  /**
   * Opens one hauler's card and adds it to whatever is already on screen.
   *
   * More than one company runs this account's work, each with their own card,
   * and comparing them is the point of having them here. So a second file joins
   * the first rather than replacing it — except for the same hauler twice,
   * which is a corrected card and does replace, otherwise every reload would
   * leave two prices for one lane and no way to tell which was current.
   */
  async function loadCard(file: File, hauler: string) {
    try {
      const read = await readRateCard(file, hauler);
      if (!read.lanes.length) {
        onToast("ไม่พบชีตราคาในไฟล์นี้ — การ์ดราคาคือชีตที่ขึ้นต้นด้วย Origin City");
        return;
      }
      setCard((held) => {
        if (!held) return read;
        const kept = held.lanes.filter((lane) => lane.carrier !== read.lanes[0].carrier);
        return {
          file: held.file === read.file ? held.file : `${held.file}, ${read.file}`,
          // The bands come off the card being read; a second card quoting the
          // same clause lands on the same eleven, and one that does not would
          // be a different contract and is worth seeing as extra bands.
          bands: read.bands.length >= held.bands.length ? read.bands : held.bands,
          lanes: [...kept, ...read.lanes],
          issues: [...held.issues, ...read.issues],
        };
      });
      onToast(`อ่านการ์ดของ ${hauler} แล้ว ${read.lanes.length} แถว · ${read.bands.length} ช่วงราคาน้ำมัน`);
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  /**
   * Every warehouse the delivery jobs actually name, with how many each holds.
   *
   * Counted in one pass. The dropdown showed the count beside each name by
   * filtering the whole delivery list again per option, which is a full scan
   * per warehouse on every render of a screen whose whole job is to be looked
   * at while somebody changes the filter.
   */
  const warehouses = useMemo(() => {
    const tally = new Map<string, number>();
    deliveries.forEach((job) => {
      const name = (job.wh ?? "").trim().toUpperCase();
      if (name) tally.set(name, (tally.get(name) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [deliveries]);

  const mine = useMemo(
    () => (warehouse === "ALL"
      ? deliveries
      : deliveries.filter((job) => (job.wh ?? "").trim().toUpperCase() === warehouse)),
    [deliveries, warehouse],
  );

  const months = useMemo(() => {
    const tally = new Map<string, number>();
    mine.forEach((job) => {
      const key = monthOf(job);
      if (key) tally.set(key, (tally.get(key) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [mine]);

  const rows = useMemo(
    () => (month === "ALL" ? mine : mine.filter((job) => monthOf(job) === month))
      .slice()
      .sort((a, b) => dnum(a.date) - dnum(b.date)),
    [mine, month],
  );

  /** The one figure the account checks first. */
  const total = useMemo(
    () => rows.reduce((sum, job) => {
      const value = Number((job.cost ?? "").replace(/,/g, ""));
      return sum + (Number.isFinite(value) ? value : 0);
    }, 0),
    [rows],
  );

  /**
   * Writes one haulier's part of the card to the register.
   *
   * One haulier, not the whole card: their files arrive separately, and saving
   * SSL must not disturb THAI KOT. The endpoint replaces that haulier's lanes
   * and leaves the rest alone, which is the opposite of what the subcontractor
   * seeder does — that one clears the book before it loads.
   */
  const saveCard = useCallback(async (hauler: string) => {
    if (!card) return;
    const mine = card.lanes.filter((lane) => lane.carrier === hauler);
    if (!mine.length) { onToast("ไม่มีเส้นทางของผู้ขนส่งรายนี้ให้บันทึก"); return; }

    setSaving(true);
    try {
      const response = await apiFetch("/api/customer-rates", {
        method: "PUT",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({
          customer: "CHEMOURS",
          carrier: hauler,
          // An open-ended top band carries Infinity, and JSON.stringify writes
          // that as null, which the API would read as no ceiling at all. These
          // cards all quote closed ranges, so this never fires today — but a
          // band that silently loses its ceiling is a price that applies at
          // every diesel figure above it, and that is worth one line to stop.
          bands: card.bands.map((band) => ({
            label: band.label,
            min: Number.isFinite(band.min) ? band.min : 0,
            max: Number.isFinite(band.max) ? band.max : 9999,
          })),
          lanes: mine.map((lane) => ({
            carrier: hauler, from: lane.from, to: lane.to,
            postalCode: lane.county, prices: lane.prices,
          })),
        }),
      });
      const answer = await response.json().catch(() => null) as { lanes?: number; prices?: number; message?: string } | null;
      if (!response.ok) { onToast(answer?.message ?? `บันทึกไม่สำเร็จ (${response.status})`); return; }
      onToast(`บันทึกการ์ดของ ${hauler} แล้ว ${answer?.lanes ?? mine.length} เส้นทาง · ${answer?.prices ?? 0} ราคา`);
    } catch (error) {
      onToast("บันทึกไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setSaving(false);
    }
  }, [card, onToast]);

  /** The whole set of receipt shapes, replaced together — see the note on the endpoint. */
  const saveTemplates = useCallback(async (rows: FormTemplate[]) => {
    const response = await apiFetch("/api/cargo-forms", {
      method: "PUT",
      headers: { "content-type": "application/json", accept: "application/json" },
      body: JSON.stringify(rows.map((row) => ({
        customer: row.customer, sourceFile: row.file, columns: row.columns,
      }))),
    });
    const answer = await response.json().catch(() => null) as { customers?: number; message?: string } | null;
    if (!response.ok) throw new Error(answer?.message ?? `บันทึกไม่สำเร็จ (${response.status})`);
    setTemplates(rows);
    return answer?.customers ?? rows.length;
  }, []);

  // Placed after every hook above it, so the early return cannot change how
  // many run. The warehouse and month pickers narrow jobs, and a rate card has
  // no jobs in it, so this tab draws on its own rather than under controls that
  // would do nothing to it.
  if (tab === RATES_TAB) {
    return (
      <ChemoursRates
        card={card}
        haulers={haulerNames}
        onLoad={loadCard}
        onSave={saveCard}
        saving={saving}
        onToast={onToast}
      />
    );
  }

  // The receipt is a blank document until somebody fills it in, so the
  // warehouse and month pickers have nothing to narrow. Its customer list is
  // every customer the register knows rather than this account alone: the form
  // is issued for all of them, and keeping a second list of customer names is
  // how the two come to disagree.
  if (tab === "Cargo Receipt") {
    return <CargoForm stored={templates} onStore={saveTemplates} onToast={onToast} />;
  }

  function exportSheet() {
    if (!rows.length) { onToast("ไม่มีงานให้ส่งออกในมุมมองนี้"); return; }
    // Two header rows, as the workbook has them: the grouped heading and the
    // sub-heading underneath. Written as plain rows rather than merged cells —
    // the customer reads the file, and a merge that survives one Excel version
    // and not the next is not worth the risk.
    const head = columns.map((column) => column.head);
    const subHead = columns.map((column) => column.sub ?? "");
    const body = rows.map((job) => columns.map((column) => column.read(job)));
    // Their summary sheet opens with the warehouse and the period before the
    // table starts. Both are read off the filters above rather than typed, so
    // the heading cannot say one month while the rows show another.
    const preamble = summary
      ? [
        ["Ware house", ":", "", warehouse === "ALL" ? "ทุกคลัง" : warehouse],
        ["Period", ":", "", month === "ALL" ? "ทุกเดือน" : monthSpan(month)],
        [],
      ]
      : [];
    const sheet = XLSX.utils.aoa_to_sheet([...preamble, head, subHead, ...body]);
    sheet["!cols"] = columns.map((column) => ({ wch: Math.max(10, column.head.length + 4) }));
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, summary ? "Summary" : "Del details-CHEM");
    const scope = (warehouse === "ALL" ? "ALL" : warehouse) + "_" + (month === "ALL" ? "ALL" : month);
    const name = `${summary ? "Summary_CHEM" : "Del_details_CHEM"}_${scope}.xlsx`;
    XLSX.writeFile(book, name);
    onToast(`ส่งออก ${rows.length} รายการแล้ว · ${name}`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>คลังสินค้า</span>
          <select value={warehouse} onChange={(e) => setWarehouse(e.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="UNITHAI">
              UNITHAI · {warehouses.find(([name]) => name === "UNITHAI")?.[1] ?? 0} รายการ
            </option>
            {warehouses.filter(([name]) => name !== "UNITHAI").map(([name, held]) => (
              <option key={name} value={name}>{name} · {held} รายการ</option>
            ))}
            <option value="ALL">ทุกคลัง · {deliveries.length} รายการ</option>
          </select>
        </div>

        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>เดือน</span>
          <select value={month} onChange={(e) => setMonth(e.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="ALL">ทุกเดือน · {mine.length} รายการ</option>
            {months.map(([key, held]) => (
              <option key={key} value={key}>{monthKeyLabel(key)} · {held} รายการ</option>
            ))}
          </select>
        </div>

        <div style={css("display:flex;flex-direction:column;gap:2px;margin-left:auto")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค่าขนส่งรวม</span>
          <span style={css("font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>
            {total ? total.toLocaleString("en-US") : "—"}
          </span>
        </div>

        <button onClick={exportSheet}
          style={css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
          Export Excel
        </button>
      </div>

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.6")}>
        ทุกคอลัมน์ในรายงานนี้อ่านจากทะเบียนงานจริง ไม่มีช่องไหนที่เว้นไว้เพราะไม่มีข้อมูล ·
        งานที่เข้ารายงานคืองานประเภท DELIVERY ของคลังที่เลือก — ไฟล์ต้นฉบับระบุหัวกระดาษว่า UNITHAI
        จึงตั้งเป็นค่าเริ่มต้น เปลี่ยนได้ที่ช่องคลังสินค้า
      </div>

      {(
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        {rows.length === 0 ? (
          <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            {deliveries.length === 0
              ? "ยังไม่มีงานประเภท DELIVERY ในทะเบียน"
              : "ไม่มีงานของคลังและเดือนที่เลือก"}
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {columns.map((column, index) => (
                    <th key={index} style={css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>
                      {column.head}
                      {column.sub && (
                        <span style={css("display:block;font-weight:400;color:#7B8CA0")}>{column.sub}</span>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((job) => (
                  <tr key={job.key} className="row-hover">
                    {columns.map((column, index) => (
                      <td key={index} style={css("padding:7px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap" +
                        (column.align === "right" ? ";text-align:right;font-family:'IBM Plex Mono',monospace" : ""))}>
                        {column.read(job) || "—"}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      )}
    </div>
  );
}
