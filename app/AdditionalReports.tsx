"use client";

import { useMemo, useState } from "react";

export type ReportKey = "volume" | "operation" | "scorecard" | "safety" | "claims";

const palette = ["#168ce8", "#1a2baa", "#ec6b31", "#750083", "#df3ca7", "#49a7c4"];

const volumeRows = [
  ["DGT", 82.09, 17.91, 0, 0], ["PK", 73.33, 26.67, 0, 0], ["PHANPONG", 100, 0, 0, 0],
  ["JTC", 59.77, 40.23, 0, 0], ["T.O.", 56.41, 43.59, 0, 0], ["SHORE", 15.63, 84.37, 0, 0],
  ["9ISARA", 88.24, 11.76, 0, 0], ["WHITELINE", 78.57, 21.43, 0, 0], ["TATIYAPON", 11.48, 88.52, 0, 0],
  ["FNP", 19.35, 80.65, 0, 0], ["A C N", 50, 50, 0, 0], ["WATTANAKOL", 7.89, 92.11, 0, 0],
  ["WEALTHY", .47, 99.53, 0, 0], ["NHP", 0, 100, 0, 0], ["AIR SEA", 0, 100, 0, 0],
  ["APPA", 0, 100, 0, 0], ["JKP", 0, 100, 0, 0], ["NATNISA", 0, 0, 100, 0], ["PHURADA", 0, 100, 0, 0],
];

const operatorRows = [
  ["MALIWAN", 263, 0, 0, 23], ["Ananya", 208, 8, 16, 37], ["NATTIKORN", 94, 0, 0, 0],
  ["JIRATCHAYA", 0, 80, 129, 0], ["TITCHANATORN", 0, 23, 96, 0], ["UTHAI", 0, 89, 609, 0], ["WATSANA", 0, 186, 269, 0],
];

const scoreRows = [
  ["DGT Cross Haul", 100], ["Jakapol Transport Co., Ltd.", 100], ["Phanpong Logistics", 100],
  ["Sangja Transport Co., Ltd.", 100], ["SBT Transport Co., Ltd.", 100], ["Tatiyaphol Transport Co., Ltd", 100],
  ["WATTANAKOL TRANSPORTS", 100], ["Wealthy Logistic", 100], ["JTC Logistics Co., Ltd.", 97], ["Natnisa Transport Co., Ltd.", 90],
];

const safetyRows = [
  ["DGT", 1, 0, 1, 0], ["JTC", 0, 1, 2, 1], ["NATNISA", 0, 0, 2, 0], ["PHANPONG", 0, 1, 0, 0],
  ["PHURADA", 0, 1, 0, 0], ["PK", 0, 0, 3, 0], ["SHORE", 0, 0, 3, 0], ["TATIYAPOL", 0, 1, 0, 0],
  ["WATTANAKOL", 0, 4, 0, 0], ["WEALTHY", 0, 1, 0, 0],
];

const claims = [
  [36, "Audi", 1923749.69], [120, "Benz", 775669.40], [15, "BMW", 243230.33], [55, "Porsche", 448941.33], [15, "Volvo", 780922.19],
];

function Donut({ values, center, labels }: { values: number[]; center: string; labels: string[] }) {
  const total = values.reduce((sum, value) => sum + value, 0) || 1;
  let cursor = 0;
  const stops = values.map((value, index) => {
    const start = cursor;
    cursor += value / total * 100;
    return `${palette[index % palette.length]} ${start}% ${cursor}%`;
  }).join(",");
  return <div className="report-donut-wrap"><div className="report-donut" style={{ background: `conic-gradient(${stops})` }}><div><b>{center}</b><span>Total</span></div></div><div className="mini-legend">{values.map((value, index) => <span key={labels[index]}><i style={{ background: palette[index % palette.length] }} />{labels[index]} <b>{value.toLocaleString()}</b></span>)}</div></div>;
}

function ReportHeader({ title, kicker, selected, onSelected, options }: { title: string; kicker: string; selected: string; onSelected: (value: string) => void; options: string[] }) {
  return <div className="report-head"><div><p>{kicker}</p><h2>{title}</h2><span>Source-backed monthly view · click a chart segment or filter to narrow the report</span></div><div className="report-controls"><label>REPORTING PERIOD<select><option>June 2026</option><option>May 2026</option><option>April 2026</option></select></label><label>FILTER<select value={selected} onChange={(event) => onSelected(event.target.value)}><option>All</option>{options.map((option) => <option key={option}>{option}</option>)}</select></label></div></div>;
}

