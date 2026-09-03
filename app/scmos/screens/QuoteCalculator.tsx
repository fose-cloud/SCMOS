"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
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

export function QuoteCalculator({ onToast }: { onToast: (m: string) => void }) {
  const [card, setCard] = useState<Card | null>(null);
  const [vehicle, setVehicle] = useState("4W");
  const [km, setKm] = useState("");
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
        </div>
      )}

      <CardEditor card={card} busy={busy} onSave={save} />
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

          <div style={css("overflow-x:auto")}>
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
          </div>

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
