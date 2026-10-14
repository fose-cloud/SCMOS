/** Basic editor bounds, shared by the controls and their exports. No external document service. */
export type Crop = { x: number; y: number; width: number; height: number };
export function cropPixels(crop: Crop, width: number, height: number) {
  if (![crop.x, crop.y, crop.width, crop.height, width, height].every(Number.isFinite)
    || width <= 0 || height <= 0 || crop.x < 0 || crop.y < 0 || crop.width <= 0 || crop.height <= 0
    || crop.x + crop.width > 100 || crop.y + crop.height > 100) throw new Error("กรอบครอปต้องอยู่ในรูป และขนาดต้องมากกว่า 0");
  const x = Math.floor(width * crop.x / 100), y = Math.floor(height * crop.y / 100);
  const w = Math.min(width - x, Math.round(width * crop.width / 100));
  const h = Math.min(height - y, Math.round(height * crop.height / 100));
  if (w < 1 || h < 1) throw new Error("กรอบครอปเล็กเกินไป");
  return { x, y, width: w, height: h };
}

export function imageMime(name: string): string | null {
  const ext = name.split(".").at(-1)?.toLowerCase();
  return ext === "jpg" || ext === "jpeg" ? "image/jpeg" : ext === "png" ? "image/png" : ext === "webp" ? "image/webp" : null;
}

/** Read dimensions before decoding: a tiny compressed image must not allocate an unbounded bitmap. */
export function imageDimensions(bytes: Uint8Array): { width: number; height: number } {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let width = 0, height = 0;
  if (bytes.length >= 24 && bytes[0] === 137 && bytes[1] === 80 && bytes[2] === 78 && bytes[3] === 71) {
    width = view.getUint32(16); height = view.getUint32(20);
  } else if (bytes.length >= 4 && bytes[0] === 255 && bytes[1] === 216) {
    let offset = 2;
    while (offset + 4 < bytes.length) {
      if (bytes[offset++] !== 255) break;
      while (bytes[offset] === 255) offset++;
      const marker = bytes[offset++];
      if (marker === 217 || marker === 218) break;
      if (marker === 1 || (marker >= 208 && marker <= 215)) continue;
      if (offset + 2 > bytes.length) break;
      const length = view.getUint16(offset);
      if (length < 2 || offset + length > bytes.length) break;
      if ([192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207].includes(marker) && length >= 7) {
        height = view.getUint16(offset + 3); width = view.getUint16(offset + 5); break;
      }
      offset += length;
    }
  } else if (bytes.length >= 30 && String.fromCharCode(...bytes.subarray(0, 4)) === "RIFF"
    && String.fromCharCode(...bytes.subarray(8, 12)) === "WEBP") {
    const kind = String.fromCharCode(...bytes.subarray(12, 16));
    if (kind === "VP8X") {
      width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
      height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
    } else if (kind === "VP8L" && bytes[20] === 47) {
      const bits = view.getUint32(21, true); width = 1 + (bits & 0x3fff); height = 1 + ((bits >>> 14) & 0x3fff);
    } else if (kind === "VP8 " && bytes[23] === 157 && bytes[24] === 1 && bytes[25] === 42) {
      width = view.getUint16(26, true) & 0x3fff; height = view.getUint16(28, true) & 0x3fff;
    }
  }
  if (width <= 0 || height <= 0 || width * height > 24_000_000 || Math.max(width, height) > 16000)
    throw new Error("รูปไม่รองรับหรือใหญ่เกินขีดจำกัด editor (24 ล้านพิกเซล/ด้านละ 16,000) — กรุณาใช้โปรแกรมรูปภาพ");
  return { width, height };
}

export async function renderImage(bytes: Blob, quarterTurns: number, crop: Crop): Promise<HTMLCanvasElement> {
  imageDimensions(new Uint8Array(await bytes.arrayBuffer()));
  const bitmap = await createImageBitmap(bytes);
  try {
    if (bitmap.width * bitmap.height > 24_000_000 || Math.max(bitmap.width, bitmap.height) > 16000)
      throw new Error("รูปใหญ่เกินขีดจำกัด editor (24 ล้านพิกเซล/ด้านละ 16,000) — กรุณาใช้โปรแกรมรูปภาพ");
    const turns = ((quarterTurns % 4) + 4) % 4;
    const rotated = document.createElement("canvas");
    rotated.width = turns % 2 ? bitmap.height : bitmap.width;
    rotated.height = turns % 2 ? bitmap.width : bitmap.height;
    const ctx = rotated.getContext("2d");
    if (!ctx) throw new Error("browser ไม่รองรับ canvas");
    ctx.translate(rotated.width / 2, rotated.height / 2);
    ctx.rotate(turns * Math.PI / 2);
    ctx.drawImage(bitmap, -bitmap.width / 2, -bitmap.height / 2);
    const rect = cropPixels(crop, rotated.width, rotated.height);
    const output = document.createElement("canvas"); output.width = rect.width; output.height = rect.height;
    const dest = output.getContext("2d");
    if (!dest) throw new Error("browser ไม่รองรับ canvas");
    dest.drawImage(rotated, rect.x, rect.y, rect.width, rect.height, 0, 0, rect.width, rect.height);
    return output;
  } finally { bitmap.close(); }
}

