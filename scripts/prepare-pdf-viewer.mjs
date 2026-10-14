// Package only pinned, trusted PDF.js assets under this app's origin. No runtime CDN/document upload.
import { copyFile, cp, mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";
const source = new URL("../node_modules/pdfjs-dist/", import.meta.url);
const target = new URL("../public/vendor/pdfjs/6.4.299/", import.meta.url);
await mkdir(target, { recursive: true });
for (const name of ["pdf.min.mjs", "pdf.worker.min.mjs"])
  await copyFile(new URL("build/" + name, source), new URL(name, target));
await copyFile(new URL("LICENSE", source), new URL("LICENSE", target));
for (const name of ["cmaps", "standard_fonts", "wasm", "iccs"])
  await cp(fileURLToPath(new URL(name, source)), fileURLToPath(new URL(name, target)), { recursive: true });
console.log("Prepared same-origin PDF.js viewer assets (6.4.299)");
