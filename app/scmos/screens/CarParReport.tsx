"use client";

import { useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { apiFetch } from "../api";
import { STAGES, stageLabel } from "../incidentStages";
import { css } from "../theme";

/**
 * Where the CAR/PAR cases stand: how old, how many closed, what keeps coming back.
 *
 * The register screen next door lists cases so somebody can work one. This
 * counts them so somebody can answer for the month — the three questions the
 * card promised: aging, closure rate, and repeat findings.
 *
 * Read from the same `/api/incidents` the register uses. Nothing is recomputed
 * from a second source, so a case that shows as closed here is closed there.
 */

type Case = {
  id: number; reference: string; kind: string; category: string; title: string;
  stage: string; grade: string; responsiblePerson: string; dueDate: string;
  raisedAt: string; overdue: boolean;
};

/** A case is finished when it has reached the end of the ladder. */
const CLOSED = new Set(["closed", "verified"]);

/**
 * Age brackets, in days.
 *
 * Thirty is the first line because that is the review cycle; ninety because a
 * case open a quarter is a different conversation from one open a month.
 */
const BANDS: [string, (days: number) => boolean][] = [
  ["0–7 วัน", (d) => d <= 7],
  ["8–30 วัน", (d) => d > 7 && d <= 30],
  ["31–90 วัน", (d) => d > 30 && d <= 90],
  ["เกิน 90 วัน", (d) => d > 90],
];

export function CarParReport({ onToast, onBack }: {
  onToast: (message: string) => void;
  onBack: () => void;
}) {
  const [cases, setCases] = useState<Case[] | null>(null);

  useEffect(() => {
    let alive = true;
    (async () => {
      const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
      if (!response.ok || !alive) return;
      const body = await response.json() as Case[];
      if (alive) setCases(body);
    })();
    return () => { alive = false; };
  }, []);

  /**
   * The moment the ages are counted from, taken once when the screen opens.
   *
   * Read fresh on every render it would drift while somebody reads the page,
   * and a report whose numbers move under the reader is worse than one that is
   * a few minutes old.
   */
  const [now] = useState(() => Date.now());
  const open = useMemo(() => (cases ?? []).filter((one) => !CLOSED.has(one.stage)), [cases]);
  const closed = useMemo(() => (cases ?? []).filter((one) => CLOSED.has(one.stage)), [cases]);

  const ageOf = (one: Case) => {
    const at = new Date(one.raisedAt).getTime();
    return Number.isNaN(at) ? null : Math.floor((now - at) / 86_400_000);
  };

  /** Open cases by how long they have been open. Undatable ones are named. */
  const aging = useMemo(() => {
    const counts = BANDS.map(([label]) => ({ label, count: 0 }));
    let undated = 0;
    for (const one of open) {
      const days = ageOf(one);
      if (days === null) { undated++; continue; }
      const at = BANDS.findIndex(([, holds]) => holds(days));
      if (at >= 0) counts[at].count++;
    }
    return { counts, undated };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  /** Every stage, including the ones nothing is sitting at. */
  const byStage = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const stage of STAGES) counts[stage] = 0;
    for (const one of cases ?? []) counts[one.stage] = (counts[one.stage] ?? 0) + 1;
    return counts;
  }, [cases]);

  /**
   * The same finding raised more than once.
   *
   * Counted on the category, which is what the form asks somebody to choose, so
   * two cases land together only when a person said they were the same kind of
   * problem. Guessing from the free-text title would group things nobody meant
   * to group.
   */
  const repeats = useMemo(() => {
    const tally = new Map<string, { total: number; open: number }>();
    for (const one of cases ?? []) {
      const key = (one.category || "").trim() || "ไม่ระบุหมวด";
      const held = tally.get(key) ?? { total: 0, open: 0 };
      held.total++;
      if (!CLOSED.has(one.stage)) held.open++;
      tally.set(key, held);
    }
    return [...tally.entries()]
      .filter(([, v]) => v.total > 1)
      .sort((a, b) => b[1].total - a[1].total)
      .map(([category, v]) => ({ category, ...v }));
  }, [cases]);

  const rate = cases?.length ? Math.round((closed.length / cases.length) * 100) : null;

  function exportSheet() {
    if (!cases?.length) { onToast("ยังไม่มีเคสให้ส่งออก"); return; }
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["CAR / PAR Report"], [],
      ["เคสทั้งหมด", cases.length], ["ปิดแล้ว", closed.length], ["ยังเปิดอยู่", open.length],
      ["อัตราการปิด %", rate ?? "คิดไม่ได้"], [],
      ["อายุของเคสที่ยังเปิด"], ...aging.counts.map((b) => [b.label, b.count]),
      ...(aging.undated ? [["อ่านวันที่เปิดไม่ได้", aging.undated]] : []), [],
      ["ตามขั้นตอน"], ...STAGES.map((s) => [stageLabel(s), byStage[s] ?? 0]), [],
      ["หมวดที่เกิดซ้ำ", "ทั้งหมด", "ยังเปิด"],
      ...repeats.map((r) => [r.category, r.total, r.open]),
    ]), "สรุป");

    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["อ้างอิง", "ชนิด", "หมวด", "เรื่อง", "ขั้นตอน", "ระดับ", "ผู้รับผิดชอบ", "กำหนดเสร็จ", "อายุ (วัน)", "เกินกำหนด"],
      ...(cases).map((one) => [
        one.reference, one.kind, one.category, one.title, stageLabel(one.stage),
        one.grade, one.responsiblePerson, one.dueDate, ageOf(one) ?? "", one.overdue ? "ใช่" : "",
      ]),
    ]), "รายเคส");

    XLSX.writeFile(book, `CarPar_${new Date().toISOString().slice(0, 10).replace(/-/g, "")}.xlsx`);
    onToast(`ส่งออกแล้ว ${cases.length} เคส`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:10px")}>
        <button onClick={onBack} style={BTN_SECONDARY}>← กลับไปรายการรายงาน</button>
        <span style={css("font-size:13px;font-weight:600;color:#0A2240")}>CAR / PAR Report · รายงานข้อบกพร่องและการแก้ไข</span>
        {cases !== null && (
          <button onClick={exportSheet} style={css(BTN_PRIMARY + ";margin-left:auto")}>Export Excel</button>
        )}
      </div>

      {cases === null ? (
        <Card>กำลังอ่านเคส…</Card>
      ) : cases.length === 0 ? (
        <Card>ยังไม่มีเคส CAR/PAR ในระบบ — รายงานนี้จะมีตัวเลขเมื่อเริ่มเปิดเคส</Card>
      ) : (
        <>
          <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
            <Tile label="เคสทั้งหมด" value={String(cases.length)} />
            <Tile label="ปิดแล้ว" value={String(closed.length)} tone="#16794C" />
            <Tile label="ยังเปิดอยู่" value={String(open.length)} tone={open.length ? "#B45309" : undefined} />
            {/* A rate over nothing is not zero percent, it is no rate. */}
            <Tile label="อัตราการปิด" value={rate === null ? "—" : `${rate}%`}
              note={rate === null ? "ยังไม่มีเคสให้คิด" : `${closed.length} จาก ${cases.length}`} />
            <Tile label="เกินกำหนด" value={String(cases.filter((one) => one.overdue).length)}
              tone={cases.some((one) => one.overdue) ? "#B42318" : undefined} />
          </div>

          <Section title="อายุของเคสที่ยังเปิดอยู่" note="นับจากวันที่เปิดเคสถึงวันนี้">
            {aging.counts.map((band) => (
              <Row key={band.label} label={band.label} value={band.count}
                max={Math.max(1, ...aging.counts.map((b) => b.count))}
                tone={band.label === "เกิน 90 วัน" ? "#B42318" : "#2E7DD1"} />
            ))}
            {aging.undated > 0 && (
              <div style={css("padding:8px 15px;font-size:11.5px;color:#B45309")}>
                {aging.undated} เคสอ่านวันที่เปิดไม่ได้ — ไม่ถูกจัดเข้าช่วงอายุใด
              </div>
            )}
          </Section>

          <Section title="ตามขั้นตอน" note="ทุกขั้นตอนแสดงเสมอ รวมขั้นที่ยังไม่มีเคสค้าง — ขั้นที่ว่างคือข้อมูล ไม่ใช่ความว่างเปล่า">
            {STAGES.map((stage) => (
              <Row key={stage} label={stageLabel(stage)} value={byStage[stage] ?? 0}
                max={Math.max(1, ...STAGES.map((s) => byStage[s] ?? 0))} tone="#0A6E8A" />
            ))}
          </Section>

          <Section title="หมวดที่เกิดซ้ำ"
            note="นับตามหมวดที่ผู้เปิดเคสเลือกไว้เอง — แสดงเฉพาะหมวดที่เกิดมากกว่าหนึ่งครั้ง">
            {repeats.length === 0 ? (
              <div style={css("padding:18px 15px;text-align:center;font-size:12.5px;color:#16794C")}>
                ยังไม่มีหมวดใดเกิดซ้ำ
              </div>
            ) : repeats.map((one) => (
              <Row key={one.category} label={one.category} value={one.total}
                max={Math.max(1, ...repeats.map((r) => r.total))} tone="#B45309"
                note={one.open > 0 ? `ยังเปิดอยู่ ${one.open}` : "ปิดครบแล้ว"} />
            ))}
          </Section>
        </>
      )}
    </div>
  );
}

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const BTN_PRIMARY = "height:30px;padding:0 15px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit";
const BTN_SECONDARY = css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit");

