/**
 * The monthly report as a PowerPoint deck.
 *
 * A deck rather than a PDF because the two are read differently: a PDF is
 * forwarded, a deck is stood in front of. The figures are identical — this
 * builds from the same report document the screen renders, so a slide and the
 * page it came from cannot disagree.
 *
 * <b>No images, ever.</b> pptxgenjs reaches for `image-size` when a picture is
 * added, and every published version of that package (up to 2.0.2, the latest)
 * carries a denial-of-service advisory in its ICNS, JXL and HEIF parsers with
 * no patched release to move to. Nothing here calls `addImage`: the slides are
 * text, shapes, tables and native PowerPoint charts, so the vulnerable parser is
 * never reached. `reportDeck.test.mjs` asserts that rather than trusting it,
 * because "we happen not to use images" is a fact about today and a test is a
 * fact about tomorrow.
 *
 * A native chart rather than a picture of one is also the better artefact: it
 * arrives in PowerPoint editable, with its numbers intact, so somebody can
 * restyle it for their own deck without coming back and asking for a new export.
 */

/** One line of the report, as the API computes it. */
export type DeckLine = {
  name: string;
  trips: number;
  measurable: number;
  onTime: number;
  late: number;
  /** Null when nothing could be measured, or the sample was too small. */
  otd: number | null;
  coverage: number;
};

export type DeckReport = {
  customer: string;
  month: string;
  summary: DeckLine;
  target: number;
  vendors: DeckLine[];
  delayReasons: { label: string; value: number }[];
  trend: { month: string; otd: number | null; measurable: number }[];
  meetsTarget: boolean | null;
  confidence: string;
  minimumSample: number;
  generatedBy: string;
  generatedAt: string;
};

/*
 * A4 landscape, because this pack is presented and then printed.
 *
 * 16:9 is the better shape on a screen and the wrong one in a ring binder, and
 * these go into a customer review folder.
 */
const PAGE = { name: "A4", width: 11.69, height: 8.27 };

/** Tahoma renders Thai on every Windows machine these decks will be opened on. */
const FONT = "Tahoma";

const NAVY = "0A2240";
const INK = "31465C";
const MUTED = "7B8CA0";
const RULE = "D8E0E8";
const BAR = "2E7DD1";
const MET = "16794C";
const MISSED = "B42318";

const nf = (n: number) => n.toLocaleString("en-US");

/** A rate, or an em dash. Never "0%" for something nobody measured. */
const rate = (value: number | null) => (value === null ? "—" : `${value}%`);

/**
 * The verdict in words.
 *
 * Beside the colour and never instead of it: the green and the red are 6.7
 * apart for a deuteranope, and a slide is exactly where somebody reads a colour
 * from across a room.
 */
function verdict(otd: number | null, target: number, minimumSample: number, measurable: number) {
  if (otd === null) {
    return measurable === 0
      ? { text: "วัดไม่ได้", colour: MUTED }
      : { text: `วัดได้ ${measurable} เที่ยว · น้อยกว่า ${minimumSample}`, colour: MUTED };
  }
  return otd >= target
    ? { text: "▲ ถึงเป้า", colour: MET }
    : { text: "▼ ต่ำกว่าเป้า", colour: MISSED };
}

/**
 * Builds and downloads the deck.
 *
 * The library is imported here rather than at the top of the module so that its
 * megabyte lands only in the browser of somebody who actually pressed the
 * button — this application is forty screens and none of the other thirty-nine
 * should pay for a PowerPoint writer they never call.
 */
export async function downloadDeck(report: DeckReport, summaryText: string) {
  const { default: PptxGenJS } = await import("pptxgenjs");
  const pptx = new PptxGenJS();

  pptx.defineLayout(PAGE);
  pptx.layout = PAGE.name;
  pptx.author = "SCMOS";
  pptx.company = "LESCHACO (Thailand) Ltd.";
  pptx.title = `${report.customer} · Monthly Trucking Performance · ${report.month}`;

  cover(pptx, report, summaryText);
  vendors(pptx, report);
  if (report.delayReasons.length > 0) delays(pptx, report);

  const name = `SCMOS_${report.customer.replace(/[^\w]+/g, "_")}_${report.month.replace("/", "-")}.pptx`;
  await pptx.writeFile({ fileName: name });
  return name;
}

/* ------------------------------------------------------------------ slides */

type Deck = InstanceType<typeof import("pptxgenjs").default>;

