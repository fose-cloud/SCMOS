/**
 * Capacity Planning's fleet tables (30 Sep 2026): a carrier's heads, tails and drivers, and the papers each
 * carries. The department's screen reads every carrier's (`/api/fleet`); a carrier's reads and writes its own
 * (`/api/carrier/fleet`). The required papers come from the API — Rules/FleetDocuments.cs names them — so
 * the forms below ask for what the server will insist on.
 */

export type FleetFile = { id: number; fileName: string; expiryDate: string; canShow: boolean; uploadedBy: string; uploadedAt: string };
/** state: valid · expiring · expired · missing · no-expiry — SupplierCompliance's states. */
export type FleetPaper = {
  code: string; english: string; thai: string; expires: boolean; state: string; daysLeft: number | null;
  file: FleetFile | null; versions: number;
};
export type FleetTruck = {
  id: number; supplierId: number; supplier: string; plate: string; kind: string; vehicleType: string;
  dgCapable: boolean; status: string; state: string; papers: FleetPaper[]; createdBy: string; createdAt: string | null;
};
export type FleetDriver = {
  id: number; supplierId: number; supplier: string; name: string; phone: string; licenceNo: string;
  licenceExpiry: string; status: string; state: string; papers: FleetPaper[]; createdBy: string; createdAt: string | null;
};
export type FleetRequirement = { code: string; english: string; thai: string; expires: boolean };
export type Fleet = {
  trucks: FleetTruck[]; drivers: FleetDriver[]; truckPapers: FleetRequirement[]; driverPapers: FleetRequirement[];
  vehicleTypes: string[]; storageReady: boolean;
};

export const KIND_TH: Record<string, string> = { head: "หัว", tail: "หาง" };

/** How a paper's state reads on screen: the words and their colour. */
export function paperTone(state: string, daysLeft: number | null): { text: string; colour: string; background: string } {
  switch (state) {
    case "valid": return { text: daysLeft === null ? "มีแล้ว" : `เหลือ ${daysLeft} วัน`, colour: "#16794C", background: "#EDF7F1" };
    case "expiring": return { text: `ใกล้หมดอายุ · ${daysLeft ?? 0} วัน`, colour: "#B45309", background: "#FFF8F0" };
    case "expired": return { text: `หมดอายุแล้ว${daysLeft !== null ? ` · เกิน ${Math.abs(daysLeft)} วัน` : ""}`, colour: "#B42318", background: "#FEF0EE" };
    case "no-expiry": return { text: "ไม่ได้ระบุวันหมดอายุ", colour: "#8A6D0B", background: "#FFFBEB" };
    default: return { text: "ยังไม่มี", colour: "#B42318", background: "#FEF0EE" };
  }
}

/** A row's overall word — the worst of its papers. */
export function rowState(state: string): { text: string; colour: string } {
  switch (state) {
    case "valid": return { text: "เอกสารครบ", colour: "#16794C" };
    case "no-expiry": return { text: "ขาดวันหมดอายุ", colour: "#8A6D0B" };
    case "expiring": return { text: "ใกล้หมดอายุ", colour: "#B45309" };
    case "expired": return { text: "เอกสารหมดอายุ", colour: "#B42318" };
    default: return { text: "เอกสารไม่ครบ", colour: "#B42318" };
  }
}

export type TruckForm = { plate: string; kind: string; vehicleType: string; dgCapable: boolean };
export type DriverForm = { name: string; phone: string; licenceNo: string };
/** The chosen file and, for a dated paper, its expiry as the date input gives it (yyyy-mm-dd). */
export type PaperPick = { file: File | null; expiry: string };

/** Why the form cannot be sent yet, or "" when it can — the same checks the API makes first. */
export function truckFormProblem(form: TruckForm, papers: Record<string, PaperPick>, required: FleetRequirement[]): string {
  if (form.plate.replace(/[^\p{L}\p{N}]/gu, "").length < 2) return "ระบุทะเบียนรถ";
  if (form.kind !== "head" && form.kind !== "tail") return "เลือกหัวหรือหาง";
  if (!form.vehicleType) return "เลือกประเภทรถ";
  const missing = required.filter((need) => !papers[need.code]?.file).map((need) => need.thai);
  return missing.length ? "แนบ " + missing.join(", ") : "";
}

export function driverFormProblem(form: DriverForm, papers: Record<string, PaperPick>, required: FleetRequirement[]): string {
  if (!form.name.trim()) return "ระบุชื่อพนักงานขับรถ";
  if (form.licenceNo.replace(/[^\p{L}\p{N}]/gu, "").length < 4) return "ระบุเลขที่ใบขับขี่";
  const missing = required.filter((need) => !papers[need.code]?.file).map((need) => need.thai);
  return missing.length ? "แนบ " + missing.join(", ") : "";
}

/** Rows a search and a kind filter leave, inactive ones only when asked for. */
export function filterTrucks(trucks: FleetTruck[], search: string, kind: string, showInactive: boolean): FleetTruck[] {
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return trucks.filter((truck) => (showInactive || truck.status === "active")
    && (!kind || truck.kind === kind)
    && words.every((word) => [truck.plate, truck.supplier, truck.vehicleType].some((field) => field.toLowerCase().includes(word))));
}

export function filterDrivers(drivers: FleetDriver[], search: string, showInactive: boolean): FleetDriver[] {
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return drivers.filter((driver) => (showInactive || driver.status === "active")
    && words.every((word) => [driver.name, driver.supplier, driver.licenceNo, driver.phone].some((field) => field.toLowerCase().includes(word))));
}
