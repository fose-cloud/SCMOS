"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ChangeEvent, DragEvent } from "react";

import { apiFetch, setDevUser } from "./scmos/api";
import { Chrome, type FilterDef, type HeaderAction, type TabItem } from "./scmos/Chrome";
import { DataTable } from "./scmos/DataTable";
import { buildDb, type Ship } from "./scmos/demo";
import { ACCOUNTS, META, opIdForName, SCREENS_WITH_FILTERS, TAB_DEFS, type Account, type Screen } from "./scmos/nav";
import { prep, flagJob, type Job, type Ops, type RawOps } from "./scmos/ops";
import { normaliseField } from "./scmos/standard";
import { exportDashboard, exportJobs, exportRates, parseWorkbook, type DupDecision, type ImportPreview } from "./scmos/excel";
import { deleteView, describeView, listViews, saveView, type SavedView, type ViewState } from "./scmos/views";
import { clearJobs, deleteJobs, loadJobs, loadPlanFile, saveJobs } from "./scmos/store";
import { cleanupJobs, duplicateGroups, type CleanupReport, type DupGroup } from "./scmos/cleanup";
import { ALL_PERIOD, filterPeriod, periodLabel, type Period } from "./scmos/period";
import { CleanupReportModal, DuplicatesModal } from "./scmos/overlays/DataOverlays";
import { ImportModal, SavedViewsModal } from "./scmos/overlays/ExcelOverlays";
import { buildTable, type Filters } from "./scmos/tables";
import { BTN_PRIMARY, BTN_SECONDARY, STATUS, STATUS_RE, css } from "./scmos/theme";
import { fdate, nowHM, pad } from "./scmos/util";

import { Dashboard } from "./scmos/screens/Dashboard";
import { Rates } from "./scmos/screens/Rates";
import { Kpi } from "./scmos/screens/Kpi";
import { Monitoring } from "./scmos/screens/Monitoring";
import { PreRun } from "./scmos/screens/PreRun";
import { Audit, NotBuilt } from "./scmos/screens/Audit";
import { Suppliers } from "./scmos/screens/Suppliers";
import { Incidents } from "./scmos/screens/Incidents";
import { Assistant } from "./scmos/screens/Assistant";
import { Evaluation, Vendor } from "./scmos/screens/SupplierFlows";
import { Quotation } from "./scmos/screens/Quotation";
import { Today } from "./scmos/screens/Today";
import { CapacityBoard } from "./scmos/screens/CapacityBoard";
import { Documents } from "./scmos/screens/Documents";
import { Administration } from "./scmos/screens/Administration";
import { Verification } from "./scmos/screens/Verification";

/**
 * Screens the menu names that the system cannot honestly fill yet.
 *
 * Empty at the moment — every menu entry now renders something real. Kept, with
 * `NotBuilt`, because the next screen the menu promises before the backend can
 * answer it should say so rather than being filled with plausible figures. A
 * screen full of invented numbers is how a system starts being trusted for
 * things it cannot do.
 */
const NOT_BUILT: Partial<Record<Screen, { ready: string[]; missing: string[] }>> = {};

/**
 * Screens that render their own component off the API.
 *
 * The generic table builder draws nothing for these names now, but the header's
 * fallback Export Excel — which only raises a toast — would be a button that
 * lies about what it does, so listing them here suppresses it.
 */
const OWN_SCREEN: Partial<Record<Screen, true>> = {
  subcontractors: true, carpar: true, incident: true, assistant: true,
  vendor: true, evaluation: true, quotation: true,
  capacity: true, documents: true, admin: true, docverify: true,
};
import type { RateBook } from "./scmos/rates";
import { Detail, type AuditEntry } from "./scmos/screens/Detail";
import { BillingAging, Reports } from "./scmos/screens/Panels";
import { Booking } from "./scmos/screens/Booking";
import { Workspace, workspaceTabCounts, type WsState } from "./scmos/screens/Workspace";

import { Login } from "./scmos/overlays/Login";
import { DelayModal, DocsDrawer, Notifications, ProfileMenu, SettingsModal, Toast, type Field, type StoredDoc } from "./scmos/overlays/Overlays";
import type { Alert, WsTarget } from "./scmos/alerts";
import { globalSearch, type SearchHit } from "./scmos/search";
import { DEFAULT_PREFS, EMPTY_PROFILE, loadPrefs, loadProfile, readAvatar, savePrefs, saveProfile, type Prefs, type Profile } from "./scmos/settings";
import { AddJobModal, AssignModal, JobDrawer } from "./scmos/overlays/WorkspaceOverlays";

const EMPTY_FILTERS: Filters = { dir: "All", cust: "All", sub: "All", truck: "All", status: "All", month: "All" };
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug"];


const EMPTY_WS = {
  cat: "ALL", cust: "ALL", trucker: "ALL", date: "ALL", kpi: "All", assignee: "All Team",
  status: "ALL", type: "ALL", year: "ALL", month: "ALL",
  edit: null as { key: string; field: string } | null, editVal: "",
  sort: null as { key: string; dir: "asc" | "desc" } | null,
  picked: [] as string[],
};

const EMPTY_DELAY = { reason: "", party: "", start: "", eta: "", next: "", note: "" };

type Props = {
  /** Verified identity from App Service Web App Login; null in local dev. */
  initialUser: Account | null;
  /** Where the platform's sign-out lives, when there is a real session to end. */
  signOutHref: string | null;
};

