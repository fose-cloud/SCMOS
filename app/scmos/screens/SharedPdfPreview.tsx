"use client";
import { useEffect, useRef, useState } from "react";
import type { PDFDocumentLoadingTask, RenderTask } from "pdfjs-dist";

const ASSETS = "/vendor/pdfjs/6.4.299/";
/** Canvas-only display: no PDF JavaScript/actions, external links, form submissions or native viewer dependency. */
export function SharedPdfPreview({ blob, page, onPages, onReady }: {
  blob: Blob; page: number; onPages: (pages: number) => void; onReady: (ready: boolean) => void;
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let stale = false;
    let loadingTask: PDFDocumentLoadingTask | undefined;
    let renderTask: RenderTask | undefined;
    void (async () => {
      setLoading(true); setError(""); onReady(false);
      try {
        // This is a fixed, self-hosted module, not user input. Keep Node-only PDF.js imports out of the Next client bundle.
        const pdfjs = await import(/* webpackIgnore: true */ /* turbopackIgnore: true */ ASSETS + "pdf.min.mjs") as typeof import("pdfjs-dist");
        if (stale) return;
        pdfjs.GlobalWorkerOptions.workerSrc = ASSETS + "pdf.worker.min.mjs";
        const bytes = new Uint8Array(await blob.arrayBuffer());
        if (stale) return;
        loadingTask = pdfjs.getDocument({ data: bytes, enableXfa: false,
          useSystemFonts: false, cMapUrl: ASSETS + "cmaps/", cMapPacked: true,
          standardFontDataUrl: ASSETS + "standard_fonts/", wasmUrl: ASSETS + "wasm/", iccUrl: ASSETS + "iccs/" });
        const doc = await loadingTask.promise;
        if (stale) return;
        if (doc.numPages > 200) throw new Error("PDF เกิน 200 หน้า — กรุณาดาวน์โหลด");
        onPages(doc.numPages);
        if (!Number.isInteger(page) || page < 1 || page > doc.numPages) throw new Error("ไม่พบหน้านี้");
        const pdfPage = await doc.getPage(page);
        if (stale) return;
        const natural = pdfPage.getViewport({ scale: 1 });
        const scale = Math.min(1.5, 1500 / Math.max(natural.width, natural.height));
        const viewport = pdfPage.getViewport({ scale });
        const target = canvas.current;
        if (!target || !Number.isFinite(viewport.width * viewport.height) || viewport.width <= 0 || viewport.height <= 0)
          throw new Error("ขนาดหน้า PDF ไม่ถูกต้อง");
        // Render off-screen, then commit only the latest request. Stale renders never touch the visible canvas.
        const output = document.createElement("canvas"); output.width = Math.ceil(viewport.width); output.height = Math.ceil(viewport.height);
        renderTask = pdfPage.render({ canvas: output, viewport });
        await renderTask.promise;
        if (stale) return;
        target.width = output.width; target.height = output.height; target.getContext("2d")?.drawImage(output, 0, 0);
        onReady(true);
      } catch (e) { if (!stale) setError(e instanceof Error ? e.message : "แสดง PDF ไม่ได้"); }
      finally {
        if (!stale) setLoading(false);
        await loadingTask?.destroy();
      }
    })();
    return () => { stale = true; renderTask?.cancel(); void loadingTask?.destroy(); };
  }, [blob, page, onPages, onReady]);
  return <div style={{ padding: 12, background: "#E9EEF3", textAlign: "center", borderRadius: 5 }}>
    {loading && <p role="status">กำลังวาดหน้า PDF…</p>}
    {error && <p role="alert" style={{ color: "#B42318" }}>{error} — ใช้ปุ่มดาวน์โหลดเพื่อเปิดด้วยโปรแกรม PDF</p>}
    <canvas ref={canvas} aria-label={`ตัวอย่าง PDF หน้า ${page}`} style={{ maxWidth: "100%", background: "#fff", display: error || loading ? "none" : "inline-block" }} />
  </div>;
}
