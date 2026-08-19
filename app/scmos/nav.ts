export type Screen =
  | "dashboard" | "workspace" | "booking" | "monitoring" | "prerun" | "docverify"
  | "subcontractors" | "capacity" | "rates" | "billing" | "kpi" | "incident" | "carpar"
  | "audit" | "documents" | "reports" | "assistant"
  | "vendor" | "evaluation" | "quotation" | "abs" | "admin"
  | "loreal" | "carrier" | "myjob" | "training";

/**
 * The menu, in the order the work happens.
 *
 * A booking arrives, is worked, gets a truck, runs, is checked the night
 * before, has its documents verified — then the reference screens, then the
 * things that happen once a year. The order is the process, not the alphabet.
 *
 * [screen, English label, Thai label, icon rects as [x, y, w, h]]
 */
export const NAV: [Screen, string, string, number[][]][] = [
  ["dashboard", "Dashboard", "แดชบอร์ด", [[2, 2, 5, 5], [9, 2, 5, 5], [2, 9, 5, 5], [9, 9, 5, 5]]],
  ["workspace", "Workspace", "พื้นที่คีย์งาน", [[2, 2, 12, 4], [2, 8, 5, 6], [9, 8, 5, 2], [9, 12, 5, 2]]],
  // A heading, not a destination — see HEADINGS below.
  ["docverify", "Document Verification", "ตรวจสอบเอกสาร", [[3, 2, 10, 12], [5, 6, 6, 1.5], [5, 9, 4, 1.5]]],
  ["subcontractors", "Supplier", "ผู้รับเหมาช่วง", [[2, 6, 4, 8], [7, 3, 4, 11], [12, 8, 2, 6]]],
  ["capacity", "Capacity", "วางแผนกำลังรถ", [[2, 2, 12, 3], [2, 7, 5, 7], [9, 7, 5, 3], [9, 12, 5, 2]]],
  ["rates", "Rate Management", "อัตราค่าขนส่ง", [[2, 10, 3, 4], [6, 6, 3, 8], [10, 2, 3, 12]]],
  ["billing", "Billing Control", "ควบคุมการวางบิล", [[2, 4, 12, 8], [4, 7, 4, 2]]],
  ["kpi", "KPI", "ตัวชี้วัด", [[2, 10, 3, 4], [6, 6, 3, 8], [10, 2, 3, 12], [2, 2, 2, 2]]],
  ["incident", "Incident", "เหตุผิดปกติ", [[7, 2, 2, 8], [7, 12, 2, 2], [2, 12, 3, 2], [11, 12, 3, 2]]],
  ["carpar", "CAR / PAR", "การแก้ไข/ป้องกัน", [[7, 2, 2, 8], [7, 12, 2, 2]]],
  ["audit", "Audit", "ประวัติการใช้งาน", [[2, 2, 12, 2], [2, 6, 12, 2], [2, 10, 8, 2]]],
  ["documents", "Document Center", "ศูนย์เอกสาร", [[3, 2, 10, 12], [5, 5, 6, 1], [5, 8, 6, 1], [5, 11, 4, 1]]],
  ["reports", "Reports", "รายงาน", [[2, 2, 12, 2], [2, 6, 12, 1.5], [2, 9.5, 9, 1.5], [2, 13, 6, 1.5]]],
  ["assistant", "AI Assistant", "ผู้ช่วย AI", [[4, 3, 8, 8], [6, 12, 4, 2], [2, 5, 2, 2], [12, 5, 2, 2]]],
  ["vendor", "Add New Vendor", "เพิ่มผู้ขนส่งใหม่", [[2, 6, 5, 8], [8, 3, 6, 3], [8, 8, 6, 3], [8, 12, 6, 2]]],
  ["evaluation", "Annual Evaluation", "ประเมินประจำปี", [[2, 2, 12, 12], [5, 6, 6, 1.5], [5, 9, 6, 1.5]]],
  ["quotation", "Rate Quotation", "ขอใบเสนอราคา", [[2, 2, 10, 12], [4, 5, 6, 1.5], [4, 8, 6, 1.5], [4, 11, 4, 1.5]]],
  ["training", "Customer Training Control", "อบรมคนขับ", [[2, 2, 12, 3], [2, 7, 5, 7], [9, 7, 5, 7]]],
  ["carrier", "งานของบริษัท", "Carrier Portal", [[2, 3, 10, 7], [12, 6, 2, 4], [4, 12, 8, 2]]],
  ["abs", "ABS", "ระบบ ABS", [[2, 2, 12, 12], [5, 5, 6, 6]]],
  ["admin", "Administration", "ผู้ดูแลระบบ", [[2, 2, 5, 5], [9, 2, 5, 5], [9, 9, 5, 5]]],
];

