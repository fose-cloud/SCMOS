import type { Account } from "./nav";
import { STATUS_RE, type Job } from "./ops";
import { isKpiReady } from "./ops";
import { tmin } from "./util";

/**
 * The alert centre reads the operation plan, not a fixed list: every line is a
 * count of real jobs with a way into the workspace that shows exactly those
 * jobs. An alert with nothing behind it is not rendered at all.
 */

/** Where an alert (or a dashboard figure) sends the workspace. */
export type WsTarget = {
  tab?: string; cat?: string; kpi?: string; status?: string; date?: string;
  /** Set by an API alert: the screen that answers it, and the job it is about. */
  screen?: string; jobKey?: string;
};

export type AlertLevel = "Critical" | "Warning" | "Information";

export type Alert = {
  id: string;
  level: AlertLevel;
  title: string;
  th: string;
  body: string;
  count: number;
  target: WsTarget;
};

const LEVEL_ORDER: Record<AlertLevel, number> = { Critical: 0, Warning: 1, Information: 2 };

/** Export jobs whose closing time falls before the truck is due to arrive. */
export function gateInRisk(j: Job): boolean {
  if (j.cat !== "EXPORT") return false;
  const closing = tmin(j.closingTime);
  const arrival = tmin(j.arrTime || j.planTime);
  return closing !== null && arrival !== null && closing - arrival < 0;
}

export function buildAlerts(jobs: Job[], me: Account): Alert[] {
  const open = jobs.filter((j) => !STATUS_RE.done.test(j.status));
  // On the owner id, like every other ownership test — see `owns` in SCMOSApp.
  const mine = me.opId ? jobs.filter((j) => j.opId === me.opId) : [];

  const dayCount: Record<string, number> = {};
  jobs.forEach((j) => { if (j.date) dayCount[j.date] = (dayCount[j.date] || 0) + 1; });
  const busiest = Object.keys(dayCount).sort((a, b) => dayCount[b] - dayCount[a])[0] ?? "";

  const missingTruck = open.filter((j) => j.cat !== "DELIVERY" && (!j.licence || !j.driver || !j.contact));
  const noArrival = open.filter((j) => j.cat !== "DELIVERY" && !j.arrTime);

  const candidates: Alert[] = [
    {
      id: "delayed",
      level: "Critical",
      title: "Delayed jobs need an update",
      th: "งานล่าช้าที่ต้องติดตาม",
      body: "งานที่สถานะเป็น Delayed อยู่ตอนนี้ · เปิดเพื่อดูสาเหตุและผู้รับผิดชอบ",
      count: jobs.filter((j) => STATUS_RE.delayed.test(j.status)).length,
      target: { tab: "DELAY", kpi: "Delay" },
    },
    {
      id: "format",
      level: "Critical",
      title: "Records break the data standard",
      th: "ข้อมูลผิดรูปแบบ ทำให้หลุดจาก KPI",
      body: "ค่าที่ระบบอ่านไม่ได้ เช่น วันที่ เวลา เลขตู้ หรือสถานะนอกลำดับ — งานเหล่านี้ไม่ถูกนับใน KPI จนกว่าจะแก้",
      count: jobs.filter((j) => !isKpiReady(j)).length,
      target: { tab: "PENDING", kpi: "Fmt" },
    },
    {
      id: "gatein",
      level: "Critical",
      title: "Export closing time already passed",
      th: "งานส่งออกเสี่ยงตกเรือ",
      body: "เวลา Closing มาก่อนเวลาที่รถจะถึง — ต้องเร่งหรือแจ้งเปลี่ยนรอบเรือ",
      count: jobs.filter(gateInRisk).length,
      target: { tab: "PENDING", cat: "EXPORT" },
    },
    {
      id: "waiting",
      level: "Warning",
      title: "Jobs still waiting for a truck",
      th: "งานที่ยังไม่มีรถ",
      body: "ยังไม่ได้จัดรถให้งานเหล่านี้ · จัดก่อนถึงวันโหลด",
      count: jobs.filter((j) => STATUS_RE.waiting.test(j.status)).length,
      target: { tab: "PENDING", kpi: "Wait" },
    },
    {
      id: "truckdata",
      level: "Warning",
      title: "Truck details missing",
      th: "ยังไม่มีทะเบียนรถ / คนขับ / เบอร์ติดต่อ",
      body: "งานที่ยังไม่ปิดและยังกรอกข้อมูลรถไม่ครบ — ติดตามจากผู้ขนส่งก่อนวันโหลด",
      count: missingTruck.length,
      target: { tab: "PENDING", kpi: "Act" },
    },
    {
      id: "arrival",
      level: "Warning",
      title: "Arrival time not recorded",
      th: "ยังไม่ลงเวลาถึง",
      body: "ไม่มีเวลาถึง ทำให้คำนวณอัตราตรงเวลาไม่ได้",
      count: noArrival.length,
      target: { tab: "PENDING", kpi: "Act" },
    },
    {
      id: "mine",
      level: "Warning",
      title: "Your jobs need action",
      th: "งานของคุณที่ต้องดำเนินการ",
      body: "งานที่ " + me.name + " รับผิดชอบและยังมีข้อมูลค้างหรือสถานะที่ต้องจัดการ",
      count: mine.filter((j) => j.action).length,
      target: { tab: "MY JOBS", kpi: "Act" },
    },
    {
      id: "busiest",
      level: "Information",
      title: "Busiest day in the plan",
      th: "วันที่งานหนาแน่นที่สุด: " + busiest,
      body: "เตรียมกำลังรถและคนสำหรับวันนี้เป็นพิเศษ",
      count: dayCount[busiest] ?? 0,
      target: { tab: "PENDING", date: busiest },
    },
    {
      id: "done",
      level: "Information",
      title: "Jobs completed",
      th: "งานที่ปิดแล้ว",
      body: "งานที่ส่งมอบและปิดเรียบร้อย พร้อมส่งต่อให้ฝ่ายวางบิล",
      count: jobs.filter((j) => STATUS_RE.done.test(j.status)).length,
      target: { tab: "COMPLETED", kpi: "Done" },
    },
  ];

  return candidates
    .filter((a) => a.count > 0)
    .sort((a, b) => LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level] || b.count - a.count);
}