/** The masthead every slide carries, so a page photographed alone still says whose it is. */
function masthead(slide: ReturnType<Deck["addSlide"]>, report: DeckReport, heading: string) {
  slide.addText("SCMOS", {
    x: 0.45, y: 0.32, w: 2, h: 0.3, fontFace: FONT, fontSize: 11, bold: true,
    color: MUTED, charSpacing: 2,
  });
  slide.addText(heading, {
    x: 0.45, y: 0.6, w: 8, h: 0.45, fontFace: FONT, fontSize: 22, bold: true, color: NAVY,
  });
  slide.addText(`${report.customer}  |  ${report.month}`, {
    x: 0.45, y: 1.05, w: 8, h: 0.3, fontFace: FONT, fontSize: 13, color: INK,
  });
  slide.addShape("line", {
    x: 0.45, y: 1.45, w: PAGE.width - 0.9, h: 0, line: { color: NAVY, width: 1.5 },
  });
}

function cover(pptx: Deck, report: DeckReport, summaryText: string) {
  const slide = pptx.addSlide();
  masthead(slide, report, "Monthly Trucking Performance");

  const s = report.summary;
  const decided = verdict(s.otd, report.target, report.minimumSample, s.measurable);

  // Four figures, each with what it was measured over. A tile without its base
  // is the number somebody quotes in a meeting without knowing what it rests on.
  const tiles: [string, string, string, string][] = [
    [nf(s.trips), "TRIPS", "เที่ยวทั้งหมด", NAVY],
    [rate(s.otd), "ON-TIME DELIVERY", `วัดได้ ${nf(s.measurable)} จาก ${nf(s.trips)}`, decided.colour],
    [nf(s.late), "DELAYED", "ถึงช้ากว่าแผน", NAVY],
    [`${report.target}%`, "TARGET", "เป้าหมายที่ตกลงไว้", MUTED],
  ];

  tiles.forEach(([value, label, note, colour], at) => {
    const x = 0.45 + at * 2.75;
    slide.addShape("rect", {
      x, y: 1.7, w: 2.5, h: 1.15, fill: { color: "F4F7FA" }, line: { color: RULE, width: 0.5 },
    });
    slide.addText(value, {
      x: x + 0.15, y: 1.82, w: 2.2, h: 0.45, fontFace: FONT, fontSize: 26, bold: true, color: colour,
    });
    slide.addText(label, {
      x: x + 0.15, y: 2.28, w: 2.2, h: 0.22, fontFace: FONT, fontSize: 8.5, bold: true,
      color: MUTED, charSpacing: 1,
    });
    slide.addText(note, {
      x: x + 0.15, y: 2.48, w: 2.2, h: 0.25, fontFace: FONT, fontSize: 8.5, color: MUTED,
    });
  });

  slide.addText(decided.text, {
    x: 0.45, y: 2.95, w: 5, h: 0.25, fontFace: FONT, fontSize: 11, bold: true, color: decided.colour,
  });
  // How far the whole page can be trusted, on the page rather than left for the
  // reader to work out from a base they were not shown.
  slide.addText(`ความครบถ้วนของข้อมูล · ${report.confidence}`, {
    x: 3.2, y: 2.95, w: 8, h: 0.25, fontFace: FONT, fontSize: 10, color: MUTED,
  });

  chart(slide, report, { x: 0.45, y: 3.4, w: 5.3, h: 3.1 });

  slide.addText("MANAGEMENT SUMMARY", {
    x: 6.0, y: 3.4, w: 5.2, h: 0.25, fontFace: FONT, fontSize: 9.5, bold: true,
    color: MUTED, charSpacing: 1,
  });
  slide.addText(summaryText.trim() || "— ยังไม่ได้เขียนบทสรุป —", {
    x: 6.0, y: 3.7, w: 5.24, h: 2.8, fontFace: FONT, fontSize: 11, color: INK,
    valign: "top", lineSpacingMultiple: 1.25,
  });

  footer(slide, report);
}

/**
 * On-time by carrier, as a real PowerPoint chart.
 *
 * Only the carriers a rate can honestly be quoted for. A bar chart including
 * everybody who ran once at 100% ranks noise, and on a slide it ranks it in
 * front of a customer.
 */
function chart(slide: ReturnType<Deck["addSlide"]>, report: DeckReport,
  at: { x: number; y: number; w: number; h: number }) {
  const shown = report.vendors.filter((one) => one.otd !== null);

  slide.addText("ON-TIME BY CARRIER", {
    x: at.x, y: at.y, w: at.w, h: 0.25, fontFace: FONT, fontSize: 9.5, bold: true,
    color: MUTED, charSpacing: 1,
  });

  if (shown.length === 0) {
    slide.addText("ยังไม่มีผู้ขนส่งรายใดมีเที่ยวที่วัดผลได้มากพอจะคิดเป็นเปอร์เซ็นต์", {
      x: at.x, y: at.y + 0.35, w: at.w, h: 0.4, fontFace: FONT, fontSize: 10, color: MUTED,
    });
    return;
  }

  slide.addChart("bar", [{
    name: "ตรงเวลา %",
    labels: shown.map((one) => one.name),
    values: shown.map((one) => one.otd as number),
  }], {
    x: at.x, y: at.y + 0.3, w: at.w, h: at.h - 0.55,
    barDir: "bar",
    chartColors: [BAR],
    catAxisLabelFontFace: FONT, catAxisLabelFontSize: 9,
    valAxisLabelFontFace: FONT, valAxisLabelFontSize: 9,
    valAxisMaxVal: 100, valAxisMinVal: 0,
    showLegend: false,
    showValue: true,
    dataLabelFontFace: FONT, dataLabelFontSize: 8, dataLabelPosition: "outEnd",
  });

  // The target belongs on the chart, and a native chart cannot carry a
  // reference line — so it is said in words directly beneath it.
  slide.addText(`เป้าหมาย ${report.target}%`
    + (shown.length < report.vendors.length
      ? ` · แสดงเฉพาะรายที่วัดผลได้ตั้งแต่ ${report.minimumSample} เที่ยว (อีก ${report.vendors.length - shown.length} รายไม่ถึง)`
      : ""), {
    x: at.x, y: at.y + at.h - 0.22, w: at.w, h: 0.22,
    fontFace: FONT, fontSize: 8, color: MUTED,
  });
}

