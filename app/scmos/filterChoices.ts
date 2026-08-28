/** Separator used to keep several selected values in the existing string state. */
export const PICK_SEP = "|";

/** Values used by the older single-choice controls to mean "no filter". */
const EMPTY_CHOICES = new Set(["", "ALL", "All Team"]);

/** Decode one filter value into the values selected by the user. */
export function chosenIn(value: string): string[] {
  return EMPTY_CHOICES.has(value) ? [] : value.split(PICK_SEP).filter(Boolean);
}

/** Compact text for the closed picker and the active-filter chip. */
export function pickLabel(value: string, render: (item: string) => string = (item) => item): string {
  const list = chosenIn(value);
  if (!list.length) return "ALL";
  return render(list[0]) + (list.length === 1 ? "" : " +" + (list.length - 1));
}

/** True when the value belongs to an any-of selection, or no filter is set. */
export function matchesChosen(value: string, wanted: string): boolean {
  const list = chosenIn(wanted);
  return !list.length || list.includes(value);
}