/**
 * Screens that hang under another one.
 *
 * A customer report is not a step in the process, so it does not belong in the
 * main list beside Booking and Pre-Run — but it reads the same register the
 * Workspace does, and somebody looking for it will look there. It nests.
 */
export const SUB_NAV: Partial<Record<Screen, [Screen, string, string, number[][]][]>> = {
  // Still in the order the work happens — a booking is taken, the run is
  // watched, the truck is checked the night before. They sit under Workspace
  // because that is the screen they are all about, and the top-level list is
  // shorter for it. The icons come with them: collapsed to a 64px rail there
  // is no label to read, and a sub-menu that vanishes when the rail narrows is
  // three screens nobody can reach.
  workspace: [
    ["myjob", "My Job", "งานของฉัน", [[2, 2, 12, 4], [2, 8, 5, 6], [9, 8, 5, 2], [9, 12, 5, 2]]],
    ["booking", "Booking", "จองรถบรรทุก", [[2, 3, 10, 7], [12, 6, 2, 4], [3, 12, 3, 2], [10, 12, 3, 2]]],
    ["monitoring", "Shipment Monitor", "ติดตามการขนส่ง", [[2, 7, 3, 3], [6, 7, 3, 3], [10, 7, 4, 3], [3, 12, 10, 1.5]]],
    ["prerun", "Pre-Run", "ตรวจก่อนออกงาน", [[2, 3, 12, 2], [2, 7, 8, 2], [2, 11, 5, 2], [11, 9, 3, 5]]],
    ["loreal", "L'OREAL", "รายงานตู้ลูกค้า", [[2, 2, 12, 3], [2, 7, 12, 1.5], [2, 11, 8, 1.5]]],
  ],
};

/**
 * Menu entries that group rather than go anywhere.
 *
 * Clicking one folds its children instead of opening a screen. Workspace became
 * one when everything it used to hold moved down into My Job: a parent that
 * still navigated would open a page with nothing on it, and the only way to
 * learn that is to click it.
 */
export const HEADINGS: Screen[] = ["workspace"];

/**
 * What a carrier's account is allowed to open.
 *
 * Not a smaller version of the operator's menu — a different one. Every screen
 * left out reads the whole register, and a subcontractor who clicks one gets a
 * refusal, which looks like a broken system rather than a boundary working as
 * intended. Their own jobs are the whole of what they came for.
 */
export const CARRIER_SCREENS: Screen[] = ["carrier"];

/** Every screen in the menu, parents and children alike. */
export const ALL_NAV: [Screen, string, string, number[][]][] = [
  ...NAV,
  ...Object.values(SUB_NAV).flat(),
];

