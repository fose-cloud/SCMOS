import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  // The App Service hostname, so the Open Graph image resolves to an absolute
  // URL when the app is linked internally. Falls back to localhost in dev.
  metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000"),
  title: "SCMOS · Subcontractor Management Operating System",
  description: "Operation workspace, shipment monitoring and supplier governance for Leschaco (Thailand) Ltd.",
  icons: { icon: "/logo.png", shortcut: "/logo.png" },
  openGraph: {
    title: "SCMOS · Subcontractor Management Operating System",
    description: "Operation workspace, shipment monitoring and supplier governance.",
    images: [{ url: "/og.png", width: 1680, height: 944 }],
  },
  twitter: {
    card: "summary_large_image",
    title: "SCMOS · Subcontractor Management Operating System",
    description: "Operation workspace, shipment monitoring and supplier governance.",
    images: ["/og.png"],
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="th">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
        <link
          href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=IBM+Plex+Sans+Thai:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500;600&family=Source+Sans+3:wght@400;500;600;700&family=Source+Serif+4:opsz,wght@8..60,500;8..60,600&display=swap"
          rel="stylesheet"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
