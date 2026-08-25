"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ChangeEvent, DragEvent } from "react";

import { apiFetch, setDevUser } from "./scmos/api";
import { Chrome, type FilterDef, type HeaderAction, type TabItem } from "./scmos/Chrome";
import { DataTable } from "./scmos/DataTable";
import { buildDb, type Ship } from "./scmos/demo";
import { ACCOUNTS, CARRIER_SCREENS, HEADINGS, META, opIdForName, SCREENS_WITH_FILTERS, SUB_NAV, TAB_DEFS, type Account, type Screen } from "./scmos/nav";
import { prep, flagJob, type Job, type Ops, type RawOps } from "./scmos/ops";
import { bookingStats } from "./scmos/booking";
import { DEFAULT_STATUS, normaliseField, type Fix } from "./scmos/standard";
import { exportDashboard, exportJobs, exportRates, parseWorkbook, type DupDecision, type ImportPreview } from "./scmos/excel";
import { deleteView, describeView, listViews, saveView, type SavedView, type ViewState } from "./scmos/views";
import { clearJobs, deleteJobs, loadJobs, loadJobsPage, loadPlanFile, saveJobs } from "./scmos/store";
import { SaveQueue } from "./scmos/saveQueue";
import { forget, pageCacheKey, readCachedPage, writeCachedPage } from "./scmos/pageCache";
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
import { Postpone } from "./scmos/screens/Postpone";
import { Chemours } from "./scmos/screens/Chemours";
import { OperationalIssues } from "./scmos/screens/OperationalIssues";
import type { NewIssue } from "./scmos/issues";
import { JobRotation } from "./scmos/screens/JobRotation";
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
  capacity: true, documents: true, admin: true, docverify: true, abs: true, loreal: true, carrier: true, training: true,
};
import type { RateBook } from "./scmos/rates";
import { Detail, type AuditEntry } from "./scmos/screens/Detail";
import { BillingAging, Reports } from "./scmos/screens/Panels";
import { Booking } from "./scmos/screens/Booking";
import { Workspace, workspaceTabCounts, type WorkspaceServerPage, type WsState } from "./scmos/screens/Workspace";

import { Abs } from "./scmos/screens/Abs";
import { Loreal } from "./scmos/screens/Loreal";
import { CarrierPortal } from "./scmos/screens/CarrierPortal";
import { Training } from "./scmos/screens/Training";
import { Login } from "./scmos/overlays/Login";
import { DelayModal, DocsDrawer, Notifications, ProfileMenu, SettingsModal, Toast, type Field, type StoredDoc } from "./scmos/overlays/Overlays";
import type { Alert, WsTarget } from "./scmos/alerts";
import { globalSearch, type SearchHit } from "./scmos/search";
import { DEFAULT_PREFS, EMPTY_PROFILE, loadPrefs, loadProfile, readAvatar, savePrefs, saveProfile, type Prefs, type Profile } from "./scmos/settings";
import { AddJobModal, AssignModal, JobChangeModal, JobDrawer } from "./scmos/overlays/WorkspaceOverlays";

const EMPTY_FILTERS: Filters = { dir: "All", cust: "All", sub: "All", truck: "All", status: "All", month: "All" };
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug"];


const EMPTY_WS = {
  cat: "ALL", cust: "ALL", trucker: "ALL", date: "ALL", kpi: "All", assignee: "All Team",
  status: "ALL", type: "ALL", year: "ALL", month: "ALL", from: "", to: "",
  edit: null as { key: string; field: string } | null, editVal: "",
  sort: null as { key: string; dir: "asc" | "desc" } | null,
  picked: [] as string[],
};

const EMPTY_DELAY = { reason: "", party: "", start: "", eta: "", next: "", note: "" };

/**
 * How long to wait before each further attempt at the first load, in ms.
 *
 * Sized against the real thing rather than a guess: a paused database was timed
 * at 110 seconds to come back, and the attempt that met it head-on sat unanswered
 * for two minutes before that. So the ladder holds 170 seconds of waiting across
 * seven attempts, and each attempt's own request time is on top of it — a failed
 * one during a resume tends to hang rather than refuse, which buys more room
 * again. Long enough for the worst resume seen; short enough that somebody whose
 * network is genuinely down is told so rather than left watching.
 *
 * Out here rather than in the component so the effect that reads it needs no
 * dependency on it.
 */
const WAKE_DELAYS = [0, 5_000, 10_000, 20_000, 30_000, 45_000, 60_000];

/** Screens whose tools genuinely need the whole register in the browser. */
const REGISTER_SCREENS = new Set<Screen>([
  "myjob", "monitoring", "booking", "training", "loreal", "chemours", "prerun", "postpone",
  // The issue screen needs it to suggest job references and to say which
  // shipment an issue is about; Reports needs it for Delay Analysis.
  "issues", "reports",
  // Administration needs it only for the clear panel, which offers the months
  // a person actually has work in. Counting those off the register is what
  // stops an administrator picking a month that would delete nothing and
  // being told it worked. It is a rarely opened screen and the read is
  // cached, so the cost lands once rather than on anybody's hot path.
  "admin",
]);

/** The Chemours tab that hosts the Domestic grid rather than a report. */
const DOMESTIC_TAB = "งาน Domestic";

function selectedTab(screen: Screen, tab: string): string {
  const tabs = TAB_DEFS[screen] ?? [];
  return tab && tabs.includes(tab) ? tab : tabs[0] ?? "";
}

function screenNeedsRegister(screen: Screen, tab: string): boolean {
  // TODAY has its own compact API answer. The other dashboard tabs calculate
  // charts over arbitrary periods and therefore still need the full register.
  if (screen === "dashboard") return selectedTab(screen, tab) !== "TODAY";
  return REGISTER_SCREENS.has(screen);
}

type Props = {
  /** Verified identity from App Service Web App Login; null in local dev. */
  initialUser: Account | null;
  /** Where the platform's sign-out lives, when there is a real session to end. */
  signOutHref: string | null;
  /** Whether the passwordless demo gate is available. Never true in a deployed build. */
  demo: boolean;
};

