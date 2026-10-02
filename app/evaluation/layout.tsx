import type { Metadata } from "next";

// The page outside SCMOS is never indexed, and it names no referrer when it leaves (1 Oct 2026).
export const metadata: Metadata = {
  title: "Carrier Annual Evaluation · Leschaco",
  robots: { index: false, follow: false },
  referrer: "no-referrer",
  openGraph: null,
  twitter: null,
};

export default function EvaluationLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return children;
}