function Card({ children }: { children: React.ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
      {children}
    </div>
  );
}

function Section({ title, note, children }: { title: string; note: string; children: React.ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
      <div style={css("padding:11px 15px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>{title}</div>
        <div style={css("font-size:11px;color:#7B8CA0;margin-top:2px")}>{note}</div>
      </div>
      <div style={css("padding:6px 0")}>{children}</div>
    </div>
  );
}

function Row({ label, value, max, tone, note }: {
  label: string; value: number; max: number; tone: string; note?: string;
}) {
  return (
    <div style={css("display:flex;align-items:center;gap:12px;padding:5px 15px")}>
      <span style={css("width:170px;flex:none;font-size:12px;color:#31465C")}>{label}</span>
      <span style={css("flex:1;height:9px;background:#EDF1F5;border-radius:3px;overflow:hidden;min-width:60px")}>
        <span style={css(`display:block;height:100%;border-radius:3px;background:${tone};width:${Math.round((value / max) * 100)}%`)} />
      </span>
      {note && <span style={css("font-size:11px;color:#94A3B8;flex:none")}>{note}</span>}
      <span style={css("width:44px;flex:none;text-align:right;font-family:'IBM Plex Mono',monospace;font-size:13px;font-weight:600;color:#0A2240")}>{value}</span>
    </div>
  );
}

function Tile({ label, value, tone, note }: { label: string; value: string; tone?: string; note?: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;min-width:140px;display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${tone ?? "#0A2240"}`)}>{value}</span>
      {note && <span style={css("font-size:10.5px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}