export function SCMOSApp({ initialUser, signOutHref, demo }: Props) {
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
  /**
   * Falling back to the first demo account was fine while this was a demo. It
   * is not fine in front of a team: when the platform sends no identity, every
   * one of them is greeted as Watsana, sees her name in the header, and reads
   * the empty screen behind it as the system being broken rather than as
   * nobody being signed in.
   */
  const [auth, setAuth] = useState<Account | null>(initialUser ?? (demo ? ACCOUNTS[0] : null));

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
  /** The job whose date is being moved or which is being called off, and which of the two. */
  const [changing, setChanging] = useState<{ key: string; mode: "move" | "cancel" } | null>(null);
  const [assignFor, setAssignFor] = useState<string | null>(null);
  /**
   * A job handed from the workspace drawer to the issue log.
   *
   * Carries the job key, not only the written reference, so the issue attaches
   * to exactly the job somebody was looking at — no matching, nothing to get
   * wrong. Cleared once the issue screen has taken it, so opening that menu
   * again later does not reopen a half-written form.
   */
  const [issueDraft, setIssueDraft] = useState<NewIssue | null>(null);

  /**
   * A job handed from the workspace drawer to the incident register.
   *
   * Only the key and a heading: the case is opened against the job, and
   * everything else — what, where, when, who, why, how, and the photographs —
   * is filled in on the incident screen, which is where that form already
   * lives.
   */
  const [incidentDraft, setIncidentDraft] =
    useState<{ jobKey: string; title: string } | null>(null);

  const [addCat, setAddCat] = useState<string | null>(null);
  /** Rows inserted into the grid and still being filled in. See insertRow. */
  const [pinnedKeys, setPinnedKeys] = useState<string[]>([]);
  const [addForm, setAddForm] = useState<Record<string, string>>({});
  const [aiFields, setAiFields] = useState<string[]>([]);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiMsg, setAiMsg] = useState("");

  // ---- Excel + saved views ----------------------------------------------
  const [importOpen, setImportOpen] = useState(false);
  const [importPreview, setImportPreview] = useState<ImportPreview | null>(null);
  const [importBusy, setImportBusy] = useState(false);
  const [importSaving, setImportSaving] = useState(false);
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
  const [sync, setSync] = useState<{ state: "idle" | "waking" | "stale" | "saving" | "saved" | "error" | "off"; at: string; message: string }>(
    { state: "idle", at: "", message: "" },
  );
  // Jobs and shipments are edited in place; bump this to re-render after a mutation.
  const [revision, setRevision] = useState(0);
  const touch = useCallback(() => setRevision((r) => r + 1), []);

  // ---- persistence -------------------------------------------------------
  /** Who to record against a save, kept in a ref so effects never read a stale name. */
  const meRef = useRef(ACCOUNTS[0].full);
  /** Jobs changed since the last write, collapsed by key so one save covers them. */
  const jobSaveQueue = useRef(new SaveQueue<Job>());
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const flush = useCallback((): Promise<{ ok: boolean; message: string }> => {
    return jobSaveQueue.current.flush(async (batch, reason) => {
      setSync((prev) => (prev.state === "off" ? prev : { state: "saving", at: prev.at, message: "" }));
      const result = await saveJobs(batch, meRef.current, reason);
      setSync((prev) => (prev.state === "off" ? prev
        : result.ok ? { state: "saved", at: nowHM(), message: "" }
          : { state: "error", at: prev.at, message: result.message }));
      return result;
    });
  }, []);

  /**
   * Writes everything pending now, and waits for it.
   *
   * The debounce is right for typing down a column and wrong for anything that
   * then goes and asks the server what it holds. The workspace grid is drawn
   * from the API's answer, so a caller that changes the view has to know the
   * write is already in the register.
   */
  const flushNow = useCallback(() => {
    if (saveTimer.current) { clearTimeout(saveTimer.current); saveTimer.current = null; }
    return flush();
  }, [flush]);

  /**
   * Every edit lands in the database. Writes are batched over a short window so
   * typing down a column is one save, not one per keystroke.
   */
  const persist = useCallback((jobs: Job[], reason = "") => {
    if (!jobs.length) return;
    jobSaveQueue.current.enqueue(jobs, reason);
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => { void flush(); }, 700);
  }, [flush]);

  /**
   * What the empty screen says while there is no register to draw.
   *
   * "Loading operation data…" is true of a request in flight and a lie about a
   * database that is taking two minutes to start or one that never answered.
   * The sync badge that says which of the three it is only renders once the
   * register has arrived, so during the wait it is not on screen — which is
   * exactly when somebody wants to know.
   */
  const loadingNote = sync.state === "waking"
    ? "กำลังปลุกฐานข้อมูล… ครั้งแรกของวันใช้เวลาราวสองนาที ไม่ต้องรีเฟรช"
    : sync.state === "off"
      ? `เปิดข้อมูลไม่ได้ — ${sync.message || "ต่อฐานข้อมูลไม่ได้"} · รีเฟรชหน้าเพื่อลองใหม่`
      : "Loading operation data…";

  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const setToast = useCallback((message: string) => {
    setToastValue(message);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToastValue(""), 2600);
  }, []);

  /**
   * Loads the complete register only when the current feature needs it.
   *
   * TODAY, supplier, document and administration screens have compact endpoints
   * of their own. Downloading 2.6 MB of jobs before those screens could render
   * made every visit pay for data it might never use. Workspace still starts its
   * page request immediately; this background read fills its summary panels,
   * export, duplicate detection and editing tools.
   */
  const registerNeeded = screenNeedsRegister(screen, tab)
    || gq.trim().length > 0
    || importOpen;
  const registerLoadStarted = useRef(false);
  const appMounted = useRef(true);
  useEffect(() => {
    // React Strict Mode mounts, cleans up and mounts effects again in
    // development. Resetting here keeps the second setup alive while the final
    // cleanup still prevents an async load writing after a real unmount.
    appMounted.current = true;
    return () => { appMounted.current = false; };
  }, []);

  useEffect(() => {
    if (!registerNeeded || registerLoadStarted.current) return;
    registerLoadStarted.current = true;

    (async () => {
      // Azure SQL serverless pauses itself after an hour with nobody on it, and
      // waking takes about two minutes — 110 seconds, measured, during which
      // requests either hang or come back 503 because the API's one worker is
      // sitting on them. A single attempt used to settle the question: the app
      // announced it had no database, fell through to a plan file that is
      // deliberately not deployed, and showed an empty workspace until somebody
      // thought to reload. The first person in each morning met that, and an
      // import keyed into that workspace went nowhere.
      //
      // So it asks again, with enough patience for a resume, and says which of
      // the two is happening. Only the last attempt is allowed to conclude that
      // there is no database.
      let stored = await loadJobs();
      for (let attempt = 1; attempt < WAKE_DELAYS.length; attempt++) {
        // "file-only" is the API not answering. An empty register answers.
        if (!appMounted.current || stored.source !== "file-only") break;
        setSync({
          state: "waking", at: "",
          message: `ฐานข้อมูลกำลังเริ่มทำงาน — ครั้งที่ ${attempt}/${WAKE_DELAYS.length - 1} · ${stored.error}`,
        });
        await new Promise((resume) => setTimeout(resume, WAKE_DELAYS[attempt]));
        if (!appMounted.current) return;
        stored = await loadJobs();
      }
      if (!appMounted.current) return;

      if (stored.jobs?.length) {
        setOps(prep({ jobs: stored.jobs }));
        setSync({ state: "saved", at: stored.updatedAt ? stored.updatedAt.slice(11, 16) : "", message: "" });
        return;
      }

      let raw: RawOps;
      try {
        raw = await loadPlanFile();
      } catch {
        if (appMounted.current) {
          setOps({ jobs: [], masters: { customers: [], truckers: [], operators: ["Watsana", "Uthai", "Ananya", "Jiratchaya", "Maliwan"], cyYards: [], warehouses: [], provinces: [] } });
          setSync({ state: "off", at: "", message: "อ่านไฟล์แผนไม่สำเร็จ" });
        }
        return;
      }
      if (!appMounted.current) return;

      const prepared = prep(raw);
      setOps(prepared);

      if (stored.source === "file-only") {
        setSync({ state: "off", at: "", message: stored.error || "ต่อฐานข้อมูลไม่ได้" });
        return;
      }
      // First run against an empty register: hand it the delivered plan.
      setSync({ state: "saving", at: "", message: "" });
      const seeded = await saveJobs(prepared.jobs, meRef.current);
      if (!appMounted.current) return;
      setSync(seeded.ok
        ? { state: "saved", at: nowHM(), message: "seeded" }
        : { state: "error", at: "", message: seeded.message });
    })();
  }, [registerNeeded]);

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
    { role: string; opId: string; name: string; init: string; known: boolean;
      full: string; authorised: boolean; actingFor: string[] } | null>(null);
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
          account?: { role?: string; opId?: string; name?: string; init?: string; full?: string };
          can?: string[];
          known?: boolean;
          authorised?: boolean;
          actingFor?: string[];
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
            full: body.account.full ?? "",
            // Absent means an older API that had no such idea, and every
            // account it answered for was one it had let in. Defaulting to
            // refused would lock out a working deployment mid-upgrade.
            authorised: body.authorised !== false,
            actingFor: body.actingFor ?? [],
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
  const actingFor = identity?.actingFor ?? [];
  /**
   * A carrier's account. Their menu is their own, and the register screens are
   * not in it — the API refuses them, and a rail full of buttons that refuse is
   * indistinguishable from an outage.
   */
  const isCarrier = (identity?.role || base.role) === "Subcontractor";

  // The default landing screen is the dashboard, which a carrier may not open.
  // Without this they arrive on a screen that is not in their own menu, and the
  // application appears to have loaded nothing at all.
  useEffect(() => {
    if (isCarrier && !CARRIER_SCREENS.includes(screen)) setScreen(CARRIER_SCREENS[0]);
  }, [isCarrier, screen]);

  // A heading is not a page. Anything that still points at one — a stored
  // landing preference from before Workspace became a section, a drill-down
  // written against the old name — lands on its first child instead of on
  // nothing at all.
  useEffect(() => {
    if (!HEADINGS.includes(screen)) return;
    const first = (SUB_NAV[screen] ?? [])[0];
    if (first) setScreen(first[0]);
  }, [screen]);
  const isSupervisor = able("ApproveAi");
  /**
   * The Domestic grid, hosted by The Chemours.
   *
   * Every Domestic job is that account's work, so it is worked there rather
   * than in the general workspace. It is the same grid component with its
   * category locked — one implementation, because two would have drifted.
   */
  const domesticGrid = screen === "chemours" && selectedTab("chemours", tab) === DOMESTIC_TAB;
  const lockedCat = domesticGrid ? "DELIVERY" : undefined;
  const isWorkspace = (screen === "myjob" || domesticGrid) && !isDetail;

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
  /**
   * Mine to work on — my own, and anybody's I am covering for today.
   *
   * The list comes from `/api/me`, which reads the same service the API checks
   * before accepting a write. Deciding it here from a second copy of the rule
   * is how the grid ends up offering an edit the server then refuses.
   */
  const owns = (job: Job) =>
    (!!me.opId && job.opId === me.opId)
    || (!!job.opId && actingFor.includes(job.opId));

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

  // The header feed is secondary to the page the person came to see. On TODAY,
  // wait for that compact response before asking the API to calculate every
  // notification rule; on register-backed screens, wait for the register read.
  // This prevents several full-register calculations competing during startup.
  const [primaryContentSettled, setPrimaryContentSettled] = useState(false);
  const showingToday = screen === "dashboard" && selectedTab(screen, tab) === "TODAY";
  const notificationsReady = identityState === "ready"
    && (showingToday ? primaryContentSettled : registerNeeded ? ops !== null : true);
  const settlePrimaryContent = useCallback(() => setPrimaryContentSettled(true), []);

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
    if (!notificationsReady) return;
    let cancelled = false;
    (async () => {
      try {
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
      } catch {
        // A missing notification badge must not turn into an unhandled rejection
        // or block the primary screen. The next register change retries it.
      }
    })();
    // The feed is re-read after a local edit. Loading the register itself does
    // not change it, so `ops` is deliberately not a dependency.
    return () => { cancelled = true; };
  }, [revision, notificationsReady]);

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
    if (target.screen && target.screen !== "myjob") {
      setNotif(false);
      go(target.screen as Screen);
      return;
    }
    setScreen("myjob");
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
      // Every saved page describes jobs that no longer exist.
      forget();
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

  /**
   * The page the workspace is about to draw, asked for directly.
   *
   * The register is still loaded — the panels above the grid, the calendar,
   * duplicate detection and the Excel export all read the whole thing — but the
   * grid no longer waits for it. Twenty-five rows arrive in a fraction of the
   * time 2,626 take, and the tab counts come with them, computed over the whole
   * register by the side that holds it.
   *
   * Asked per category, because the grid splits into independently paged
   * sections when no category is chosen. That is one request per visible
   * section rather than one, and still a fiftieth of the bytes.
   */
  /**
   * The page each grid section is on. Held here because the request that
   * fetches a page and the control that changes it must read the same number.
   */
  const [sectionPages, setSectionPages] = useState<Record<string, number>>({});

  const [serverPages, setServerPages] =
    useState<Record<string, WorkspaceServerPage> | undefined>(undefined);
  /** Whether what is on screen is last visit's answer, still waiting on this one. */
  const [fromCache, setFromCache] = useState(false);

  useEffect(() => {
    if (!isWorkspace) return;
    let cancelled = false;

    (async () => {
      const wanted = lockedCat ? [lockedCat]
        : ws.cat === "ALL" ? ["IMPORT", "EXPORT"] : [ws.cat];
      const queries = wanted.map((cat) => ({
        tab: activeTab, cat,
        year: ws.year, month: ws.month, day: ws.date, from: ws.from, to: ws.to,
        q, sort: ws.sort?.key, dir: ws.sort?.dir,
        page: sectionPages[cat] ?? 1, per: prefs.perPage,
        customer: ws.cust, trucker: ws.trucker, type: ws.type,
        status: ws.status, assignee: ws.assignee, kpi: ws.kpi,
      }));

      // Draw last time's answer first, if this exact view has one.
      //
      // The rows you are about to see are nearly always the rows you saw last
      // time, and waiting for the network to confirm that meant a placeholder on
      // every visit — two minutes of one on the first visit of the day, while
      // the database woke up. These are replaced the moment the real answer
      // arrives; the request below goes out either way. See pageCache for why
      // they live in sessionStorage and not on the disk.
      const cached: Record<string, WorkspaceServerPage> = {};
      queries.forEach((query, index) => {
        const saved = readCachedPage(pageCacheKey(me.opId, query));
        if (!saved) return;
        cached[wanted[index]] = {
          jobs: prep({ jobs: saved.jobs }).jobs,
          total: saved.total,
          pageCount: saved.pageCount,
          counts: saved.counts,
          dates: saved.dates,
        };
      });
      if (!cancelled && Object.keys(cached).length === wanted.length) {
        setServerPages(cached);
        setFromCache(true);
      }

      const answers = await Promise.all(queries.map((query) => loadJobsPage(query)));
      if (cancelled) return;
      setFromCache(false);

      // One failure and the whole thing falls back to the register in the
      // browser, which is slower and still correct. A half-filled grid would
      // not be.
      if (answers.some((answer) => answer === null)) { setServerPages(undefined); return; }

      const next: Record<string, WorkspaceServerPage> = {};
      wanted.forEach((cat, index) => {
        const answer = answers[index]!;
        writeCachedPage(pageCacheKey(me.opId, queries[index]), answer);
        // Through `prep`, because the grid draws fields it derives — the
        // priority column, the validation marks — and stored rows carry none
        // of them.
        next[cat] = {
          jobs: prep({ jobs: answer.jobs }).jobs,
          total: answer.total,
          pageCount: answer.pageCount,
          counts: answer.counts,
          dates: answer.dates,
        };
      });
      setServerPages(next);
    })();

    return () => { cancelled = true; };
    // `me.opId` keys the saved pages, so a change of account must re-read them
    // rather than show this person the last one's rows.
  }, [isWorkspace, lockedCat, activeTab, ws.cat, ws.year, ws.month, ws.date, ws.from, ws.to, ws.cust, ws.trucker,
      ws.type, ws.status, ws.assignee, ws.kpi, ws.sort?.key, ws.sort?.dir, q,
      sectionPages, prefs.perPage, revision, me.opId]);

  // Changing what is being looked at puts every section back to its first page.
  // Left alone, a filter that narrows to eight jobs would open on page four of
  // the previous selection and look empty.
  useEffect(() => {
    setSectionPages({});
  }, [activeTab, ws.cat, ws.year, ws.month, ws.date, ws.cust, ws.trucker,
      ws.type, ws.status, ws.assignee, ws.kpi, ws.sort?.key, ws.sort?.dir, q, prefs.perPage]);

  const workspacePageOps = useMemo(() => {
    if (!serverPages) return null;
    const jobs = Object.values(serverPages).flatMap((answer) => answer.jobs);
    // Page rows already came through `prep`; the second pass only rebuilds the
    // small master lists Workspace expects while the full register is pending.
    // An answered empty page is still ready state, not another loading state.
    return prep({ jobs: jobs as unknown as RawOps["jobs"] });
  }, [serverPages]);
  const workspaceOps = ops ?? workspacePageOps;

  const wsCounts = useMemo(
    () => {
      if (!isWorkspace) return {};
      if (ops) return workspaceTabCounts(ops, me.opId, lockedCat ?? ws.cat);
      if (!serverPages) return {};

      const answers = Object.values(serverPages);
      const counts: Record<string, number> = {};
      answers.forEach((answer) => {
        Object.entries(answer.counts).forEach(([name, count]) => {
          counts[name] = (counts[name] ?? 0) + count;
        });
      });
      // Calendar counts distinct dates; adding category counts would count the
      // same day two or three times in a mixed workspace.
      counts.CALENDAR = new Set(answers.flatMap((answer) => answer.dates)).size;
      return counts;
    },
    // `revision` is deliberate — see the note on `filtered` above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [isWorkspace, ops, serverPages, me.opId, ws.cat, revision],
  );

  const tabs: TabItem[] = tabList.map((t) => ({
    label: isWorkspace && wsCounts[t] !== undefined ? t + "  " + wsCounts[t] : t,
    active: t === activeTab,
    go: () => { setTab(t); setPage(1); },
  }));

  // CAR/PAR is off this list now that the screen reads the real register: a
  // sidebar badge counted off the demo file would contradict the screen it
  // points at.
  //
  // Booking counts the real queue — the jobs the Booking screen itself would
  // list, missing a carrier, a plate or a driver. It used to count invented
  // shipments, so the number beside the menu and the number on the screen it
  // opened were unrelated, and the badge was the one people read first.
  //
  // Billing has no badge at all now. There is no billing data in the register
  // to count, and a fabricated figure next to a menu item is worse than no
  // figure: it is read as fact, and nothing on the screen it leads to
  // contradicts it. It comes back when there is something real to count.
  const navCounts: Record<string, number> = useMemo(() => {
    const jobs = ops?.jobs ?? [];
    const counts: Record<string, number> = {};
    if (!jobs.length) return counts;
    const stages = bookingStats(jobs);
    counts.booking =
      stages["no-carrier"].length + stages["no-plate"].length + stages["no-driver"].length;
    return counts;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ops, revision]);

  // ---- actions -----------------------------------------------------------
  const go = (next: Screen) => {
    setScreen(next);
    setPage(1);
    setQ("");
    setSel(null);
  };

  /**
   * Opens the add-job form on a category, seeded the way the modal's own
   * chooser seeds it. Both entry points go through here so a job started from
   * a report toolbar cannot begin life with a different status to one started
   * from the chooser.
   */
  const startAddJob = (cat: string) => {
    setAddCat(cat);
    if (cat !== "CHOOSE") {
      setAddForm({ cat, op: me.name, status: cat === "DELIVERY" ? "Scheduled" : "Waiting Truck" });
    }
    setAiFields([]);
    setAiMsg("");
  };

  const openImport = () => {
    setImportPreview(null);
    setImportError("");
    setDupChoice({});
    setDupCursor(0);
    setImportOpen(true);
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
        { label: "Import from Excel", style: BTN_SECONDARY, go: openImport },
        { label: "Export Excel", style: BTN_SECONDARY, go: handleExport },
        { label: "+ แทรกแถว", style: BTN_SECONDARY, go: insertRow },
        { label: "+ ADD JOB", style: BTN_PRIMARY, go: () => startAddJob("CHOOSE") },
      ];
    }
    if (screen === "chemours") {
      // The same importer the workspace uses — it reads the delivery workbook
      // now — and the add form opened straight on DELIVERY, because that is the
      // only kind of job this report draws. Export stays on the screen itself,
      // next to the warehouse and month it exports.
      return [
        { label: "Import from Excel", style: BTN_SECONDARY, go: openImport },
        { label: "+ สร้างงาน", style: BTN_PRIMARY, go: () => startAddJob("DELIVERY") },
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
  /**
   * One cell written, with its column's rule applied.
   *
   * Shared by the inline editor and by a pasted block, because the whole point
   * of the column rules is that a value means the same thing however it
   * arrived. A date pasted from a spreadsheet as 3/7/26 becomes 03/07/2026
   * exactly as a typed one does, and one that cannot be read is left as it was
   * typed and marked, not quietly reshaped into something plausible.
   *
   * Returns the correction it made, so the caller can say what happened — one
   * cell says it in full, a pasted block says how many.
   */
  function writeCell(job: Job, field: keyof Job, value: string): Fix | null | false {
    const old = (job[field] as string) || "";
    if (value === old) return false;

    (job[field] as unknown as string) = value;
    const fix = normaliseField(job as unknown as Record<string, unknown>, String(field));
    flagJob(job);
    pushAct(job, String(field), old, (job[field] as string) || "");
    return fix;
  }

  /**
   * A block of cells pasted in one go.
   *
   * Reports once rather than per cell: twenty toasts is not twenty pieces of
   * information. What it does say is the part a person cannot see for
   * themselves — how many values were reformatted to fit their column, and how
   * many cells were skipped because the job belongs to somebody else. Skipping
   * quietly would let a paste look like it landed when half of it did not.
   */
  function pasteCells(edits: { job: Job; field: keyof Job; value: string }[]) {
    const touched = new Map<string, Job>();
    let changed = 0;
    let fixed = 0;
    let refused = 0;

    for (const edit of edits) {
      if (!canEditJob(edit.job)) { refused++; continue; }
      const fix = writeCell(edit.job, edit.field, edit.value);
      if (fix === false) continue;
      changed++;
      if (fix) fixed++;
      touched.set(edit.job.key, edit.job);
    }

    if (touched.size > 0) persist([...touched.values()]);
    touch();

    if (changed === 0 && refused === 0) { setToast("ค่าที่วางเหมือนเดิมทุกช่อง"); return; }
    setToast(
      `วางแล้ว ${changed} ช่อง · ${touched.size} งาน`
      + (fixed ? ` · จัดรูปแบบให้ ${fixed} ช่อง` : "")
      + (refused ? ` · ข้าม ${refused} ช่องที่เป็นงานของผู้อื่น` : ""),
    );
  }

  function setField(job: Job, field: keyof Job, value: string) {
    const old = (job[field] as string) || "";
    if (value === old) { touch(); return; }

    const fix = writeCell(job, field, value) || null;
    const saved = (job[field] as string) || "";

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
   * Prepares the import on copies, writes those copies, and only then updates
   * the board. A database timeout therefore leaves the preview and the original
   * jobs untouched, ready for the operator to retry.
   */
  async function confirmImport() {
    if (!importPreview?.jobs.length) return;
    if (!ops) {
      setImportError("กำลังโหลดทะเบียนงาน กรุณารอสักครู่แล้วลองอีกครั้ง");
      return;
    }

    setImportSaving(true);
    setImportError("");

    try {
      const { jobs: parsed, dups, fileName } = importPreview;
      const matched = new Map(dups.map((dup) => [dup.key, dup]));
      const fresh: Job[] = [];
      const overwrites: { existing: Job; next: Job }[] = [];
      let skipped = 0;

      const copyJob = (job: Job): Job => ({
        ...job,
        hist: (job.hist || []).map((entry) => ({ ...entry })),
        flags: [...(job.flags || [])],
        issues: [...(job.issues || [])],
        fixes: [...(job.fixes || [])],
      });

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

          const next = copyJob(dup.existing);
          const target = next as unknown as Record<string, unknown>;
          dup.diffs.forEach((difference) => {
            target[difference.field] = difference.to;
            pushAct(next, difference.label, difference.from, difference.to);
          });
          flagJob(next);
          overwrites.push({ existing: dup.existing, next });
          continue;
        }

        const next = copyJob(job);
        next.hist = [{ ts: nowHM(), user: me.name, field: "นำเข้าจาก Excel", old: "—", neu: fileName }];
        flagJob(next);
        fresh.push(next);
      }

      const toSave = fresh.concat(overwrites.map(({ next }) => next));
      const errors = fresh.filter((job) => job.issues.some((issue) => issue.severity === "error")).length;

      // The same serial queue used by normal edits keeps this batch available if
      // Azure SQL is still resuming and the first write times out.
      persist(toSave, `นำเข้าจาก Excel · ${fileName}`);
      setToast("กำลังบันทึกงานที่นำเข้า…");
      const written = await flushNow();
      if (!written.ok) {
        const message = "นำเข้าไม่สำเร็จ — ยังไม่ได้บันทึกลงฐานข้อมูล: " + written.message;
        setImportError(message + " · งานยังอยู่ในหน้าต่างนี้ กดนำเข้าอีกครั้งเพื่อลองใหม่");
        setToast(message);
        return;
      }

      // Nothing visible changes until the database has acknowledged the batch.
      if (fresh.length) ops.jobs.unshift(...fresh);
      overwrites.forEach(({ existing, next }) => Object.assign(existing, next));
      closeImport();

      setTab("PENDING");
      setWs((prev) => ({ ...prev, cat: "ALL", date: "ALL", kpi: "All", assignee: "All Team", status: "ALL", type: "ALL", year: "ALL", month: "ALL", from: "", to: "" }));
      setPage(1);
      setToast(
        `นำเข้า ${fresh.length} งานใหม่` +
        (overwrites.length ? ` · ทับของเดิม ${overwrites.length}` : "") +
        (skipped ? ` · ข้าม ${skipped}` : "") +
        (errors ? ` · ${errors} งานรูปแบบผิด กด FORMAT ERROR เพื่อแก้` : ""),
      );
      touch();
    } catch (error) {
      const message = "นำเข้าไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error));
      setImportError(message);
      setToast(message);
    } finally {
      setImportSaving(false);
    }
  }

  /**
   * The plan changed: the job moved to another day, or it is not happening.
   *
   * Both go down the same path as any other edit — normalised, written to the
   * job's own history, saved through the API that checks who owns it — because
   * a cancellation that skipped the audit trail would be the one change nobody
   * could account for.
   *
   * `origDate` is written once and never overwritten. It is where the job was
   * *first* meant to go, not where it was before this hop; the hops themselves
   * are already in the history. A shipment moved four times should still show
   * the date it was originally booked for, which is the number the customer
   * conversation is actually about.
   */
  function applyJobChange(job: Job, mode: "move" | "cancel",
                          change: { date: string; moveBy: string; reason: string }) {
    if (!ops) return;
    if (!canEditJob(job)) {
      setToast("แก้ไม่ได้ — งานนี้เป็นของ " + job.op);
      return;
    }

    if (mode === "move") {
      const from = job.date;
      if (!job.origDate) job.origDate = from;
      job.date = change.date;
      job.moveBy = change.moveBy;
      job.moveReason = change.reason;
      pushAct(job, "เลื่อนวันส่งงาน (" + change.moveBy + ")", from, change.date);
    } else {
      const before = job.status;
      job.status = "CANCELLED";
      job.cancelReason = change.reason;
      pushAct(job, "ยกเลิกงาน", before, "CANCELLED · " + change.reason);
    }

    flagJob(job);
    persist([job], change.reason);
    setChanging(null);
    touch();
    setToast(mode === "move"
      ? "เลื่อนเป็น " + change.date + " แล้ว · ดูได้ในแท็บ CANCEL / MOVED"
      : "ยกเลิกงานแล้ว · งานยังอยู่ในระบบ ดูได้ในแท็บ CANCEL / MOVED");
  }

  /**
   * Puts a new job straight into the grid, ready to type into.
   *
   * The modal asks for the date, the customer and the carrier before it will
   * create anything, which is right when somebody is entering one job from a
   * booking email and wrong when they are working down a list: it costs a
   * dialog per row. This makes the row first and lets the cells be filled in
   * place, which is how the plan is keyed in the workbook this replaced.
   *
   * The row is a real job from the outset — the cell editor upserts on every
   * edit, so there is no half-created state to reconcile and nothing is lost if
   * the tab closes mid-row. It is flagged incomplete, which is what the grid
   * already shows for a job missing its carrier or its plate.
   *
   * It is pinned while it is being filled. The grid is ordered by date and then
   * by carrier, so the moment a date is typed the row belongs somewhere else
   * and would leave the screen under the cursor of the person still typing.
   * Pinning holds it at the top until they say they are done; the sort is not
   * suspended, only deferred.
   */
  function insertRow() {
    if (!ops) return;
    if (!able("EditOwnJobs")) {
      setToast("บัญชีนี้ไม่มีสิทธิ์เพิ่มงาน");
      return;
    }

    // The category the grid is already showing, so the row lands in the section
    // the person is looking at rather than a different one.
    const cat = ws.cat !== "ALL" ? ws.cat : "IMPORT";
    const key = "N" + Date.now();
    // Today, because a row keyed today is nearly always for today or later, and
    // a blank date sorts to the end of the list where nobody can see it.
    const now = new Date();
    const today = `${pad(now.getDate())}/${pad(now.getMonth() + 1)}/${now.getFullYear()}`;
    const job: Job = {
      key, id: key, cat, op: me.name, opId: me.opId,
      date: today, customer: "", trucker: cat === "DELIVERY" ? "LESCHACO DTT" : "",
      jobCode: "", abs: "", booking: "", product: "", fclLcl: "", agent: "", destination: "",
      plant: "", planTime: "", type: "", cyYard: "", returnLoc: "", emptyReturn: "", weight: "",
      container: "", seal: "", tare: "", licence: "", driver: "", contact: "",
      arrDate: "", arrTime: "", closingDate: "", closingTime: "", reason: "", remark: "",
      ot: "", pickupPlan: "", pickupTime: "", cs: "", incident: "", freightType: "",
      origDate: "", moveReason: "", moveBy: "", cancelReason: "",
      status: DEFAULT_STATUS,
      hist: [{ ts: nowHM(), user: me.name, field: "แทรกแถวใหม่", old: "—", neu: today }],
      flags: [], action: true, prio: "MEDIUM", issues: [], fixes: [],
    };
    flagJob(job);
    ops.jobs.unshift(job);
    persist([job]);
    setPinnedKeys((prev) => [...prev, key]);
    setWs((prev) => ({ ...prev, edit: { key, field: "customer" }, editVal: "" }));
    setToast("แทรกแถวแล้ว — กรอกข้อมูลได้เลย แถวจะอยู่บนสุดจนกว่าจะกดเสร็จ");
    touch();
  }

  /** Lets a filled row go to wherever the date and carrier put it. */
  function donePinning(key: string) {
    setPinnedKeys((prev) => prev.filter((k) => k !== key));
    void flushNow().then(() => touch());
    setToast("บันทึกแล้ว — เรียงเข้าที่ตามวันที่และผู้ขนส่ง");
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
      closingDate: "", closingTime: "", reason: "", remark: "", ot: "", pickupPlan: "", pickupTime: "", cs: "",
      incident: "", freightType: "",
      origDate: "", moveReason: "", moveBy: "", cancelReason: "",
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
  // Looked up rather than stored: the cell editor writes to the object in
  // `ops.jobs`, and a copy held in state here would go stale on the first edit.
  const pinnedJobs = useMemo(
    () => (ops ? pinnedKeys.map((key) => ops.jobs.find((j) => j.key === key)).filter((j): j is Job => !!j) : []),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [ops, pinnedKeys, revision],
  );
  const changingJob = changing && ops ? ops.jobs.find((x) => x.key === changing.key) ?? null : null;
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

  /**
   * Signed in with Microsoft, and not on the list.
   *
   * This is a full stop rather than a banner. Everything below it would render
   * an application made of failed requests — empty tabs, a KPI screen of
   * dashes, a workspace with no rows — and a person reading that screen
   * concludes the system is broken, not that nobody has given them access yet.
   * The two look identical unless one of them says so.
   */
  if (identity && !identity.authorised) {
    return (
      <div style={css("min-height:100vh;display:flex;align-items:center;justify-content:center;background:#F4F6F8;padding:24px")}>
        <div style={css("max-width:460px;background:#fff;border:1px solid #E3E8EE;border-radius:8px;padding:28px 30px")}>
          <div style={css("font-size:15px;font-weight:700;color:#0F2B46;margin-bottom:10px")}>
            ยังไม่ได้รับสิทธิ์เข้าใช้ระบบ
          </div>
          <div style={css("font-size:13px;color:#5A6B7D;line-height:1.75")}>
            คุณลงชื่อเข้าใช้กับ Microsoft สำเร็จแล้ว
            {identity.full ? <> ในชื่อ <b>{identity.full}</b></> : null} แต่ยังไม่มีผู้ดูแลระบบเพิ่มบัญชีนี้
            เข้าทะเบียนพนักงานของ SCMOS จึงยังเปิดข้อมูลงานให้ไม่ได้
            <br /><br />
            แจ้งผู้ดูแลระบบให้เพิ่มบัญชีนี้ในเมนู <b>Administrator</b> แล้วลงชื่อเข้าใช้ใหม่อีกครั้ง
          </div>
          <div style={css("margin-top:18px;display:flex;gap:9px;flex-wrap:wrap")}>
            <button onClick={() => { setIdentityState("loading"); setIdentityAttempt((n) => n + 1); }}
              style={css("height:33px;padding:0 15px;border:1px solid #0F2B46;background:#0F2B46;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
              ตรวจสอบอีกครั้ง
            </button>
            <a href="/.auth/logout"
              style={css("height:33px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;text-decoration:none")}>
              ออกจากระบบ
            </a>
          </div>
        </div>
      </div>
    );
  }

  if (!auth && !demo) {
    return (
      <div style={css("min-height:100vh;display:flex;align-items:center;justify-content:center;background:#F4F6F8;padding:24px")}>
        <div style={css("max-width:420px;background:#fff;border:1px solid #E3E8EE;border-radius:8px;padding:28px 30px")}>
          <div style={css("font-size:15px;font-weight:700;color:#0F2B46;margin-bottom:10px")}>ยังไม่ได้ลงชื่อเข้าใช้</div>
          <div style={css("font-size:13px;color:#5A6B7D;line-height:1.75")}>
            ระบบยังไม่ได้รับข้อมูลผู้ใช้จาก Microsoft ลองเข้าใหม่อีกครั้ง
            ถ้ายังไม่ได้ ให้แจ้งผู้ดูแลระบบ
          </div>
          <a href="/.auth/login/aad"
            style={css("display:inline-flex;align-items:center;height:34px;padding:0 16px;margin-top:18px;border:1px solid #0F2B46;background:#0F2B46;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;text-decoration:none")}>
            เข้าสู่ระบบด้วย Microsoft
          </a>
        </div>
      </div>
    );
  }

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
          setScreen("myjob");
          setTab("MY JOBS");
          setWs(EMPTY_WS);
          setToast("Signed in as " + account.full + " · " + account.role);
        }}
      />
    );
  }

  const wsState: WsState = { ...ws, tab: activeTab, q, page, cat: lockedCat ?? ws.cat };

  return (
    <>
      <Chrome
        screen={screen}
        onNavigate={go}
        navCounts={navCounts}
        allowed={isCarrier ? CARRIER_SCREENS : undefined}
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
          forget();
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
                  {/* Only claim they cannot edit when they actually cannot. An
                      Administrator set through Auth:Roles has every capability
                      and no owner id — telling them they are locked out would
                      be the banner lying to the one person who can fix it. */}
                  {able("EditAnyJob")
                    ? <> แต่สิทธิ์ <b>{me.role}</b> ยังแก้ไขงานของทีมได้ตามปกติ</>
                    : <> และแก้ไขอะไรไม่ได้ — สิทธิ์ปัจจุบันคือ <b>{me.role || "อ่านอย่างเดียว"}</b></>}
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
              <Today onDrill={(next) => go(next as Screen)} onSettled={settlePrimaryContent} />
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
                note={loadingNote}
                tab={activeTab}
                // Every figure on the dashboard is a way into the workspace:
                // clicking one lands on the same jobs it counted.
                onDrill={openTarget}
              />
            )}

            {isWorkspace && (workspaceOps
              ? <Workspace
                  ops={workspaceOps}
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
                  onDrawer={ops ? setDrawer : () => setToast("กำลังโหลดข้อมูลสรุปก่อนเปิดรายละเอียด…")}
                  onDelay={setOpsDelay}
                  onSaveCell={saveCell}
                  onPasteCells={pasteCells}
                  onToast={setToast}
                  lockedCat={lockedCat}
                  onSetField={setField}
                  onStatusChange={changeJobStatus}
                  onSort={() => undefined}
                  canEdit={(job) => !!ops && canEditJob(job)}
                  canAssign={able("AssignJobs")}
                  serverPages={serverPages}
                  fullRegisterLoaded={!!ops}
                  sectionPages={sectionPages}
                  onSectionPage={(layout, next) =>
                    setSectionPages((was) => ({ ...was, [layout]: next }))}
                  per={prefs.perPage}
                  // While last visit's rows are on screen the badge says so,
                  // rather than implying they came from the database just now.
                  sync={fromCache && sync.state !== "error" && sync.state !== "off"
                    ? { state: "stale", at: sync.at, message: "" } : sync}
                  panels={prefs.panels}
                  onPanel={(key) => setPrefs((prev) => savePrefs({ ...prev, panels: { ...prev.panels, [key]: !prev.panels[key] } }))}
                  onBulkStatus={bulkStatus}
                  onBulkAssign={bulkAssign}
                  onBulkDelete={(keys) => { void removeJobs(keys); }}
                  pinned={pinnedJobs}
                  onDonePinning={donePinning}
                  onView={(v) => { workspaceView.current = v; }}
                />
              : <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:" +
                  (sync.state === "off" ? "#B42318" : "#94A3B8"))}>
                  {loadingNote}
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
                    setScreen("myjob");
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
            {screen === "admin" && <Administration jobs={ops?.jobs ?? []} me={me.name} onToast={setToast} />}
            {screen === "abs" && <Abs />}
            {screen === "carrier" && <CarrierPortal onToast={setToast} />}
            {screen === "training" && (
              <Training onToast={setToast}
                registerCustomers={[...new Set((ops?.jobs ?? [])
                  .map((job) => job.customer.trim()).filter(Boolean))]} />
            )}
            {screen === "loreal" && <Loreal jobs={ops?.jobs ?? []} onToast={setToast} />}
            {screen === "issues" && (
              <OperationalIssues
                jobs={ops?.jobs ?? []}
                prefill={issueDraft}
                onPrefillTaken={() => setIssueDraft(null)}
                onToast={setToast}
              />
            )}
            {screen === "rotation" && <JobRotation me={me.id} onToast={setToast} />}
            {screen === "chemours" && !domesticGrid && (
              <Chemours jobs={ops?.jobs ?? []} tab={selectedTab("chemours", tab)} onToast={setToast} />
            )}
            {screen === "docverify" && (
              <Verification canUpload={able("UploadDocuments")} onToast={setToast} />
            )}
            {screen === "billing" && (
              <>
                {/* The register holds no billing. This screen is still drawn
                    from the generated sample, and says so where somebody
                    reading the numbers will see it — the dashboard's billing
                    panels already carry the same badge. It comes off the day
                    there is an invoice table behind it. */}
                <div style={css("border:1px solid #F5E3C7;background:#FFFAEF;border-radius:5px;padding:11px 14px;margin-bottom:12px;display:flex;align-items:center;gap:10px;flex-wrap:wrap")}>
                  <span style={css("font-family:'IBM Plex Mono',monospace;font-size:10px;font-weight:600;letter-spacing:.06em;color:#B45309;background:#FDF2DF;border-radius:3px;padding:3px 7px")}>
                    DEMO DATA
                  </span>
                  <span style={css("font-size:11.5px;color:#B45309")}>
                    ตัวเลขในหน้านี้เป็นข้อมูลตัวอย่าง ยังไม่ได้ต่อกับข้อมูลการวางบิลจริง — ใช้ตัดสินใจไม่ได้
                  </span>
                </div>
                <BillingAging filtered={filtered} />
              </>
            )}
            {screen === "reports" && <Reports jobs={ops?.jobs ?? []} toast={setToast} />}

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
            {(screen === "incident" || screen === "carpar") && (
              <Incidents
                prefill={incidentDraft}
                onPrefillTaken={() => setIncidentDraft(null)}
                onToast={setToast}
              />
            )}
            {screen === "assistant" && (
              <Assistant canApprove={isSupervisor} onToast={setToast}
                onOpenJob={(key) => { openTarget({ tab: "PENDING" }); setDrawer(key); }} />
            )}
            {screen === "vendor" && <Vendor canManage={isSupervisor} onToast={setToast} />}
            {screen === "evaluation" && <Evaluation canManage={isSupervisor} onToast={setToast} />}
            {screen === "quotation" && <Quotation diesel={diesel} onDiesel={setDiesel} onToast={setToast} />}

            {screen === "postpone" && (
              <Postpone
                me={{ opId: me.opId, name: me.name }}
                // The same drawer the workspace opens, so a job looked at here
                // is edited the way it is edited anywhere else.
                onOpenJob={(key) => setDrawer(key)}
              />
            )}

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
            forget();
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
          onToast={setToast}
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
          onRaiseIssue={() => {
            const now = new Date();
            setIssueDraft({
              detail: "",
              jobKey: drawerJob.key,
              // The reference as the issue log writes it, so the row reads the
              // same as one typed in by hand.
              jobRef: drawerJob.booking || drawerJob.jobCode || drawerJob.abs
                || drawerJob.container || "",
              foundOn: `${pad(now.getDate())}/${pad(now.getMonth() + 1)}/${now.getFullYear()}`,
              foundAt: nowHM(),
              // Whoever is moving it is usually who rang in about it.
              reporter: drawerJob.trucker || "",
            });
            setDrawer(null);
            go("issues");
            setToast("เปิดฟอร์มแจ้งปัญหาของงาน " + (drawerJob.jobCode || drawerJob.customer));
          }}
          onRaiseIncident={() => {
            setIncidentDraft({
              jobKey: drawerJob.key,
              title: [drawerJob.customer, drawerJob.jobCode || drawerJob.abs || drawerJob.container]
                .filter(Boolean).join(" · "),
            });
            setDrawer(null);
            go("incident");
            setToast("เปิดเคส CAR/PAR ของงาน " + (drawerJob.jobCode || drawerJob.customer));
          }}
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
          onMove={() => setChanging({ key: drawerJob.key, mode: "move" })}
          onCancelJob={() => setChanging({ key: drawerJob.key, mode: "cancel" })}
          onDelete={() => { void removeJobs([drawerJob.key]); }}
        />
      )}

      {changingJob && changing && (
        <JobChangeModal
          job={changingJob}
          mode={changing.mode}
          onApply={(change) => applyJobChange(changingJob, changing.mode, change)}
          onClose={() => setChanging(null)}
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
          onChoose={startAddJob}
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
          saving={importSaving}
          registerReady={!!ops}
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
