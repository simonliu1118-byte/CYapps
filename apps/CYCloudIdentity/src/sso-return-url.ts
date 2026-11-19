// Registered SSO return URL policy (SSO_PORTAL_DESIGN.md §4.1).
//
// A return URL is a fixed HTTPS callback on an App's own domain. The provider
// stores one canonical string per App and every later comparison is exact
// string equality: no prefix, wildcard, case-folding or normalization at
// comparison time. Anything that the WHATWG URL parser would rewrite in a way
// that changes meaning (dot segments, backslashes, percent-encoding, IP hosts,
// credentials, query, fragment, explicit ports) is rejected rather than fixed.

const MAX_RETURN_URL_LENGTH = 512;
const DNS_LABEL = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;
const TOP_LEVEL_LABEL = /^(?:[a-z]{2,63}|xn--[a-z0-9-]{1,59})$/;
// Unreserved path characters plus "/". Percent-encoding is deliberately not
// accepted so that one callback cannot have several spellings.
const PATH = /^\/[A-Za-z0-9._~\-/]*$/;

function isDomainHost(hostname: string): boolean {
  if (hostname.length < 4 || hostname.length > 253) return false;
  const labels = hostname.split(".");
  if (labels.length < 2) return false;
  if (!labels.every(label => DNS_LABEL.test(label))) return false;
  // The URL parser already turns dotted/hex/octal IPv4 spellings into
  // a.b.c.d; an all-numeric final label is never a registrable domain.
  return TOP_LEVEL_LABEL.test(labels[labels.length - 1]);
}

/**
 * Returns the canonical `https://<lowercase-host><path>` form, or null when the
 * input is not an acceptable SSO return URL.
 */
export function canonicalSsoReturnUrl(value: unknown): string | null {
  if (typeof value !== "string") return null;
  if (value.length < 12 || value.length > MAX_RETURN_URL_LENGTH) return null;
  // Printable ASCII only; no whitespace, backslash, credentials, query or fragment
  // characters anywhere in the raw input (the parser would otherwise hide them).
  if (!/^[\x21-\x7e]+$/.test(value) || /[\\@?#%]/.test(value)) return null;
  if (!/^https:\/\//i.test(value)) return null;

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.protocol !== "https:" || url.username !== "" || url.password !== "") return null;
  if (url.search !== "" || url.hash !== "" || url.port !== "") return null;
  if (!isDomainHost(url.hostname)) return null;

  // The raw authority and path must already be canonical apart from host case:
  // the parser must not have dropped a default port, removed dot segments or
  // otherwise rewritten what the administrator typed.
  const rest = value.slice("https://".length);
  const slash = rest.indexOf("/");
  if (slash <= 0) return null;
  const authority = rest.slice(0, slash);
  const rawPath = rest.slice(slash);
  if (authority.toLowerCase() !== url.hostname) return null;
  if (rawPath !== url.pathname || !PATH.test(rawPath)) return null;
  if (rawPath.includes("//")) return null;
  if (rawPath.split("/").some(segment => segment === "." || segment === "..")) return null;

  return `https://${url.hostname}${url.pathname}`;
}

/** Exact comparison used everywhere a return URL is matched. */
export function ssoReturnUrlMatches(registered: string | null, candidate: unknown): boolean {
  return typeof registered === "string" && typeof candidate === "string" && registered === candidate;
}
