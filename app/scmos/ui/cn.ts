import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * shadcn's class joiner: clsx for the conditionals, tailwind-merge so a later
 * class wins over an earlier one that sets the same property.
 *
 * Without the merge, `cn("p-4", "p-6")` emits both and the browser picks by
 * stylesheet order rather than by call order — which is how a variant ends up
 * unable to override the base it was written to override.
 */
export const cn = (...classes: ClassValue[]) => twMerge(clsx(classes));
