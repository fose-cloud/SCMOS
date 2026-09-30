"use client";

import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { filterLanes, parseCarrierRates, pricedBands, vehiclesOf, type CarrierRates as Rates } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

/**
 * The carrier's own rates, read only (29 Sep 2026): the lanes of the rate book
 * written against its company, each vehicle's price on each fuel band it is
 * priced on. The API sends nothing else — no other carrier's lane or band, no
 * quote, no selling price — and a band the lanes shown leave empty is no column.
 */
export function CarrierRates() {
  const [rates, setRates] = useState<Rates | null>(null);
  const [error, setError] = useState("");
  const [service, setService] = useState("");
  const [query, setQuery] = useState("");

  useEffect(() => {
    let alive = true;
    void (async () => {
      try {
        const response = await apiFetch("/api/carrier/rates", { headers: { accept: "application/json" } });
        const body: unknown = await response.json().catch(() => null);
        if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิดอัตราค่าขนส่งไม่ได้ (${response.status})`);
        if (alive) setRates(parseCarrierRates(body));
      } catch (problem) {
        if (alive) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
      }
    })();
    return () => { alive = false; };
  }, []);

  const services = useMemo(() => [...new Set((rates?.lanes ?? []).map(one => one.service).filter(Boolean))].sort(), [rates]);
  const shown = useMemo(() => filterLanes(rates?.lanes ?? [], service, query), [rates, service, query]);

  if (error) return <Notice tone="#B45309">{error}</Notice>;
  if (!rates) return <div style={css("padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>กำลังโหลด…</div>;

  const bands = pricedBands(rates.bands, shown);
  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px")}>
        <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>อัตราค่าขนส่งของบริษัท</div>
        <div style={css("font-size:16px;font-weight:700;color:#0F2B46;margin-top:2px")}>{rates.supplierName}</div>
      </div>

      <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:center;background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:9px 10px")}>
        <select aria-label="บริการ" value={service} onChange={(event) => setService(event.target.value)}
          style={css("height:31px;padding:0 8px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font-size:12px;font-family:inherit")}>
          <option value="">ทุกบริการ</option>
          {services.map(one => <option key={one} value={one}>{one}</option>)}
        </select>
        <input aria-label="ค้นหาเส้นทาง" placeholder="ค้นหา ต้นทาง ปลายทาง ลูกค้า" value={query} onChange={(event) => setQuery(event.target.value)}
          style={css("height:31px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;flex:1;min-width:180px")} />
        <span style={css("font-size:11.5px;color:#7B8CA0")}>{shown.length} เส้นทาง</span>
      </div>

      {shown.length === 0
        ? <Notice tone="#7B8CA0">{rates.lanes.length === 0 ? "ยังไม่มีอัตราค่าขนส่งของบริษัทในระบบ" : "ไม่พบเส้นทางที่ค้นหา"}</Notice>
        : <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px")}><ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:" + (420 + bands.length * 78) + "px")}>
            <thead>
              <tr>
                {["บริการ", "ลูกค้า", "ต้นทาง → ปลายทาง", "รถ"].map(head => <th key={head} style={css(HEAD)}>{head}</th>)}
                {bands.map(one => <th key={one.position} style={css(HEAD + "text-align:right")} title={`ดีเซล ${one.min}–${one.max}`}>{one.label}</th>)}
              </tr>
            </thead>
            <tbody>
              {shown.flatMap(one => vehiclesOf(one).map((vehicle, index) => (
                <tr key={one.id + ":" + vehicle}>
                  <td style={css(CELL)}>{index === 0 ? one.service : ""}</td>
                  <td style={css(CELL)}>{index === 0 ? one.customer || "—" : ""}</td>
                  <td style={css(CELL)}>{index === 0 ? <>{one.from || "—"} → {one.to || "—"}{one.county ? <span style={css("color:#7B8CA0")}> · {one.county}</span> : null}</> : ""}</td>
                  <td style={css(CELL + "white-space:nowrap")}>{vehicle}</td>
                  {bands.map(b => {
                    const price = one.prices[vehicle]?.[b.position];
                    return <td key={b.position} style={css(CELL + "text-align:right;font-family:'IBM Plex Mono',monospace")}>
                      {price == null ? "—" : price.toLocaleString("en-US")}</td>;
                  })}
                </tr>
              )))}
            </tbody>
          </table>
        </ZoomBox></div>}
    </div>
  );
}

const HEAD = "text-align:left;padding:9px 10px;background:#EEF3F8;color:#52657A;font-weight:650;white-space:nowrap;";
const CELL = "padding:8px 10px;border-top:1px solid #E6ECF3;vertical-align:top;color:#0F2B46;";

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:18px 20px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}
