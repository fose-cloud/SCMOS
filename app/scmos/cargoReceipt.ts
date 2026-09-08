/**
 * Filling the cargo receipt from the Domestic job it is for.
 *
 * The receipt is a document that gets signed at a customer's gate, and until
 * now every field on it was typed again from a screen two tabs away. The
 * register already holds all of it — the consignee, the delivery note, the
 * pallets, the kilos — so this is a copy, not a calculation.
 *
 * A copy with one thing deliberately left out. The times the truck arrived and
 * left are the whole point of a receipt: they are written at the gate, by
 * whoever is standing there, and a form that arrived with them already filled
 * in would be a record of what was planned being signed as what happened.
 *
 * No imports on purpose — the mapping is the part worth testing and it should
 * be testable without a browser, an Excel reader or a register behind it.
 */

/** As much of a Domestic job as the receipt reads. Everything is optional
 *  because a job keyed in this morning has most of it still blank. */
export type ReceiptJob = {
  key?: string;
  cat?: string;
  date?: string;
  /** The consignee — what the summary sheet heads "Customer List". Not the
   *  account. The Chemours is who we bill; this is who signs. */
  customer?: string;
  destination?: string;
  province?: string;
  zip?: string;
  wh?: string;
  /** The haulier running the trip — SSL, THAI KOT. Their driver signs the receipt. */
  trucker?: string;
  licence?: string;
  /** The customer's own paperwork: the SAP order and the delivery note. */
  sapOrder?: string;
  deliverNo?: string;
  /**
   * The two the customer's own sheet has swapped captions for.
   *
   * ops.ts records it: on their summary the D-codes sit under "JOB NO." and the
   * LSTH job numbers under "SID NUMBER", so the import puts the pair right by
   * shape rather than by caption. The Domestic grid shows them under the
   * customer's captions, which is what the operators read — so "the SID NUMBER
   * column" means jobCode and "the JOB NO. column" means dCode, and this
   * receipt is filled from the columns as they see them.
   */
  jobCode?: string;
  dCode?: string;
  sid?: string;
  /** What is being carried, as the Domestic grid's PRODUCT NAME column holds it. */
  product?: string;
  pallet?: string;
  kgs?: string;
  weight?: string;
  remark?: string;
  /** How many of each truck went, which is what the form's vehicle line says. */
  v4?: string;
  v6?: string;
  v10?: string;
  vtr?: string;
  vtl?: string;
  jobNo?: string;
};

export type ReceiptHead = {
  jobNo: string;
  createDate: string;
  invoiceNo: string;
  vessel: string;
  eta: string;
  portOfDischarge: string;
  deliveryDate: string;
  blNo: string;
  truckNo: string;
  packages: string;
  grossWeight: string;
  remark: string;
  customer: string;
  receiverName: string;
  receiverAddress: string;
  agent: string;
  containerNo: string;
  truckIn: string;
  truckOut: string;
  vehicle: string;
  plate: string;
  crew: "" | "with" | "without";
};

const text = (value: unknown): string => String(value ?? "").replace(/\s+/g, " ").trim();

/**
 * How a job is named in the picker.
 *
 * Date, haulier, consignee and destination, because that is how somebody
 * looking for "SSL's Ampacet run this morning" holds it in their head — and on
 * a day when two hauliers run to the same consignee, the company is the thing
 * that tells the rows apart. The delivery note comes last and only when there
 * is one: it is the exact answer when two runs still look alike, and noise on
 * the many days when they do not.
 */
export function receiptLabel(job: ReceiptJob): string {
  const parts = [text(job.date)];
  const carrier = text(job.trucker);
  if (carrier) parts.push(carrier);
  parts.push(text(job.customer) || "ไม่ระบุผู้รับ");
  const where = text(job.destination) || text(job.province) || text(job.zip);
  if (where) parts.push(where);
  const note = text(job.deliverNo) || text(job.sid);
  if (note) parts.push(note);
  return parts.join(" · ");
}

/**
 * Which truck came: the haulier and the plate.
 *
 * Both, into the form's one TRUCK NO. field, rather than a new row. This
 * document is reproduced cell for cell from the account's own file and the
 * second of its three signature lines is ลายมือชื่อผู้รับบรรทุก — the carrier's.
 * So the company belongs on the receipt; adding a caption the customer has
 * never seen to put it there does not.
 *
 * Whichever of the two the job has. A Domestic job often carries the haulier
 * and no plate, because the plate is known at the gate and the company was
 * booked days before.
 */
export function receiptTruck(job: ReceiptJob): string {
  return [text(job.trucker), text(job.licence)].filter(Boolean).join(" · ");
}

/**
 * Where the goods are going, in the words the receipt wants.
 *
 * The destination if the job has one; otherwise the province and postcode,
 * which most Domestic rows do carry — the summary sheet is keyed on ZIP CODE
 * and often names nothing else. Never left blank when anything is known: a
 * receipt whose "Delivery to" is empty is one somebody has to ask about.
 */
export function receiptDestination(job: ReceiptJob): string {
  const named = text(job.destination);
  const where = [text(job.province), text(job.zip)].filter(Boolean).join(" ");
  if (named && where && !named.includes(text(job.zip))) return `${named} ${where}`.trim();
  return named || where;
}

/**
 * The heading block, filled from the job.
 *
 * Vessel, ETA, B/L, container and port of discharge stay empty and are not
 * guessed at. This is a lorry leaving a warehouse in Bangna; the form carries
 * those lines because the same document covers import work, and all seven of
 * the real copies leave every one of them blank.
 *
 * So do the truck's times. They are written at the gate by whoever is standing
 * there, and a form that arrived with them filled in would be a record of what
 * was planned being signed as what happened.
 */