export function SCMOSApp({ initialUser, signOutHref }: Props) {
  // ---- chrome / navigation -----------------------------------------------
  const [screen, setScreen] = useState<Screen>("dashboard");
  const [collapsed, setCollapsed] = useState(false);
  const [gq, setGq] = useState("");
  const [searchOpen, setSearchOpen] = useState(false);
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const [tab, setTab] = useState("");
  const [f, setF] = useState<Filters>(EMPTY_FILTERS);
  /** Day / month / year the dashboard reports on. */
  const [period, setPeriod] = useState<Period>(ALL_PERIOD);
  const [sel, setSel] = useState<number | null>(null);

  // ---- session -----------------------------------------------------------
  // With real auth the visitor is already authenticated at the edge, so the demo
  // account switcher is bypassed entirely and sign-out hands back to the platform.
  const [auth, setAuth] = useState<Account | null>(initialUser ?? ACCOUNTS[0]);

  // Set during render rather than from an effect: the first load fires from an
  // effect declared further down, and it has to carry the account with it. The
  // call is idempotent and touches no React state. Deployed, it does nothing —
  // App Service has already said who this is.
  setDevUser(auth);
  const [loginU, setLoginU] = useState("watsana");
  const [loginP, setLoginP] = useState("password");
  const [loginErr, setLoginErr] = useState("");

  // ---- overlays ----------------------------------------------------------
  const [toast, setToastValue] = useState("");
  const [notif, setNotif] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [cleanupReport, setCleanupReport] = useState<CleanupReport | null>(null);
  const [dupGroups, setDupGroups] = useState<DupGroup[] | null>(null);
  const [dataBusy, setDataBusy] = useState(false);
  // Read from localStorage after mount so the server render and the first client
  // render agree; `applyPrefs` then lands the stored choices.
  const [prefs, setPrefs] = useState<Prefs>(DEFAULT_PREFS);
  const [profile, setProfile] = useState<Profile>(EMPTY_PROFILE);
  const [docsOpen, setDocsOpen] = useState(false);
  const [docs, setDocs] = useState<StoredDoc[]>([]);
  const [dragOver, setDragOver] = useState(false);
  const [audit, setAudit] = useState<AuditEntry[]>([]);
  const [delay, setDelay] = useState(EMPTY_DELAY);
  const [delayFor, setDelayFor] = useState<number | null>(null);
  const [opsDelay, setOpsDelay] = useState<string | null>(null);

  // ---- workspace ---------------------------------------------------------
  const [ws, setWs] = useState(EMPTY_WS);
  const [drawer, setDrawer] = useState<string | null>(null);
  const [assignFor, setAssignFor] = useState<string | null>(null);
  const [addCat, setAddCat] = useState<string | null>(null);
  const [addForm, setAddForm] = useState<Record<string, string>>({});
  const [aiFields, setAiFields] = useState<string[]>([]);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiMsg, setAiMsg] = useState("");

  // ---- Excel + saved views ----------------------------------------------
  const [importOpen, setImportOpen] = useState(false);
  const [importPreview, setImportPreview] = useState<ImportPreview | null>(null);
  const [importBusy, setImportBusy] = useState(false);
  const [importError, setImportError] = useState("");
  // One decision per duplicate row, keyed by the incoming job's key.
  const [dupChoice, setDupChoice] = useState<Record<string, DupDecision>>({});
  const [dupCursor, setDupCursor] = useState(0);
  const [viewsOpen, setViewsOpen] = useState(false);
  const [views, setViews] = useState<SavedView[]>([]);
  const [viewName, setViewName] = useState("");
  // What the workspace grid is showing right now, reported up by Workspace.
  const workspaceView = useRef<{ jobs: Job[]; layout: string }>({ jobs: [], layout: "ALL" });

  // ---- data --------------------------------------------------------------
  const db = useMemo(() => buildDb(), []);
  const [ops, setOps] = useState<Ops | null>(null);
  /** How the plan is getting to the database, reported in the workspace header. */
  const [sync, setSync] = useState<{ state: "idle" | "saving" | "saved" | "error" | "off"; at: string; message: string }>(
    { state: "idle", at: "", message: "" },
  );
  // Jobs and shipments are edited in place; bump this to re-render after a mutation.
  const [revision, setRevision] = useState(0);
  const touch = useCallback(() => setRevision((r) => r + 1), []);

  // ---- persistence -------------------------------------------------------
  /** Who to record against a save, kept in a ref so effects never read a stale name. */
  const meRef = useRef(ACCOUNTS[0].full);
  /** Jobs changed since the last write, collapsed by key so one save covers them. */
  const dirty = useRef(new Map<string, Job>());
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  /** Why the pending batch is being written, when the change needed explaining. */
  const pendingReason = useRef("");

  const flush = useCallback(async () => {
    const batch = [...dirty.current.values()];
    dirty.current.clear();
    if (!batch.length) return;
    const reason = pendingReason.current;
    pendingReason.current = "";
    setSync((prev) => (prev.state === "off" ? prev : { state: "saving", at: prev.at, message: "" }));
    const result = await saveJobs(batch, meRef.current, reason);
    setSync((prev) => (prev.state === "off" ? prev
      : result.ok ? { state: "saved", at: nowHM(), message: "" }
        : { state: "error", at: prev.at, message: result.message }));
  }, []);

  /**
   * Every edit lands in the database. Writes are batched over a short window so
   * typing down a column is one save, not one per keystroke.
   */
  const persist = useCallback((jobs: Job[], reason = "") => {
    if (!jobs.length) return;
    jobs.forEach((job) => dirty.current.set(job.key, job));
    if (reason) pendingReason.current = reason;
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => { void flush(); }, 700);
  }, [flush]);

  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const setToast = useCallback((message: string) => {
    setToastValue(message);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToastValue(""), 2600);
  }, []);

  /**
   * The plan comes from the register once it holds anything. An empty one is seeded
   * from `public/data/ops.json` so the July plan is keyed exactly once; if the
   * database cannot be reached at all the app still opens on the file, and says
   * so, rather than showing an empty workspace.
   */
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const stored = await loadJobs();
      if (cancelled) return;

      if (stored.jobs?.length) {
        setOps(prep({ jobs: stored.jobs }));
        setSync({ state: "saved", at: stored.updatedAt ? stored.updatedAt.slice(11, 16) : "", message: "" });
        return;
      }

      let raw: RawOps;
      try {
        raw = await loadPlanFile();
      } catch {
        if (!cancelled) {
          setOps({ jobs: [], masters: { customers: [], truckers: [], operators: ["Watsana", "Uthai", "Ananya", "Jiratchaya", "Maliwan"], cyYards: [], warehouses: [], provinces: [] } });
          setSync({ state: "off", at: "", message: "อ่านไฟล์แผนไม่สำเร็จ" });
        }
        return;
      }
      if (cancelled) return;

      const prepared = prep(raw);
      setOps(prepared);

      if (stored.source === "file-only") {
        setSync({ state: "off", at: "", message: stored.error || "ต่อฐานข้อมูลไม่ได้" });
        return;
      }
      // First run against an empty register: hand it the delivered plan.
      setSync({ state: "saving", at: "", message: "" });
      const seeded = await saveJobs(prepared.jobs, meRef.current);
      if (cancelled) return;
      setSync(seeded.ok
        ? { state: "saved", at: nowHM(), message: "seeded" }
        : { state: "error", at: "", message: seeded.message });
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => () => { if (toastTimer.current) clearTimeout(toastTimer.current); }, []);

  // Leaving the tab should not cost the last edit still sitting in the window.
  useEffect(() => {
    const onHide = () => { if (document.visibilityState === "hidden") void flush(); };
    document.addEventListener("visibilitychange", onHide);
    return () => document.removeEventListener("visibilitychange", onHide);
  }, [flush]);

  useEffect(() => {
    const stored = loadPrefs();
    setPrefs(stored);
    setCollapsed(stored.collapsed);
    setScreen(stored.landing);
  }, []);

  // The profile is stored per sign-in name, so switching demo accounts loads
  // that person's own picture and details.
  const signedInAs = (auth ?? ACCOUNTS[0]).user;
  useEffect(() => { setProfile(loadProfile(signedInAs)); }, [signedInAs]);

  // ---- derived -----------------------------------------------------------
  const isDetail = sel !== null;
  const metaKey = isDetail ? "detail" : screen;
  const meta = META[metaKey] || ["", "", ""];

  // The header search is a launcher now, not a filter: it opens the record where
  // it lives instead of quietly narrowing whatever table happens to be on screen.
  const filtered = useMemo(() => {
    return db.ships.filter((s) => {
      if (f.dir !== "All" && s.dir !== f.dir) return false;
      if (f.cust !== "All" && s.cust !== f.cust) return false;
      if (f.sub !== "All" && s.sub !== f.sub) return false;
      if (f.truck !== "All" && s.truck !== f.truck) return false;
      if (f.status !== "All" && s.status !== f.status) return false;
      if (f.month !== "All" && fdate(s.plan).slice(3) !== f.month) return false;
      return true;
    });
    // `revision` is deliberate: shipments are edited in place, so nothing else in
    // the dependency list changes identity when a row is updated.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [db, f, revision]);

  /**
   * Who this person is and what they may do — both from the API.
   *
   * The API reads the same platform headers the page render did, and it is the
   * copy that answers for every write, so it decides the role, the owner id and
   * the capability list. The browser used to work all three out for itself:
   * a role table the code told you to keep in step with the API's, an email
   * matcher beside `StaffDirectory.Match`, and a capability test that read
   * `role !== "Operation User"`. Three second opinions about who somebody is.
   *
   * Until this lands the account has no owner id and no capabilities, so no job
   * looks like yours and no write control is offered. That is the safe direction
   * to be wrong in: a moment of too few buttons is recoverable, a moment of too
   * many is not.
   */
  const [identity, setIdentity] = useState<
    { role: string; opId: string; name: string; init: string; known: boolean } | null>(null);
  const [can, setCan] = useState<Set<string>>(new Set());
  /**
   * Whether the capability list ever arrived.
   *
   * This needs a state of its own, and learning that cost a session. When the
   * fetch simply failed, `can` stayed empty and every cell in the grid went
   * read-only — correct, and completely silent. The rows still said MY JOB,
   * because ownership comes from the page render, so the screen looked normal
   * and nothing could be typed into. "Safe when it fails" is only half a design;
   * the other half is saying that it failed.
   */
  const [identityState, setIdentityState] = useState<"loading" | "ready" | "failed">("loading");
  const [identityAttempt, setIdentityAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    let retry: ReturnType<typeof setTimeout> | null = null;

    (async () => {
      try {
        const response = await apiFetch("/api/me", { headers: { accept: "application/json" } });
        if (cancelled) return;
        if (!response.ok) throw new Error("HTTP " + response.status);

        const body = await response.json() as {
          account?: { role?: string; opId?: string; name?: string; init?: string };
          can?: string[];
          known?: boolean;
        };
        if (cancelled) return;

        setCan(new Set(body.can ?? []));
        if (body.account) {
          setIdentity({
            role: body.account.role ?? "",
            opId: body.account.opId ?? "",
            name: body.account.name ?? "",
            init: body.account.init ?? "",
            known: body.known !== false,
          });
        }
        setIdentityState("ready");
      } catch {
        if (cancelled) return;
        setIdentityState("failed");
        // Keep trying, backing off to half a minute. A blip on the way to the
        // API should cost a few seconds of read-only, not the rest of the
        // session — and the banner below says what is happening meanwhile.
        const wait = Math.min(2000 * 2 ** identityAttempt, 30000);
        retry = setTimeout(() => setIdentityAttempt((n) => n + 1), wait);
      }
    })();

    return () => { cancelled = true; if (retry) clearTimeout(retry); };
  }, [signedInAs, identityAttempt]);

  const base = auth ?? ACCOUNTS[0];
  // The API's answer wins over whatever the page render guessed. In development
  // the demo account already carries a role, so this changes nothing; deployed,
  // it is the only thing that fills them in.
  const me: Account = identity
    ? { ...base, role: identity.role || base.role, opId: identity.opId || base.opId,
        name: identity.name || base.name, init: identity.init || base.init }
    : base;

  const able = (capability: string) => can.has(capability);
  const isSupervisor = able("ApproveAi");
  const isWorkspace = screen === "workspace" && !isDetail;

  // The rate book is nearly two megabytes of subcontractor quotations, so it is
  // fetched the first time somebody opens a screen that prices work, rather than
  // on every load. Booking needs it as much as Rates does — choosing a carrier
  // without seeing what they charge is the decision this is meant to inform.
  const [rates, setRates] = useState<RateBook | null>(null);
  const [ratesError, setRatesError] = useState("");
  const ratesAsked = useRef(false);

  // One diesel price for the whole app. Every quoted rate steps with it, so the
  // booking screen and the rate screen must not be able to disagree about it.
  const [diesel, setDiesel] = useState(32.94);

  useEffect(() => {
    if ((screen !== "rates" && screen !== "booking") || ratesAsked.current) return;
    ratesAsked.current = true;
    (async () => {
      try {
        // From the API, not the public folder. Eighteen carriers' negotiated
        // prices were reachable by anyone who guessed the URL; now they are
        // behind the same sign-in as everything else.
        const response = await apiFetch("/api/rates", { headers: { accept: "application/json" } });
        if (!response.ok) throw new Error("HTTP " + response.status);
        const body = await response.json() as {
          bands: { label: string; min: number; max: number }[];
          lanes: { id: number; carrier: string; service: string; customer: string;
                   from: string; to: string; county: string; remark: string;
                   prices: Record<string, (number | null)[]> }[];
          surcharges: { service: string; no: string; description: string;
                        currency: string; rate: string; unit: string }[];
        };
        setRates({
          bands: body.bands,
          lanes: body.lanes.map((lane) => ({ ...lane, id: String(lane.id) })),
          surcharges: body.surcharges,
          sources: [],
          issues: [],
          builtAt: "",
        });
      } catch (error) {
        setRatesError(error instanceof Error ? error.message : String(error));
      }
    })();
  }, [screen]);

  /**
   * Whose job this is.
   *
   * On the owner id, never on the display name. Sign-in introduces the operator
   * as an email and a full name; the plan workbooks call the same person
   * "Watsana". Matching the two spellings against each other worked only for as
   * long as the app made up its own accounts, and would have handed every
   * operator an empty workspace the day Web App Login was switched on.
   */
  const owns = (job: Job) => !!me.opId && job.opId === me.opId;

  /** What the dashboard reports on: the register narrowed to the chosen period. */
  const periodJobs = useMemo(
    () => filterPeriod(ops?.jobs ?? [], period),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [ops, period, revision],
  );

  const searchGroups = useMemo(
    () => globalSearch(gq, ops, db),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [gq, ops, db, revision],
  );

  /**
   * Alerts come from the API now.
   *
   * They used to be computed here, which meant the twelve rules the operation
   * agreed on existed only in the browser — invisible to the backend, and a
   * second copy waiting to disagree with the .NET one. `/api/notifications` runs
   * them where every other rule now lives.
   */
  const [alerts, setAlerts] = useState<Alert[]>([]);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/notifications", { headers: { accept: "application/json" } });
      if (!response.ok || cancelled) return;
      const feed = await response.json() as {
        alerts: { kind: string; level: string; english: string; thai: string; action: string;
                  screen: string; title: string; detail: string; targetId: string; count: number }[];
      };
      if (cancelled) return;
      setAlerts(feed.alerts.map((alert) => ({
        id: alert.kind,
        level: alert.level as Alert["level"],
        title: alert.title,
        th: alert.thai,
        body: alert.detail ? `${alert.detail} · ${alert.action}` : alert.action,
        count: alert.count,
        // Every alert names the screen that answers it; the workspace ones also
        // land on the job the count is about.
        target: { tab: "PENDING", screen: alert.screen, jobKey: alert.targetId },
      })));
    })();
    // The feed is a view of the register, so it is re-read whenever the register
    // changes underneath it.
    return () => { cancelled = true; };
  }, [revision, ops]);

  const criticalAlerts = alerts.filter((a) => a.level === "Critical").length;

  const myJobs = ops ? ops.jobs.filter(owns) : [];
  const myStats = {
    total: myJobs.length,
    open: myJobs.filter((j) => !STATUS_RE.done.test(j.status)).length,
    running: myJobs.filter((j) => /transit|arrived|loading|pickup|departed|gate/i.test(j.status)).length,
    delayed: myJobs.filter((j) => /delay/i.test(j.status)).length,
    action: myJobs.filter((j) => j.action).length,
    format: myJobs.filter((j) => j.issues.some((i) => i.severity === "error")).length,
  };

  /** One way in for every alert, profile shortcut and dashboard figure. */
  /** Opens whatever the header search matched, wherever that record lives. */
  function openSearchHit(hit: SearchHit) {
    setGq("");
    setSearchOpen(false);
    const a = hit.action;

    if (a.kind === "job") {
      // Clear the workspace filters first, or the job the person just searched
      // for can land outside the current slice and look missing.
      openTarget({ tab: "PENDING" });
      setDrawer(a.key);
      return;
    }
    if (a.kind === "customer") { openTarget({ tab: "PENDING" }); setWs((prev) => ({ ...prev, cust: a.value })); return; }
    if (a.kind === "trucker") { openTarget({ tab: "PENDING" }); setWs((prev) => ({ ...prev, trucker: a.value })); return; }
    if (a.kind === "ship") { go("monitoring"); setSel(a.id); return; }
    go(a.screen);
  }

  function openTarget(target: WsTarget) {
    // An API alert names the screen that answers it. Sending every alert to the
    // workspace would mean clicking "CAR/PAR overdue" lands on a job list that
    // cannot show a case.
    if (target.screen && target.screen !== "workspace") {
      setNotif(false);
      go(target.screen as Screen);
      return;
    }
    setScreen("workspace");
    setTab(target.tab ?? "PENDING");
    setPage(1);
    setQ("");
    setSel(null);
    setNotif(false);
    setProfileOpen(false);
    setWs((prev) => ({
      ...prev,
      cat: target.cat ?? "ALL",
      cust: "ALL",
      trucker: "ALL",
      type: "ALL",
      year: "ALL",
      month: "ALL",
      assignee: "All Team",
      kpi: target.kpi ?? "All",
      status: target.status ?? "ALL",
      date: target.date ?? "ALL",
    }));
    if (target.jobKey) setDrawer(target.jobKey);
  }

  function updatePrefs(next: Prefs) {
    setPrefs(savePrefs(next));
    setCollapsed(next.collapsed);
    setPage(1);
  }

  function updateProfile(next: Profile) {
    setProfile(next);
    if (!saveProfile(me.user, next)) {
      setToast("บันทึกลงเครื่องไม่สำเร็จ — พื้นที่เก็บข้อมูลของเบราว์เซอร์เต็ม");
    }
  }

  /**
   * Replaces everything in the database with the delivered plan file. This is
   * how a corrected or untruncated `ops.json` gets in — it is destructive, so
   * it asks first and is limited to supervisors.
   */
  async function reloadFromPlanFile() {
    if (!able("AdministerData")) {
      setToast("โหลดแผนใหม่ได้เฉพาะผู้ดูแลระบบ");
      return;
    }
    if (!window.confirm(
      "โหลดแผนใหม่จากไฟล์ ops.json?\n\nงานทั้งหมดในฐานข้อมูล รวมถึงที่คีย์และแก้ไว้ จะถูกลบแล้วแทนที่ด้วยข้อมูลในไฟล์",
    )) return;

    setSync({ state: "saving", at: "", message: "" });
    try {
      const raw = await loadPlanFile();
      const cleared = await clearJobs(me.full);
      if (!cleared.ok) throw new Error(cleared.message);
      const prepared = prep(raw);
      const saved = await saveJobs(prepared.jobs, me.full);
      if (!saved.ok) throw new Error(saved.message);
      setOps(prepared);
      setSync({ state: "saved", at: nowHM(), message: "" });
      setSettingsOpen(false);
      setToast(`โหลดแผนใหม่แล้ว ${prepared.jobs.length} งาน`);
      touch();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      setSync({ state: "error", at: "", message });
      setToast("โหลดแผนใหม่ไม่สำเร็จ: " + message);
    }
  }

  /**
   * Puts the whole register through the data standard once more, with the batch
   * context a single import row does not have. Every change lands on the job's
   * own history, so the pass is auditable and nothing happens quietly.
   */
  function runCleanup() {
    if (!ops) return;
    if (!able("AdministerData")) {
      setToast("ล้างข้อมูลย้อนหลังได้เฉพาะผู้ดูแลระบบ");
      return;
    }
    const { report, changed } = cleanupJobs(ops.jobs);
    const stamp = { ts: nowHM(), user: me.name };
    const byJob = new Map<string, typeof report.changes>();
    report.changes.forEach((c) => {
      const list = byJob.get(c.key) ?? [];
      list.push(c);
      byJob.set(c.key, list);
    });
    changed.forEach((job) => {
      const entries = (byJob.get(job.key) ?? []).map((c) => ({
        ...stamp, field: "ล้างข้อมูล · " + c.field, old: c.from || "—", neu: c.to || "(ว่าง)",
      }));
      job.hist = (job.hist || []).concat(entries);
    });

    setCleanupReport(report);
    setSettingsOpen(false);
    if (changed.length) {
      setDataBusy(true);
      persist(changed);
      void flush().finally(() => setDataBusy(false));
    }
    touch();
  }

  function openDuplicates() {
    if (!ops) return;
    setDupGroups(duplicateGroups(ops.jobs));
    setSettingsOpen(false);
  }

  /** Keeps the fullest copy of a repeated job and drops the rest. */
  async function mergeGroups(groups: DupGroup[]) {
    if (!ops || !groups.length) return;
    const score = (job: Job) => {
      const record = job as unknown as Record<string, unknown>;
      const filled = Object.keys(record).filter((k) => typeof record[k] === "string" && (record[k] as string).trim()).length;
      return filled * 10 + (job.hist?.length ?? 0);
    };

    const drop: string[] = [];
    groups.forEach((group) => {
      const keep = group.jobs.slice().sort((a, b) => score(b) - score(a))[0];
      group.jobs.forEach((job) => { if (job.key !== keep.key) drop.push(job.key); });
    });
    if (!drop.length) return;

    setDataBusy(true);
    const dropped = new Set(drop);
    ops.jobs = ops.jobs.filter((job) => !dropped.has(job.key));
    const result = await deleteJobs(drop, me.full);
    setDataBusy(false);
    setDupGroups(duplicateGroups(ops.jobs));
    touch();
    setToast(result.ok
      ? `รวมงานซ้ำแล้ว · ลบ ${drop.length} แถว เหลืองานทั้งหมด ${ops.jobs.length}`
      : "ลบไม่สำเร็จ: " + result.message);
  }

  async function pickAvatar(file: File | undefined) {
    if (!file) return;
    try {
      const avatar = await readAvatar(file);
      updateProfile({ ...profile, avatar });
      setToast("อัปเดตรูปโปรไฟล์แล้ว");
    } catch (error) {
      setToast("อัปโหลดรูปไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  /**
   * Who may write to this job.
   *
   * On capabilities, not on a role name. The old test was
   * `role !== "Operation User"`, which quietly meant "anyone who is not an
   * operator can edit anything" — true of a supervisor, and equally true of a
   * Viewer, a CS account and a subcontractor.
   */
  const canEditJob = (job: Job) =>
    able("EditAnyJob") || (able("EditOwnJobs") && owns(job));

  const tabList = TAB_DEFS[screen] || [];
  const activeTab = tab && tabList.indexOf(tab) >= 0 ? tab : tabList[0] || "";

  const wsCounts = useMemo(
    () => (isWorkspace ? workspaceTabCounts(ops, me.opId, ws.cat) : {}),
    // `revision` is deliberate — see the note on `filtered` above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [isWorkspace, ops, me.name, ws.cat, revision],
  );

  const tabs: TabItem[] = tabList.map((t) => ({
    label: isWorkspace && wsCounts[t] !== undefined ? t + "  " + wsCounts[t] : t,
    active: t === activeTab,
    go: () => { setTab(t); setPage(1); },
  }));

  // CAR/PAR is off this list now that the screen reads the real register: a
  // sidebar badge counted off the demo file would contradict the screen it
  // points at.
  const navCounts: Record<string, number> = {
    billing: db.ships.filter((x) => x.bill === "Overdue").length,
    booking: db.ships.filter((x) => x.status === "Waiting Truck").length,
  };

  // ---- actions -----------------------------------------------------------
  const go = (next: Screen) => {
    setScreen(next);
    setPage(1);
    setQ("");
    setSel(null);
  };

  const actions: HeaderAction[] = (() => {
    if (isDetail) {
      return [
        { label: "← Back to list", style: BTN_SECONDARY, go: () => setSel(null) },
        { label: "Edit Shipment", style: BTN_PRIMARY, go: () => setToast("Edit mode — draft saved locally") },
      ];
    }
    if (isWorkspace) {
      return [
        { label: "Saved views", style: BTN_SECONDARY, go: () => { setViews(listViews()); setViewName(""); setViewsOpen(true); } },
        { label: "Import from Excel", style: BTN_SECONDARY, go: () => { setImportPreview(null); setImportError(""); setDupChoice({}); setDupCursor(0); setImportOpen(true); } },
        { label: "Export Excel", style: BTN_SECONDARY, go: handleExport },
        { label: "+ ADD JOB", style: BTN_PRIMARY, go: () => setAddCat("CHOOSE") },
      ];
    }
    if (screen === "dashboard") {
      return [{ label: "Export Excel", style: BTN_SECONDARY, go: handleDashboardExport }];
    }
    if (screen === "documents") {
      // The browser-local drawer holds files this session attached but never
      // sent anywhere. It stays reachable while that path exists, next to the
      // register of files that really are in Blob Storage.
      return docs.length > 0
        ? [{ label: `▤ ไฟล์ในเครื่อง ${docs.length}`, style: BTN_SECONDARY, go: () => setDocsOpen((v) => !v) }]
        : [];
    }
    if (screen === "subcontractors") {
      // Adding a supplier is its own screen, with the onboarding statuses on it.
      // Sending people there beats a button that opens a form somewhere else.
      return [{ label: "+ Add Supplier", style: BTN_PRIMARY, go: () => go("vendor") }];
    }
    // No header action on KPI yet. The generic fallback offers an Export Excel
    // that only raises a toast, and a button that claims to export and does not
    // is worse than no button.
    // Screens with no real export yet get no button. The generic fallback below
    // offers an "Export Excel" that only raises a toast, and a button that
    // claims to export and does not is worse than no button.
    if (screen === "kpi" || screen === "monitoring" || screen === "prerun" || screen === "audit") return [];
    if (NOT_BUILT[screen] || OWN_SCREEN[screen]) return [];
    if (screen === "booking") {
      const waiting = (ops?.jobs ?? []).filter((j) => !/complet|delivered|gate-in/i.test(j.status) && !j.licence.trim());
      return [
        {
          label: `Export Excel ${waiting.length}`,
          style: BTN_SECONDARY,
          go: () => {
            if (!waiting.length) { setToast("ไม่มีงานค้างจองรถ"); return; }
            exportJobs(waiting, waiting[0].cat === "EXPORT" ? "EXPORT" : "IMPORT", "TruckBooking");
            setToast(`ส่งออก ${waiting.length} งานที่ยังไม่มีรถ`);
          },
        },
      ];
    }
    if (screen === "rates") {
      return [
        {
          label: "Export Excel",
          style: BTN_SECONDARY,
          go: () => {
            if (!rates) { setToast("ตารางราคายังโหลดไม่เสร็จ"); return; }
            exportRates(rates);
            setToast(`ส่งออก ${rates.lanes.length.toLocaleString()} เส้นทางแล้ว`);
          },
        },
      ];
    }
    return [{ label: "Export Excel", style: BTN_SECONDARY, go: () => setToast("Exporting " + filtered.length + " records to Excel…") }];
  })();

  const showFilters = SCREENS_WITH_FILTERS.indexOf(screen) >= 0 && !isDetail;
  const setFilter = (key: keyof Filters, value: string) => { setF({ ...f, [key]: value }); setPage(1); };
  const filterDefs: FilterDef[] = [
    { label: "DIRECTION", value: f.dir, options: ["All", "Import", "Export"], onChange: (v) => setFilter("dir", v) },
    { label: "CUSTOMER", value: f.cust, options: ["All", ...db.CUST], onChange: (v) => setFilter("cust", v) },
    { label: "SUBCONTRACTOR", value: f.sub, options: ["All", ...db.SUBS], onChange: (v) => setFilter("sub", v) },
    { label: "TRUCK TYPE", value: f.truck, options: ["All", ...db.TRUCKS], onChange: (v) => setFilter("truck", v) },
    { label: "STATUS", value: f.status, options: ["All", ...Object.keys(STATUS)], onChange: (v) => setFilter("status", v) },
    { label: "MONTH", value: f.month, options: ["All", ...MONTHS], onChange: (v) => setFilter("month", v) },
  ];

  // ---- document store ----------------------------------------------------
  const moduleLabel = (META[screen] || ["Module"])[0];
  let recordLabel = "General / module level";
  if (drawer && ops) {
    const j = ops.jobs.find((x) => x.key === drawer);
    if (j) recordLabel = (j.jobCode || j.jobNo || j.abs || "") + " · " + j.customer;
  } else if (sel !== null) {
    const s = db.ships.find((x) => x.id === sel);
    if (s) recordLabel = s.abs + " · " + s.cust;
  }
  const inScopeDocs = docs.filter((d) => d.module === moduleLabel);

  async function ingestDocs(fileList: FileList | null) {
    const list = Array.from(fileList || []);
    if (!list.length) return;
    const added: StoredDoc[] = [];
    for (const file of list) {
      let preview = "";
      if (/^image\//.test(file.type) && file.size < 3_500_000) {
        preview = await readDataUrl(file).catch(() => "");
      }
      const now = new Date();
      added.push({
        id: "DOC" + Date.now() + "-" + Math.round(Math.random() * 9999),
        name: file.name,
        size: fsize(file.size),
        kind: docKind(file),
        type: guessDocType(file.name),
        module: moduleLabel,
        record: recordLabel,
        recordLabel,
        by: me.name,
        at: pad(now.getDate()) + "/" + pad(now.getMonth() + 1) + "/" + now.getFullYear() + " " + nowHM(),
        preview,
        status: "Stored",
      });
    }
    setDocs((prev) => added.concat(prev));
    setDragOver(false);
    setToast(added.length + (added.length > 1 ? " documents" : " document") + " stored to " + moduleLabel);
  }

  const docTotals = ["PDF", "Image", "Excel", "Word", "File"]
    .map((k) => ({ k, n: docs.filter((d) => d.kind === k).length }))
    .filter((x) => x.n > 0);

  // ---- workspace mutations ----------------------------------------------
  function pushAct(job: Job, field: string, oldV: string, newV: string) {
    job.hist = (job.hist || []).concat([{ ts: nowHM(), user: me.name, field, old: oldV || "—", neu: newV || "—" }]);
  }

  /**
   * A booking decision written onto the job.
   *
   * There is no separate booking record — the plan is the record — so choosing a
   * carrier or keying a plate is the same kind of edit the workspace makes, down
   * the same path: normalised, flagged, written to the job's history and saved.
   * A job that gets its truck here leaves the booking queue because the queue is
   * read from the job, not tracked alongside it.
   */
  function assignTruck(job: Job, patch: Partial<Job>) {
    if (!canEditJob(job)) {
      setToast("แก้ไม่ได้ — งานนี้เป็นของ " + job.op);
      return;
    }

    // Replacing a carrier needs a reason; naming the first one does not. There
    // is nothing to explain about filling a blank, and asking anyway is how a
    // team learns to type "update" into every box.
    let reason = "";
    const replacing = !!patch.trucker
      && job.trucker.trim().length > 0
      && patch.trucker.trim() !== job.trucker.trim();

    if (replacing) {
      const typed = window.prompt(
        `เปลี่ยนผู้ขนส่งจาก ${job.trucker} เป็น ${patch.trucker}\n\nระบุเหตุผล (จะบันทึกไว้ในประวัติ)`,
        "",
      );
      if (typed === null) return;
      reason = typed.trim();
      if (!reason) {
        setToast("ต้องระบุเหตุผลในการเปลี่ยนผู้ขนส่ง");
        return;
      }
    }

    const record = job as unknown as Record<string, unknown>;
    let changed = 0;

    for (const [field, value] of Object.entries(patch)) {
      const typed = String(value ?? "").trim();
      const old = String(record[field] ?? "");
      if (!typed || typed === old) continue;

      record[field] = typed;
      normaliseField(record, field);
      pushAct(job, field, old, String(record[field] ?? ""));
      changed++;
    }

    if (!changed) return;

    // Naming a carrier is what moves a job off "Waiting Truck". The later
    // statuses are the operators' to set — this only makes the one step the
    // booking screen is responsible for.
    if (patch.trucker && /waiting truck/i.test(job.status)) {
      const old = job.status;
      job.status = "Truck Confirmed";
      pushAct(job, "status", old, job.status);
    }

    flagJob(job);
    persist([job], reason);
    touch();
  }

  /**
   * Saves an inline edit against the data standard: unambiguous typing is
   * corrected on the spot, anything that would need a guess is kept as typed
   * but flagged, with the required format shown so it can be retyped.
   */
  function saveCell(job: Job, field: keyof Job) {
    setField(job, field, ws.editVal);
    setWs((prev) => ({ ...prev, edit: null }));
  }

  /**
   * Writes one value onto one field, against the data standard.
   *
   * Separate from `saveCell` because a dropdown knows its new value straight
   * away and the text editor keeps it in `ws.editVal`. A select that called
   * `saveCell` would save the *previous* render's value — React state does not
   * update in time — which is a bug that would have looked like the dropdown
   * lagging one choice behind.
   */
  function setField(job: Job, field: keyof Job, value: string) {
    const old = (job[field] as string) || "";
    if (value === old) { touch(); return; }

    (job[field] as unknown as string) = value;

    const record = job as unknown as Record<string, unknown>;
    const fix = normaliseField(record, String(field));
    const saved = (job[field] as string) || "";
    flagJob(job);
    pushAct(job, String(field), old, saved);

    const issue = job.issues.find((i) => i.field === String(field));
    if (fix) {
      setToast("จัดรูปแบบให้แล้ว ✓  " + fix.label + ": " + fix.from + " → " + fix.to);
    } else if (issue) {
      setToast("⚠ " + issue.label + " รูปแบบไม่ถูกต้อง — ต้องเป็น " + issue.expected + " เช่น " + issue.example);
    } else {
      setToast("Saved ✓  " + String(field) + ": " + (old || "—") + " → " + (saved || "—"));
    }
    persist([job]);
    touch();
  }

  /**
   * Bulk edits stop at the ownership line the grid draws: an Operation User
   * changes their own jobs, a supervisor the team's. Anything skipped is
   * reported rather than silently dropped.
   */
  function bulkStatus(keys: string[], value: string) {
    if (!ops) return;
    const chosen = ops.jobs.filter((j) => keys.indexOf(j.key) >= 0);
    const allowed = chosen.filter(canEditJob);
    allowed.forEach((job) => {
      const old = job.status;
      if (old === value) return;
      job.status = value;
      flagJob(job);
      pushAct(job, "Status", old, value);
    });
    const skipped = chosen.length - allowed.length;
    persist(allowed);
    setWs((prev) => ({ ...prev, picked: [] }));
    setToast(
      `เปลี่ยนสถานะ ${allowed.length} งานเป็น ${value}` +
      (skipped ? ` · ข้าม ${skipped} งานของคนอื่น` : ""),
    );
    touch();
  }

  function bulkAssign(keys: string[], owner: string) {
    if (!ops) return;
    if (!able("AssignJobs")) {
      setToast("การมอบหมายงานทำได้เฉพาะระดับหัวหน้างานขึ้นไป");
      return;
    }
    const chosen = ops.jobs.filter((j) => keys.indexOf(j.key) >= 0);
    chosen.forEach((job) => {
      const old = job.op;
      if (old === owner) return;
      job.op = owner;
      job.opId = opIdForName(owner);
      pushAct(job, "Assigned To", old, owner);
    });
    persist(chosen);
    setWs((prev) => ({ ...prev, picked: [] }));
    setToast(`มอบหมาย ${chosen.length} งานให้ ${owner} แล้ว`);
    touch();
  }

  /**
   * Removes jobs from the plan for good — the register row goes with them, so
   * this asks first and, like every other write, stops at the ownership line.
   */
  async function removeJobs(keys: string[]) {
    if (!ops || !keys.length) return;
    const chosen = ops.jobs.filter((j) => keys.indexOf(j.key) >= 0);
    const allowed = chosen.filter(canEditJob);
    if (!allowed.length) {
      setToast("ลบไม่ได้ — งานที่เลือกเป็นของคนอื่นทั้งหมด");
      return;
    }
    const skipped = chosen.length - allowed.length;
    const sample = allowed.slice(0, 3).map((j) => j.jobCode || j.abs || j.jobNo || j.customer).join(", ");
    if (!window.confirm(
      `ลบ ${allowed.length} งานออกจากระบบ?\n\n${sample}${allowed.length > 3 ? " และอีก " + (allowed.length - 3) + " งาน" : ""}` +
      `${skipped ? "\n\n(ข้าม " + skipped + " งานของคนอื่น)" : ""}\n\nลบแล้วกู้คืนไม่ได้`,
    )) return;

    const dropped = new Set(allowed.map((j) => j.key));
    ops.jobs = ops.jobs.filter((j) => !dropped.has(j.key));
    if (drawer && dropped.has(drawer)) setDrawer(null);
    setWs((prev) => ({ ...prev, picked: prev.picked.filter((k) => !dropped.has(k)) }));
    touch();

    const result = await deleteJobs([...dropped], me.full);
    setToast(result.ok
      ? `ลบ ${allowed.length} งานแล้ว` + (skipped ? ` · ข้าม ${skipped} งานของคนอื่น` : "") + ` · เหลือ ${ops.jobs.length} งาน`
      : "ลบออกจากฐานข้อมูลไม่สำเร็จ: " + result.message);
  }

  function changeJobStatus(job: Job, value: string) {
    if (value === "Delayed") { setOpsDelay(job.key); return; }
    const old = job.status;
    job.status = value;
    flagJob(job);
    pushAct(job, "Status", old, value);
    persist([job]);
    setToast("Saved ✓  " + (job.jobCode || job.abs) + " → " + value);
    touch();
  }

  function changeShipStatus(ship: Ship, value: string) {
    if (!(me.role !== "Operation User" || ship.op === me.name)) {
      setToast("View only — this job belongs to " + ship.op);
      return;
    }
    if (value === "Delayed") { setDelayFor(ship.id); return; }
    const old = ship.status;
    ship.status = value;
    ship.lastUpdate = "11 Aug " + nowHM();
    setAudit((prev) => [{ ts: "11 Aug 2026 " + nowHM(), user: me.name, job: ship.abs, field: "Current Status", old, neu: value, ip: "10.20.4.17" }, ...prev]);
    setToast(ship.abs + " → " + value);
    touch();
  }

  // ---- AI document extraction -------------------------------------------
  async function aiRead(fileList: FileList | null) {
    const list = Array.from(fileList || []).slice(0, 4);
    if (!list.length) return;
    setAiBusy(true);
    setAiMsg("Reading " + list.length + " file" + (list.length > 1 ? "s" : "") + " …");
    try {
      const body = new FormData();
      body.append("category", addCat || "IMPORT");
      list.forEach((file) => body.append("files", file));
      const response = await apiFetch("/api/ai-extract", { method: "POST", body });
      const result = await response.json() as { fields?: Record<string, string>; error?: string };
      if (!response.ok) throw new Error(result.error || "Extraction failed");
      const fields = result.fields ?? {};
      const keys = Object.keys(fields);
      setAddForm((prev) => ({ ...prev, ...fields }));
      setAiFields(keys);
      setAiMsg(keys.length
        ? "AI filled " + keys.length + " fields — review the highlighted ones before saving."
        : "No operational fields found in this file.");
      await ingestDocs(fileList);
    } catch (error) {
      setAiMsg("Could not read the file: " + (error instanceof Error ? error.message : String(error)) + " You can still key the job manually.");
    } finally {
      setAiBusy(false);
    }
  }

  // ---- Excel + saved views ----------------------------------------------
  const currentViewState = (): ViewState => ({
    tab: activeTab, cat: ws.cat, cust: ws.cust, trucker: ws.trucker,
    date: ws.date, kpi: ws.kpi, assignee: ws.assignee,
    status: ws.status, type: ws.type, year: ws.year, month: ws.month, q,
  });

  /** Writes the dashboard itself — one sheet per panel — not just a row dump. */
  function handleDashboardExport() {
    // Exports what the screen is showing, period and all.
    const jobs = periodJobs;
    if (!jobs.length) { setToast("ยังไม่มีข้อมูลงานให้ส่งออก"); return; }
    try {
      const scope = activeTab.replace(/\s+/g, "") + "_" + periodLabel(period).replace(/\s+/g, "-");
      const name = exportDashboard(jobs, scope);
      setToast(`ส่งออกแดชบอร์ด ${jobs.length} งานแล้ว · ${name}`);
    } catch (error) {
      setToast("ส่งออกไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  function handleExport() {
    const { jobs, layout } = workspaceView.current;
    if (!jobs.length) { setToast("ไม่มีงานให้ส่งออกในมุมมองนี้"); return; }
    try {
      const name = exportJobs(jobs, layout, `${layout}_${activeTab.replace(/\s+/g, "")}`);
      setToast(`ส่งออก ${jobs.length} งานแล้ว · ${name}`);
    } catch (error) {
      setToast("ส่งออกไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  function closeImport() {
    setImportOpen(false);
    setImportPreview(null);
    setImportError("");
    setDupChoice({});
    setDupCursor(0);
  }

  async function readImportFile(file: File | undefined) {
    if (!file) return;
    setImportBusy(true);
    setImportError("");
    setImportPreview(null);
    setDupChoice({});
    setDupCursor(0);
    try {
      const preview = await parseWorkbook(file, me.name, ops?.jobs ?? []);
      setImportPreview(preview);
      // A duplicate that changes nothing has no decision in it — skipping and
      // overwriting land on the same job — so it starts answered and the
      // operator is only asked about rows that actually differ.
      const seeded: Record<string, DupDecision> = {};
      preview.dups.forEach((dup) => { if (!dup.diffs.length) seeded[dup.key] = "skip"; });
      setDupChoice(seeded);
      setDupCursor(Math.max(0, preview.dups.findIndex((dup) => dup.diffs.length)));
    } catch (error) {
      setImportError("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setImportBusy(false);
    }
  }

  /**
   * Applies the per-row decisions: skipped rows are dropped, overwrites are
   * written field by field onto the job already on the board (with the change
   * history to match), and everything else comes in as a new job.
   */
  function confirmImport() {
    if (!ops || !importPreview?.jobs.length) return;
    const { jobs: parsed, dups, fileName } = importPreview;
    const matched = new Map(dups.map((dup) => [dup.key, dup]));

    const fresh: Job[] = [];
    let overwritten = 0;
    let skipped = 0;

    for (const job of parsed) {
      const dup = matched.get(job.key);
      // Undecided cannot happen — the modal blocks confirm until every row is
      // answered — but skipping is the safe reading if it ever does.
      const choice = dup ? dupChoice[dup.key] ?? "skip" : "new";

      if (dup && choice === "skip") { skipped++; continue; }

      if (dup && choice === "overwrite") {
        // The modal does not offer overwrite on someone else's job; this is the
        // backstop so a stale decision cannot write across ownership.
        if (!canEditJob(dup.existing)) { skipped++; continue; }
        const target = dup.existing as unknown as Record<string, unknown>;
        dup.diffs.forEach((d) => {
          target[d.field] = d.to;
          pushAct(dup.existing, d.label, d.from, d.to);
        });
        flagJob(dup.existing);
        overwritten++;
        continue;
      }

      job.hist = [{ ts: nowHM(), user: me.name, field: "นำเข้าจาก Excel", old: "—", neu: fileName }];
      flagJob(job);
      fresh.push(job);
    }

    if (fresh.length) ops.jobs.unshift(...fresh);
    // Both sides of the import land in the database: the new jobs and the
    // existing ones the operator chose to overwrite.
    persist(fresh.concat(dups.filter((d) => dupChoice[d.key] === "overwrite").map((d) => d.existing)));
    const errors = fresh.filter((j) => j.issues.some((i) => i.severity === "error")).length;
    closeImport();
    setTab("PENDING");
    setWs((prev) => ({ ...prev, cat: "ALL", date: "ALL", kpi: "All", assignee: "All Team", status: "ALL", type: "ALL", year: "ALL", month: "ALL" }));
    setPage(1);
    setToast(
      `นำเข้า ${fresh.length} งานใหม่` +
      (overwritten ? ` · ทับของเดิม ${overwritten}` : "") +
      (skipped ? ` · ข้าม ${skipped}` : "") +
      (errors ? ` · ${errors} งานรูปแบบผิด กด FORMAT ERROR เพื่อแก้` : ""),
    );
    touch();
  }

  // ---- add / assign ------------------------------------------------------
  function saveAddJob() {
    if (!ops) return;
    const cat = addCat || "IMPORT";
    const required = cat === "DELIVERY" ? ["date", "customer", "wh"] : ["date", "customer", "trucker"];
    if (!required.every((k) => !!addForm[k])) {
      setToast("Complete the required fields first · กรอกข้อมูลที่จำเป็นให้ครบ");
      return;
    }
    const key = "N" + Date.now();
    const owner = addForm.op || me.name;
    const job: Job = {
      key, id: key, cat, op: owner, opId: opIdForName(owner) || me.opId,
      date: "", customer: "", trucker: cat === "DELIVERY" ? "LESCHACO DTT" : "",
      jobCode: "", abs: "", booking: "", product: "", fclLcl: "", agent: "", destination: "",
      plant: "", planTime: "", type: "", cyYard: "", returnLoc: "", emptyReturn: "", weight: "",
      container: "", seal: "", tare: "", licence: "", driver: "", contact: "", arrDate: "", arrTime: "",
      closingDate: "", closingTime: "", reason: "", remark: "", ot: "", pickupPlan: "", cs: "",
      incident: "", freightType: "",
      status: cat === "DELIVERY" ? "Scheduled" : "Waiting Truck",
      hist: [], flags: [], action: true, prio: "MEDIUM", issues: [], fixes: [],
      ...addForm,
    };
    if (cat === "DELIVERY") {
      job.jobCode = job.jobNo || "";
      job.destination = (job.province || "") + " " + (job.zip || "");
      job.type = [job.v4 && job.v4 + "×4W", job.v6 && job.v6 + "×6W", job.v10 && job.v10 + "×10W", job.vtr && job.vtr + "×TRAILER"].filter(Boolean).join(" ");
      job.weight = job.kgs || "";
    }
    job.hist = [{ ts: nowHM(), user: me.name, field: "Job created", old: "—", neu: job.jobCode || job.jobNo || job.abs || job.customer }];
    flagJob(job);
    ops.jobs.unshift(job);
    persist([job]);
    setAddCat(null);
    setAddForm({});
    setAiFields([]);
    setAiMsg("");
    setWs((prev) => ({ ...prev, cat, date: "ALL" }));
    setTab("MY JOBS");
    setPage(1);
    setDrawer(key);
    setToast("Job created — " + (job.jobCode || job.jobNo || job.customer) + " assigned to " + job.op);
    touch();
  }

  function saveDelay() {
    if (!delay.reason || !delay.party || !delay.eta || !delay.next) {
      setToast("Delay category, responsible party and times are required");
      return;
    }
    const job = opsDelay && ops ? ops.jobs.find((x) => x.key === opsDelay) : null;
    if (job) {
      const old = job.status;
      job.status = "Delayed";
      job.reason = delay.reason + " (" + delay.party + ")";
      job.remark = delay.note || job.remark;
      flagJob(job);
      pushAct(job, "Status", old, "Delayed — " + delay.reason);
      persist([job]);
      setOpsDelay(null);
      setDelay(EMPTY_DELAY);
      setToast("Delay recorded for " + (job.jobCode || job.abs));
      touch();
      return;
    }
    const ship = delayFor !== null ? db.ships.find((x) => x.id === delayFor) : null;
    if (!ship) return;
    const old = ship.status;
    ship.status = "Delayed";
    ship.delay = delay.reason;
    ship.respParty = delay.party;
    ship.next = delay.next;
    ship.lastUpdate = "11 Aug " + nowHM();
    setAudit((prev) => [{ ts: "11 Aug 2026 " + nowHM(), user: me.name, job: ship.abs, field: "Current Status", old, neu: "Delayed (" + delay.reason + ")", ip: "10.20.4.17" }, ...prev]);
    setDelayFor(null);
    setDelay(EMPTY_DELAY);
    setToast(ship.abs + " marked Delayed — reason recorded");
    touch();
  }

  // ---- table -------------------------------------------------------------
  const table = useMemo(() => {
    if (isDetail || isWorkspace || OWN_SCREEN[screen]) return null;
    return buildTable({
      screen, tab: activeTab, db, filtered, q, page, per: prefs.perPage,
      onSort: () => setToast("Sorted"),
      selectShip: (id) => setSel(id),
      toast: setToast,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [screen, activeTab, db, filtered, q, page, f, prefs.perPage, isDetail, isWorkspace, revision]);

  const selectedShip = sel !== null ? db.ships.find((x) => x.id === sel) ?? null : null;
  const drawerJob = drawer && ops ? ops.jobs.find((x) => x.key === drawer) ?? null : null;
  const assignJob = assignFor && ops ? ops.jobs.find((x) => x.key === assignFor) ?? null : null;
  const delayJob = opsDelay && ops ? ops.jobs.find((x) => x.key === opsDelay) ?? null : null;
  const delayShip = delayFor !== null ? db.ships.find((x) => x.id === delayFor) ?? null : null;

  const delayFields: Field[] = [
    { label: "DELAY CATEGORY *", kind: "select", value: delay.reason, options: ["", "Port Congestion", "Traffic Congestion", "Truck Breakdown", "Truck Shortage", "Driver Issue", "Customer Waiting", "Warehouse Waiting", "Documentation", "Customs", "Container Issue", "Accident", "Other"], onChange: (v) => setDelay({ ...delay, reason: v }) },
    { label: "RESPONSIBLE PARTY *", kind: "select", value: delay.party, options: ["", "Transporter", "Port", "Customer", "LESCHACO", "Customs", "Warehouse", "Other"], onChange: (v) => setDelay({ ...delay, party: v }) },
    { label: "DELAY START TIME", kind: "text", value: delay.start, ph: "e.g. 13:20", onChange: (v) => setDelay({ ...delay, start: v }) },
    { label: "EXPECTED RESOLUTION TIME *", kind: "text", value: delay.eta, ph: "e.g. 16:30", onChange: (v) => setDelay({ ...delay, eta: v }) },
    { label: "NEXT UPDATE TIME *", kind: "text", value: delay.next, ph: "e.g. 14:00", onChange: (v) => setDelay({ ...delay, next: v }) },
    { label: "REMARK", kind: "text", value: delay.note, ph: "What has been done so far", onChange: (v) => setDelay({ ...delay, note: v }) },
  ];

  const onDragOver = (e: DragEvent<HTMLLabelElement>) => { e.preventDefault(); if (!dragOver) setDragOver(true); };
  const onDragLeave = (e: DragEvent<HTMLLabelElement>) => { e.preventDefault(); setDragOver(false); };

  if (!auth) {
    return (
      <Login
        username={loginU}
        password={loginP}
        error={loginErr}
        onUsername={setLoginU}
        onPassword={setLoginP}
        onSignIn={() => {
          const account = ACCOUNTS.find((a) => a.user === loginU.trim().toLowerCase());
          if (!account || !loginP) {
            setLoginErr("Invalid username or password · ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");
            return;
          }
          setAuth(account);
          setLoginErr("");
          setScreen("workspace");
          setTab("MY JOBS");
          setWs(EMPTY_WS);
          setToast("Signed in as " + account.full + " · " + account.role);
        }}
      />
    );
  }

  const wsState: WsState = { ...ws, tab: activeTab, q, page };

  return (
    <>
      <Chrome
        screen={screen}
        onNavigate={go}
        navCounts={navCounts}
        collapsed={collapsed}
        onToggleSidebar={() => setCollapsed((c) => !c)}
        gq={gq}
        onGq={(v) => { setGq(v); setPage(1); }}
        searchGroups={searchGroups}
        searchOpen={searchOpen}
        onSearchOpen={setSearchOpen}
        onSearchHit={openSearchHit}
        userName={profile.full || me.full}
        userRole={me.role}
        userInit={(profile.init || me.init).toUpperCase()}
        userAvatar={profile.avatar}
        onLogout={() => {
          if (signOutHref) { window.location.href = signOutHref; return; }
          setAuth(null);
          setLoginP("");
        }}
        onProfile={() => { setProfileOpen((v) => !v); setNotif(false); }}
        onSettings={() => { setSettingsOpen(true); setProfileOpen(false); }}
        alertCount={String(alerts.length)}
        alertTone={criticalAlerts ? "red" : alerts.length ? "amber" : "blue"}
        onToggleNotif={() => { setNotif((v) => !v); setProfileOpen(false); }}
        crumb={(isDetail && selectedShip ? "SCMOS / " + (META[screen]?.[0] ?? "") + " / " + selectedShip.abs
          : "SCMOS / " + meta[0]).toUpperCase()}
        title={meta[0]}
        titleTh={meta[1]}
        blurb={meta[2]}
        actions={actions}
        tabs={tabs}
        filters={showFilters ? { defs: filterDefs, q, onQ: (v) => { setQ(v); setPage(1); }, onReset: () => { setF(EMPTY_FILTERS); setQ(""); setPage(1); setToast("Filters reset"); } } : null}
      >
        {isDetail && selectedShip && (
          <Detail
            ship={selectedShip}
            auth={me}
            audit={audit}
            onStatus={(v) => changeShipStatus(selectedShip, v)}
            onEdit={() => setToast(
              me.role !== "Operation User" || selectedShip.op === me.name
                ? "Edit mode — draft saved locally"
                : "View only — this job belongs to " + selectedShip.op,
            )}
          />
        )}

        {!isDetail && (
          <>
            {/* The capability list never arrived, so nothing is editable. Said
                out loud, with a way to try again: a grid that silently refuses
                every keystroke is the most confusing state this app can be in,
                because the rows still say MY JOB. */}
            {identityState === "failed" && (
              <div style={css("background:#FEF6F5;border:1px solid #F3C9C4;border-left:3px solid #B42318;border-radius:5px;padding:13px 16px;margin-bottom:14px;display:flex;gap:14px;align-items:center;flex-wrap:wrap")}>
                <div style={css("flex:1;min-width:260px")}>
                  <div style={css("font-size:13px;font-weight:650;color:#B42318;margin-bottom:3px")}>
                    โหลดสิทธิ์การใช้งานไม่ได้ — ตอนนี้แก้ไขอะไรไม่ได้
                  </div>
                  <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.6")}>
                    ติดต่อ API ไม่ได้ ระบบจึงยังไม่รู้ว่าคุณมีสิทธิ์ทำอะไรบ้าง และไม่เดาให้
                    ข้อมูลที่เห็นอยู่อ่านจากไฟล์แผนสำรอง — กำลังลองใหม่ให้อัตโนมัติ
                  </div>
                </div>
                <button onClick={() => { setIdentityState("loading"); setIdentityAttempt((n) => n + 1); }}
                  style={css("height:31px;padding:0 14px;border:1px solid #B42318;background:#fff;color:#B42318;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  ลองใหม่
                </button>
              </div>
            )}

            {/* Signed in, but the staff directory has never heard of them. Every
                job will look like somebody else's and nothing will be editable,
                which is correct and indistinguishable from a broken system
                unless it is said out loud. */}
            {identity && !identity.known && (
              <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:5px;padding:13px 16px;margin-bottom:14px")}>
                <div style={css("font-size:13px;font-weight:650;color:#B45309;margin-bottom:3px")}>
                  บัญชีนี้ยังไม่อยู่ในทะเบียนพนักงาน
                </div>
                <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.6")}>
                  ลงชื่อเข้าใช้สำเร็จแล้ว แต่ระบบยังไม่รู้ว่าคุณเป็นใครในแผน จึงไม่มีงานไหนนับเป็นของคุณ
                  และแก้ไขอะไรไม่ได้ — สิทธิ์ปัจจุบันคือ <b>{me.role || "อ่านอย่างเดียว"}</b>
                  <br />
                  ให้ผู้ดูแลระบบเพิ่มบัญชีนี้ใน <code style={css("font-family:ui-monospace,monospace")}>StaffDirectory</code> หรือ
                  ตั้งบทบาทใน <code style={css("font-family:ui-monospace,monospace")}>Auth:Roles</code> ของ API
                </div>
              </div>
            )}

            {/* TODAY comes from the API rather than the in-browser jobs, so it
                bypasses the period bar the other three tabs share — "today" is
                not a filter over a chosen period, it is the day itself. */}
            {screen === "dashboard" && activeTab === "TODAY" && (
              <Today onDrill={(next) => go(next as Screen)} />
            )}

            {screen === "dashboard" && activeTab !== "TODAY" && (
              <Dashboard
                db={db}
                filtered={filtered}
                jobs={periodJobs}
                allJobs={ops?.jobs ?? []}
                period={period}
                onPeriod={setPeriod}
                loaded={!!ops}
                tab={activeTab}
                // Every figure on the dashboard is a way into the workspace:
                // clicking one lands on the same jobs it counted.
                onDrill={openTarget}
              />
            )}

            {isWorkspace && (ops
              ? <Workspace
                  ops={ops}
                  me={me}
                  ws={wsState}
                  set={(patch) => {
                    if (patch.tab !== undefined) setTab(patch.tab);
                    if (patch.q !== undefined) setQ(patch.q);
                    if (patch.page !== undefined) setPage(patch.page);
                    const rest = { ...patch };
                    delete rest.tab; delete rest.q; delete rest.page;
                    if (Object.keys(rest).length) setWs((prev) => ({ ...prev, ...rest }));
                    if (patch.page === undefined && (patch.cat || patch.cust || patch.trucker || patch.date || patch.kpi || patch.assignee)) setPage(1);
                  }}
                  onDrawer={setDrawer}
                  onDelay={setOpsDelay}
                  onSaveCell={saveCell}
                  onSetField={setField}
                  onStatusChange={changeJobStatus}
                  onSort={() => undefined}
                  canEdit={canEditJob}
                  canAssign={able("AssignJobs")}
                  per={prefs.perPage}
                  sync={sync}
                  panels={prefs.panels}
                  onPanel={(key) => setPrefs((prev) => savePrefs({ ...prev, panels: { ...prev.panels, [key]: !prev.panels[key] } }))}
                  onBulkStatus={bulkStatus}
                  onBulkAssign={bulkAssign}
                  onBulkDelete={(keys) => { void removeJobs(keys); }}
                  onView={(v) => { workspaceView.current = v; }}
                />
              : <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  Loading operation data…
                </div>)}

            {screen === "kpi" && (
              <Kpi
                period={period}
                onDrill={(next) => setScreen(next as Screen)}
                // Every figure on the KPI screen is a way into the jobs behind
                // it. A rate you cannot open is a rate you cannot act on.
                onOpenJobs={(filter) => {
                  openTarget({ tab: "PENDING", kpi: filter.kpi, status: filter.status });
                  if (filter.trucker) setWs((prev) => ({ ...prev, trucker: filter.trucker! }));
                }}
              />
            )}

            {screen === "monitoring" && (ops
              ? <Monitoring jobs={periodJobs} canEdit={canEditJob} onToast={setToast} />
              : <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  กำลังโหลดแผนงาน…
                </div>)}

            {screen === "rates" && (
              <Rates book={rates} error={ratesError} diesel={diesel} onDiesel={setDiesel} onToast={setToast} />
            )}

            {screen === "booking" && (ops
              ? <Booking
                  jobs={periodJobs}
                  book={rates}
                  diesel={diesel}
                  canEdit={canEditJob}
                  onAssign={assignTruck}
                  onOpen={(job) => {
                    setScreen("workspace");
                    setWs((prev) => ({ ...prev, cat: job.cat, date: "ALL" }));
                    setTab("PENDING");
                    setDrawer(job.key);
                  }}
                  onToast={setToast}
                />
              : <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  กำลังโหลดแผนงาน…
                </div>)}
            {screen === "capacity" && (
              <CapacityBoard canEdit={able("EditOwnJobs")} onToast={setToast} />
            )}
            {screen === "documents" && <Documents canReview={able("ApproveRetention")} />}
            {screen === "admin" && <Administration />}
            {screen === "docverify" && (
              <Verification canUpload={able("UploadDocuments")} onToast={setToast} />
            )}
            {screen === "billing" && <BillingAging filtered={filtered} />}
            {screen === "reports" && <Reports toast={setToast} />}

            {/* Screens the new menu introduces. Each says what the backend can
                already do and what is missing, rather than showing invented
                figures — a screen full of plausible demo numbers is how a
                system starts being trusted for things it cannot do. */}
            {screen === "prerun" && <PreRun canEdit={(job) => canEditJob(job)} jobs={ops?.jobs ?? []} onToast={setToast} />}
            {screen === "audit" && <Audit canView={isSupervisor} />}

            {/* Supplier register, CAR/PAR and the assistant all read the API
                rather than the demo file. Incident and CAR/PAR are one register
                in the database — a case carries its kind — so both menu names
                open the same cases rather than two half-registers. */}
            {screen === "subcontractors" && <Suppliers canManage={isSupervisor} onToast={setToast} />}
            {(screen === "incident" || screen === "carpar") && <Incidents onToast={setToast} />}
            {screen === "assistant" && (
              <Assistant canApprove={isSupervisor} onToast={setToast}
                onOpenJob={(key) => { openTarget({ tab: "PENDING" }); setDrawer(key); }} />
            )}
            {screen === "vendor" && <Vendor canManage={isSupervisor} onToast={setToast} />}
            {screen === "evaluation" && <Evaluation canManage={isSupervisor} onToast={setToast} />}
            {screen === "quotation" && <Quotation diesel={diesel} onDiesel={setDiesel} onToast={setToast} />}

            {NOT_BUILT[screen] && <NotBuilt detail={NOT_BUILT[screen]} />}

            {table && <DataTable model={table} onPage={setPage} onTool={(label) => setToast(label === "Export Excel" ? "Exported " + table.total + " rows" : label + " — coming from the column chooser")} />}
          </>
        )}
      </Chrome>

      {notif && (
        <Notifications
          alerts={alerts}
          scope={ops?.jobs.length ?? 0}
          onOpen={openTarget}
          onClose={() => setNotif(false)}
        />
      )}

      {profileOpen && (
        <ProfileMenu
          me={me}
          profile={profile}
          stats={myStats}
          onOpen={openTarget}
          onSettings={() => { setProfileOpen(false); setSettingsOpen(true); }}
          onLogout={() => {
            setProfileOpen(false);
            if (signOutHref) { window.location.href = signOutHref; return; }
            setAuth(null);
            setLoginP("");
          }}
          onClose={() => setProfileOpen(false)}
        />
      )}

      {settingsOpen && (
        <SettingsModal
          me={me}
          profile={profile}
          onProfile={updateProfile}
          onAvatar={pickAvatar}
          prefs={prefs}
          onChange={updatePrefs}
          canReload={able("AdministerData")}
          onReloadPlan={reloadFromPlanFile}
          onCleanup={runCleanup}
          onDuplicates={openDuplicates}
          onClose={() => setSettingsOpen(false)}
        />
      )}

      {cleanupReport && (
        <CleanupReportModal
          report={cleanupReport}
          saving={dataBusy}
          onClose={() => setCleanupReport(null)}
        />
      )}

      {dupGroups && (
        <DuplicatesModal
          groups={dupGroups}
          busy={dataBusy}
          onMerge={(group) => { void mergeGroups([group]); }}
          onMergeAll={() => { void mergeGroups(dupGroups.filter((g) => g.reUploaded && g.statuses.length <= 1 && g.owners.length <= 1)); }}
          onOpenJob={(key) => { setDupGroups(null); openTarget({ tab: "PENDING" }); setDrawer(key); }}
          onClose={() => setDupGroups(null)}
        />
      )}

      {docsOpen && (
        <DocsDrawer
          scopeLabel={moduleLabel + (recordLabel !== "General / module level" ? " · " + recordLabel : "")}
          docs={inScopeDocs}
          allCount={docs.length}
          totals={docTotals}
          dragOver={dragOver}
          onClose={() => setDocsOpen(false)}
          onInput={(e: ChangeEvent<HTMLInputElement>) => { ingestDocs(e.target.files); e.target.value = ""; }}
          onDrop={(e) => { e.preventDefault(); ingestDocs(e.dataTransfer.files); }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onRemove={(id) => { setDocs((prev) => prev.filter((d) => d.id !== id)); setToast("Document removed"); }}
        />
      )}

      {(delayJob || delayShip) && (
        <DelayModal
          reference={delayJob
            ? (delayJob.jobCode || delayJob.abs) + " · " + delayJob.customer + " · " + delayJob.trucker
            : delayShip ? delayShip.abs + " · " + delayShip.cust + " · " + delayShip.sub : ""}
          fields={delayFields}
          onCancel={() => { setDelayFor(null); setOpsDelay(null); }}
          onSave={saveDelay}
        />
      )}

      {drawerJob && (
        <JobDrawer
          job={drawerJob}
          mine={owns(drawerJob)}
          canEdit={canEditJob(drawerJob)}
          onClose={() => setDrawer(null)}
          onEdit={() => {
            if (!canEditJob(drawerJob)) {
              setToast("View only — handled by " + drawerJob.op);
              return;
            }
            setDrawer(null);
            setToast("Click any highlighted cell in the row to edit");
          }}
          onDuplicate={() => {
            if (!ops) return;
            const copy: Job = {
              ...drawerJob, key: "X" + Date.now(), id: "X" + Date.now(),
              container: "", seal: "", licence: "", driver: "", contact: "", arrDate: "", arrTime: "",
              status: drawerJob.cat === "DELIVERY" ? "Scheduled" : "Waiting Truck", hist: [],
            };
            flagJob(copy);
            ops.jobs.unshift(copy);
            persist([copy]);
            setDrawer(copy.key);
            setToast("Job duplicated — container and driver cleared");
            touch();
          }}
          onReassign={() => {
            if (!able("AssignJobs")) {
              setToast("Only Supervisor and above can reassign jobs");
              return;
            }
            setAssignFor(drawerJob.key);
          }}
          onDelete={() => { void removeJobs([drawerJob.key]); }}
        />
      )}

      {assignJob && ops && (
        <AssignModal
          reference={(assignJob.jobCode || assignJob.jobNo || assignJob.abs) + " · " + assignJob.customer}
          current={assignJob.op}
          operators={ops.masters.operators}
          loads={ops.masters.operators.reduce<Record<string, number>>((acc, name) => {
            acc[name] = ops.jobs.filter((j) => j.op === name).length;
            return acc;
          }, {})}
          onPick={(name) => {
            const old = assignJob.op;
            assignJob.op = name;
            assignJob.opId = opIdForName(name);
            pushAct(assignJob, "Assigned Operator", old, name);
            persist([assignJob]);
            setAssignFor(null);
            setToast("Reassigned " + (assignJob.jobCode || assignJob.jobNo) + ": " + old + " → " + name);
            touch();
          }}
          onClose={() => setAssignFor(null)}
        />
      )}

      {addCat && ops && (
        <AddJobModal
          cat={addCat}
          masters={ops.masters}
          form={addForm}
          aiFields={aiFields}
          aiBusy={aiBusy}
          aiMessage={aiMsg}
          dragOver={dragOver}
          onChoose={(cat) => {
            setAddCat(cat);
            setAddForm({ cat, op: me.name, status: cat === "DELIVERY" ? "Scheduled" : "Waiting Truck" });
            setAiFields([]);
            setAiMsg("");
          }}
          onField={(key, value) => setAddForm((prev) => ({ ...prev, [key]: value }))}
          onAiInput={(e) => { aiRead(e.target.files); e.target.value = ""; }}
          onAiDrop={(e) => { e.preventDefault(); setDragOver(false); aiRead(e.dataTransfer.files); }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onClose={() => { setAddCat(null); setAiFields([]); setAiMsg(""); }}
          onSave={saveAddJob}
        />
      )}

      {importOpen && (
        <ImportModal
          preview={importPreview}
          busy={importBusy}
          error={importError}
          dragOver={dragOver}
          decisions={dupChoice}
          canOverwrite={(dup) => canEditJob(dup.existing)}
          dupCursor={dupCursor}
          onDupCursor={setDupCursor}
          onDecide={(keys, decision) => setDupChoice((prev) => {
            const next = { ...prev };
            keys.forEach((key) => { next[key] = decision; });
            return next;
          })}
          onFile={(e) => { readImportFile(e.target.files?.[0]); e.target.value = ""; }}
          onDrop={(e) => { e.preventDefault(); setDragOver(false); readImportFile(e.dataTransfer.files?.[0]); }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onConfirm={confirmImport}
          onClose={closeImport}
        />
      )}

      {viewsOpen && (
        <SavedViewsModal
          views={views}
          current={describeView(currentViewState())}
          name={viewName}
          onName={setViewName}
          onSave={() => {
            if (!viewName.trim()) { setToast("ตั้งชื่อมุมมองก่อนบันทึก"); return; }
            setViews(saveView(viewName, currentViewState()));
            setToast(`บันทึกมุมมอง “${viewName.trim()}” แล้ว`);
            setViewName("");
          }}
          onApply={(view) => {
            setTab(view.state.tab);
            setQ(view.state.q);
            setPage(1);
            setWs((prev) => ({
              ...prev,
              cat: view.state.cat, cust: view.state.cust, trucker: view.state.trucker,
              date: view.state.date, kpi: view.state.kpi, assignee: view.state.assignee,
              // Views saved before the process board carry none of these.
              status: view.state.status ?? "ALL",
              type: view.state.type ?? "ALL",
              year: view.state.year ?? "ALL",
              month: view.state.month ?? "ALL",
            }));
            setViewsOpen(false);
            setToast(`ใช้มุมมอง “${view.name}”`);
          }}
          onDelete={(name) => { setViews(deleteView(name)); setToast(`ลบมุมมอง “${name}” แล้ว`); }}
          onClose={() => setViewsOpen(false)}
        />
      )}

      <Toast message={toast} />
    </>
  );
}

/* ------------------------------------------------------------ doc helpers */

function readDataUrl(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}

function fsize(n: number) {
  return n > 1048576 ? (n / 1048576).toFixed(1) + " MB" : Math.max(1, Math.round(n / 1024)) + " KB";
}

function docKind(file: File) {
  const name = (file.name || "").toLowerCase();
  const type = file.type || "";
  if (/pdf/.test(type) || /\.pdf$/.test(name)) return "PDF";
  if (/^image\//.test(type)) return "Image";
  if (/sheet|excel|\.xlsx?$/.test(type + name)) return "Excel";
  if (/word|\.docx?$/.test(type + name)) return "Word";
  return "File";
}

function guessDocType(filename: string) {
  const n = (filename || "").toLowerCase();
  if (/do|delivery.?order/.test(n)) return "Delivery Order";
  if (/bl|b\/l|bill.?of.?lading/.test(n)) return "Bill of Lading";
  if (/inv/.test(n)) return "Invoice";
  if (/pack/.test(n)) return "Packing List";
  if (/book/.test(n)) return "Booking Confirmation";
  if (/pod|receipt|sign/.test(n)) return "POD";
  if (/msds|sds/.test(n)) return "MSDS / SDS";
  if (/e.?card|gate/.test(n)) return "E-Card / Gate Pass";
  if (/custom|entry|release/.test(n)) return "Customs Release";
  if (/photo|img|jpg|jpeg|png/.test(n)) return "Photo Evidence";
  return "Other Document";
}