function VolumeReport() {
  const [selected, setSelected] = useState("All");
  const rows = selected === "All" ? volumeRows : volumeRows.filter(([name]) => name === selected);
  return <div className="additional-report"><ReportHeader kicker="SUBCONTRACTOR MANAGEMENT" title="Trip volume & equipment mix" selected={selected} onSelected={setSelected} options={volumeRows.map((row) => String(row[0]))} />
    <div className="report-stat-row"><div><span>TOTAL TRIPS</span><b>2,130</b><small>June 2026</small></div><div><span>FCL IMPORT</span><b>1,119</b><small>52.5% of total</small></div><div><span>FCL EXPORT</span><b>565</b><small>26.5% of total</small></div><div><span>LCL TOTAL</span><b>446</b><small>20.9% of total</small></div></div>
    <div className="report-layout wide-left"><article className="report-panel"><div className="report-title"><div><p>TRIP DISTRIBUTION</p><h3>Total trips by subcontractor</h3></div><span>Share by flow</span></div><div className="stack-bars">{rows.map(([name, exp, imp, lclImp, lclExp]) => <div className="stack-row" key={String(name)}><b>{name}</b><div className="stack-track"><i style={{ width: `${exp}%`, background: palette[0] }}>{Number(exp) >= 12 ? `${Number(exp).toFixed(1)}%` : ""}</i><i style={{ width: `${imp}%`, background: palette[1] }}>{Number(imp) >= 12 ? `${Number(imp).toFixed(1)}%` : ""}</i><i style={{ width: `${lclImp}%`, background: palette[2] }}>{Number(lclImp) >= 12 ? `${Number(lclImp).toFixed(1)}%` : ""}</i><i style={{ width: `${lclExp}%`, background: palette[3] }}>{Number(lclExp) >= 12 ? `${Number(lclExp).toFixed(1)}%` : ""}</i></div></div>)}</div><div className="chart-key"><span><i style={{background:palette[0]}}/>FCL Export</span><span><i style={{background:palette[1]}}/>FCL Import</span><span><i style={{background:palette[2]}}/>LCL Import</span><span><i style={{background:palette[3]}}/>LCL Export</span></div></article>
      <div className="donut-grid"><article className="report-panel"><div className="report-title"><h3>LCL Import</h3></div><Donut values={[93,235,58]} center="386" labels={["4WH","6WH","10WH"]}/></article><article className="report-panel"><div className="report-title"><h3>FCL Import</h3></div><Donut values={[51,668,4,386,11]} center="1,119" labels={["20'TK","20'","20'RF","40'","40'RF"]}/></article><article className="report-panel"><div className="report-title"><h3>LCL Export</h3></div><Donut values={[41,13,6]} center="60" labels={["6WH","4WH","10WH"]}/></article><article className="report-panel"><div className="report-title"><h3>FCL Export</h3></div><Donut values={[245,273,44,3]} center="565" labels={["20'RF","20'","40'","40'RF"]}/></article></div>
    </div></div>;
}

function OperationReport() {
  const [selected, setSelected] = useState("All");
  const rows = selected === "All" ? operatorRows : operatorRows.filter(([name]) => name === selected);
  return <div className="additional-report"><ReportHeader kicker="TEAM OPERATIONS" title="Workload by coordinator" selected={selected} onSelected={setSelected} options={operatorRows.map((row) => String(row[0]))} /><div className="report-stat-row"><div><span>TOTAL JOBS HANDLED</span><b>2,130</b><small>7 coordinators</small></div><div><span>HIGHEST WORKLOAD</span><b>698</b><small>Uthai · 32.8%</small></div><div><span>EXPORT FCL</span><b>565</b><small>26.5% of total</small></div><div><span>IMPORT LCL</span><b>1,119</b><small>52.5% of total</small></div></div>
    <div className="report-layout wide-left"><article className="report-panel"><div className="report-title"><div><p>OPERATION OWNERSHIP</p><h3>Jobs by responsible coordinator</h3></div><span>Number of jobs</span></div><div className="group-bars">{rows.map(([name, a,b,c,d]) => <div className="group-row" key={String(name)}><b>{name}</b><div>{[a,b,c,d].map((value,index)=><span key={index}><i style={{width:`${Number(value)/6.2}%`,background:palette[index]}}/>{Number(value) ? value : ""}</span>)}</div></div>)}</div><div className="chart-key"><span><i style={{background:palette[0]}}/>FCL Export</span><span><i style={{background:palette[1]}}/>LCL Import</span><span><i style={{background:palette[2]}}/>FCL Import</span><span><i style={{background:palette[3]}}/>LCL Export</span></div></article><div className="donut-grid"><article className="report-panel"><h3>LCL Import</h3><Donut values={[93,235,58]} center="386" labels={["4WH","6WH","10WH"]}/></article><article className="report-panel"><h3>FCL Import</h3><Donut values={[665,4,386,11,53]} center="1,119" labels={["20'","20'RF","40'","40'RF","20'TK"]}/></article><article className="report-panel"><h3>LCL Export</h3><Donut values={[41,13,6]} center="60" labels={["4WH","6WH","10WH"]}/></article><article className="report-panel"><h3>FCL Export</h3><Donut values={[245,273,44,3]} center="565" labels={["20'","20'RF","40'","40'RF"]}/></article></div></div></div>;
}

