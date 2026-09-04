/**
 * What build this is, in one place.
 *
 * It was written into the sidebar as a literal and nowhere else, so the login
 * screen adding a version badge would have made it two literals — and a version
 * number that disagrees with itself is worse than none, because the one you can
 * see is the one you quote when you report a fault.
 *
 * A leaf module on purpose: it imports nothing, so anything may import it.
 */
export const APP_VERSION = "v2.4.1";

/** Which deployment, for the sidebar's environment line. */
export const APP_ENVIRONMENT = "Production · TH-BKK";
