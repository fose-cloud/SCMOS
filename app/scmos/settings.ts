/**
 * Personal application settings.
 *
 * Same reasoning as saved views: where a person lands, how many rows they want
 * per page and whether the sidebar starts collapsed are per-person UI
 * preferences, not shared operational data, so they live in localStorage rather
 * than the register. Each one is actually applied — nothing here is decorative.
 */

/**
 * Which workspace panels are expanded. The analytics above the grid are useful
 * but they push the job list off the screen, so each one folds away and the
 * choice is remembered.
 */
export type PanelPrefs = { kpi: boolean; process: boolean; team: boolean; filters: boolean };

export const DEFAULT_PANELS: PanelPrefs = { kpi: true, process: false, team: false, filters: true };

export type Prefs = {
  /** Screen to open after sign-in. */
  landing: "workspace" | "dashboard";
  /** Rows per page in every table. */
  perPage: number;
  /** Start with the navigation rail collapsed. */
  collapsed: boolean;
  panels: PanelPrefs;
};

export const PER_PAGE_OPTIONS = [25, 50, 100, 200];

export const DEFAULT_PREFS: Prefs = {
  landing: "workspace",
  perPage: 25,
  collapsed: false,
  panels: DEFAULT_PANELS,
};

const KEY = "scmos.prefs";

function storage(): Storage | null {
  // Called during the server render too, where there is no window.
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null; // private mode / storage disabled
  }
}

export function loadPrefs(): Prefs {
  const store = storage();
  if (!store) return DEFAULT_PREFS;
  try {
    const raw = JSON.parse(store.getItem(KEY) ?? "{}") as Partial<Prefs>;
    const panels = (raw.panels ?? {}) as Partial<PanelPrefs>;
    return {
      landing: raw.landing === "dashboard" ? "dashboard" : "workspace",
      perPage: PER_PAGE_OPTIONS.indexOf(Number(raw.perPage)) >= 0 ? Number(raw.perPage) : DEFAULT_PREFS.perPage,
      collapsed: raw.collapsed === true,
      panels: {
        kpi: panels.kpi !== undefined ? panels.kpi === true : DEFAULT_PANELS.kpi,
        process: panels.process === true,
        team: panels.team === true,
        filters: panels.filters !== undefined ? panels.filters === true : DEFAULT_PANELS.filters,
      },
    };
  } catch {
    return DEFAULT_PREFS;
  }
}

export function savePrefs(prefs: Prefs): Prefs {
  const store = storage();
  if (store) {
    try {
      store.setItem(KEY, JSON.stringify(prefs));
    } catch {
      // Quota or private mode — the settings simply do not persist.
    }
  }
  return prefs;
}

/* --------------------------------------------------------------- profile */

/**
 * The parts of an identity a person may change about themselves.
 *
 * `name`, `user` and `role` are deliberately absent. `name` is the key the
 * workspace matches job ownership on (`job.op === user.name`), `user` is the
 * sign-in id, and `role` is a permission — none of them are a display setting,
 * so they are shown read-only and changed in Administration.
 */
export type Profile = {
  /** Display name shown on screen; the ownership key stays untouched. */
  full: string;
  /** Two or three letters for the avatar when there is no picture. */
  init: string;
  email: string;
  phone: string;
  /** Square data URL, or "" when the person has no picture. */
  avatar: string;
};

export const EMPTY_PROFILE: Profile = { full: "", init: "", email: "", phone: "", avatar: "" };

const PROFILE_KEY = (user: string) => "scmos.profile." + user;

export function loadProfile(user: string): Profile {
  const store = storage();
  if (!store) return EMPTY_PROFILE;
  try {
    const raw = JSON.parse(store.getItem(PROFILE_KEY(user)) ?? "{}") as Partial<Profile>;
    return {
      full: typeof raw.full === "string" ? raw.full : "",
      init: typeof raw.init === "string" ? raw.init.slice(0, 3) : "",
      email: typeof raw.email === "string" ? raw.email : "",
      phone: typeof raw.phone === "string" ? raw.phone : "",
      avatar: typeof raw.avatar === "string" && raw.avatar.startsWith("data:image/") ? raw.avatar : "",
    };
  } catch {
    return EMPTY_PROFILE;
  }
}

/** Returns the stored profile, or null when the browser refused to keep it. */
export function saveProfile(user: string, profile: Profile): Profile | null {
  const store = storage();
  if (!store) return null;
  try {
    store.setItem(PROFILE_KEY(user), JSON.stringify(profile));
    return profile;
  } catch {
    // Almost always the 5 MB quota, and almost always the picture.
    return null;
  }
}

export const MAX_AVATAR_BYTES = 8 * 1024 * 1024;

/**
 * Turns a chosen picture into a small square data URL. Photos off a phone are
 * several megabytes, and localStorage holds about five in total, so the file is
 * drawn down to 160px and re-encoded rather than stored as picked.
 */
export function readAvatar(file: File, size = 160): Promise<string> {
  return new Promise((resolve, reject) => {
    if (!file.type.startsWith("image/")) {
      reject(new Error("ไฟล์นี้ไม่ใช่รูปภาพ"));
      return;
    }
    if (file.size > MAX_AVATAR_BYTES) {
      reject(new Error("ไฟล์ใหญ่เกิน 8 MB"));
      return;
    }
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      try {
        const canvas = document.createElement("canvas");
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext("2d");
        if (!ctx) throw new Error("วาดรูปไม่สำเร็จ");
        // Cover-crop from the middle so portraits are not squashed.
        const side = Math.min(img.naturalWidth, img.naturalHeight);
        ctx.fillStyle = "#ffffff";
        ctx.fillRect(0, 0, size, size);
        ctx.drawImage(
          img,
          (img.naturalWidth - side) / 2, (img.naturalHeight - side) / 2, side, side,
          0, 0, size, size,
        );
        resolve(canvas.toDataURL("image/jpeg", 0.85));
      } catch (error) {
        reject(error instanceof Error ? error : new Error(String(error)));
      }
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error("อ่านรูปภาพไม่สำเร็จ"));
    };
    img.src = url;
  });
}
