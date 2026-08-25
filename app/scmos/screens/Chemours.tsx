"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type { Job } from "../ops";
import { apiFetch } from "../api";
import { CargoForm, type FormTemplate } from "./CargoForm";
import { ChemoursRates, readRateCard, type RateCard } from "./ChemoursRates";

/**
 * The Chemours account: what it costs to run, and what the customer signs for.
 *
 * This screen used to carry two reports as well — a Delivery Details sheet
 * built column for column from `Del details-CHEM`, and a summary sheet built
 * from `สรุปงาน Chemous 2026`. Both are gone at the account team's request:
 * they were not used. The jobs behind them are worked in the Domestic grid,
 * which SCMOSApp draws instead of this component, and exported from there.
 *
 * What is left is the two things this screen is for. The rate card, which is
 * this account's own prices and deliberately not in the subcontractor book. And
 * the cargo receipt, which is a document that gets signed.
 */

/** The tab that holds this account's own transport prices. */
export const RATES_TAB = "ค่าขนส่ง";

/**
 * The card as the API stores it, and the translation into the one this screen
 * works with.
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

export function Chemours({ jobs, tab, canEditRates, onToast }: {
  jobs: Job[];
  /** Which of the account's documents is being looked at. */
  tab: string;
  /**
   * Whether this account may write a rate.
   *
   * Operation User can read the card and not change it — the grant list draws
   * that line at Assistant Manager. The screen says so on the button rather
   * than letting somebody fill a card in, press save and collect a refusal from
   * the server after the work is done.
   */
  canEditRates: boolean;
  onToast: (message: string) => void;
}) {
  const [card, setCard] = useState<RateCard | null>(null);
  const [saving, setSaving] = useState(false);
  /** The receipt shapes already on file, so the picker is filled before anybody opens a folder. */
  const [templates, setTemplates] = useState<FormTemplate[] | null>(null);

  const haulerNames = useMemo(
    () => [...new Set(jobs.map((job) => job.trucker).filter(Boolean))].sort(),
    [jobs],
  );

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

  /**
   * Opens one haulier's card and adds it to whatever is already on screen.
   *
   * More than one company runs this account's work, each with their own card,
   * and comparing them is the point of having them here. So a second file joins
   * the first rather than replacing it — except for the same haulier twice,
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
          // same clause lands on the same positions, and one that does not is a
          // different contract and is worth seeing as extra bands.
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
   * Writes the card to the register, one haulier at a time.
   *
   * A haulier at a time because their files arrive separately and saving SSL
   * must not disturb THAI KOT: the endpoint replaces that haulier's lanes and
   * leaves the rest of the customer's card alone. That is the opposite of what
   * the subcontractor seeder does next door, which clears the book before it
   * loads.
   *
   * "ทั้งหมด" saves every haulier on the card, as a sequence of those writes. It
   * used to be refused, on the grounds that a view across cards cannot be
   * written back as one — true, and beside the point, because every lane
   * carries its own haulier and grouping by that is exact. A button that greyed
   * itself out instead simply looked broken, which is how it was reported.
   */
  const saveCard = useCallback(async (hauler: string) => {
    if (!card) return;

    const groups = hauler === "ALL"
      ? [...new Set(card.lanes.map((lane) => lane.carrier))].sort()
      : [hauler];

    // An open-ended top band carries Infinity, and JSON.stringify writes that as
    // null, which the API would read as no ceiling at all. These cards all quote
    // closed ranges so it never fires — but a band that silently loses its
    // ceiling is a price that applies at every diesel figure above it.
    const bands = card.bands.map((band) => ({
      label: band.label,
      min: Number.isFinite(band.min) ? band.min : 0,
      max: Number.isFinite(band.max) ? band.max : 9999,
    }));

    setSaving(true);
    try {
      let lanes = 0;
      let prices = 0;
      for (const carrier of groups) {
        const mine = card.lanes.filter((lane) => lane.carrier === carrier);
        if (!mine.length) continue;

        const response = await apiFetch("/api/customer-rates", {
          method: "PUT",
          headers: { "content-type": "application/json", accept: "application/json" },
          body: JSON.stringify({
            customer: "CHEMOURS",
            carrier,
            bands,
            lanes: mine.map((lane) => ({
              carrier, from: lane.from, to: lane.to,
              postalCode: lane.county, prices: lane.prices,
            })),
          }),
        });
        const answer = await response.json().catch(() => null) as
          { lanes?: number; prices?: number; message?: string } | null;
        if (!response.ok) {
          onToast(`${carrier}: ${answer?.message ?? `บันทึกไม่สำเร็จ (${response.status})`}`);
          return;
        }
        lanes += answer?.lanes ?? mine.length;
        prices += answer?.prices ?? 0;
      }

      if (!lanes) { onToast("ไม่มีเส้นทางให้บันทึก"); return; }
      onToast(`บันทึกแล้ว ${groups.join(", ")} · ${lanes} เส้นทาง · ${prices} ราคา`);
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

  // The receipt is a blank document until somebody fills it in, and the rate
  // card has no jobs in it, so neither wants the filters this screen used to
  // carry for its reports. Both draw on their own.
  if (tab === "Cargo Receipt") {
    return <CargoForm stored={templates} onStore={saveTemplates} onToast={onToast} />;
  }

  return (
    <ChemoursRates
      card={card}
      haulers={haulerNames}
      onLoad={loadCard}
      onSave={saveCard}
      canSave={canEditRates}
      saving={saving}
      onToast={onToast}
    />
  );
}
