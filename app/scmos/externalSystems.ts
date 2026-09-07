/**
 * The systems SCMOS is meant to talk to, and has not been connected to yet.
 *
 * Four of them now, and one screen behind all four. ABS had a screen of its
 * own — a route, a live probe of an endpoint that does not exist, and an
 * honest panel saying so — and the obvious way to add CCS, LINE and Outlook
 * was to copy that file three times. Every rule written twice in this
 * repository has since disagreed with itself, so the shape is data and the
 * screen reads it.
 *
 * What each entry is for: hold the place in the menu, prove the route and the
 * permission check line up, and write down what has to be known before the
 * real thing can be built. That last part is the valuable half. These are all
 * integrations with somebody else's system, and the work that blocks them is
 * never the screen — it is an address, a credential, and a decision about
 * which key joins their records to ours.
 *
 * No imports on purpose: this is a list and a little logic over it, and it
 * should be readable without a browser.
 */

export type SystemId = "abs" | "ccs" | "line" | "outlook";

export type ExternalSystem = {
  id: SystemId;
  /** As the menu says it. */
  name: string;
  thai: string;
  /** One line: what this connection is for. Shown under the title. */
  purpose: string;
  /**
   * The SCMOS endpoint this screen probes.
   *
   * Always ours, never the other system's. The browser must not hold anybody's
   * API key, and the permission check belongs in the same place as every other
   * menu's.
   */
  endpoint: string;
  /** What has to be settled before this can be built. The useful half. */
  unknowns: string[];
  /**
   * Where the design already exists, so the next person does not start from a
   * blank page. Empty when there is no written spec yet.
   */
  spec?: string;
};

export const EXTERNAL_SYSTEMS: ExternalSystem[] = [
  {
    id: "abs",
    name: "ABS",
    thai: "ระบบ ABS",
    purpose: "ระบบงานเดิมของ LESCHACO — ต่อเพื่อดึงงานเข้ามาโดยไม่ต้องคีย์ซ้ำ",
    endpoint: "/api/abs/status",
    unknowns: [
      "ABS เก็บข้อมูลอะไร และหน้านี้ต้องแสดงอะไรเป็นอย่างแรก",
      "ที่อยู่ของ API ฝั่ง ABS และวิธียืนยันตัวตน — คีย์, OAuth หรือผ่าน network ภายใน",
      "SCMOS อ่านอย่างเดียว หรือเขียนกลับได้ด้วย",
      "ผูกกับงานในทะเบียนด้วยคีย์ไหน (job key, booking no. หรืออย่างอื่น)",
      "บทบาทไหนควรเห็นเมนูนี้",
    ],
  },
  {
    id: "ccs",
    name: "CCS",
    thai: "ระบบพิธีการศุลกากร",
    purpose: "Custom Clearance System ของ LESCHACO — ต่อ API เพื่อดึงงานมาคิด KPI การขนส่งรายลูกค้า",
    endpoint: "/api/ccs/status",
    unknowns: [
      "API ของ CCS อยู่ที่ไหน และยืนยันตัวตนอย่างไร — หน้า login ที่เห็นเป็นของคน ไม่ใช่ของเครื่อง",
      "งานใน CCS ผูกกับงานใน SCMOS ด้วยเลขอะไร — Reference No., Job No. หรือเลขตู้",
      "KPI รายลูกค้าต้องการฟิลด์ไหนจาก CCS ที่ทะเบียนงานยังไม่มี",
      "ลูกค้าที่แยกหน้าจอไว้แล้ว (L'ORÉAL, The Chemours) จะอ่านจาก CCS หรือจากทะเบียนเดิม",
      "ดึงตามรอบ หรือให้ CCS แจ้งมาเมื่อมีงานใหม่",
    ],
  },
  {
    id: "outlook",
    name: "Outlook",
    thai: "ศูนย์รวมอีเมล",
    purpose: "รับอีเมลจากกล่องจดหมายกลางผ่าน Microsoft Graph แล้วผูกเข้ากับงานในทะเบียน",
    endpoint: "/api/integrations/microsoft/status",
    unknowns: [
      "กล่องจดหมายกลางกล่องไหนบ้างที่จะให้ SCMOS อ่าน",
      "ต้องจด App registration ใน Entra ID และขอสิทธิ์ Mail.Read แบบ application",
      "ต้องจำกัดสิทธิ์ด้วย Exchange Online RBAC for Applications ให้อ่านได้เฉพาะกล่องที่อนุมัติ",
      "webhook ของ Graph ต้องเปิดออกอินเทอร์เน็ตได้ และต่ออายุ subscription อัตโนมัติ",
      "ไฟล์แนบเก็บใน Blob แบบ private — ต้องตกลงว่าเก็บนานเท่าไร",
    ],
    spec: "รับสเปคเต็มไว้แล้ว — ดูหัวข้อ Communication Center ในบันทึกการสนทนา",
  },
  {
    id: "line",
    name: "LINE",
    thai: "อัปเดตงานผ่าน LINE",
    purpose: "ให้ผู้รับเหมาแจ้งสถานะรถจากกลุ่ม LINE แล้วเข้ามาอัปเดตงานพร้อมประวัติที่ตรวจสอบได้",
    endpoint: "/api/integrations/line/status",
    unknowns: [
      "Channel secret และ access token ของ LINE Official Account",
      "กลุ่ม LINE กลุ่มไหนเป็นของผู้รับเหมารายไหน — เป็นตารางที่ต้องมีคนดูแล",
      "ข้อความที่คนขับพิมพ์จริงหน้าตาเป็นอย่างไร ก่อนจะเขียนตัวแยกคำ",
      "สถานะใน SCMOS ตัวไหนที่ LINE จะอัปเดตได้ และตัวไหนห้าม",
      "ข้อความที่อ่านไม่ชัดต้องเข้าคิวให้คนตรวจ ไม่ใช่เดาแล้วอัปเดต",
    ],
    spec: "รับสเปคเต็มไว้แล้ว — V1 ไม่ใช้ AI ใช้กฎกับคำสำคัญก่อน",
  },
];

export function systemById(id: string): ExternalSystem | undefined {
  return EXTERNAL_SYSTEMS.find((one) => one.id === id);
}

/** What the probe found. Not an error state — "absent" is the expected answer today. */
export type ProbeState = "checking" | "absent" | "denied" | "ready" | "error";

/**
 * What an HTTP status off the probe means.
 *
 * 404 is the ordinary answer and is reported as such rather than as a failure.
 * The endpoint has not been built; the screen exists so that the day it is,
 * this page says so without anybody editing it. A red panel every time somebody
 * opens the menu would train them to ignore the one that matters.
 */
export function probeStateFor(status: number): ProbeState {
  if (status === 404) return "absent";
  if (status === 401 || status === 403) return "denied";
  if (status >= 200 && status < 300) return "ready";
  return "error";
}

/** Whether a state should be drawn as a problem worth acting on. */
export function probeTone(state: ProbeState): "ok" | "warn" | "idle" {
  if (state === "ready") return "ok";
  if (state === "error" || state === "denied") return "warn";
  return "idle";
}
