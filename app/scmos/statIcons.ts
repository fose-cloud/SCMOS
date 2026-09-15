/**
 * The glyph vocabulary of the figure cards — a leaf with no React in it, so
 * the label-to-glyph rule can be checked without a renderer. StatCard draws
 * these; see it for why every screen's card is one component.
 */
export type StatIcon =
  | "box" | "truck" | "clock" | "warning" | "flag" | "calendar" | "cancel" | "layers"
  | "check" | "users" | "document" | "chart" | "money" | "ship" | "search" | "shield"
  // The toolbar's and the tabs' — one stroke weight with the figures' glyphs.
  | "plus" | "upload" | "download" | "gear" | "copy" | "expand" | "columns" | "sort"
  | "sheet" | "calculator" | "terms" | "refresh" | "database" | "keyboard" | "home" | "fuel";

export const STAT_PATHS: Record<StatIcon, string> = {
  box: "M3 7l9-4 9 4v10l-9 4-9-4V7zm9 4l9-4M12 11v10M12 11L3 7",
  truck: "M1 6h12v9H1zM13 9h4l4 4v2h-8zM5.5 18a1.5 1.5 0 100-3 1.5 1.5 0 000 3zm11 0a1.5 1.5 0 100-3 1.5 1.5 0 000 3z",
  clock: "M12 21a9 9 0 100-18 9 9 0 000 18zM12 7v5l3 2",
  warning: "M12 3l10 18H2L12 3zm0 7v4m0 3v.5",
  flag: "M5 21V4m0 0h12l-2 4 2 4H5",
  calendar: "M4 6h16v15H4zM4 10h16M8 3v4m8-4v4",
  cancel: "M12 21a9 9 0 100-18 9 9 0 000 18zM9 9l6 6m0-6l-6 6",
  layers: "M12 3l9 5-9 5-9-5 9-5zm-9 9l9 5 9-5m-18 4l9 5 9-5",
  check: "M12 21a9 9 0 100-18 9 9 0 000 18zM8 12l3 3 5-6",
  users: "M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7zm-6 9a6 6 0 0112 0zm12-9a3 3 0 100-6m0 15a5 5 0 014-5",
  document: "M6 3h8l4 4v14H6zM14 3v4h4M9 12h6m-6 4h6",
  chart: "M4 20V10m6 10V4m6 16v-7m4 7H2",
  money: "M3 6h18v12H3zM12 15a3 3 0 100-6 3 3 0 000 6zM6 9h.01M18 15h.01",
  ship: "M3 15l2-6h14l2 6M3 15c2 2 4 2 6 0 2 2 4 2 6 0 2 2 4 2 6 0M8 9V5h8v4M12 3v2",
  search: "M11 18a7 7 0 100-14 7 7 0 000 14zm5-2l5 5",
  shield: "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3zm-3 9l2 2 4-4",
  plus: "M12 5v14M5 12h14",
  upload: "M12 16V4m-5 5l5-5 5 5M4 20h16",
  download: "M12 4v12m-5-5l5 5 5-5M4 20h16",
  gear: "M12 15a3 3 0 100-6 3 3 0 000 6zm7.4-3l1.6-1-1.6-2.8-1.9.6a7 7 0 00-1.7-1L15.5 5h-3.2l-.3 2a7 7 0 00-1.7 1l-1.9-.6L6.8 10.2 8.4 11.3a7 7 0 000 1.4l-1.6 1.1 1.6 2.8 1.9-.6a7 7 0 001.7 1l.3 2h3.2l.3-2a7 7 0 001.7-1l1.9.6 1.6-2.8-1.6-1.1a7 7 0 000-1.4z",
  copy: "M9 9h11v11H9zM5 15H4V4h11v1",
  expand: "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5",
  columns: "M4 4h16v16H4zM10 4v16M16 4v16",
  sort: "M4 7h16M7 12h10M10 17h4",
  sheet: "M5 3h10l4 4v14H5zM15 3v4h4M8 12h8M8 16h8M8 8h3",
  calculator: "M6 3h12v18H6zM9 7h6M9 12h.01M12 12h.01M15 12h.01M9 16h.01M12 16h.01M15 16h.01",
  terms: "M6 3h12v18H6zM9 8h6M9 12h6M9 16h3",
  refresh: "M20 12a8 8 0 01-14.5 4.6M4 12a8 8 0 0114.5-4.6M4 5v4h4M20 19v-4h-4",
  database: "M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3zm-8 3v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3",
  keyboard: "M3 7h18v10H3zM6 10h.01M9 10h.01M12 10h.01M15 10h.01M18 10h.01M7 14h10",
  fuel: "M4 21V5a2 2 0 012-2h7a2 2 0 012 2v16M3 21h12M7 8h5M15 9h2a2 2 0 012 2v6a1.5 1.5 0 003 0v-7l-3-3",
  home: "M3 11l9-8 9 8v10h-6v-6H9v6H3z",
};

