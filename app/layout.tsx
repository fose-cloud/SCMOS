import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SCMOS Report & Dashboard",
  description: "Governed, auditable logistics KPI reporting for Leschaco Thailand.",
  icons: { icon: "/logo.png", shortcut: "/logo.png" },
  openGraph: { title: "SCMOS Report & Dashboard", description: "Trusted data. Traceable KPI.", images: [{ url: "/og.png", width: 1680, height: 944 }] },
  twitter: { card: "summary_large_image", title: "SCMOS Report & Dashboard", description: "Trusted data. Traceable KPI.", images: ["/og.png"] },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="th"><body>{children}</body></html>;
}
