"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { quoteMany, quoteSheetVehicle } from "../quoteBatch";
import { css } from "../theme";
import { directionsLink, hasRoute } from "../mapsLink";
import { ATTRIBUTION, TILE, pointsFrom, tileUrl, view } from "../slippyMap";

/** What the routing engine answered, or why it did not. */
type Measured = {
  ok: boolean; km: number; message: string; fromLabel: string; toLabel: string;
  /** The road, flat [lon, lat, lon, lat, …]. Empty when it could not be read. */
  path: number[];
};
import { ZoomBox } from "../TableFrame";
import {
  BASIS_TH, type OptionBasis, type QuoteOption, type VehicleRate,
} from "../quoteRate";

/**
 * What to charge for a journey, from the distance it covers.
 *
 * The arithmetic is in quoteRate.ts, which imports nothing and is tested on its
 * own; this screen collects the question and shows the working. It shows the
 * working deliberately — a quotation that arrives as one number is one nobody
 * can defend to a carrier, and every line here is the line somebody will be
 * asked about.
 *
 * The card itself is edited below the calculator rather than on a settings page
 * somewhere else, because the person who notices a rate is wrong is the person
 * quoting with it.
 */

type Extra = {
  id: number; label: string; basis: OptionBasis;
  rate: number; active: boolean; position: number;
};

/** What a set of past prices looked like. */
type Band = { count: number; low: number; mid: number; high: number };

type Look = {
  /** The remembered distance, when this journey has been priced before. */
  known: { id: number; fromPlace: string; toPlace: string; km: number; setBy: string; setAt: string; usedCount: number } | null;
  /** What carriers have quoted for it. */
  quoted: Band | null;
  /** What the rate book holds for it. */
  contracted: Band | null;
  /** The lanes those figures came from, so a match can be judged. */
  matched: string[];
  /** Below this many prices, a range says more about the sample than the price. */
  minimum: number;
};

type Card = {
  vehicles: (VehicleRate & { id: number; position: number })[];
  extras: Extra[];
  marginPercent: number;
  updatedBy: string;
  updatedAt: string;
};

type DraftRoute = { key: string; from: string; to: string; km: string };

const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;width:100%;font-family:inherit");
const NUM = css("height:28px;width:80px;border:1px solid #C9D6E2;border-radius:3px;padding:0 7px;font-size:12px;font-family:ui-monospace,monospace;text-align:right");
const baht = (n: number) => n.toLocaleString("en-US");

/**
 * The road, drawn.
 *
 * Not Google's embed, which this replaced. That needed a key behind a billing
 * account nobody here wanted to open, and it drew a route Google had worked out
 * for itself — beside a distance that came from somewhere else. This draws the
 * road OpenRouteService actually measured, so the picture and the number are
 * the same journey.
 *
 * Tiles from OpenStreetMap, which asks for no key and no account. The
 * projection is in slippyMap, without imports, and tested there.
 */