/** [title, Thai title, blurb] keyed by screen (plus the shipment drill-down). */
export const META: Record<string, [string, string, string]> = {
  dashboard: ["Executive & Operational Dashboard", "ภาพรวมการปฏิบัติงาน", "Executive = ภาพรวมทั้งแผน · Operational = สิ่งที่ต้องทำวันนี้ · Wall Board = จอแสดงผลหน้างาน — ทุกตัวเลขคิดจากงานจริงใน Operation Workspace คลิกตัวเลขเพื่อเปิดงานชุดนั้นใน Workspace และกด Export Excel เพื่อดึงออกเป็นไฟล์"],
  myjob: ["Operation Workspace", "พื้นที่ทำงานฝ่ายปฏิบัติการ", "Everyone sees the whole team. You edit only the jobs assigned to you."],
  detail: ["Shipment Detail", "รายละเอียดงานขนส่ง", "Full operational traceability: planned vs actual at every milestone, with communication and exception history."],
  booking: ["Truck Booking", "การจองรถบรรทุก", "Validate bookings, request capacity and escalate sequentially through carriers A → B → C while tracking confirmation SLA."],
  subcontractors: ["Subcontractor Master", "ทะเบียนผู้รับเหมาช่วง", "Approved carrier register with fleet, insurance, licence and safety validity."],
  rates: ["Transportation Rate Management", "การจัดการอัตราค่าขนส่ง", "Cost vs selling rate with fuel adjustment and margin control per lane."],
  capacity: ["Capacity Planning", "การวางแผนกำลังรถ", "Daily and weekly truck availability against confirmed demand."],
  billing: ["Billing Control", "การควบคุมการวางบิล", "Supplier invoices must be received within 4 calendar days after delivery / loading completion."],
  monitoring: ["Shipment Monitoring", "ติดตามการขนส่ง", "ติดตามงานตั้งแต่จ่ายงานจนปิดงาน — แผนกับเวลาจริงของทุกขั้นตอน พร้อมบันทึกความล่าช้าและสาเหตุ"],
  kpi: ["Operational KPI", "ตัวชี้วัดการปฏิบัติงาน", "ทุกตัวเลขคำนวณจากทะเบียนงานจริงฝั่ง .NET ตามกฎชุดเดียวกับที่หน้า Workspace ใช้ — เปลี่ยนช่วงเวลาแล้วทุกค่าคิดใหม่ทั้งหมด"],
  carpar: ["CAR / PAR Management", "การจัดการ CAR / PAR", "ทะเบียนเดียวกับหน้า Incident — ระบบไม่ยอมให้ข้ามขั้น: ไม่มีสาเหตุที่แท้จริงก็กำหนดการแก้ไขไม่ได้ ไม่มีผู้รับผิดชอบและกำหนดเสร็จก็ติดตามไม่ได้ และปิดเคสได้เฉพาะระดับหัวหน้างานขึ้นไป"],
  incident: ["Incident & CAR / PAR", "เหตุผิดปกติและการแก้ไข", "เปิดเคสจากเหตุที่เกิดจริง บันทึก 5W1H สาเหตุที่แท้จริง การแก้ไขและการป้องกัน แล้วเดินตามขั้นจนปิดเคสด้วยลายเซ็นของคน"],
  prerun: ["Pre-Run Check", "ตรวจก่อนออกงาน", "ยืนยันรถ คนขับ ทะเบียน และเอกสารก่อนวันงาน พร้อมจับเวลาตอบกลับตาม SLA"],
  audit: ["Audit Trail", "ประวัติการใช้งาน", "ใครแก้อะไร เมื่อไหร่ จากค่าเดิมเป็นค่าใหม่อะไร — อ่านจากประวัติที่ผูกกับงานแต่ละใบ"],
  assistant: ["AI Assistant", "ผู้ช่วย AI", "สิทธิ์ของผู้ช่วยอ่านจาก API ที่บังคับใช้จริง — อ่านและร่างได้เลย เปลี่ยนข้อมูลจริงต้องมีคนอนุมัติ และการลบไม่มีอยู่ในระบบเลย"],
  vendor: ["Add New Vendor", "เพิ่มผู้ขนส่งใหม่", "ลงทะเบียนผู้ขนส่งรายใหม่เข้าทะเบียนเดียวกับที่ Workspace และ KPI ใช้ — เริ่มที่สถานะร่าง จ่ายงานได้ต่อเมื่ออนุมัติแล้ว"],
  evaluation: ["Annual Evaluation", "ประเมินผู้ขนส่งประจำปี", "คะแนนตรงเวลา ตอบยืนยัน และความล่าช้า ดึงจาก KPI Engine ส่วนความปลอดภัยและเอกสารเป็นดุลพินิจของผู้ประเมิน"],
  quotation: ["Rate Quotation", "ขอใบเสนอราคา", "เทียบราคาผู้ขนส่งสำหรับเส้นทางและประเภทรถที่ต้องการ ตามราคาน้ำมันปัจจุบัน — อ่านจากตารางราคาใน Azure SQL"],
  documents: ["Document Register", "ทะเบียนเอกสาร", "Controlled operational and compliance documents with expiry monitoring."],
  reports: ["Management Reports", "รายงานผู้บริหาร", "Standard report catalogue with daily, weekly, monthly, yearly and custom periods."],
  training: ["Customer Training Control", "การอบรมพนักงานขับรถ", "ข้อกำหนดของลูกค้าแต่ละราย ใบรับรองของคนขับ และวันหมดอายุ — สถานะคำนวณจากวันที่ทุกครั้งที่เปิดหน้า ไม่มีงานเบื้องหลังที่ต้องรัน และคนขับที่หลักสูตรบังคับหมดอายุจะรับงานของลูกค้ารายนั้นไม่ได้"],
  carrier: ["งานของบริษัท", "Carrier Portal", "งานที่ลูกค้าส่งมาให้บริษัทนี้ กดรับพร้อมแจ้งทะเบียนรถ คนขับ และเบอร์โทร แล้วข้อมูลจะขึ้นที่หน้างานของเจ้าของงานทันที"],
  loreal: ["L'OREAL Truck Report", "รายงานรถลูกค้า L'OREAL", "ฟอร์มเดียวกับที่ส่งลูกค้าทุกเดือน ดึงจากทะเบียนงานจริง — ช่องที่ระบบยังไม่มีที่มาจะเว้นว่างและบอกไว้ ไม่เดาแทน"],
  abs: ["ABS", "ระบบ ABS", "หน้าจอยังว่าง รอเชื่อมกับ API ของโปรแกรม ABS — เมนู เส้นทาง และการตรวจสิทธิ์พร้อมแล้ว"],
  admin: ["Administration", "การดูแลระบบ", "Role-based access control and audit trail."],
};