/**
 * The glyph a toolbar button earns from its label. Nothing for a label that
 * names no known action — a button reads fine as words, and a wrong glyph
 * is worse than none.
 */
const TOOL_GLYPH: [RegExp, StatIcon][] = [
  [/^\+|แทรกแถว|เพิ่ม|add job|add supplier|\badd\b|สร้าง/i, "plus"],
  [/import|นำเข้า|อัปโหลด|upload/i, "upload"],
  [/export|ส่งออก|download|ดาวน์โหลด/i, "download"],
  [/คอลัมน์|column/i, "columns"],
  [/คัดลอก|copy/i, "copy"],
  [/เต็มจอ|full ?screen/i, "expand"],
  [/sort|เรียง/i, "sort"],
  [/saved view|มุมมอง/i, "layers"],
  [/รีเฟรช|refresh|โหลดใหม่|reload/i, "refresh"],
  [/ยกเลิก|ทิ้ง|cancel|discard/i, "cancel"],
  [/บันทึก|save/i, "check"],
  [/ค้นหา|search/i, "search"],
];

export function toolIconFor(label: string): StatIcon | null {
  for (const [pattern, icon] of TOOL_GLYPH) if (pattern.test(label)) return icon;
  return null;
}

/**
 * The glyph a screen's tab earns. The rate screens' three, the workspace's
 * lists, the dashboard's views; anything else falls through to the figure
 * vocabulary, and a tab that matches nothing goes without.
 */
const TAB_GLYPH: [RegExp, StatIcon][] = [
  [/คำนวณ|calculator/i, "calculator"],
  [/ตารางอัตรา|rate sheet|sheet/i, "sheet"],
  [/เงื่อนไข|terms|surcharge/i, "terms"],
  [/^my (jobs|work)|งานของฉัน/i, "users"],
  [/pending|รอ/i, "clock"],
  [/completed|เสร็จ/i, "check"],
  [/^all$|ทั้งหมด|overview|ภาพรวม/i, "layers"],
  [/import|export|นำเข้า|ส่งออก/i, "ship"],
  [/calendar|ปฏิทิน/i, "calendar"],
  [/today|วันนี้/i, "home"],
  [/delay|ล่าช้า/i, "clock"],
  // The Chemours' own: the runs, the card, the diesel, the check, the receipt.
  [/domestic/i, "truck"],
  [/oil|diesel|น้ำมัน/i, "fuel"],
  [/ตรวจสอบ|verify|check/i, "search"],
  [/receipt|ใบรับ/i, "document"],
  [/ค่าขนส่ง|ราคา/i, "money"],
];

export function tabIconFor(label: string): StatIcon | null {
  for (const [pattern, icon] of TAB_GLYPH) if (pattern.test(label)) return icon;
  return null;
}


/** Which glyph a label earns, in either language. First match wins. */
const GLYPH_FOR: [RegExp, StatIcon][] = [
  [/ยกเลิก|cancel/i, "cancel"],
  // Before the clock: "ตรงเวลา" is on time, not a delay.
  [/ตรงเวลา|on[- ]time|เสร็จ|complete|done|สำเร็จ|ผ่าน|ยืนยัน|confirm|approved|อนุมัติ/i, "check"],
  [/เลื่อน|postpone|reschedul|นัด|วันที่|date|calendar|เดือน|ปฏิทิน/i, "calendar"],
  [/ล่าช้า|delay|late|เวลา|time|ชั่วโมง|hour|overdue|เกินกำหนด/i, "clock"],
  [/ผิด|error|ปัญหา|issue|problem|เสี่ยง|risk|warn|ต้องดำเนินการ|ขาด|missing|หมดอายุ|expir|incident|accident|\bcar\b|\bpar\b/i, "warning"],
  [/ผู้ขนส่ง|supplier|subcon|carrier|รถ|truck|vendor|ผู้รับเหมา|เที่ยว|trip/i, "truck"],
  [/เอกสาร|document|file|ไฟล์|ใบ|invoice|bill|receipt|pod/i, "document"],
  [/อบรม|training|คน|people|staff|ผู้|user|driver|คนขับ|พนักงาน|ทีม|team|ลูกค้า|customer/i, "users"],
  [/บาท|thb|cost|ค่า|rate|price|ราคา|revenue|เงิน|money|ค่าใช้จ่าย/i, "money"],
  [/เรือ|vessel|ship|port|ท่า|ตู้|container|export|import|นำเข้า|ส่งออก/i, "ship"],
  [/ตรวจ|verify|check|audit|inspect|เช็ค/i, "search"],
  [/ประกัน|insurance|licen|compliance|ปลอดภัย|safety/i, "shield"],
  [/งาน|job|shipment|order|รวม|total|ทั้งหมด|all/i, "box"],
];

export function iconFor(label: string): StatIcon {
  for (const [pattern, icon] of GLYPH_FOR) if (pattern.test(label)) return icon;
  return "chart";
}