function vendors(pptx: Deck, report: DeckReport) {
  const slide = pptx.addSlide();
  masthead(slide, report, "Vendor Performance");

  const head = ["Vendor", "Trips", "วัดได้", "ตรงเวลา", "ล่าช้า", "OTD", "เทียบเป้า"];
  const rows = [
    head.map((text) => ({
      text,
      options: { bold: true, color: MUTED, fontSize: 9, fontFace: FONT, fill: { color: "F4F7FA" } },
    })),
    ...report.vendors.map((one) => {
      const decided = verdict(one.otd, report.target, report.minimumSample, one.measurable);
      return [
        { text: one.name, options: { bold: true, color: NAVY, fontSize: 9.5, fontFace: FONT } },
        { text: nf(one.trips), options: { align: "right" as const, fontSize: 9.5, fontFace: FONT } },
        { text: nf(one.measurable), options: { align: "right" as const, color: MUTED, fontSize: 9.5, fontFace: FONT } },
        { text: nf(one.onTime), options: { align: "right" as const, fontSize: 9.5, fontFace: FONT } },
        { text: nf(one.late), options: { align: "right" as const, fontSize: 9.5, fontFace: FONT } },
        { text: rate(one.otd), options: { align: "right" as const, bold: true, fontSize: 9.5, fontFace: FONT } },
        { text: decided.text, options: { color: decided.colour, fontSize: 8.5, fontFace: FONT } },
      ];
    }),
  ];

  slide.addTable(rows, {
    x: 0.45, y: 1.7, w: PAGE.width - 0.9,
    colW: [2.6, 1.0, 1.0, 1.1, 1.0, 1.0, 3.09],
    border: { type: "solid", pt: 0.5, color: "EDF1F5" },
    autoPage: true, autoPageRepeatHeader: true, autoPageLineWeight: -0.4,
  });

  footer(slide, report);
}

function delays(pptx: Deck, report: DeckReport) {
  const slide = pptx.addSlide();
  masthead(slide, report, "Delay Reasons");

  slide.addTable([
    [
      { text: "สาเหตุ", options: { bold: true, color: MUTED, fontSize: 9, fontFace: FONT, fill: { color: "F4F7FA" } } },
      { text: "จำนวนเที่ยว", options: { bold: true, color: MUTED, fontSize: 9, fontFace: FONT, fill: { color: "F4F7FA" }, align: "right" as const } },
    ],
    ...report.delayReasons.map((one) => [
      { text: one.label, options: { fontSize: 10, fontFace: FONT, color: INK } },
      { text: nf(one.value), options: { fontSize: 10, fontFace: FONT, align: "right" as const, bold: true } },
    ]),
  ], {
    x: 0.45, y: 1.7, w: 7.5, colW: [5.5, 2.0],
    border: { type: "solid", pt: 0.5, color: "EDF1F5" },
    autoPage: true, autoPageRepeatHeader: true,
  });

  footer(slide, report);
}

/**
 * What every slide says at the bottom.
 *
 * The methodology travels with the deck because a slide gets separated from its
 * covering email, and "88% on time" means nothing without "of the trips that
 * recorded an arrival".
 */
function footer(slide: ReturnType<Deck["addSlide"]>, report: DeckReport) {
  slide.addText(
    "ทุกอัตราคิดจากเที่ยวที่บันทึกเวลาถึงไว้เท่านั้น และแสดงจำนวนฐานกำกับ · "
    + `เที่ยวที่ไม่มีเวลาถึงไม่ถูกนับเป็นทั้งตรงเวลาและล่าช้า · ออกโดย ${report.generatedBy} · ${report.generatedAt}`,
    {
      x: 0.45, y: PAGE.height - 0.55, w: PAGE.width - 0.9, h: 0.3,
      fontFace: FONT, fontSize: 7.5, color: MUTED,
    });
}
