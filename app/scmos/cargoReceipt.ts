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
  jobCode?: string;
  dCode?: string;
  sid?: string;
  pallet?: string;
  kgs?: string;
  weight?: string;
  remark?: string;
};

export type ReceiptHead = {
  customer: string;
  deliveryTo: string;
  invoiceNo: string;
  vessel: string;
  truckNo: string;
  date: string;
  blNo: string;
  eta: string;
};

export type ReceiptLine = {
  packages: string;
  weight: string;
  truckIn: string;
  truckOut: string;
  remark: string;
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
 * Vessel and ETA stay empty and are not guessed at. This is a lorry leaving a
 * warehouse in Bangna; there is no ship and no flight, and the form carries
 * those lines because the same document is used for import work.
 */
export function receiptHead(job: ReceiptJob): ReceiptHead {
  return {
    customer: text(job.customer),
    deliveryTo: receiptDestination(job),
    // The delivery note is what the consignee's gate checks against, so it goes
    // where the form asks for an invoice number. The SAP order underneath is
    // what the customer's own system calls the same movement.
    invoiceNo: text(job.deliverNo),
    vessel: "",
    truckNo: receiptTruck(job),
    date: text(job.date),
    blNo: text(job.sapOrder),
    eta: "",
  };
}

/**
 * The body, which is one line for a Domestic run.
 *
 * Pallets under "No. of P'kg(s)" and kilos under "Approx Weight", because that
 * is what those two columns of the summary sheet hold. `weight` is read as a
 * fallback for `kgs`: the general grid writes kilos there and the Domestic
 * import writes them in both, so a job keyed either way still fills in.
 *
 * Truck in and truck out are left empty — see the note at the top of the file.
 */
export function receiptLine(job: ReceiptJob): ReceiptLine {
  return {
    packages: text(job.pallet),
    weight: text(job.kgs) || text(job.weight),
    truckIn: "",
    truckOut: "",
    remark: text(job.remark),
  };
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
