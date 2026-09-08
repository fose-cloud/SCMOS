import type { NextConfig } from "next";
const config: NextConfig = {
  poweredByHeader: false,
  experimental: { externalDir: true },
  // This entry is local QA only. Do not publish this folder.
};
export default config;