export const TAB_DEFS: Record<string, string[]> = {
  // My Workspace: the operator's own day, in the order they work it. Only MY
  // JOBS narrows to the person — the rest show the whole team, because seeing
  // what the team is carrying is the point, and ownership controls editing
  // rather than looking.
  myjob: ["MY JOBS", "PENDING", "TODAY", "TOMORROW", "DELAY", "DOCUMENT MISSING", "COMPLETED", "CALENDAR"],
  // TODAY leads: the first question anyone opening the system has is what is
  // happening now, and it is the one tab whose every figure comes from the API.
  dashboard: ["TODAY", "Executive", "Operational", "Wall Board"],
  booking: ["Booking Queue", "Carrier Escalation", "SLA"],
  // Supplier and CAR/PAR read the real register now, and both carry their own
  // controls. Tabs that narrowed the demo table would be buttons that do
  // nothing, which is worse than no tabs.
  billing: ["Aging", "Invoices", "Advance Receipts"],
  // Capacity, Document Center and Administration carry their own controls now
  // that they read the API; tabs that narrowed a demo table would do nothing.
  reports: ["Catalogue", "Scheduled"],
};

// The dashboard is off this list on purpose: it reads the real operation jobs,
// and its own tiles and pipeline rows are how you narrow it. Rates is off it for
// the same reason — it reads the real quotations and carries its own controls,
// including the diesel price every figure on it depends on.
export const SCREENS_WITH_FILTERS = [
  "billing",
];

export type Account = {
  user: string; name: string; full: string; role: string; id: string; init: string;
  /** Directory id (OP-01…). What job ownership is decided on. */
  opId: string;
};

export const DEFAULT_ROLE = "Operation User";

/**
 * The demo sign-in list, and the name-to-owner-id map the plan needs.
 *
 * Two jobs, both local to the browser:
 *
 * 1. The development login screen, which exists because there is no identity
 *    provider in front of `npm run dev`. Deployed, Web App Login has already
 *    said who this is and `/api/me` says what they may do — this list is not
 *    consulted for either.
 *
 * 2. `opIdForName`, which turns the operator name written in a plan workbook
 *    into an owner id. The importer needs it before a job has ever reached the
 *    API, so a freshly keyed job looks like yours immediately rather than after
 *    a round trip. The API derives the same id on save from `StaffDirectory`,
 *    and its answer is the one stored.
 *
 * `name` must stay spelled exactly as it appears in ops.json — that spelling is
 * what the backfill in ops.ts matches on — and in step with `StaffDirectory.All`.
 */