export function receiptHead(job: ReceiptJob): ReceiptHead {
  return {
    // The delivery note is what the consignee's gate checks against, so it
    // goes where the form asks for an invoice number; the SAP order underneath
    // is what the customer's own system calls the same movement.
    // The grid's "SID NUMBER" column, which is where the LSTH job number
    // actually lives — see the note on jobCode above.
    jobNo: text(job.jobCode) || text(job.jobNo),
    createDate: "",
    invoiceNo: text(job.deliverNo),
    vessel: "",
    eta: "",
    portOfDischarge: "",
    deliveryDate: text(job.date),
    blNo: text(job.sapOrder),
    truckNo: receiptTruck(job),
    packages: text(job.pallet),
    grossWeight: text(job.kgs) || text(job.weight),
    remark: text(job.remark),
    customer: text(job.customer),
    // Who receives it and where. The register knows the destination; the
    // consignee's own name and address are not in it, and inventing them onto
    // a signed document is not something a screen should do.
    receiverName: "",
    receiverAddress: receiptDestination(job),
    agent: "",
    containerNo: "",
    truckIn: "",
    truckOut: "",
    vehicle: vehicleLine(job),
    plate: text(job.licence),
    crew: "",
  };
}

/**
 * One line of the item table, as far as the register can fill it.
 *
 * Which columns exist is a fact about the customer, so this answers per
 * heading rather than returning a fixed row. The register holds the delivery
 * note, the pallets and the kilos; the product name, the PO number and the UN
 * class live in the customer's own paperwork and are typed in.
 */
export function receiptItem(job: ReceiptJob, column: string, columns: readonly string[] = []): string {
  const head = column.replace(/\s+/g, " ").trim().toUpperCase();
  const heads = columns.map((one) => one.replace(/\s+/g, " ").trim().toUpperCase());
  const hasOwnDCode = heads.some((one) => /^D-?CODE$/.test(one));
  // PO NO is filled from the grid's "JOB NO." column, which the caption swap
  // means is the D-code. Asked for by the department: it is the reference their
  // consignee's gate checks the delivery against.
  //
  // A preset that carries a D-code column of its own gets the value there and
  // not here, or MERIT's receipt would print the same number twice under two
  // headings.
  if (/^PO\s*NO/.test(head)) return hasOwnDCode ? "" : text(job.dCode);
  if (/^D-?CODE$/.test(head)) return text(job.dCode);
  if (/^DELIVERY\s*NO/.test(head)) return text(job.deliverNo);
  if (/^PRODUCT/.test(head)) return text(job.product);
  if (/P.?KG|PACKAGE/.test(head)) return text(job.pallet);
  if (/^QTY|NET\s*WEIGHT|WEIGHT/.test(head)) return text(job.kgs) || text(job.weight);
  return "";
}

/**
 * What the vehicle line says: how many of each truck, and their plates.
 *
 * Lives here rather than with the form's fixed text: these are the job's own
 * 4W/6W/10W/TAIL LIFT counts, so it is job knowledge, not document knowledge.
 *
 * The copies read "1X6WH", "2X10WH", "1X6WH , 1X10WH" and "1X6WH Tail Lift",
 * which is exactly the 4W/6W/10W/TAIL LIFT counts the Domestic grid already
 * carries. Composed rather than typed again.
 *
 * Tail lift is a property of the truck sent, not a truck of its own, so it is
 * appended to the line rather than counted beside the others — which is how
 * all seven copies write it.
 */
export function vehicleLine(counts: ReceiptJob): string {
  const n = (value: string | undefined) => {
    const number = Number(String(value ?? "").replace(/[,\s]/g, ""));
    return Number.isFinite(number) && number > 0 ? Math.round(number) : 0;
  };
  const parts: string[] = [];
  for (const [count, label] of [
    [n(counts.v4), "4WH"], [n(counts.v6), "6WH"], [n(counts.v10), "10WH"], [n(counts.vtr), "TRAILER"],
  ] as [number, string][]) {
    if (count > 0) parts.push(`${count}X${label}`);
  }
  const lift = n(counts.vtl);
  // A tail lift on its own is a lorry: the register's own TAIL LIFT column is
  // used instead of a wheel size, never beside one — thirty-seven rows of the
  // August sheet have no job carrying both. It is priced as a 6-wheel, so it is
  // written as one here too, which is how the receipts word it.
  if (lift > 0 && parts.length === 0) return `${lift}X6WH Tail Lift`;
  const line = parts.join(" , ");
  return line && lift > 0 ? `${line} Tail Lift` : line;
}

/**
 * The Domestic jobs worth offering, newest first.
 *
 * Only DELIVERY, because that is what this tab is for and a container job would
 * fill the form with a vessel this document has no room for. A job with neither
 * a consignee nor a destination is left out: it would show in the list as a
 * date and nothing else, and picking it would blank the form somebody had
 * already started.
 */
export function receiptChoices(jobs: readonly ReceiptJob[]): ReceiptJob[] {
  return jobs
    .filter((job) => text(job.cat).toUpperCase() === "DELIVERY")
    .filter((job) => text(job.customer) || text(job.destination))
    .slice()
    .sort((a, b) => sortKey(b).localeCompare(sortKey(a)));
}

/** dd/MM/yyyy read as yyyyMMdd, so December does not sort before February. */
function sortKey(job: ReceiptJob): string {
  const date = text(job.date);
  const parts = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(date);
  return parts ? `${parts[3]}${parts[2]}${parts[1]}` : date;
}
