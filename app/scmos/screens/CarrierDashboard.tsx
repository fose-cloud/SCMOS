"use client";

import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { parseCarrierDashboardJobs } from "../carrierPortal";
import { filterDashboardJobs, type DashboardFilters } from "../dashboardFilters";
import type { Screen } from "../nav";
import { prep, type Job, type RawOps } from "../ops";
import { filterPeriod, type Period } from "../period";
import { Dashboard } from "./Dashboard";

/** Where a department dashboard's link leads on a carrier's own menu. */
const CARRIER_TARGET: Record<string, Screen> = {
  incident: "carrierkpi", kpi: "carrierkpi", subcontractors: "carrierkpi", reports: "carrierkpi",
  monitoring: "carriermyjob", workspace: "carriermyjob", myjob: "carriermyjob", booking: "carriernew",
  postpone: "carrierpostpone", billing: "carrierbilling", capacity: "carriercapacity",
};

/**
 * The carrier's own Dashboard (30 Sep 2026): the department's dashboard,
 * unchanged, fed this company's jobs (/api/carrier/dashboard/jobs) and this
 * company's measures (ControlTower reads /api/carrier/dashboard/measures in
 * carrier mode). Every figure is counted the way the department's is, over one
 * carrier's work; every link lands on the carrier's own screens.
 */
export function CarrierDashboard({ period, onPeriod, filters, onFilters, tab, userName, onNavigate }: {
  period: Period; onPeriod: (period: Period) => void;
  filters: DashboardFilters; onFilters: (filters: DashboardFilters) => void;
  tab: string; userName: string; onNavigate: (screen: Screen) => void;
}) {
  const [jobs, setJobs] = useState<Job[] | null>(null);
  const [company, setCompany] = useState("");
  const [offered, setOffered] = useState(0);
  const [error, setError] = useState("");

  useEffect(() => {
    let alive = true;
    void (async () => {
      try {
        const response = await apiFetch("/api/carrier/dashboard/jobs", { headers: { accept: "application/json" } });
        const body: unknown = await response.json().catch(() => null);
        if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิดแดชบอร์ดไม่ได้ (${response.status})`);
        const parsed = parseCarrierDashboardJobs(body);
        if (!alive) return;
        // The same preparation the department's register goes through, so the figures are counted alike.
        setJobs(prep({ jobs: parsed.jobs } as unknown as RawOps).jobs);
        setCompany(parsed.supplierName);
        setOffered(parsed.offered);
      } catch (problem) {
        if (alive) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
      }
    })();
    return () => { alive = false; };
  }, []);

  const periodJobs = useMemo(() => filterPeriod(jobs ?? [], period), [jobs, period]);
  const shown = useMemo(() => filterDashboardJobs(periodJobs, filters), [periodJobs, filters]);
  const go = (screen: string) => onNavigate(CARRIER_TARGET[screen] ?? (screen.startsWith("carrier") ? screen as Screen : "carrier"));

  return (
    <Dashboard
      jobs={shown} filters={filters} onFilters={onFilters} allJobs={jobs ?? []}
      period={period} onPeriod={onPeriod} loaded={jobs !== null} note={error || undefined}
      tab={tab} userName={userName}
      onDrill={() => onNavigate("carriermyjob")}
      onOpen={go}
      onNewJob={() => onNavigate("carriernew")}
      onImport={() => undefined} onExport={() => undefined} onAsk={() => undefined}
      onOpenKpi={() => onNavigate("carrierkpi")}
      carrier={{ company, offered }}
    />
  );
}