export const ACCOUNTS: Account[] = [
  { user: "watsana", name: "Watsana", full: "Watsana", role: DEFAULT_ROLE, id: "OP-01", opId: "OP-01", init: "WA" },
  { user: "uthai", name: "Uthai", full: "Uthai", role: DEFAULT_ROLE, id: "OP-02", opId: "OP-02", init: "UT" },
  { user: "ananya", name: "Ananya", full: "Ananya", role: DEFAULT_ROLE, id: "OP-03", opId: "OP-03", init: "AN" },
  { user: "maliwan", name: "Maliwan", full: "Maliwan", role: DEFAULT_ROLE, id: "OP-04", opId: "OP-04", init: "MA" },
  { user: "jiratchaya", name: "Jiratchaya", full: "Jiratchaya", role: DEFAULT_ROLE, id: "OP-05", opId: "OP-05", init: "JI" },
  { user: "titchanatorn", name: "Titchanatorn", full: "Titchanatorn", role: "Operation Supervisor", id: "SV-01", opId: "SV-01", init: "TI" },
  { user: "nattikorn", name: "Nattikorn", full: "Nattikorn", role: "Assistant Manager", id: "AM-01", opId: "AM-01", init: "NA" },
  { user: "admin", name: "Admin", full: "Admin", role: "Administrator", id: "AD-01", opId: "AD-01", init: "AD" },

  // Kept in step with StaffDirectory.All. These four exist so the roles that
  // were defined and enforced but unoccupied can actually be signed in as —
  // a capability set nobody has used is one nobody has tested.
  { user: "cs", name: "Customerservice", full: "Customer Service", role: "CS", id: "CS-01", opId: "CS-01", init: "CS" },
  { user: "management", name: "Management", full: "Management", role: "Management", id: "MG-01", opId: "MG-01", init: "MG" },
  { user: "viewer", name: "Viewer", full: "Viewer", role: "Viewer", id: "VW-01", opId: "VW-01", init: "VW" },
  { user: "subcontractor", name: "Subcontractor", full: "Subcontractor", role: "Subcontractor", id: "SC-01", opId: "SC-01", init: "SC" },
];

/**
 * The owner id for a name off the plan, or an empty string when the name is not
 * one of the five. An unknown owner is left without an id rather than guessed
 * at — an unassigned job is a visible problem, a misassigned one is not.
 */
export function opIdForName(name: string): string {
  const first = (name ?? "").trim().split(/\s+/)[0];
  if (!first) return "";
  const found = ACCOUNTS.find((account) => account.name.toLowerCase() === first.toLowerCase());
  return found?.opId ?? "";
}

// Matching a signed-in email to a directory person lives in
// `StaffDirectory.Match` and nowhere else. The browser had its own copy, which
// would have been a second opinion about who somebody is — and ownership, which
// decides whose jobs a person can edit, is derived from it. `/api/me` answers
// instead.

/**
 * Gone on purpose.
 *
 * What a role may do is decided by `Rules/Roles.cs` and read from `/api/me` as a
 * capability list. A role-name array in the browser was a second opinion the API
 * never saw — and its real failure mode was quieter than that: every test
 * written against it asked "is this person senior", when the question was
 * always "may this person do this particular thing".
 */
export const DEFAULT_LANDING_ROLE = DEFAULT_ROLE;

/** Alert centre feed: [severity, title, body, relative time]. */
export const ALERTS: [string, string, string, string][] = [
  ["Critical", "Truck not confirmed — 3 bookings", "ABS26042, ABS26049, ABS26056 loading tomorrow 06:00 have no confirmed carrier after 2 escalations.", "12 min ago"],
  ["Critical", "Billing overdue", "9 supplier invoices exceed the 4-day KPI. Largest exposure: SANGJA ฿184,200.", "28 min ago"],
  ["Critical", "Transport licence expiring", "NATNISA licence TL-2222 expires 02 Sep 2026 — renewal evidence not received.", "1 h ago"],
  ["Warning", "Truck plate missing", "5 confirmed bookings still have no plate 4 hours before loading.", "1 h ago"],
  ["Warning", "Billing approaching 4-day limit", "11 jobs reach the KPI limit tomorrow.", "2 h ago"],
  ["Warning", "Supplier capacity shortage", "Trailer 40ft shortage of 24 units for week 33.", "3 h ago"],
  ["Warning", "CAR/PAR due date", "CAR-26-07 (SHORE) target date is tomorrow, evidence outstanding.", "4 h ago"],
  ["Information", "Upcoming loading", "18 shipments scheduled for loading in the next 12 hours.", "5 h ago"],
  ["Information", "Insurance expiry", "WEALTHY cargo insurance expires 30 Sep 2026.", "Yesterday"],
];
