"use client";

import { useEffect } from "react";
import { css } from "./theme";

/**
 * The right-click menu on a grid: copy and paste, on the rectangle.
 *
 * Asked for on 16 Sep 2026. The keys did this already; the menu is the same
 * two actions where the mouse looks for them. Drawn by the screen that owns
 * the grid, fed by `useGridRange`, and put away by a click anywhere else or
 * Escape.
 */
export function GridMenu({
  at, onCopy, onPaste, onClose, canPaste = true,
}: {
  at: { x: number; y: number } | null;
  onCopy: () => void;
  onPaste: () => void;
  onClose: () => void;
  /** False on a rectangle nobody may write into — the paste is shown greyed. */
  canPaste?: boolean;
}) {
  useEffect(() => {
    if (!at) return;
    const away = () => onClose();
    const key = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    // The click that opens the menu has finished by the time these attach;
    // the next one anywhere, or a scroll, closes it.
    window.addEventListener("mousedown", away);
    window.addEventListener("scroll", away, true);
    window.addEventListener("keydown", key);
    return () => {
      window.removeEventListener("mousedown", away);
      window.removeEventListener("scroll", away, true);
      window.removeEventListener("keydown", key);
    };
  }, [at, onClose]);

  if (!at) return null;
  // Kept on screen when the click was near the right or bottom edge.
  const left = Math.min(at.x, (typeof window === "undefined" ? 0 : window.innerWidth) - 190);
  const top = Math.min(at.y, (typeof window === "undefined" ? 0 : window.innerHeight) - 90);
  const item = (enabled: boolean) =>
    "display:flex;justify-content:space-between;gap:18px;width:100%;padding:7px 12px;border:0;background:transparent;"
    + "font-size:12.5px;font-family:inherit;text-align:left;border-radius:3px;"
    + (enabled ? "color:#16232F;cursor:pointer" : "color:#A0AEC0;cursor:default");

  return (
    <div role="menu" tabIndex={-1} onMouseDown={(event) => event.stopPropagation()}
      style={css(`position:fixed;left:${left}px;top:${top}px;z-index:80;min-width:180px;padding:4px;background:#fff;`
        + "border:1px solid #D0D8E0;border-radius:6px;box-shadow:0 8px 24px rgba(10,34,64,.16)")}>
      <button type="button" role="menuitem" onClick={onCopy} style={css(item(true))}>
        <span>คัดลอก</span><span style={css("color:#94A3B8;font-size:11px")}>Ctrl+C</span>
      </button>
      <button type="button" role="menuitem" disabled={!canPaste} onClick={() => { if (canPaste) onPaste(); }}
        style={css(item(canPaste))}>
        <span>วาง</span><span style={css("color:#94A3B8;font-size:11px")}>Ctrl+V</span>
      </button>
    </div>
  );
}
