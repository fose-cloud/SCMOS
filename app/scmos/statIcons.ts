/**
 * The glyph vocabulary of the figure cards — a leaf with no React in it, so
 * the label-to-glyph rule can be checked without a renderer. StatCard draws
 * these; see it for why every screen's card is one component.
 */
export type StatIcon =
  | "box" | "truck" | "clock" | "warning" | "flag" | "calendar" | "cancel" | "layers"
  | "check" | "users" | "document" | "chart" | "money" | "ship" | "search" | "shield";

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
};

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