function ScorecardReport() {
  const [selected, setSelected] = useState("All");
  const rows = selected === "All" ? scoreRows : scoreRows.filter(([name]) => name === selected);
  return <div className="additional-report"><ReportHeader kicker="KPI SUBCONTRACTOR" title="Performance scorecard" selected={selected} onSelected={setSelected} options={scoreRows.map((row)=>String(row[0]))}/><div className="report-stat-row"><div><span>PASS RATE</span><b>100%</b><small>Target &gt; 75%</small></div><div><span>AVERAGE SCORE</span><b>98.7</b><small>Across 10 subcontractors</small></div><div><span>ZERO MAJOR ACCIDENT</span><b>100%</b><small>All assessed partners</small></div><div><span>LOWEST SCORE</span><b>90</b><small>Still above threshold</small></div></div>
    <article className="report-panel score-panel"><div className="report-title"><div><p>OVERALL SCORE</p><h3>Total scoring percentage by subcontractor</h3></div><span className="target-chip">Pass threshold 75%</span></div><div className="score-bars">{rows.map(([name,value])=><div key={String(name)}><b>{name}</b><span><i style={{width:`${value}%`}}/><em style={{left:"75%"}}/>
    </span><strong>{value}</strong></div>)}</div></article><div className="metric-cards">{[["Zero Accident (Major)","100%"],["Zero Accident (Minor)","99.7%"],["Cargo damage reporting","100%"],["On-time delivery","98.9%"],["Vehicle safety readiness","100%"],["Customer satisfaction","99.4%"]].map(([label,value],index)=><article key={label} className="report-panel"><span className="metric-ring" style={{background:`conic-gradient(${palette[index%palette.length]} 0 ${value}, #e7edf1 ${value} 100%)`}}><i>{value}</i></span><div><h3>{label}</h3><small>Monthly weighted score</small></div></article>)}</div></div>;
}

function SafetyReport() {
  const [selected, setSelected] = useState("All");
  const rows = selected === "All" ? safetyRows : safetyRows.filter(([name])=>name===selected);
  const topCases = [["JTC",4],["WATTANAKOL",4],["PK",3],["SHORE",3],["DGT",2],["NATNISA",2]];
  return <div className="additional-report"><ReportHeader kicker="SAFETY & ACCIDENT" title="Trucking safety performance" selected={selected} onSelected={setSelected} options={safetyRows.map((row)=>String(row[0]))}/><div className="report-stat-row"><div><span>TOTAL SHIPMENTS</span><b>12K</b><small>Selected period</small></div><div><span>TOTAL CASES</span><b>22</b><small>All incident types</small></div><div><span>INCIDENT RATE</span><b>0.18%</b><small>Cases ÷ shipments</small></div><div><span>MAJOR ACCIDENT</span><b>1</b><small>Requires management review</small></div></div>
    <div className="safety-top"><article className="report-panel"><div className="report-title"><div><p>CONCENTRATION</p><h3>Top 5 case contributors</h3></div></div><Donut values={topCases.map(x=>Number(x[1]))} center="18" labels={topCases.map(x=>String(x[0]))}/></article><article className="report-panel gauge-card"><p>INDIVIDUAL SHIPMENT / TOTAL</p><div className="gauge" style={{background:"conic-gradient(from 270deg, #168ce8 0 25%, #e7e3de 25% 50%, transparent 50% 100%)"}}><b>12.0%</b></div><small>Top contributor share</small></article><article className="report-panel safety-callouts"><div><b>12K</b><span>Total individual shipment</span></div><div><b>22</b><span>Total cases</span></div></article><article className="report-panel gauge-card"><p>INCIDENT / MONTHLY SHIPMENT</p><div className="gauge" style={{background:"conic-gradient(from 270deg, #168ce8 0 25%, #e7e3de 25% 50%, transparent 50% 100%)"}}><b>0.91</b></div><small>Index vs. 1.81 ceiling</small></article></div>
    <article className="report-panel incident-chart"><div className="report-title"><div><p>INCIDENT TYPE</p><h3>Cases by subcontractor</h3></div></div><div className="incident-bars">{rows.map(([name,...values])=><div key={String(name)}><span className="bars">{values.map((value,index)=><i key={index} style={{height:`${Number(value)*34}px`,background:palette[index]}}>{Number(value)||""}</i>)}</span><b>{name}</b></div>)}</div><div className="chart-key"><span><i style={{background:palette[0]}}/>Major accident</span><span><i style={{background:palette[1]}}/>Minor accident</span><span><i style={{background:palette[2]}}/>Complaint</span><span><i style={{background:palette[3]}}/>Breakdown / loading</span></div></article></div>;
}

