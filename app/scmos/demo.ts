import { fdate, ftime, money, pad, rng } from "./util";

export type Ship = {
  id: number;
  abs: string;
  job: string;
  bkg: string;
  cust: string;
  dir: "Import" | "Export";
  sType: string;
  fcl: string;
  dg: string;
  cont: string;
  contType: string;
  sub: string;
  truck: string;
  plate: string;
  trailer: string;
  driver: string;
  phone: string;
  from: string;
  to: string;
  plan: Date;
  actual: Date | null;
  delivery: Date | null;
  status: string;
  delay: string;
  next: string;
  bill: string;
  invDays: number | null;
  cost: number;
  sell: number;
  remark: string;
  op: string;
  opId: string;
  assignedBy: string;
  assignedDate: string;
  lastUpdate: string;
  flags: string[];
  action: boolean;
  prio: "HIGH" | "MEDIUM" | "LOW";
  respParty: string;
  eta: string;
};

export type Supplier = {
  code: string; name: string; vendor: string; contact: string; phone: string;
  email: string; billEmail: string; service: string; trucks: number; area: string;
  dg: string; reefer: string; iso: string; gps: string; ins: string; insExp: string;
  lic: string; licExp: string; safety: string; fleet: number; drivers: number;
  status: string; trips: number; late: number; score: number;
};

export type CarPar = {
  no: string; cust: string; sub: string; date: string; type: string;
  owner: string; target: string; age: number; status: string;
};

export type Rate = {
  cust: string; sub: string; from: string; to: string; truck: string;
  contType: string; dg: string; base: number; fuelBase: number; fuelNow: number;
  fuelAdj: number; addl: number; sell: number; eff: string; exp: string; status: string;
};

export type Db = {
  ships: Ship[];
  suppliers: Supplier[];
  carpar: CarPar[];
  rates: Rate[];
  SUBS: string[];
  CUST: string[];
  TRUCKS: string[];
  PORTS: string[];
  WH: string[];
  DELAY: string[];
  OPS: string[];
};

