import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Azure App Service runs the built server with `node server.js`. Standalone
  // puts that server and only the dependencies it actually reached into
  // .next/standalone, so the deployment package is the app rather than the
  // whole node_modules tree.
  output: "standalone",

  // The register carries real driver names and phone numbers, so nothing about
  // the stack goes out in a response header.
  poweredByHeader: false,

  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "same-origin" },
          { key: "X-Frame-Options", value: "DENY" },
        ],
      },
    ];
  },
};

export default nextConfig;
