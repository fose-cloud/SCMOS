export type GridEditKey = {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
};

export type GridEditIntent =
  | { mode: "keep" }
  | { mode: "replace"; value: string };

/**
 * Turns a key pressed over a selected grid cell into the way editing starts.
 *
 * F2 and Enter keep the existing value, while a printable character replaces
 * it — the same two gestures Excel uses. Shortcut modifiers always stand aside
 * so copy, paste, undo and browser shortcuts keep working. Shift is allowed for
 * printable keys because it is how uppercase letters and many symbols are
 * typed, but Shift+Enter remains available to the rest of the page.
 */
export function gridEditIntent(key: GridEditKey): GridEditIntent | null {
  if (key.ctrlKey || key.metaKey || key.altKey) return null;

  if ((key.key === "F2" || key.key === "Enter") && !key.shiftKey) {
    return { mode: "keep" };
  }

  // `Array.from` counts Unicode code points, so a Thai character is treated as
  // one printable key just like an English letter. Named keys such as Escape,
  // ArrowLeft and Process contain more than one code point and are ignored.
  if (Array.from(key.key).length === 1) {
    return { mode: "replace", value: key.key };
  }

  return null;
}