function ClaimsReport() {
  const [selected, setSelected] = useState("All");
  const visible = selected === "All" ? claims : claims.filter(([,customer])=>customer===selected);
  const total = visible.reduce((sum,row)=>sum+Number(row[2]),0);
  return <div className="additional-report"><ReportHeader kicker="CLAIM MANAGEMENT" title="Claim exposure dashboard" selected={selected} onSelected={setSelected} options={claims.map(row=>String(row[1]))}/><div className="report-stat-row"><div><span>TOTAL CLAIM AMOUNT</span><b>฿{(total/1e6).toFixed(2)}M</b><small>February 2026</small></div><div><span>CLAIM RECORDS</span><b>{visible.reduce((sum,row)=>sum+Number(row[0]),0)}</b><small>Across selected customers</small></div><div><span>SUBCONTRACTOR</span><b>ABC</b><small>100% of selected claims</small></div><div><span>LARGEST CUSTOMER</span><b>Audi</b><small>฿1.92M exposure</small></div></div>
    <div className="claim-grid"><article className="report-panel"><div className="report-title"><div><p>MONTHLY TREND</p><h3>Claim amount by month</h3></div></div><div className="single-bar"><span>February</span><i style={{width:`${Math.min(total/50000,100)}%`}}/><b>฿{(total/1e6).toFixed(2)}M</b></div></article><article className="report-panel"><div className="report-title"><div><p>SUBCONTRACTOR SHARE</p><h3>Claim amount by subcontractor</h3></div></div><Donut values={[total]} center={`฿${(total/1e6).toFixed(2)}M`} labels={["ABC"]}/></article><article className="report-panel customer-bars"><div className="report-title"><div><p>CUSTOMER EXPOSURE</p><h3>Claim amount by customer</h3></div></div>{visible.map(([count,customer,amount],index)=><div key={String(customer)}><b>{customer}</b><span><i style={{width:`${Number(amount)/20000}%`,background:palette[index]}}/></span><strong>฿{(Number(amount)/1e6).toFixed(2)}M</strong></div>)}</article></div>
    <article className="report-panel claim-table"><div className="report-title"><div><p>AUDIT DETAIL</p><h3>Claim register</h3></div><button onClick={()=>window.print()}>Export PDF</button></div><table><thead><tr><th>YEAR</th><th>MONTH</th><th>SUBCONTRACTOR</th><th>CUSTOMER</th><th>NO. OF CLAIMS</th><th>CLAIM AMOUNT</th><th>STATUS</th></tr></thead><tbody>{visible.map(([count,customer,amount])=><tr key={String(customer)}><td>2026</td><td>February</td><td>ABC</td><td>{customer}</td><td>{count}</td><td>฿{Number(amount).toLocaleString(undefined,{minimumFractionDigits:2})}</td><td><span className="pill not-assessable">Under review</span></td></tr>)}</tbody><tfoot><tr><td colSpan={4}>TOTAL</td><td>{visible.reduce((sum,row)=>sum+Number(row[0]),0)}</td><td>฿{total.toLocaleString(undefined,{minimumFractionDigits:2})}</td><td/></tr></tfoot></table></article></div>;
}

export function AdditionalReports({ report }: { report: ReportKey }) {
  const component = useMemo(() => ({ volume: <VolumeReport />, operation: <OperationReport />, scorecard: <ScorecardReport />, safety: <SafetyReport />, claims: <ClaimsReport /> })[report], [report]);
  return component;
}