export function buildDb(): Db {
  const R = rng(20260811);
  const pick = <T,>(a: T[]) => a[Math.floor(R() * a.length)];

  const SUBS = ["WEALTHY", "WHITELINE", "DGT", "PHURADA", "SHORE", "NATNISA", "SANGJA", "KANIN TRANSPORT", "SSL", "SBT"];
  const CUST = ["Siam Chemical Co., Ltd.", "Thai Auto Parts Mfg.", "Bangkok Foods Intl.", "PTT Polymer Marketing", "Nissin Electric (TH)", "Delta Components Asia", "SCG Packaging", "Hana Microelectronics"];
  const TRUCKS = ["4-Wheel", "6-Wheel", "10-Wheel", "Trailer 40ft", "Trailer 20ft", "Reefer 40ft", "Flatbed"];
  const PORTS = ["Laem Chabang Port B1", "Laem Chabang Port C3", "Bangkok Port (Klong Toey)", "Lat Krabang ICD", "Map Ta Phut Terminal"];
  const WH = ["Amata Nakorn WH", "Bangna KM.19 DC", "Rojana Ayutthaya Plant", "Hemaraj ESIE Plant", "Wellgrow Industrial WH", "Navanakorn Factory"];
  const DELAY = ["Port congestion", "Customs hold", "Truck breakdown", "Driver late", "Traffic on Motorway 7", "Customer not ready", "Document missing", "Heavy rain"];
  const DRIVERS = ["Somchai T.", "Anucha K.", "Prasit W.", "Wichai S.", "Manop R.", "Chatchai P.", "Narong D.", "Suriya B.", "Kittipong M.", "Adisak L."];

  const POOL: string[] = [];
  const dist: Record<string, number> = {
    Completed: 46, "In Transit": 11, Delayed: 8, "Waiting Truck": 8, "Truck Assigned": 4,
    "Driver Assigned": 4, "Plate Received": 4, "Ready for Pickup": 4, "Arrived Customer": 4,
    "Loading / Delivery": 5, Cancelled: 2,
  };
  Object.keys(dist).forEach((k) => { for (let i = 0; i < dist[k]; i++) POOL.push(k); });

  const base = new Date(Date.UTC(2026, 7, 11, 8, 0));
  const ships: Ship[] = [];

  for (let i = 0; i < 132; i++) {
    const dir: "Import" | "Export" = R() < 0.54 ? "Import" : "Export";
    const st = POOL[Math.floor(R() * POOL.length)];
    const dayOff = Math.floor(R() * 62) - 58;
    const plan = new Date(base.getTime() + dayOff * 86400000 + Math.floor(R() * 12) * 3600000);
    const truck = pick(TRUCKS);
    const fcl = truck.indexOf("Trailer") === 0 || truck.indexOf("Reefer") === 0 ? "FCL" : R() < 0.35 ? "FCL" : "LCL";
    const dg = R() < 0.16;
    const sub = pick(SUBS);
    const cust = pick(CUST);
    const late = st === "Delayed";
    const actual = new Date(plan.getTime() + (late ? 2 + R() * 5 : R() * 1.2 - 0.4) * 3600000);
    const done = st === "Completed";
    const delivery = new Date(actual.getTime() + (2 + R() * 4) * 3600000);
    const assigned = ["Waiting Truck", "Cancelled"].indexOf(st) < 0;
    const cost = Math.round((3800 + R() * 9400) / 50) * 50;
    const sell = Math.round((cost * (1.06 + R() * 0.24)) / 50) * 50;
    const invDays = done ? Math.floor(R() * 8) : null;
    let bill = "Not Due";
    if (done && invDays !== null) {
      bill = invDays <= 2 ? "Within KPI" : invDays === 3 ? "Due Soon" : invDays <= 4 ? "Within KPI" : "Overdue";
    }
    if (done && R() < 0.32) bill = "Completed";

    ships.push({
      id: i,
      abs: "ABS" + (26000 + i * 7),
      job: (dir === "Import" ? "TIM" : "TEX") + "26-" + (4100 + i * 3),
      bkg: "BK" + (98000 + i * 13),
      cust,
      dir,
      sType: dir === "Import" ? pick(["Port to Door", "Door to Door", "CY Pickup"]) : pick(["Door to Port", "Factory Stuffing", "CFS Return"]),
      fcl,
      dg: dg ? "DG" : "Non-DG",
      cont: fcl === "FCL" ? pick(["TCLU", "MSKU", "CSNU", "TGHU", "HLXU"]) + (Math.floor(R() * 9000000) + 1000000) : "—",
      contType: fcl === "FCL" ? pick(["40'HC", "20'GP", "40'GP", "40'RF"]) : "—",
      sub,
      truck,
      plate: assigned ? Math.floor(R() * 80) + 10 + "-" + (Math.floor(R() * 9000) + 1000) + " BKK" : "—",
      trailer: assigned && truck.indexOf("Trailer") === 0 ? Math.floor(R() * 80) + 10 + "-" + (Math.floor(R() * 9000) + 1000) + " CHB" : "—",
      driver: assigned ? pick(DRIVERS) : "—",
      phone: assigned ? "08" + Math.floor(R() * 9) + "-" + (Math.floor(R() * 900) + 100) + "-" + (Math.floor(R() * 9000) + 1000) : "—",
      from: dir === "Import" ? pick(PORTS) : pick(WH),
      to: dir === "Import" ? pick(WH) : pick(PORTS),
      plan,
      actual: assigned ? actual : null,
      delivery: done || st === "Delayed" ? delivery : null,
      status: st,
      delay: late ? pick(DELAY) : "—",
      next: ["Completed", "Cancelled"].indexOf(st) < 0 ? ftime(new Date(base.getTime() + (1 + R() * 5) * 3600000)) : "—",
      bill,
      invDays,
      cost,
      sell,
      remark: R() < 0.22 ? pick(["Customer requested early pickup", "Second attempt delivery", "Reefer set -18°C", "DG Class 3 — placard checked", "Overtime gate-in approved"]) : "—",
      // filled in below
      op: "", opId: "", assignedBy: "", assignedDate: "", lastUpdate: "",
      flags: [], action: false, prio: "LOW", respParty: "—", eta: "—",
    });
  }

  ships.sort((a, b) => b.plan.getTime() - a.plan.getTime());

  const OPS = ["Watsana", "Uthai", "Ananya", "Jiratchaya", "Maliwan"];
  ships.forEach((s, i) => {
    s.op = OPS[(i * 3 + s.id) % 5];
    s.opId = "OP-" + pad(((i * 3 + s.id) % 5) + 1);
    s.assignedBy = "Titchanatorn (Supervisor)";
    s.assignedDate = fdate(new Date(s.plan.getTime() - 86400000)) + " 08:32";
    s.lastUpdate = fdate(s.plan) + " " + ftime(new Date(s.plan.getTime() + 5400000));

    const flags: string[] = [];
    if (s.status === "Waiting Truck") flags.push("Truck not confirmed");
    if (s.plate === "—" && ["Waiting Truck", "Cancelled"].indexOf(s.status) < 0) flags.push("Truck plate missing");
    if (s.driver === "—" && ["Waiting Truck", "Cancelled"].indexOf(s.status) < 0) flags.push("Driver missing");
    if (s.status === "Delayed") flags.push("Shipment delayed");
    if (s.status === "Completed" && s.bill === "Overdue") flags.push("Billing document missing");
    s.flags = flags;
    s.action = flags.length > 0 && s.status !== "Cancelled";
    s.prio =
      s.status === "Delayed" ? "HIGH"
        : s.status === "Waiting Truck" || flags.length > 1 ? "HIGH"
          : ["In Transit", "Loading / Delivery", "Arrived Customer"].indexOf(s.status) >= 0 ? "MEDIUM"
            : "LOW";
    s.respParty = s.status === "Delayed" ? ["Subcontractor", "Port / Customs", "Customer"][s.id % 3] : "—";
    s.eta = s.actual ? ftime(new Date(s.actual.getTime() + 3600000)) : "—";
  });

  const CONTACTS = ["K. Somsak", "K. Wanida", "K. Piyapong", "K. Nattaya", "K. Chaiwat", "K. Sudarat", "K. Weerachai", "K. Pornthip", "K. Anan", "K. Duangjai"];
  const AREAS = ["EEC / Bangkok", "Nationwide", "EEC only", "Central / East", "Bangkok / Samut Prakan", "Nationwide", "EEC / Ayutthaya", "Central", "EEC / Rayong", "Bangkok"];
  const TRUCK_COUNTS = [3, 5, 4, 6, 3, 4, 5, 7, 4, 3];

  const suppliers: Supplier[] = SUBS.map((n, i) => {
    const s = ships.filter((x) => x.sub === n);
    const done = s.filter((x) => x.status === "Completed").length;
    const late = s.filter((x) => x.status === "Delayed").length;
    const score = Math.max(58, Math.min(98, Math.round(93 - late * 3.2 + (done % 5) - i * 0.6 + R() * 4)));
    return {
      code: "SUB-" + pad(i + 1),
      name: n,
      vendor: "V" + (70210 + i * 11),
      contact: CONTACTS[i],
      phone: "02-" + (300 + i * 7) + "-" + (4000 + i * 13),
      email: n.toLowerCase().split(" ")[0] + "@transport.co.th",
      billEmail: "billing@" + n.toLowerCase().split(" ")[0] + ".co.th",
      service: i % 3 === 0 ? "FCL / LCL / DG" : i % 3 === 1 ? "FCL / LCL" : "FCL / Reefer",
      trucks: TRUCK_COUNTS[i],
      area: AREAS[i],
      dg: i % 3 === 0 ? "Yes" : "No",
      reefer: i % 4 === 0 ? "Yes" : "No",
      iso: i % 5 === 0 ? "Yes" : "No",
      gps: i === 7 ? "Partial" : "Yes",
      ins: money(5000000 + i * 1000000),
      insExp: i % 4 === 0 ? "30 Sep 2026" : i % 3 === 0 ? "14 Nov 2026" : "28 Feb 2027",
      lic: "TL-" + (2210 + i * 3),
      licExp: i === 4 ? "02 Sep 2026" : "31 Mar 2027",
      safety: i === 8 ? "Expired" : "Valid",
      fleet: 8 + i * 3,
      drivers: 11 + i * 4,
      status: i === 8 ? "Suspended" : i === 5 ? "Conditional" : i === 9 ? "Under Evaluation" : "Approved",
      trips: s.length,
      late,
      score,
    };
  });

  const carpar: CarPar[] = [];
  for (let i = 0; i < 18; i++) {
    const st = ["Open", "Investigation", "Action Required", "Waiting Evidence", "Pending Verification", "Closed", "Overdue"][Math.floor(R() * 7)];
    carpar.push({
      no: (R() < 0.6 ? "CAR" : "PAR") + "-26-" + pad(i + 1),
      cust: pick(CUST),
      sub: pick(SUBS),
      date: fdate(new Date(base.getTime() - Math.floor(R() * 90) * 86400000)),
      type: pick(["Late delivery", "Cargo damage", "Wrong documentation", "Driver behaviour", "Truck condition", "Missing PPE", "Wrong container returned", "Twist lock not secured"]),
      owner: pick(["K. Wanida", "K. Somsak", "K. Nattaya", "K. Chaiwat"]),
      target: fdate(new Date(base.getTime() + (Math.floor(R() * 40) - 12) * 86400000)),
      age: Math.floor(R() * 62),
      status: st,
    });
  }

  const rates: Rate[] = [];
  for (let i = 0; i < 26; i++) {
    const cost = Math.round((3600 + R() * 9000) / 50) * 50;
    const sell = Math.round((cost * (0.97 + R() * 0.34)) / 50) * 50;
    rates.push({
      cust: pick(CUST), sub: pick(SUBS), from: pick(PORTS), to: pick(WH), truck: pick(TRUCKS),
      contType: pick(["40'HC", "20'GP", "40'GP", "40'RF"]),
      dg: R() < 0.2 ? "DG" : "Non-DG",
      base: cost, fuelBase: 31.5, fuelNow: 33.75,
      fuelAdj: Math.round(cost * 0.028),
      addl: Math.round((R() * 1200) / 50) * 50,
      sell, eff: "01 Jul 2026",
      exp: i % 5 === 0 ? "31 Aug 2026" : "31 Dec 2026",
      status: i % 5 === 0 ? "Expiring" : i % 7 === 0 ? "Draft" : "Active",
    });
  }

  return { ships, suppliers, carpar, rates, SUBS, CUST, TRUCKS, PORTS, WH, DELAY, OPS };
}
