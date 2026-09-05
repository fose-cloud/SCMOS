"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { directionsEmbed, directionsLink, hasRoute } from "../mapsEmbed";

/** What the routing engine answered, or why it did not. */
type Measured = {
  ok: boolean; km: number; message: string; fromLabel: string; toLabel: string;
};
import { ZoomBox } from "../TableFrame";
import {
  BASIS_TH, type OptionBasis, type QuoteOption, type VehicleRate, quote,
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

const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;width:100%;font-family:inherit");
const NUM = css("height:28px;width:80px;border:1px solid #C9D6E2;border-radius:3px;padding:0 7px;font-size:12px;font-family:ui-monospace,monospace;text-align:right");
const baht = (n: number) => n.toLocaleString("en-US");

/**
 * The route on a map, beside the distance it exists to check.
 *
 * The calculator asks for kilometres and has never had anything to check them
 * against — the first person to price a lane types a number from memory, and
 * everybody after that starts from theirs. Google will not be asked what the
 * distance is: it draws the two ends and the road between them, and a person
 * still decides what to put in the box and whether to record it. A distance is
 * a judgement, which is why the stored one carries the name of whoever made it.
 *
 * The plain Google Maps link is offered whether or not a key is configured,
 * because it needs none — and on a site with no key it is the whole feature.
 */
function RouteMap({ mapsKey, from, to }: { mapsKey: string; from: string; to: string }) {
  const embed = directionsEmbed(mapsKey, from, to);
  const link = directionsLink(from, to);

  return (
    <div style={css("margin-top:10px;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden;background:#fff")}>
      <div style={css("padding:8px 12px;border-bottom:1px solid #E9EFF5;display:flex;"
        + "align-items:center;gap:10px;flex-wrap:wrap;font-size:11.5px;color:#7B8CA0")}>
        <span><b style={css("color:#0A2240")}>{from.trim()}</b> → <b style={css("color:#0A2240")}>{to.trim()}</b></span>
        {/* Google's own reading of the road, for checking the box against —
            never written into it. Opening it is a person's decision and so is
            what they do with what they see. */}
        {link && (
          <a href={link} target="_blank" rel="noreferrer noopener"
            style={css("margin-left:auto;color:#1D5FA8;font-weight:600;text-decoration:none")}>
            เปิดใน Google Maps ↗
          </a>
        )}
      </div>

      {embed ? (
        <iframe
          title={`เส้นทาง ${from.trim()} ถึง ${to.trim()}`}
          src={embed}
          width="100%"
          height="360"
          style={css("border:0;display:block")}
          loading="lazy"
          allowFullScreen
          referrerPolicy="strict-origin-when-cross-origin"
        />
      ) : (
        /*
         * Said plainly rather than drawn as an empty box. A blank map on a
         * pricing screen reads as "Google has nothing for this route", which
         * would be a statement about the journey instead of about the setting.
         */
        <div style={css("padding:22px 16px;font-size:12px;color:#7B8CA0;line-height:1.7")}>
          ยังไม่ได้ตั้งค่า Google Maps สำหรับระบบนี้ — แผนที่ในหน้าจึงยังไม่แสดง
          <div style={css("color:#94A3B8;font-size:11.5px;margin-top:4px")}>
            ตั้งค่า <code style={css("font-family:ui-monospace,monospace")}>GOOGLE_MAPS_EMBED_KEY</code>
            {" "}ที่ App Service application settings แล้วรีสตาร์ท · ปุ่ม “เปิดใน Google Maps” ด้านบนใช้ได้อยู่แล้วโดยไม่ต้องตั้งค่า
          </div>
        </div>
      )}
    </div>
  );
}

export function QuoteCalculator({ onToast, mapsKey }: {
  onToast: (m: string) => void;
  /**
   * Google Maps embed key, or empty when none is configured.
   *
   * From a server-rendered prop rather than NEXT_PUBLIC_, so it is an
   * application setting that takes effect without a rebuild — see app/page.tsx.
   */
  mapsKey: string;
}) {
  const [card, setCard] = useState<Card | null>(null);
  const [vehicle, setVehicle] = useState("4W");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [km, setKm] = useState("");
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
        ok: false, km: 0, fromLabel: "", toLabel: "",
        message: `อ่านระยะทางไม่สำเร็จ (HTTP ${reply.status})`,
      });
    } catch {
      setMeasured({ ok: false, km: 0, message: "ติดต่อระบบไม่ได้", fromLabel: "", toLabel: "" });
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

  /*
   * Both questions about a journey, asked together.
   *
   * Waited on rather than asked per keystroke: a place name is a dozen
   * characters and the history search reads two rate tables.
   */
  useEffect(() => {
    if (!from.trim() || !to.trim()) return;
    const wait = window.setTimeout(async () => {
      const query = new URLSearchParams({ from: from.trim(), to: to.trim(), vehicle });
      const response = await apiFetch(`/api/journeys/look?${query}`, { headers: { accept: "application/json" } });
      if (!response.ok) return;
      const answer = await response.json() as Look;
      setFetched({ journey: `${from.trim()}→${to.trim()}|${vehicle}`, look: answer });
      // A distance already agreed fills the box, so nobody types it twice and
      // the two typings differ. Anything already typed is left alone.
      if (answer.known && !km.trim()) setKm(String(answer.known.km));
    }, 450);
    return () => window.clearTimeout(wait);
    // `km` is deliberately absent: filling the box must not re-run the lookup.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to, vehicle]);

  /** Records the distance for this journey so the next quotation starts with it. */
  async function remember() {
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
      setFetched((was) => was ? { ...was, look: { ...was.look, known: reply.journey! } } : was);
    }
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

  const look = fetched?.journey === `${from.trim()}→${to.trim()}|${vehicle}` ? fetched.look : null;

  const answer = quote(card.vehicles, {
    vehicle,
    km: Number(km.replace(/,/g, "")),
    dangerousGoods: dg,
    marginPercent: Number(margin) || 0,
    options,
  });

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {/* ------------------------------------------------ the question */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ประเภทรถ" width="150px">
            <select value={vehicle} onChange={(e) => setVehicle(e.target.value)} style={INPUT}>
              {card.vehicles.map((one) => (
                <option key={one.code} value={one.code}>{one.label}</option>
              ))}
            </select>
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
                {Number(km.replace(/,/g, "")) > 0 && Number(km.replace(/,/g, "")) !== look.known.km && (
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
          <RouteMap mapsKey={mapsKey} from={from} to={to} />
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
      </div>

      {/* ------------------------------------------------- the working */}
      {answer.refusals.length > 0 ? (
        <div style={css("background:#FFF8F5;border:1px solid #F2C4B4;border-radius:5px;padding:13px 16px;font-size:12.5px;color:#9A3412")}>
          {answer.refusals.join(" · ")}
        </div>
      ) : (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
            {card.vehicles.find((one) => one.code === vehicle)?.label} ·
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

      <CardEditor card={card} busy={busy} onSave={save} />
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