function RouteMap({ from, to, path }: { from: string; to: string; path: number[] }) {
  const link = directionsLink(from, to);
  const road = pointsFrom(path);

  /*
   * The picture is laid out for the width it actually has.
   *
   * The zoom is chosen so the whole journey fits, which means it has to be
   * chosen against a real number of pixels. Computed against a fixed 900 and
   * then drawn into a narrower panel, the tiles and the road are positioned for
   * a picture nobody is looking at — and the end of the route is simply clipped
   * off, which is the one thing a map of a journey must not do.
   */
  const [width, setWidth] = useState(WIDTH);
  const box = useRef<HTMLDivElement | null>(null);

  const remeasure = useCallback(() => {
    const seen = Math.round(box.current?.getBoundingClientRect().width ?? 0);
    if (seen > 0) setWidth(seen);
  }, []);

  /*
   * A callback ref that measures on the spot, and a window listener after that.
   *
   * Two things ruled out the obvious answers. An effect with an empty
   * dependency list ran while the box did not exist — it only appears once
   * there is a road to draw in it — so it found a null ref and never ran again.
   * And a ResizeObserver attached to the real, sized element never delivered a
   * single callback in the browser this was checked in, so it could not be
   * shown to work; measuring the node the moment it arrives can be.
   *
   * Between them these cover what actually changes the panel's width: the first
   * time it appears, and the window being resized. Nothing else on this screen
   * moves it.
   */
  const attach = useCallback((node: HTMLDivElement | null) => {
    box.current = node;
    if (node) setWidth(Math.round(node.getBoundingClientRect().width) || WIDTH);
  }, []);

  useEffect(() => {
    window.addEventListener("resize", remeasure);
    return () => window.removeEventListener("resize", remeasure);
  }, [remeasure]);

  const drawn = view(road, width, HEIGHT);

  return (
    <div style={css("margin-top:10px;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden;background:#fff")}>
      <div style={css("padding:8px 12px;border-bottom:1px solid #E9EFF5;display:flex;"
        + "align-items:center;gap:10px;flex-wrap:wrap;font-size:11.5px;color:#7B8CA0")}>
        <span><b style={css("color:#0A2240")}>{from.trim()}</b> → <b style={css("color:#0A2240")}>{to.trim()}</b></span>
        {/* Needs no key either, and is the way to drag the route around, read an
            alternative, or check the picture against somebody else's map. */}
        {link && (
          <a href={link} target="_blank" rel="noreferrer noopener"
            style={css("margin-left:auto;color:#1D5FA8;font-weight:600;text-decoration:none")}>
            เปิดใน Google Maps ↗
          </a>
        )}
      </div>

      {drawn ? (
        <div ref={attach}
          style={css(`position:relative;width:100%;max-width:${WIDTH}px;height:${HEIGHT}px;`
            + "overflow:hidden;background:#E9EFF5;margin:0 auto")}>
          {drawn.tiles.map((tile) => (
            /* Not `loading="lazy"`. Absolutely positioned inside a clipped box,
               the browser never resolved these as visible and never requested a
               single one — the panel drew its road over an empty grey square.
               There is nothing to defer anyway: the tiles are the panel, and
               the panel only exists once somebody has asked for the map. */
            /* A plain img, not next/image, which would proxy every tile through
               this app's optimiser — a bill and a cache for pictures
               OpenStreetMap already serves ready-sized. */
            // eslint-disable-next-line @next/next/no-img-element
            <img key={`${tile.z}/${tile.x}/${tile.y}`} src={tileUrl(tile)} alt=""
              width={TILE} height={TILE} draggable={false}
              style={css(`position:absolute;left:${tile.left}px;top:${tile.top}px;`
                + `width:${TILE}px;height:${TILE}px;user-select:none`)} />
          ))}

          <svg width={width} height={HEIGHT} viewBox={`0 0 ${width} ${HEIGHT}`}
            style={css("position:absolute;inset:0;pointer-events:none")} aria-hidden="true">
            {/* Twice: a pale casing under a solid line, so the road stays
                readable over both a dark motorway and a white sea. */}
            <polyline points={drawn.line.map((at) => `${at.x},${at.y}`).join(" ")}
              fill="none" stroke="#ffffff" strokeWidth="7"
              strokeLinecap="round" strokeLinejoin="round" opacity="0.85" />
            <polyline points={drawn.line.map((at) => `${at.x},${at.y}`).join(" ")}
              fill="none" stroke="#1D5FA8" strokeWidth="3.5"
              strokeLinecap="round" strokeLinejoin="round" />
            {[drawn.line[0], drawn.line[drawn.line.length - 1]].map((at, which) => at && (
              <circle key={which} cx={at.x} cy={at.y} r="6"
                fill={which === 0 ? "#16794C" : "#B3261E"} stroke="#fff" strokeWidth="2.5" />
            ))}
          </svg>

          {/* Their licence asks for this, and so do manners. */}
          <span style={css("position:absolute;right:0;bottom:0;background:rgba(255,255,255,.78);"
            + "font-size:10px;color:#5A6B7D;padding:1px 5px;border-top-left-radius:3px")}>
            {ATTRIBUTION}
          </span>
        </div>
      ) : (
        <div style={css("padding:22px 16px;font-size:12px;color:#7B8CA0")}>
          กด “วัดระยะทางจากถนนจริง” ก่อน แล้วเส้นทางจะขึ้นบนแผนที่
          <div style={css("color:#94A3B8;font-size:11.5px;margin-top:4px")}>
            แผนที่วาดจากเส้นทางที่ระบบวัดระยะทางได้จริง — ไม่ใช่เส้นทางที่คำนวณแยกต่างหาก
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * The widest the panel is allowed to be, and how tall it always is.
 *
 * The width is a ceiling rather than a fixed size — what the picture is laid
 * out against is whatever the panel really measures, which is the only way the
 * chosen zoom fits the journey on a narrow screen as well as a wide one.
 */
const WIDTH = 900;
const HEIGHT = 380;

export function QuoteCalculator({ canEditRates, canSaveQuote, onOpenSheet, onToast }: {
  /** May change the card the price is worked out from, and the journey register. */
  canEditRates: boolean;
  /** May write the finished quotation into the sheet. Held from Operation up. */
  canSaveQuote: boolean;
  onOpenSheet: () => void; onToast: (m: string) => void;
}) {
  const [card, setCard] = useState<Card | null>(null);
  const [vehicles, setVehicles] = useState<string[]>(["4W"]);
  const [detailVehicle, setDetailVehicle] = useState("4W");
  const vehicle = vehicles.includes(detailVehicle) ? detailVehicle : vehicles[0] ?? "";
  const [customer, setCustomer] = useState("");
  const [customers, setCustomers] = useState<string[]>([]);
  const [loadTypes, setLoadTypes] = useState({ fcl: true, lcl: false, domestic: false });
  const [remark, setRemark] = useState("");
  const [savingQuote, setSavingQuote] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [savedQuote, setSavedQuote] = useState<{ key: string; number: number; date: string; count: number; routeCount: number } | null>(null);
  // Reuse the token on uncertain retries, but not after the quotation changes.
  const saveTicket = useRef<{ key: string; id: string } | null>(null);
  const saveInFlight = useRef(false);
  const [routeRows, setRouteRows] = useState<DraftRoute[]>([{ key: "first", from: "", to: "", km: "" }]);
  const [routeKey, setRouteKey] = useState("first");
  const activeRoute = routeRows.find((one) => one.key === routeKey) ?? routeRows[0];
  const { from, to, km } = activeRoute;
  const setRouteValue = (field: "from" | "to" | "km", value: string) => {
    setRouteRows((was) => was.map((one) => one.key === activeRoute.key ? { ...one, [field]: value } : one));
    if (field !== "km") setMeasured(null);
  };
  const setFrom = (value: string) => setRouteValue("from", value);
  const setTo = (value: string) => setRouteValue("to", value);
  const setKm = (value: string) => setRouteValue("km", value);
  const chooseRoute = (key: string) => { setRouteKey(key); setMeasured(null); };
  const [mapOpen, setMapOpen] = useState(false);
  /**
   * What OpenRouteService makes of the distance, once somebody asks.
   *
   * Never asked for on its own — a keystroke in the origin box would spend a
   * shared daily quota on a half-typed place name — and never written into the
   * box. It is offered, and a person presses a button to take it.
   */
  const [measured, setMeasured] = useState<Measured | null>(null);
  const [measuring, setMeasuring] = useState(false);

  async function measure() {
    if (measuring) return;
    setMeasuring(true);
    setMeasured(null);
    try {
      const reply = await apiFetch(
        `/api/journeys/measure?from=${encodeURIComponent(from.trim())}&to=${encodeURIComponent(to.trim())}`,
        { headers: { accept: "application/json" } });

      /*
       * The body is read whatever the status says.
       *
       * This used to throw the answer away on anything but 200 and show "could
       * not read the distance" — which is what it said the first time somebody
       * pressed the button on a real key, hiding a FormatException the server
       * had already named. When the server has an explanation it is the
       * explanation shown; when it has none, the status is, because a number
       * somebody can quote back is worth more than a sentence that fits.
       */
      const body = await reply.json().catch(() => null) as Measured | null;
      if (body && typeof body.ok === "boolean") setMeasured(body);
      else setMeasured({
        ok: false, km: 0, fromLabel: "", toLabel: "", path: [],
        message: `อ่านระยะทางไม่สำเร็จ (HTTP ${reply.status})`,
      });
    } catch {
      setMeasured({ ok: false, km: 0, message: "ติดต่อระบบไม่ได้", fromLabel: "", toLabel: "", path: [] });
    } finally {
      setMeasuring(false);
    }
  }
  /*
   * What the register knows about this journey: how far, and what it has cost.
   *
   * Held with the journey it was fetched for. Answers arrive after the typing
   * that asked for them, so a bare value would show the last road's prices
   * beside this road's name for as long as the request takes — and the reader
   * has no way to tell. Tagged, it is simply not shown until it belongs.
   */
  const [fetched, setFetched] = useState<{ journey: string; look: Look } | null>(null);
  const [dg, setDg] = useState(false);
  const historyVehicle = quoteSheetVehicle(vehicle, dg);
  const [margin, setMargin] = useState("");
  /** Which extras are ticked, and how many hours or trips of each. */
  const [picked, setPicked] = useState<Record<number, number>>({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/quote-card", { headers: { accept: "application/json" } });
    if (!response.ok) { onToast("อ่านตารางอัตราไม่สำเร็จ · HTTP " + response.status); return; }
    const body = await response.json() as { card: Card };
    setCard(body.card);
    setMargin((current) => (current === "" ? String(body.card.marginPercent) : current));
  }, [onToast]);

  // Fetching on mount. Every setState in load runs after an await, so it lands
  // in a microtask rather than while this body does — the rule cannot see past
  // the await and reads it as a synchronous set. Same idiom, same reason, as
  // the inquiry screen next door.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    let cancelled = false;
    void apiFetch("/api/rate-inquiries/form", { headers: { accept: "application/json" } })
      .then(async (response) => {
        if (!response.ok) return;
        const body = await response.json() as { customers?: string[] };
        if (!cancelled) setCustomers(body.customers ?? []);
      }).catch(() => { /* Suggestions are optional; typing a name still works. */ });
    return () => { cancelled = true; };
  }, []);

  /*
   * Both questions about a journey, asked together.
   *
   * Waited on rather than asked per keystroke: a place name is a dozen
   * characters and the history search reads two rate tables.
   */
  useEffect(() => {
    if (!from.trim() || !to.trim() || !historyVehicle) return;
    let cancelled = false;
    const wait = window.setTimeout(async () => {
      const query = new URLSearchParams({ from: from.trim(), to: to.trim(), vehicle: historyVehicle });
      try {
      const response = await apiFetch(`/api/journeys/look?${query}`, { headers: { accept: "application/json" } });
      if (!response.ok || cancelled) return;
      const answer = await response.json() as Look;
      if (cancelled) return;
      setFetched({ journey: `${from.trim()}→${to.trim()}|${historyVehicle}`, look: answer });
      // A distance already agreed fills the box, so nobody types it twice and
      // the two typings differ. Anything already typed is left alone.
      if (answer.known) setRouteRows((was) => was.map((one) =>
        one.key === routeKey && !one.km.trim() ? { ...one, km: String(answer.known!.km) } : one));
      } catch { /* History is optional for a new quotation. */ }
    }, 450);
    return () => { cancelled = true; window.clearTimeout(wait); };
    // The distance is filled only when that particular route is still blank.
  }, [from, to, historyVehicle, routeKey]);

  /** Records the distance for this journey so the next quotation starts with it. */
  async function remember() {
    if (!canEditRates) return;
    const journey = `${from.trim()}→${to.trim()}|${historyVehicle}`;
    try {
    const wanted = Number(km.replace(/,/g, ""));
    const response = await apiFetch("/api/journeys", {
      method: "POST",
      headers: { "content-type": "application/json", accept: "application/json" },
      body: JSON.stringify({ from: from.trim(), to: to.trim(), km: wanted }),
    });
    const reply = await response.json().catch(() => ({})) as
      { message?: string; error?: string; journey?: Look["known"] };
    onToast(reply.message ?? reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
    if (response.ok && reply.journey) {
      setFetched((was) => was?.journey === journey ? { ...was, look: { ...was.look, known: reply.journey! } } : was);
    }
    } catch { onToast("ยังยืนยันการจำระยะทางไม่ได้ กรุณาลองใหม่"); }
  }

  async function save(path: string, payload: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/quote-card${path}`, {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(payload),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
      if (response.ok) await load();
    } finally { setBusy(false); }
  }

  if (!card) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลดตารางอัตรา…</div>;
  }

  const options: QuoteOption[] = card.extras
    .filter((one) => one.active && picked[one.id] !== undefined)
    .map((one) => ({
      id: String(one.id), label: one.label, basis: one.basis,
      rate: one.rate,
      // A flat or percentage charge applies once; the others are counted.
      quantity: one.basis === "perHour" ? (picked[one.id] || 0) : 1,
    }));

  const look = fetched?.journey === `${from.trim()}→${to.trim()}|${historyVehicle}` ? fetched.look : null;

  const calculations = routeRows.map((route) => ({ route, batch: quoteMany(card.vehicles, {
    vehicles, km: Number(route.km.replace(/,/g, "")), dangerousGoods: dg,
    marginPercent: Number(margin), options,
  }) }));
  const batch = calculations.find((one) => one.route.key === activeRoute.key)!.batch;
  const focused = batch.results.find((one) => one.vehicle === vehicle);
  const answer = focused?.quote ?? { lines: [], cost: 0, margin: 0, total: 0, refusals: batch.refusals };
  const payload = {
    fromPlace: "", toPlace: "", customer: customer.trim(),
    ...loadTypes, remark: remark.trim(), km: 0,
    dangerousGoods: dg, marginPercent: Number(margin), vehicles,
    options: options.map((one) => ({ id: Number(one.id), quantity: one.quantity })),
    expectedTotals: {},
    routes: calculations.map(({ route, batch: result }) => ({
      fromPlace: route.from.trim(), toPlace: route.to.trim(), km: Number(route.km.replace(/,/g, "")),
      expectedTotals: Object.fromEntries(result.results.map((one) => [one.vehicle, one.quote.total])),
    })),
  };
  const quoteKey = JSON.stringify(payload);
  const alreadySaved = savedQuote?.key === quoteKey;
  const saveRefusals = [
    ...calculations.flatMap(({ route, batch: result }, index) => [
      ...result.refusals, ...result.sheetRefusals,
      ...(!route.from.trim() || !route.to.trim() ? ["ระบุต้นทางและปลายทางก่อนบันทึก"] : []),
    ].map((reason) => `เส้นทาง ${index + 1}: ${reason}`)),
    ...(!customer.trim() ? ["ระบุชื่อลูกค้าก่อนบันทึก"] : []),
    ...(!Object.values(loadTypes).some(Boolean) ? ["เลือกประเภทงานอย่างน้อย 1 แบบ"] : []),
  ];
  const canSave = canSaveQuote && !savingQuote && !alreadySaved && saveRefusals.length === 0;

  async function saveToSheet() {
    if (!canSave || saveInFlight.current) return;
    saveInFlight.current = true;
    setSavingQuote(true);
    setSaveError("");
    try {
      if (saveTicket.current?.key !== quoteKey) {
        saveTicket.current = { key: quoteKey, id: crypto.randomUUID() };
      }
      const response = await apiFetch("/api/quote-card/save-to-sheet", {
        method: "POST", headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ ...payload, requestId: saveTicket.current.id }),
      });
      const reply = await response.json().catch(() => ({})) as {
        message?: string; error?: string; number?: number; date?: string; count?: number; routeCount?: number;
      };
      if (!response.ok) {
        const message = reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`;
        setSaveError(message); onToast(message);
        if (response.status === 409) await load();
        return;
      }
      if (reply.number === undefined || !reply.date || reply.count === undefined) {
        throw new Error("Incomplete save receipt");
      }
      setSavedQuote({ key: quoteKey, number: reply.number, date: reply.date, count: reply.count, routeCount: reply.routeCount ?? 1 });
      onToast(reply.message ?? `บันทึกลงตารางอัตราแล้ว · NO. ${reply.number}`);
    } catch {
      setSaveError("ยังยืนยันผลบันทึกไม่ได้ กรุณากดบันทึกอีกครั้ง ระบบจะตรวจคำขอเดิมเพื่อไม่เพิ่มแถวซ้ำ");
    } finally {
      saveInFlight.current = false;
      setSavingQuote(false);
    }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {/* ------------------------------------------------ the question */}
      <fieldset disabled={savingQuote || measuring} style={css("min-width:0;margin:0;background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("display:flex;align-items:center;gap:7px;flex-wrap:wrap;margin-bottom:14px;padding-bottom:12px;border-bottom:1px solid #E9EFF5")}>
          {routeRows.map((one, index) => <button type="button" key={one.key} onClick={() => chooseRoute(one.key)} aria-pressed={one.key === activeRoute.key}
            style={css("padding:7px 11px;border:1px solid #C9D6E2;border-radius:4px;font:inherit;font-size:12px;cursor:pointer;background:" + (one.key === activeRoute.key ? "#0A2240;color:#fff" : "#fff;color:#31465C"))}>เส้นทาง {index + 1}{one.to ? ` · ${one.to}` : ""}</button>)}
          <button type="button" disabled={routeRows.length >= 20 || measuring} onClick={() => {
            const key = crypto.randomUUID();
            setRouteRows((was) => [...was, { key, from: "", to: "", km: "" }]);
            chooseRoute(key);
          }} style={css("padding:7px 11px;border:1px solid #2D7BB6;border-radius:4px;background:#fff;color:#2D7BB6;font:inherit;font-size:12px;cursor:pointer")}>+ เพิ่มเส้นทาง</button>
          {routeRows.length > 1 && <button type="button" disabled={measuring} onClick={() => {
            const remaining = routeRows.filter((one) => one.key !== activeRoute.key);
            setRouteRows(remaining); chooseRoute(remaining[0].key);
          }} style={css("padding:7px 11px;border:1px solid #E4B4AF;border-radius:4px;background:#fff;color:#B42318;font:inherit;font-size:12px;cursor:pointer")}>นำเส้นทางนี้ออกจากชุด</button>}
          <span style={css("font-size:11.5px;color:#7B8CA0")}>สูงสุด 20 เส้นทาง · ประเภทรถ กำไร และรายการเพิ่มเติมใช้ร่วมกันทั้งชุด</span>
        </div>
        <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ประเภทรถ · เลือกได้หลายแบบ" width="240px">
            <details style={css("position:relative")}>
              <summary style={{ ...INPUT, height: "auto", minHeight: 30, padding: "6px 8px", cursor: "pointer" }}>
                {vehicles.length ? `${vehicles.length} ประเภท · ${card.vehicles.filter((one) => vehicles.includes(one.code)).map((one) => one.label).join(", ")}` : "เลือกประเภทรถ"}
              </summary>
              <div style={css("position:absolute;left:0;top:100%;z-index:60;width:280px;max-width:80vw;max-height:340px;overflow:auto;background:#fff;border:1px solid #C9D6E2;border-radius:4px;box-shadow:0 8px 24px #0A224022;padding:8px")}>
                <div style={css("display:flex;gap:8px;padding:4px 3px 8px;border-bottom:1px solid #E9EFF5")}>
                  <button type="button" onClick={() => setVehicles(card.vehicles.map((one) => one.code))} style={css("font:inherit;font-size:12px;cursor:pointer")}>เลือกทั้งหมด</button>
                  <button type="button" onClick={() => setVehicles([])} style={css("font:inherit;font-size:12px;cursor:pointer")}>ล้างที่เลือก</button>
                </div>
                {card.vehicles.map((one) => (
                  <label key={one.code} style={css("display:flex;gap:8px;padding:7px 4px;font-size:12.5px;cursor:pointer")}>
                    <input type="checkbox" checked={vehicles.includes(one.code)} onChange={(event) => {
                      const checked = event.target.checked;
                      setVehicles((was) => checked ? [...was, one.code] : was.filter((code) => code !== one.code));
                    }} />
                    {one.label}
                  </label>
                ))}
              </div>
            </details>
          </Field>
          <Field label="ต้นทาง" width="180px">
            <input value={from} autoComplete="off" placeholder="เช่น LCB Port"
              onChange={(e) => setFrom(e.target.value)} style={INPUT} />
          </Field>
          <Field label="ปลายทาง" width="180px">
            <input value={to} autoComplete="off" placeholder="เช่น Amata City"
              onChange={(e) => setTo(e.target.value)} style={INPUT} />
          </Field>
          <Field label="ระยะทางไป (กม.)" width="130px">
            <input value={km} inputMode="decimal" autoComplete="off" placeholder="เช่น 120"
              onChange={(e) => setKm(e.target.value)} style={INPUT} />
          </Field>
          <Field label="กำไร (%)" width="100px">
            <input value={margin} inputMode="decimal"
              onChange={(e) => setMargin(e.target.value)} style={INPUT} />
          </Field>
          <label style={css("height:30px;display:inline-flex;align-items:center;gap:6px;font-size:12.5px;color:#31465C;cursor:pointer")}>
            <input type="checkbox" checked={dg} onChange={(e) => setDg(e.target.checked)} />
            สินค้าอันตราย (DG)
          </label>
        </div>

        {/*
          The distance, once somebody has said what it is.

          Two of every five journeys in the register are quoted more than once,
          and a distance retyped from memory each time is one that will
          eventually differ from itself. So the first person to measure it
          records it, and everybody after that starts from their number.
        */}
        {from.trim() && to.trim() && (
          <div style={css("margin-top:9px;font-size:11.5px;display:flex;gap:9px;align-items:center;flex-wrap:wrap")}>
            {look?.known ? (
              <>
                <span style={css("color:#16794C")}>
                  จำไว้แล้ว {look.known.km.toLocaleString()} กม.
                  {look.known.usedCount > 0 && ` · ใช้มาแล้ว ${look.known.usedCount} ครั้ง`}
                  <span style={css("color:#94A3B8")}> · {look.known.setBy} {look.known.setAt}</span>
                </span>
                {canEditRates && Number(km.replace(/,/g, "")) > 0 && Number(km.replace(/,/g, "")) !== look.known.km && (
                  <button type="button" onClick={() => void remember()}
                    style={css("height:24px;padding:0 10px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
                    แก้เป็น {Number(km.replace(/,/g, "")).toLocaleString()} กม.
                  </button>
                )}
              </>
            ) : Number(km.replace(/,/g, "")) > 0 ? (
              <>
                <span style={css("color:#94A3B8")}>เส้นทางนี้ยังไม่เคยบันทึกระยะทาง</span>
                <button type="button" onClick={() => void remember()}
                  style={css("height:24px;padding:0 10px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
                  จำระยะทางนี้ไว้
                </button>
              </>
            ) : (
              <span style={css("color:#94A3B8")}>ยังไม่เคยบันทึกระยะทางของเส้นทางนี้ — กรอกแล้วกดจำไว้ได้</span>
            )}

            {/* Beside the distance rather than at the foot of the screen. It is
                the only thing on the page that can contradict the number in the
                box, so it belongs where somebody typing that number is looking. */}
            <button type="button" onClick={() => void measure()} disabled={measuring}
              style={css("height:24px;padding:0 10px;border:1px solid #16794C;background:"
                + (measuring ? "#C3CFDB" : "#fff") + ";color:" + (measuring ? "#fff" : "#16794C")
                + ";border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
              {measuring ? "กำลังวัด…" : "วัดระยะทางจากถนนจริง"}
            </button>
            <button type="button" onClick={() => setMapOpen((was) => !was)}
              style={css("height:24px;padding:0 10px;border:1px solid #1D5FA8;background:"
                + (mapOpen ? "#1D5FA8" : "#fff") + ";color:" + (mapOpen ? "#fff" : "#1D5FA8")
                + ";border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
              {mapOpen ? "ซ่อนแผนที่" : "ดูเส้นทางบนแผนที่"}
            </button>
          </div>
        )}

        {measured && (
          <div style={css("margin-top:8px;font-size:11.5px;border:1px solid "
            + (measured.ok ? "#CDE6DA" : "#F0DCB4") + ";background:"
            + (measured.ok ? "#F3FAF6" : "#FFF8EC") + ";border-radius:4px;padding:8px 11px")}>
            {measured.ok ? (
              <div style={css("display:flex;gap:9px;align-items:center;flex-wrap:wrap")}>
                <span style={css("color:#16794C;font-weight:600")}>
                  ถนนจริงวัดได้ {measured.km.toLocaleString()} กม.
                </span>
                {/* What the geocoder decided the two ends were. The commonest
                    wrong answer here is not a broken call — it is a confident
                    distance to the wrong town, and this is the only way to see
                    it before the number goes into a price. */}
                {(measured.fromLabel || measured.toLabel) && (
                  <span style={css("color:#7B8CA0")}>
                    {measured.fromLabel || "—"} → {measured.toLabel || "—"}
                  </span>
                )}
                {Number(km.replace(/,/g, "")) !== measured.km && (
                  <button type="button" onClick={() => setKm(String(measured.km))}
                    style={css("height:24px;padding:0 10px;border:1px solid #16794C;background:#16794C;"
                      + "color:#fff;border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
                    ใช้ค่านี้
                  </button>
                )}
                <span style={css("color:#94A3B8")}>
                  เส้นทางรถบรรทุก · เป็นค่าที่เสนอ ยังต้องกด “จำระยะทางนี้ไว้” เอง
                </span>
              </div>
            ) : (
              <span style={css("color:#8A6D1F")}>{measured.message}</span>
            )}
          </div>
        )}

        {mapOpen && hasRoute(from, to) && (
          <RouteMap from={from} to={to} path={measured?.ok ? measured.path : []} />
        )}

        {card.extras.some((one) => one.active) && (
          <div style={css("margin-top:12px;padding-top:11px;border-top:1px solid #EEF3F8")}>
            <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:7px")}>
              รายการเพิ่มเติม
            </div>
            <div style={css("display:flex;gap:14px;flex-wrap:wrap")}>
              {card.extras.filter((one) => one.active).map((one) => {
                const on = picked[one.id] !== undefined;
                return (
                  <label key={one.id} style={css("display:inline-flex;align-items:center;gap:6px;font-size:12px;color:#31465C;cursor:pointer")}>
                    <input type="checkbox" checked={on}
                      onChange={(e) => setPicked((was) => {
                        const next = { ...was };
                        if (e.target.checked) next[one.id] = one.basis === "perHour" ? 1 : 1;
                        else delete next[one.id];
                        return next;
                      })} />
                    {one.label}
                    <span style={css("color:#94A3B8;font-size:11px")}>
                      {one.basis === "percent" ? `${one.rate}%` : `${baht(one.rate)} ${BASIS_TH[one.basis]}`}
                    </span>
                    {on && one.basis === "perHour" && (
                      <input value={String(picked[one.id])} inputMode="numeric"
                        onChange={(e) => setPicked((was) => ({ ...was, [one.id]: Number(e.target.value) || 0 }))}
                        style={css("height:24px;width:52px;border:1px solid #C9D6E2;border-radius:3px;padding:0 6px;font-size:11.5px;text-align:right;font-family:ui-monospace,monospace")} />
                    )}
                  </label>
                );
              })}
            </div>
          </div>
        )}
      </fieldset>

      {/* Each selected truck is an alternative quote, not an amount to sum. */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:12px 16px;font-size:13px;font-weight:650;color:#0A2240")}>
          ผลคำนวณพร้อมกัน · {routeRows.length} เส้นทาง × {batch.results.length} ประเภทรถ
          <span style={css("display:block;margin-top:4px;font-size:11.5px;font-weight:400;color:#7B8CA0")}>ราคาต่อเที่ยว แยกตามประเภทรถ · กดดูรายละเอียดเพื่อดูสูตรและเทียบราคาย้อนหลัง</span>
        </div>
        {batch.refusals.length > 0 && <div role="status" style={css("padding:10px 16px;background:#FFF8F5;color:#9A3412;font-size:12px")}>{batch.refusals.join(" · ")}</div>}
        {batch.results.length > 0 && <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px;white-space:nowrap")}>
            <thead style={css("background:#F2F6FA;color:#31465C;text-align:left")}>
              <tr>{["TRUCK TYPE", "ต้นทุน (บาท)", "กำไร (บาท)", "ราคาเสนอลูกค้า (บาท)", "คอลัมน์ในตารางอัตรา", ""].map((head) => <th key={head} scope="col" style={css("padding:9px 16px")}>{head}</th>)}</tr>
            </thead>
            {calculations.map(({ route, batch: result }, index) => <tbody key={route.key}>
              <tr><th colSpan={6} scope="rowgroup" style={css("padding:10px 16px;background:#E5EDF5;text-align:left")}>เส้นทาง {index + 1}: {route.from || "ยังไม่ระบุต้นทาง"} → {route.to || "ยังไม่ระบุปลายทาง"} · {route.km || "—"} กม.</th></tr>
              {result.results.map((one) => <tr key={one.vehicle} style={css("border-top:1px solid #E9EFF5;background:" + (route.key === activeRoute.key && one.vehicle === vehicle ? "#EDF5FF" : "#fff"))}>
              <th scope="row" style={css("padding:9px 16px;text-align:left")}>{one.label}{dg ? " · DG" : ""}</th>
              {[one.quote.cost, one.quote.margin, one.quote.total].map((amount, index) => <td key={index} style={css("padding:9px 16px;font-family:ui-monospace,monospace;font-weight:" + (index === 2 ? "700" : "400"))}>{one.quote.refusals.length ? "—" : baht(amount)}</td>)}
              <td style={css("padding:9px 16px;color:" + (one.sheetVehicle ? "#31465C" : "#B45309"))}>{one.sheetVehicle ?? "ยังไม่มีคอลัมน์รองรับ"}</td>
              <td style={css("padding:9px 16px")}><button type="button" disabled={savingQuote || measuring} onClick={() => { setDetailVehicle(one.vehicle); chooseRoute(route.key); }} aria-pressed={route.key === activeRoute.key && one.vehicle === vehicle} style={css("font:inherit;font-size:11.5px;cursor:pointer")}>ดูรายละเอียด {one.label}</button></td>
            </tr>)}</tbody>)}
          </table>
        </ZoomBox>}
      </div>

      <fieldset disabled={savingQuote} style={css("margin:0;min-width:0;background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("font-size:13px;font-weight:650;color:#0A2240;margin-bottom:10px")}>บันทึกผลลงตารางอัตรา</div>
        <div style={css("display:flex;align-items:flex-end;gap:12px;flex-wrap:wrap")}>
          <Field label="ลูกค้า *" width="240px"><input aria-label="ลูกค้าสำหรับบันทึกอัตรา" list="quote-sheet-customers" value={customer} maxLength={200} onChange={(event) => setCustomer(event.target.value)} placeholder="เลือกหรือพิมพ์ชื่อลูกค้า" style={INPUT} /></Field>
          <datalist id="quote-sheet-customers">{customers.map((name) => <option key={name} value={name} />)}</datalist>
          <Field label="ประเภทงาน *" width="220px"><div style={css("display:flex;align-items:center;gap:12px;height:30px")}>
            {(["fcl", "lcl", "domestic"] as const).map((key) => <label key={key} style={css("display:flex;align-items:center;gap:5px;font-size:12px")}><input type="checkbox" checked={loadTypes[key]} onChange={(event) => setLoadTypes((was) => ({ ...was, [key]: event.target.checked }))} />{key.toUpperCase()}</label>)}
          </div></Field>
          <Field label="หมายเหตุ (ถ้ามี)" width="240px"><input aria-label="หมายเหตุสำหรับบันทึกอัตรา" value={remark} maxLength={600} onChange={(event) => setRemark(event.target.value)} style={INPUT} /></Field>
          <button type="button" onClick={() => void saveToSheet()} disabled={!canSave} style={css("height:34px;padding:0 16px;background:" + (canSave ? "#0A2240" : "#8FA3B8") + ";color:#fff;border:0;border-radius:4px;font:inherit;font-size:12.5px;font-weight:650;cursor:" + (canSave ? "pointer" : "default"))}>
            {savingQuote ? "กำลังบันทึก…" : alreadySaved ? "บันทึกแล้ว" : `บันทึก ${routeRows.length} เส้นทาง · ${batch.results.length} ประเภทรถ`}
          </button>
        </div>
        <p style={css("font-size:11.5px;color:#7B8CA0;margin:10px 0 0;line-height:1.6")}>
          แต่ละเส้นทางบันทึกคนละแถว พร้อมราคาเสนอลูกค้าของรถที่เลือก (รวมกำไรและรายการเพิ่มเติม) · DATE ใช้วันบันทึกตามเวลาประเทศไทย · NO. รันต่อในเดือนนั้นและใช้ร่วมกันทั้งชุด · ผู้ขอเป็นบัญชีที่เข้าสู่ระบบ
        </p>
        {!canSaveQuote ? <p role="status" style={css("font-size:12px;color:#B45309")}>บัญชีนี้คำนวณราคาได้ แต่ไม่มีสิทธิ์บันทึกตารางอัตรา</p>
          : !alreadySaved && saveRefusals.length > 0 && <p role="status" style={css("font-size:12px;color:#B45309;margin-bottom:0")}>{[...new Set(saveRefusals)].join(" · ")}</p>}
        {saveError && <p role="alert" style={css("font-size:12px;color:#B42318")}>{saveError}</p>}
        {savedQuote && <div role="status" style={css("margin-top:12px;padding:10px;background:#F1FAF5;color:#16794C;font-size:12.5px;display:flex;gap:12px;align-items:center;flex-wrap:wrap")}>
          <span>{alreadySaved ? "บันทึกแล้ว" : "รายการที่บันทึกล่าสุด"} · DATE {savedQuote.date} · NO. {savedQuote.number} · {savedQuote.routeCount} เส้นทาง · {savedQuote.count} ช่องราคา</span>
          <button type="button" onClick={onOpenSheet} style={css("font:inherit;cursor:pointer;padding:5px 10px")}>เปิดตารางอัตรา</button>
        </div>}
      </fieldset>

      {/* ------------------------------------------------- the working */}
      {answer.refusals.length > 0 ? (
        <div style={css("background:#FFF8F5;border:1px solid #F2C4B4;border-radius:5px;padding:13px 16px;font-size:12.5px;color:#9A3412")}>
          {answer.refusals.join(" · ")}
        </div>
      ) : (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
            รายละเอียดเส้นทาง {routeRows.findIndex((one) => one.key === activeRoute.key) + 1}: {from || "—"} → {to || "—"} · {card.vehicles.find((one) => one.code === vehicle)?.label} ·
            {" "}{Number(km.replace(/,/g, "")).toLocaleString()} กม.{dg ? " · DG" : ""}
          </div>
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <tbody>
                {answer.lines.map((line, at) => (
                  <tr key={at} style={css("border-bottom:1px solid #F4F7FA")}>
                    <td style={css("padding:7px 16px;color:#31465C;width:190px")}>{line.label}</td>
                    <td style={css("padding:7px 8px;color:#94A3B8;font-size:11.5px")}>{line.detail}</td>
                    <td style={css("padding:7px 16px;text-align:right;font-family:ui-monospace,monospace;color:#0A2240")}>
                      {baht(line.amount)}
                    </td>
                  </tr>
                ))}
                <tr style={css("border-top:1px solid #E2E8F0;background:#F8FAFC")}>
                  <td style={css("padding:8px 16px;font-weight:600;color:#31465C")}>ต้นทุนรวม</td>
                  <td />
                  <td style={css("padding:8px 16px;text-align:right;font-family:ui-monospace,monospace;font-weight:600;color:#0A2240")}>
                    {baht(answer.cost)}
                  </td>
                </tr>
                <tr style={css("background:#F8FAFC")}>
                  <td style={css("padding:7px 16px;color:#31465C")}>กำไร {margin || 0}%</td>
                  <td />
                  <td style={css("padding:7px 16px;text-align:right;font-family:ui-monospace,monospace;color:#16794C")}>
                    {baht(answer.margin)}
                  </td>
                </tr>
                <tr style={css("background:#0A2240")}>
                  <td style={css("padding:10px 16px;font-weight:650;color:#fff;font-size:13px")}>ราคาเสนอลูกค้า</td>
                  <td />
                  <td style={css("padding:10px 16px;text-align:right;font-family:ui-monospace,monospace;font-weight:650;color:#fff;font-size:15px")}>
                    {baht(answer.total)}
                  </td>
                </tr>
              </tbody>
            </table>
          </ZoomBox>
        </div>
      )}

      {look && answer.refusals.length === 0 && <History look={look} total={answer.total} />}

      {canEditRates && <CardEditor card={card} busy={busy || savingQuote} onSave={save} />}
    </div>
  );
}

/**
 * What this journey has actually cost, beside what the card says it should.
 *
 * The card is a rule; these are the prices carriers really quoted and really
 * agreed. A calculated figure with nothing to check it against is a figure
 * somebody sends to a customer without knowing it is twice the going rate — and
 * on this register the same lorry on the same road has been quoted at 2.5 times
 * the price, so the going rate is not obvious.
 *
 * Where there are too few past prices to read anything from, it says so instead
 * of drawing a band across three numbers. The numbers are still shown: hiding
 * what exists would be its own kind of lie, and two quotes are worth seeing even
 * when they are not worth averaging.
 */
function History({ look, total }: { look: Look; total: number }) {
  const rows: [string, Band | null][] = [
    ["ตามสัญญา (Rate book)", look.contracted],
    ["เคยเสนอมา (Rate inquiry)", look.quoted],
  ];
  const any = rows.some(([, band]) => band);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px")}>
      <div style={css("font-size:12.5px;font-weight:650;color:#0A2240;margin-bottom:8px")}>
        เทียบกับราคาที่เคยเป็นมา
      </div>

      {!any ? (
        <div style={css("font-size:12px;color:#94A3B8")}>
          ยังไม่มีราคาย้อนหลังของเส้นทางนี้สำหรับรถประเภทที่เลือก — ตัวเลขที่คำนวณได้จึงยังไม่มีอะไรมาเทียบ
        </div>
      ) : (
        <div style={css("display:flex;flex-direction:column;gap:6px")}>
          {rows.map(([label, band]) => (
            <div key={label} style={css("display:flex;gap:10px;align-items:baseline;font-size:12px;flex-wrap:wrap")}>
              <span style={css("width:180px;color:#31465C")}>{label}</span>
              {band ? (
                <>
                  <span style={css("font-family:ui-monospace,monospace;color:#0A2240")}>
                    {baht(band.low)} – {baht(band.mid)} – {baht(band.high)}
                  </span>
                  <span style={css("color:#94A3B8;font-size:11.5px")}>
                    จาก {band.count} ราคา
                    {band.count < look.minimum && " · น้อยเกินกว่าจะสรุปเป็นช่วง"}
                  </span>
                  {/* Where the quotation sits against what was really paid. */}
                  {band.count >= look.minimum && (
                    <span style={css("font-size:11.5px;font-weight:600;color:"
                      + (total > band.high ? "#B42318" : total < band.low ? "#B45309" : "#16794C"))}>
                      {total > band.high ? "สูงกว่าที่เคยจ่ายทุกครั้ง"
                        : total < band.low ? "ต่ำกว่าที่เคยจ่ายทุกครั้ง"
                        : "อยู่ในช่วงที่เคยจ่าย"}
                    </span>
                  )}
                </>
              ) : (
                <span style={css("color:#B6C2CF;font-size:11.5px")}>ไม่มีข้อมูล</span>
              )}
            </div>
          ))}
        </div>
      )}

      {look.matched.length > 0 && (
        // What was matched, so a wrong match can be seen rather than trusted.
        <div style={css("font-size:11px;color:#94A3B8;margin-top:8px;line-height:1.6")}>
          เทียบจากเส้นทาง: {look.matched.join(" · ")}
        </div>
      )}
    </div>
  );
}

/**
 * The card, editable in place.
 *
 * Every figure the calculator uses is here and none of it is in the code. That
 * matters beyond convenience: measured against the 13,042 prices the register
 * already holds, the ×1.5 on a refrigerated truck is high — a 10W RF came out
 * at ×1.24 across thirty journeys quoting both — and a rate that wants
 * correcting should not wait for a deployment.
 */
function CardEditor({ card, busy, onSave }: {
  card: Card; busy: boolean;
  onSave: (path: string, payload: unknown) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Record<number, Partial<VehicleRate>>>({});

  const value = (row: Card["vehicles"][number], field: keyof VehicleRate) => {
    const held = draft[row.id]?.[field];
    return String(held ?? row[field]);
  };
  const edit = (id: number, field: string, raw: string) =>
    setDraft((was) => ({ ...was, [id]: { ...was[id], [field]: Number(raw) } }));

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <button type="button" onClick={() => setOpen(!open)}
        style={css("width:100%;text-align:left;padding:12px 16px;border:none;background:none;font-family:inherit;cursor:pointer;display:flex;justify-content:space-between;align-items:baseline;gap:10px")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>
          ตารางอัตรา {open ? "▾" : "▸"}
        </span>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>
          {card.vehicles.length} ประเภทรถ · กำไรตั้งต้น {card.marginPercent}%
          {card.updatedAt && ` · แก้ล่าสุด ${card.updatedAt}`}
        </span>
      </button>

      {open && (
        <div style={css("border-top:1px solid #E9EFF5;padding:13px 16px;display:flex;flex-direction:column;gap:14px")}>
          <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
            ตารางนี้ใช้ร่วมกันทั้งทีม — แก้แล้วมีผลกับทุกคนที่เสนอราคาหลังจากนี้ และทุกการแก้ไขถูกบันทึกไว้ใน Audit
          </div>

          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:560px")}>
              <thead>
                <tr style={css("background:#F8FAFC")}>
                  {["ประเภทรถ", "บาท/กม.", "ค่าเริ่มต้น", "ตัวคูณห้องเย็น", "DG (บาท)", ""].map((head) => (
                    <th key={head} style={css("padding:7px 10px;text-align:left;font-size:11px;letter-spacing:.04em;text-transform:uppercase;color:#7B8CA0;font-weight:600;white-space:nowrap")}>
                      {head}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {card.vehicles.map((row) => {
                  const touched = draft[row.id] !== undefined;
                  return (
                    <tr key={row.id} style={css("border-bottom:1px solid #F4F7FA")}>
                      <td style={css("padding:5px 10px;color:#0A2240;font-weight:600;white-space:nowrap")}>{row.label}</td>
                      <td style={css("padding:5px 10px")}>
                        <input value={value(row, "perKm")} inputMode="decimal" style={NUM}
                          onChange={(e) => edit(row.id, "perKm", e.target.value)} />
                      </td>
                      <td style={css("padding:5px 10px")}>
                        <input value={value(row, "baseCharge")} inputMode="decimal" style={NUM}
                          onChange={(e) => edit(row.id, "baseCharge", e.target.value)} />
                      </td>
                      <td style={css("padding:5px 10px")}>
                        <input value={value(row, "chill")} inputMode="decimal" style={NUM}
                          onChange={(e) => edit(row.id, "chill", e.target.value)} />
                      </td>
                      <td style={css("padding:5px 10px")}>
                        <input value={value(row, "dangerousGoods")} inputMode="decimal" style={NUM}
                          onChange={(e) => edit(row.id, "dangerousGoods", e.target.value)} />
                      </td>
                      <td style={css("padding:5px 10px;text-align:right")}>
                        {touched && (
                          <button type="button" disabled={busy}
                            onClick={() => {
                              const held = draft[row.id];
                              void onSave(`/vehicle/${row.id}`, {
                                perKm: held.perKm ?? row.perKm,
                                baseCharge: held.baseCharge ?? row.baseCharge,
                                chill: held.chill ?? row.chill,
                                dangerousGoods: held.dangerousGoods ?? row.dangerousGoods,
                              }).then(() => setDraft((was) => {
                                const next = { ...was }; delete next[row.id]; return next;
                              }));
                            }}
                            style={css("height:24px;padding:0 10px;border:1px solid #16794C;background:#fff;color:#16794C;border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
                            บันทึก
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </ZoomBox>

          <ExtrasEditor card={card} busy={busy} onSave={onSave} />
          <MarginEditor card={card} busy={busy} onSave={onSave} />
        </div>
      )}
    </div>
  );
}

function ExtrasEditor({ card, busy, onSave }: {
  card: Card; busy: boolean;
  onSave: (path: string, payload: unknown) => Promise<void>;
}) {
  const [label, setLabel] = useState("");
  const [basis, setBasis] = useState<OptionBasis>("flat");
  const [rate, setRate] = useState("");

  return (
    <div style={css("border-top:1px solid #EEF3F8;padding-top:12px")}>
      <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:8px")}>
        รายการเพิ่มเติม
      </div>
      {card.extras.map((one) => (
        <div key={one.id} style={css("display:flex;gap:9px;align-items:center;padding:4px 0;font-size:12px;border-bottom:1px solid #F4F7FA")}>
          <span style={css("flex:1;color:" + (one.active ? "#31465C" : "#B6C2CF"))}>{one.label}</span>
          <span style={css("color:#94A3B8;font-size:11.5px;white-space:nowrap")}>{BASIS_TH[one.basis]}</span>
          <span style={css("font-family:ui-monospace,monospace;color:#0A2240;width:72px;text-align:right")}>
            {one.basis === "percent" ? `${one.rate}%` : baht(one.rate)}
          </span>
          <button type="button" disabled={busy}
            onClick={() => void onSave("/extra", {
              id: one.id, label: one.label, basis: one.basis, rate: one.rate, active: !one.active,
            })}
            style={css("height:22px;padding:0 9px;border:1px solid #C9D6E2;background:#fff;color:#64748B;border-radius:3px;font-size:11px;cursor:pointer;font-family:inherit;white-space:nowrap")}>
            {one.active ? "ปิดใช้" : "เปิดใช้"}
          </button>
        </div>
      ))}

      <div style={css("display:flex;gap:7px;align-items:center;margin-top:10px;flex-wrap:wrap")}>
        <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="ชื่อรายการใหม่"
          style={css("height:28px;width:190px;border:1px solid #C9D6E2;border-radius:3px;padding:0 8px;font-size:12px;font-family:inherit")} />
        <select value={basis} onChange={(e) => setBasis(e.target.value as OptionBasis)}
          style={css("height:28px;border:1px solid #C9D6E2;border-radius:3px;padding:0 7px;font-size:12px;background:#fff;font-family:inherit")}>
          {(Object.keys(BASIS_TH) as OptionBasis[]).map((one) => (
            <option key={one} value={one}>{BASIS_TH[one]}</option>
          ))}
        </select>
        <input value={rate} inputMode="decimal" onChange={(e) => setRate(e.target.value)} placeholder="อัตรา"
          style={NUM} />
        <button type="button" disabled={busy || !label.trim()}
          onClick={() => void onSave("/extra", {
            id: 0, label: label.trim(), basis, rate: Number(rate) || 0, active: true,
          }).then(() => { setLabel(""); setRate(""); })}
          style={css("height:28px;padding:0 12px;border:1px solid #0A2240;background:" + (label.trim() ? "#0A2240" : "#C3CFDB") + ";color:#fff;border-radius:3px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
          เพิ่ม
        </button>
      </div>
    </div>
  );
}

function MarginEditor({ card, busy, onSave }: {
  card: Card; busy: boolean;
  onSave: (path: string, payload: unknown) => Promise<void>;
}) {
  const [percent, setPercent] = useState(String(card.marginPercent));
  return (
    <div style={css("border-top:1px solid #EEF3F8;padding-top:12px;display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
      <span style={css("font-size:12px;color:#31465C")}>กำไรตั้งต้นของทั้งทีม</span>
      <input value={percent} inputMode="decimal" onChange={(e) => setPercent(e.target.value)} style={NUM} />
      <span style={css("font-size:12px;color:#7B8CA0")}>%</span>
      <button type="button" disabled={busy || Number(percent) === card.marginPercent}
        onClick={() => void onSave("/margin", { percent: Number(percent) || 0 })}
        style={css("height:26px;padding:0 12px;border:1px solid #16794C;background:#fff;color:#16794C;border-radius:3px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
        บันทึก
      </button>
      {card.updatedBy && (
        <span style={css("font-size:11px;color:#94A3B8")}>แก้โดย {card.updatedBy}</span>
      )}
    </div>
  );
}

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <div style={css(`width:${width}`)}>
      <div style={css("font-size:11px;color:#7B8CA0;margin-bottom:3px;font-weight:600")}>{label}</div>
      {children}
    </div>
  );
}