export async function imageExport(bytes: Blob, name: string, turns: number, crop: Crop): Promise<Blob> {
  const mime = imageMime(name);
  if (!mime) throw new Error("รองรับ JPG, PNG และ WebP เท่านั้น");
  const canvas = await renderImage(bytes, turns, crop);
  return new Promise((resolve, reject) => canvas.toBlob(blob => {
    if (!blob || blob.type !== mime) reject(new Error("browser บันทึกชนิดรูปนี้ไม่ได้ — ไม่เปลี่ยนชนิดไฟล์อัตโนมัติ"));
    else resolve(blob);
  }, mime, 0.95));
}

export type PdfNote = { page: number; text: string; x: number; y: number; size: number };
async function loadPdf(bytes: Uint8Array) {
  const { PDFDocument, PDFDict, PDFName, PDFArray, PDFStream } = await import("pdf-lib");
  const doc = await PDFDocument.load(bytes, { updateMetadata: false }); // Never ignore encryption.
  if (doc.getPageCount() > 200) throw new Error("PDF เกิน 200 หน้า — เปิดดูได้แต่ editor ไม่รองรับ");
  const visited = new Set();
  const pending = doc.context.enumerateIndirectObjects().map(([, object]) => object);
  while (pending.length) {
    const obj = pending.pop()!;
    if (visited.has(obj)) continue;
    visited.add(obj);
    if (visited.size > 100000) throw new Error("PDF ซับซ้อนเกินขีดจำกัด editor");
    if (obj instanceof PDFDict) {
      if (obj.has(PDFName.of("ByteRange")) || obj.get(PDFName.of("FT"))?.toString() === "/Sig"
        || obj.has(PDFName.of("XFA"))) throw new Error("PDF มีลายเซ็นดิจิทัล/ช่องลายเซ็น/XFA — อ่านอย่างเดียว");
      pending.push(...obj.values());
    } else if (obj instanceof PDFArray) pending.push(...obj.asArray());
    else if (obj instanceof PDFStream) pending.push(obj.dict);
  }
  return doc;
}

export async function pdfInfo(bytes: Uint8Array): Promise<number> { return (await loadPdf(bytes)).getPageCount(); }

export async function pdfExport(bytes: Uint8Array, note: PdfNote): Promise<Uint8Array> {
  if (!Number.isInteger(note.page) || note.page < 1 || !note.text.trim() || note.text.length > 1000
    || ![note.x, note.y, note.size].every(Number.isFinite) || note.x < 0 || note.x > 100 || note.y < 0 || note.y > 100
    || note.size < 8 || note.size > 48) throw new Error("ข้อความ/หน้า/ตำแหน่งไม่ถูกต้อง (ข้อความไม่เกิน 1,000 ตัวอักษร)");
  const doc = await loadPdf(bytes);
  const page = doc.getPages()[note.page - 1];
  if (!page) throw new Error("ไม่พบหน้านี้");
  if (page.getRotation().angle !== 0) throw new Error("หน้าที่หมุนอยู่ใช้เพิ่มข้อความไม่ได้ใน editor พื้นฐาน — กรุณาใช้โปรแกรม PDF");
  const fontResponse = await fetch("/fonts/NotoSansThai.ttf", { cache: "force-cache" });
  if (!fontResponse.ok) throw new Error("โหลดฟอนต์ไทยไม่ได้ — ยังไม่บันทึกไฟล์");
  const { default: fontkit } = await import("@pdf-lib/fontkit");
  doc.registerFontkit(fontkit);
  const font = await doc.embedFont(await fontResponse.arrayBuffer(), { subset: true });
  const charset = new Set(font.getCharacterSet());
  if ([...note.text].some(c => c !== "\n" && c !== "\r" && !charset.has(c.codePointAt(0)!)))
    throw new Error("ฟอนต์รองรับไทย/อังกฤษ แต่ข้อความมีตัวอักษรที่ไม่รองรับ");
  const box = page.getCropBox();
  const x = box.x + box.width * note.x / 100;
  const y = box.y + box.height * (1 - note.y / 100) - note.size;
  const lines = note.text.replaceAll("\r", "").split("\n");
  if (lines.some(line => font.widthOfTextAtSize(line, note.size) > box.x + box.width - x)
    || y - (lines.length - 1) * note.size * 1.5 < box.y)
    throw new Error("ข้อความเลยขอบหน้า — ขึ้นบรรทัดใหม่ ลดขนาด หรือเลื่อนตำแหน่ง");
  const { rgb } = await import("pdf-lib");
  page.drawText(lines.join("\n"), { x, y, size: note.size, font, lineHeight: note.size * 1.5, color: rgb(0.04, 0.13, 0.25) });
  return doc.save();
}
