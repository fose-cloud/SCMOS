import { apiFetch } from "./api";

/**
 * Who is responsible for which customer.
 *
 * The register records which operator a job belongs to. This records what that
 * ought to be — so the two can be put side by side, which is the only way the
 * question "is this job with the right person" has ever been answerable here.
 */

export type RotationRow = {
  id: number;
  customer: string;
  /** Which sheet of the rotation workbook the row came from. */
  sheet: string;
  import: boolean;
  export: boolean;
  fcl: boolean;
  lcl: boolean;
  domestic: boolean;
  /** The cell as written: email, mobile and extension run together. */
  primaryContact: string;
  primaryEmail: string;
  primaryId: string;
  primaryName: string;
  backupContact: string;
  backupEmail: string;
  backupId: string;
  backupName: string;
  backup2Contact: string;
  backup2Email: string;
  backup2Id: string;
  backup2Name: string;
  subFcl: string;
  subLcl: string;
  subFclSupplierIds: number[];
  subLclSupplierIds: number[];
  csLcb: string;
  /** Jobs this customer currently has in the register. */
  jobs: number;
  /** How many of those sit with somebody the rotation does not name. */
  elsewhere: number;
};

export type RotationOwner = {
  id: string;
  name: string;
  email: string;
  customers: number;
  asBackup: number;
};

export type RotationPersonOption = {
  id: string;
  name: string;
  email: string;
  active: boolean;
};

export type RotationSupplierOption = {
  id: number;
  code: string;
  name: string;
  serviceType: string;
  fcl: boolean;
  lcl: boolean;
};

export type RotationOptions = {
  people: RotationPersonOption[];
  suppliers: RotationSupplierOption[];
};

/** One row keyed manually. People and carriers are selected from their masters. */
export type RotationEdit = {
  customer: string;
  import: boolean;
  export: boolean;
  fcl: boolean;
  lcl: boolean;
  domestic: boolean;
  primaryId: string;
  backupId: string;
  backup2Id: string;
  subFclSupplierIds: number[];
  subLclSupplierIds: number[];
  csLcb: string;
};

/** What an import sends. No ids: the API resolves those from the directory. */
export type NewRotationRow = Omit<RotationRow,
  "id" | "primaryId" | "primaryName"
  | "backupId" | "backupName" | "backup2Id" | "backup2Name"
  | "subFclSupplierIds" | "subLclSupplierIds"
  | "jobs" | "elsewhere">;

async function read<T>(path: string): Promise<T | null> {
  try {
    const response = await apiFetch(path, { headers: { accept: "application/json" } });
    if (!response.ok) return null;
    return await response.json() as T;
  } catch {
    return null;
  }
}

export async function loadRotation(query: { ownerId?: string; customer?: string } = {}):
  Promise<RotationRow[] | null> {
  const params = new URLSearchParams();
  if (query.ownerId) params.set("ownerId", query.ownerId);
  if (query.customer) params.set("customer", query.customer);
  const body = await read<{ rows?: RotationRow[] }>(
    "/api/rotation" + (params.toString() ? "?" + params : ""));
  return body ? body.rows ?? [] : null;
}

export async function loadRotationOwners(): Promise<RotationOwner[] | null> {
  const body = await read<{ owners?: RotationOwner[] }>("/api/rotation/owners");
  return body ? body.owners ?? [] : null;
}

export async function loadRotationOptions(): Promise<RotationOptions | null> {
  return await read<RotationOptions>("/api/rotation/options");
}

export async function saveRotation(edit: RotationEdit, id?: number):
  Promise<{ ok: boolean; message: string; id?: number }> {
  try {
    const response = await apiFetch(id ? `/api/rotation/${id}` : "/api/rotation", {
      method: id ? "PUT" : "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(edit),
    });
    const body = await response.json().catch(() => ({})) as
      { id?: number; message?: string; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: body.message ?? "บันทึกแล้ว", id: body.id };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

export async function deleteRotation(id: number): Promise<{ ok: boolean; message: string }> {
  try {
    const response = await apiFetch(`/api/rotation/${id}`, { method: "DELETE" });
    const body = await response.json().catch(() => ({})) as { message?: string; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: body.message ?? "ลบแล้ว" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Replaces the whole rotation with what a workbook says.
 *
 * Replace rather than merge, because this is one document reissued whole when
 * the team reshuffles — the file even carries the date in its name. Merging two
 * editions would leave customers sitting with somebody who has not held them
 * for months and nothing on screen to say which edition a row came from.
 */
export async function replaceRotation(rows: NewRotationRow[]):
  Promise<{ ok: boolean; message: string; added: number; replaced: number }> {
  try {
    const response = await apiFetch("/api/rotation/replace", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ rows }),
    });
    const body = await response.json().catch(() => ({})) as
      { added?: number; replaced?: number; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: "", added: body.added ?? 0, replaced: body.replaced ?? 0 };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error ? error.message : String(error),
      added: 0,
      replaced: 0,
    };
  }
}
