"use client";

import { useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { apiFetch } from "../api";
import { ALL_PERIOD, monthLabel, periodLabel, type Period } from "../period";
import { css } from "../theme";

/**
 * The carrier scorecard, one page for all of them.
 *
 * The KPI screen answers "how is this carrier doing"; this answers "who is
 * doing badly and on what", which is the question asked before a review
 * meeting. Same numbers either way — the engine works them out once and caches
 * them against the register's own timestamp, and this reads that rather than
 * doing the arithmetic again over the whole register.
 *
 * A criterion that cannot be scored says so instead of scoring zero. That is
 * the whole reason the weighted mark is quoted out of the weight that was
 * actually available: a carrier measured on 45% of the contract's weight and
 * scoring 90 of it has not scored 90 out of 100, and printing 90 beside a
 * carrier measured on all of it would be comparing two different things.
 */

type Line = {
  id: string; english: string; thai: string; weight: number;
  count: number; base: number; percent: number | null; target: number; note: string;
};
type Score = {
  carrier: string; shipments: number;
  lines: Line[];
  weighted: number | null; weightAvailable: number;
  ungradedAccidents: number;
};

export function SupplierPerformance({ onToast, onBack }: {
  onToast: (message: string) => void;
  onBack: () => void;
}) {
  const [rows, setRows] = useState<Score[] | null>(null);
  const [period, setPeriod] = useState<Period>(ALL_PERIOD);
  const [onlyShort, setOnlyShort] = useState(false);

  const search = useMemo(() => {
    const query = new URLSearchParams();
    if (period.year !== "ALL") query.set("year", period.year);
    if (period.month !== "ALL") query.set("month", period.month);
    return query.toString();
  }, [period]);

  useEffect(() => {
    let alive = true;
    (async () => {
      const response = await apiFetch(`/api/kpi/measures?${search}`,
        { headers: { accept: "application/json" } });
      if (!response.ok || !alive) return;
      const body = await response.json() as { scorecard?: Score[] | null };
      if (alive) setRows(body.scorecard ?? []);
    })();
    return () => { alive = false; };
  }, [search]);

  /** The criteria, in the agreement's order, taken from whoever has them. */
  const criteria = useMemo(() => {
    const seen = new Map<string, Line>();
    for (const row of rows ?? []) for (const line of row.lines) if (!seen.has(line.id)) seen.set(line.id, line);
    return [...seen.values()];
  }, [rows]);

  const shown = useMemo(() => {
    const list = (rows ?? []).slice();
    // Worst first, and a carrier with no mark at all last rather than best:
    // "cannot be scored" is not a good score.
    list.sort((a, b) => {
      if (a.weighted === null && b.weighted === null) return b.shipments - a.shipments;
      if (a.weighted === null) return 1;
      if (b.weighted === null) return -1;
      return a.weighted - b.weighted;
    });
    return onlyShort ? list.filter((row) => row.weighted !== null && row.weighted < TARGET) : list;
  }, [rows, onlyShort]);

  const measured = (rows ?? []).filter((row) => row.weighted !== null);
  const shortOf = measured.filter((row) => row.weighted! < TARGET);
  const unmeasured = (rows ?? []).length - measured.length;

  function exportSheet() {
    if (!shown.length) { onToast("ไม่มีผู้ขนส่งในช่วงที่เลือก"); return; }
    const head = ["ผู้ขนส่ง", "เที่ยว", "คะแนนถ่วงน้ำหนัก", "น้ำหนักที่วัดได้ %",
      ...criteria.map((line) => `${line.thai} (${line.weight}%)`)];
    const body = shown.map((row) => [
      row.carrier, row.shipments,
      row.weighted === null ? "วัดไม่ได้" : row.weighted,
      row.weightAvailable,
      ...criteria.map((line) => {
        const mine = row.lines.find((l) => l.id === line.id);
        return mine?.percent === null || mine === undefined ? "วัดไม่ได้" : mine.percent;
      }),
    ]);
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["Supplier Performance · ผลงานผู้ขนส่ง"],
      ["ช่วงที่รายงาน", periodLabel(period)],
      ["เป้าหมาย", TARGET],
      [], head, ...body,
    ]), "Scorecard");
    XLSX.writeFile(book, `SupplierPerformance_${periodLabel(period).replace(/[^\p{L}\p{N}]/gu, "-")}.xlsx`);
    onToast(`ส่งออกแล้ว ${shown.length} ผู้ขนส่ง`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:10px")}>
        <button onClick={onBack} style={BTN_SECONDARY}>← กลับไปรายการรายงาน</button>
        <span style={css("font-size:13px;font-weight:600;color:#0A2240")}>Supplier Performance · ผลงานผู้ขนส่ง</span>
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <Field label="ปี">
          <select value={period.year} onChange={(e) => setPeriod({ ...period, year: e.target.value, month: "ALL" })} style={SELECT}>
            <option value="ALL">ทุกปี</option>
            {YEARS.map((year) => <option key={year} value={year}>{year}</option>)}
          </select>
        </Field>
        <Field label="เดือน">
          <select value={period.month} onChange={(e) => setPeriod({ ...period, month: e.target.value })} style={SELECT}>
            <option value="ALL">ทุกเดือน</option>
            {MONTHS.map((mm) => <option key={mm} value={mm}>{monthLabel(mm)}</option>)}
          </select>
        </Field>
        <label style={css("display:flex;align-items:center;gap:6px;font-size:12px;color:#31465C;padding-bottom:7px")}>
          <input type="checkbox" checked={onlyShort} onChange={(e) => setOnlyShort(e.target.checked)} />
          เฉพาะรายที่ต่ำกว่าเป้า {TARGET}
        </label>
        <button onClick={exportSheet} style={css(BTN_PRIMARY + ";margin-left:auto")}>Export Excel</button>
      </div>

      {rows === null ? (
        <Card>กำลังอ่านคะแนน…</Card>
      ) : rows.length === 0 ? (
        <Card>ไม่มีผู้ขนส่งที่มีงานในช่วงที่เลือก</Card>
      ) : (
        <>
          <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
            <Tile label="ผู้ขนส่งทั้งหมด" value={String(rows.length)} />
            <Tile label={`ต่ำกว่าเป้า ${TARGET}`} value={String(shortOf.length)} tone={shortOf.length ? "#B42318" : undefined} />
            {/* Said out loud: a carrier nobody could measure is not a carrier
                doing well, and averaging them in as zero would be worse still. */}
            <Tile label="ยังคิดคะแนนไม่ได้" value={String(unmeasured)}
              note={unmeasured ? "ไม่มีข้อมูลพอจะให้คะแนน" : "คิดได้ครบ"}
              tone={unmeasured ? "#B45309" : undefined} />
          </div>

          <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
            <div style={css("padding:11px 15px;border-bottom:1px solid #E9EFF5")}>
              <div style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>
                ใบคะแนนผู้ขนส่ง · {periodLabel(period)}
              </div>
              <div style={css("font-size:11px;color:#7B8CA0;margin-top:2px")}>
                เรียงจากคะแนนต่ำสุด · เกณฑ์ที่ยังคิดคะแนนไม่ได้ขึ้นว่า “—” ไม่ใช่ 0 ·
                คะแนนถ่วงน้ำหนักคิดจากน้ำหนักที่วัดได้จริง ไม่ใช่ 100 เสมอ
              </div>
            </div>
            <div style={css("overflow-x:auto")}>
              <table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
                <thead>
                  <tr>
                    <th style={TH}>ผู้ขนส่ง</th>
                    <th style={css(TH_CSS + ";text-align:right")}>เที่ยว</th>
                    <th style={css(TH_CSS + ";text-align:right")}>คะแนน</th>
                    <th style={css(TH_CSS + ";text-align:right")}>วัดได้ %</th>
                    {criteria.map((line) => (
                      <th key={line.id} style={css(TH_CSS + ";text-align:right")} title={line.english}>
                        {line.thai}<br />
                        <span style={css("font-weight:400;color:#94A3B8")}>{line.weight}%</span>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {shown.map((row) => (
                    <tr key={row.carrier} className="row-hover">
                      <td style={css(TD + ";font-weight:600;color:#0A2240")}>{row.carrier}</td>
                      <td style={css(TD + NUM)}>{row.shipments}</td>
                      <td style={css(TD + NUM + ";font-weight:700;color:"
                        + (row.weighted === null ? "#94A3B8" : row.weighted < TARGET ? "#B42318" : "#16794C"))}>
                        {row.weighted === null ? "—" : row.weighted.toFixed(1)}
                      </td>
                      <td style={css(TD + NUM + ";color:#7B8CA0")}>{row.weightAvailable}%</td>
                      {criteria.map((line) => {
                        const mine = row.lines.find((l) => l.id === line.id);
                        const value = mine?.percent ?? null;
                        return (
                          <td key={line.id} style={css(TD + NUM + ";color:"
                            + (value === null ? "#C3CFDB" : value < mine!.target ? "#B42318" : "#475569"))}
                            title={mine?.note ?? ""}>
                            {value === null ? "—" : value.toFixed(0)}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {shown.length === 0 && (
              <div style={css("padding:22px;text-align:center;font-size:12.5px;color:#16794C")}>
                ไม่มีผู้ขนส่งที่ต่ำกว่าเป้าในช่วงนี้
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}

/** What the agreement asks for. The same number the KPI screen judges against. */
const TARGET = 95;

const YEARS = ["2025", "2026", "2027"];
const MONTHS = ["01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12"];

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;font-family:inherit;background:#fff;min-width:120px");
const TH_CSS = "background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap";
const TH = css(TH_CSS);
const TD = "padding:7px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap";
const NUM = ";text-align:right;font-family:'IBM Plex Mono',monospace";
const BTN_PRIMARY = "height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit";
const BTN_SECONDARY = css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit");

function Card({ children }: { children: React.ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
      {children}
    </div>
  );
}

function Tile({ label, value, tone, note }: { label: string; value: string; tone?: string; note?: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;min-width:150px;display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${tone ?? "#0A2240"}`)}>{value}</span>
      {note && <span style={css("font-size:10.5px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      {children}
    </label>
  );
}
