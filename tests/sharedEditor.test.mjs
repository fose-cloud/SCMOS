import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { PDFDocument, PDFDict, PDFName } from "pdf-lib";
import { cropPixels, imageMime, imageDimensions, pdfInfo, pdfExport } from "../app/scmos/sharedEditorLogic.ts";

const read = path => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("crop coordinates are bounded, scaled and never empty", () => {
  assert.deepEqual(cropPixels({ x: 25, y: 10, width: 50, height: 80 }, 400, 200), { x: 100, y: 20, width: 200, height: 160 });
  for (const crop of [{ x: -1, y: 0, width: 10, height: 10 }, { x: 90, y: 0, width: 20, height: 10 },
    { x: 0, y: 0, width: 0, height: 10 }, { x: NaN, y: 0, width: 10, height: 10 }]) assert.throws(() => cropPixels(crop, 400, 200));
  assert.throws(() => cropPixels({ x: 0, y: 0, width: 0.01, height: 1 }, 1, 1));
});
test("image export never silently converts an unsupported extension", () => {
  assert.equal(imageMime("a.JPEG"), "image/jpeg"); assert.equal(imageMime("b.png"), "image/png");
  assert.equal(imageMime("c.webp"), "image/webp"); assert.equal(imageMime("d.gif"), null);
});
test("image dimensions are checked before decoding a potentially huge bitmap", () => {
  const bytes = readFileSync(new URL("../public/logo.png", import.meta.url));
  assert.ok(imageDimensions(bytes).width > 0);
  const bomb = Buffer.from(bytes); bomb.writeUInt32BE(100000, 16); bomb.writeUInt32BE(100000, 20);
  assert.throws(() => imageDimensions(bomb), /ขีดจำกัด/);
  assert.throws(() => imageDimensions(new Uint8Array()), /ไม่รองรับ/);
});
test("PDF is parsed offline and signed documents are read-only", async () => {
  const doc = await PDFDocument.create(); doc.addPage([400, 500]);
  assert.equal(await pdfInfo(await doc.save()), 1);
  const signature = doc.context.obj({ FT: PDFName.of("Sig") }); doc.context.register(signature);
  await assert.rejects(() => pdfInfo(new Uint8Array()));
  const signedBytes = await doc.save();
  await assert.rejects(() => pdfInfo(signedBytes), /ลายเซ็น/);
});
test("PDF adds Thai text using a locally bundled font and preserves the page", async () => {
  const doc = await PDFDocument.create(); doc.addPage([400, 500]);
  const original = await doc.save();
  const fetchBefore = globalThis.fetch;
  globalThis.fetch = async url => {
    assert.equal(url, "/fonts/NotoSansThai.ttf");
    return new Response(readFileSync(new URL("../public/fonts/NotoSansThai.ttf", import.meta.url)));
  };
  try {
    const edited = await pdfExport(original, { page: 1, text: "ราคาค่าขนส่ง 1,200 THB", x: 5, y: 5, size: 12 });
    const reopened = await PDFDocument.load(edited);
    assert.equal(reopened.getPageCount(), 1); assert.equal(reopened.getPage(0).getWidth(), 400);
    assert.ok(edited.length > original.length);
    assert.ok(reopened.context.enumerateIndirectObjects().some(([, object]) => object instanceof PDFDict && object.has(PDFName.of("FontFile2"))));
    await assert.rejects(() => pdfExport(original, { page: 2, text: "bad", x: 5, y: 5, size: 12 }), /ไม่พบหน้า/);
    await assert.rejects(() => pdfExport(original, { page: 1, text: "long text", x: 100, y: 100, size: 48 }), /เลยขอบ/);
  } finally { globalThis.fetch = fetchBefore; }
});
test("editor uses private routes, optimistic tokens, history and unsaved-change protection", () => {
  const ui = read("app/scmos/screens/SharedFileEditor.tsx");
  assert.match(ui, /version: info.version/); assert.match(ui, /form.append\("version", info.version\)/);
  assert.match(ui, /revision=\$\{old.revision\}/); assert.match(ui, /URL.revokeObjectURL/);
  assert.match(ui, /controller.abort\(\)/); assert.match(ui, /beforeunload/); assert.match(ui, /busyRef.current/);
  assert.doesNotMatch(ui, /officeapps|docs.google|onlyoffice|GenerateSas/);
  const service = read("server/Scmos.Api/Services/SharedFileEditing.cs");
  assert.match(service, /EvaluationWrites.InOneAsync/); assert.match(service, /SharedFileRevisions.Add/);
  assert.match(service, /!CanWrite\(user\) \|\|/); assert.doesNotMatch(service, /CanChange\(/); assert.match(service, /DbUpdateConcurrencyException/);
  assert.match(read("server/Scmos.Api/Data/ScmosDbContext.cs"), /IsRowVersion\(\)/);
});
test("PDF rendering is self-hosted, canvas-only and uses the pinned package assets", () => {
  const preview = read("app/scmos/screens/SharedPdfPreview.tsx");
  const version = JSON.parse(read("package.json")).dependencies["pdfjs-dist"];
  assert.match(preview, new RegExp(`/vendor/pdfjs/${version.replaceAll(".", "\\.")}/`));
  assert.match(preview, /enableXfa: false/); assert.match(preview, /pdfPage.render/);
  assert.match(preview, /loadingTask\?\.destroy/); assert.match(preview, /renderTask\?\.cancel/);
  assert.doesNotMatch(preview, /https:|eval\(|getJavaScript|execute/);
  assert.match(read("scripts/prepare-pdf-viewer.mjs"), /pdf.worker.min.mjs/);
  assert.match(read("package.json"), /"postinstall": "node scripts\/prepare-pdf-viewer.mjs"/);
  assert.match(read("app/scmos/screens/SharedFileEditor.tsx"), /\(!prepared \|\| !pdfReady\)/);
});
