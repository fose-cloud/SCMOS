import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * shadcn's card, kept as source in this repository rather than pulled from a
 * package — which is what shadcn/ui is: components you own and edit, not a
 * dependency you upgrade.
 */
export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <div className={cn("rounded-lg border border-[var(--border)] bg-[var(--card)]", className)}>
      {children}
    </div>
  );
}

export function CardHeader({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn("flex flex-col gap-1 px-5 py-4", className)}>{children}</div>;
}

export function CardTitle({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <h3 className={cn("text-[13px] font-semibold tracking-tight text-[var(--foreground)]", className)}>
      {children}
    </h3>
  );
}

export function CardDescription({ className, children }: { className?: string; children: ReactNode }) {
  return <p className={cn("text-[11.5px] text-[var(--muted-foreground)]", className)}>{children}</p>;
}

export function CardContent({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn("px-5 pb-5", className)}>{children}</div>;
}
