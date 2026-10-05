import { scrypt as nodeScrypt } from "node:crypto";

export const CURRENT_CREDENTIAL_ALGORITHM = "scrypt";
const LEGACY_PBKDF2_ALGORITHM = "pbkdf2-sha256";
const LEGACY_PBKDF2_MAX_ITERATIONS = 100_000;
const SCRYPT_N = 16_384;
const SCRYPT_R = 8;
const SCRYPT_P = 1;
const SCRYPT_KEY_LENGTH = 32;
const SCRYPT_MAX_MEMORY = 64 * 1024 * 1024;
export const MIN_PASSWORD_LENGTH = 8;
export const MAX_PASSWORD_LENGTH = 16;
const SESSION_TOKEN_PREFIX = "cyid_";

function toHex(bytes: Uint8Array): string {
  return Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
}

function fromHex(value: string): Uint8Array | null {
  if (!value || value.length % 2 !== 0 || !/^[0-9a-f]+$/i.test(value)) return null;
  const bytes = new Uint8Array(value.length / 2);
  for (let index = 0; index < bytes.length; index += 1) {
    const parsed = Number.parseInt(value.slice(index * 2, index * 2 + 2), 16);
    if (!Number.isFinite(parsed)) return null;
    bytes[index] = parsed;
  }
  return bytes;
}

function constantTimeEquals(left: Uint8Array, right: Uint8Array): boolean {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) difference |= left[index] ^ right[index];
  return difference === 0;
}

async function deriveScrypt(
  password: string,
  salt: Uint8Array,
  n = SCRYPT_N,
  r = SCRYPT_R,
  p = SCRYPT_P,
): Promise<Uint8Array> {
  const passwordBytes = new TextEncoder().encode(password);
  const saltCopy = Uint8Array.from(salt);
  return new Promise<Uint8Array>((resolve, reject) => {
    nodeScrypt(
      passwordBytes,
      saltCopy,
      SCRYPT_KEY_LENGTH,
      { N: n, r, p, maxmem: SCRYPT_MAX_MEMORY },
      (error, derivedKey) => {
        if (error) {
          reject(error);
          return;
        }
        resolve(Uint8Array.from(derivedKey));
      },
    );
  });
}

async function deriveLegacyPbkdf2(password: string, salt: Uint8Array, iterations: number): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  return new Uint8Array(await crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      hash: "SHA-256",
      salt: Uint8Array.from(salt),
      iterations,
    },
    key,
    256,
  ));
}

export function normalizePassword(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const length = Array.from(value).length;
  if (length < MIN_PASSWORD_LENGTH || length > MAX_PASSWORD_LENGTH) return null;
  return value;
}

export async function createCredentialVerifier(password: string): Promise<string> {
  if (!normalizePassword(password)) throw new Error("password does not satisfy credential bounds");
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const derived = await deriveScrypt(password, salt);
  return `scrypt$${SCRYPT_N}$${SCRYPT_R}$${SCRYPT_P}$${toHex(salt)}$${toHex(derived)}`;
}

async function verifyScrypt(password: string, verifier: string): Promise<boolean> {
  const parts = verifier.toLowerCase().split("$");
  if (parts.length !== 6 || parts[0] !== CURRENT_CREDENTIAL_ALGORITHM) return false;
  const n = Number.parseInt(parts[1], 10);
  const r = Number.parseInt(parts[2], 10);
  const p = Number.parseInt(parts[3], 10);
  const salt = fromHex(parts[4]);
  const expected = fromHex(parts[5]);
  if (n !== SCRYPT_N || r !== SCRYPT_R || p !== SCRYPT_P) return false;
  if (!salt || salt.length !== 16 || !expected || expected.length !== SCRYPT_KEY_LENGTH) return false;
  try {
    const actual = await deriveScrypt(password, salt, n, r, p);
    return constantTimeEquals(actual, expected);
  } catch {
    return false;
  }
}

async function verifyLegacyPbkdf2(password: string, verifier: string): Promise<boolean> {
  const parts = verifier.toLowerCase().split("$");
  if (parts.length !== 4 || parts[0] !== LEGACY_PBKDF2_ALGORITHM) return false;
  const iterations = Number.parseInt(parts[1], 10);
  const salt = fromHex(parts[2]);
  const expected = fromHex(parts[3]);
  if (!Number.isInteger(iterations) || iterations < 1 || iterations > LEGACY_PBKDF2_MAX_ITERATIONS) return false;
  if (!salt || salt.length < 16 || salt.length > 64 || !expected || expected.length !== 32) return false;
  try {
    const actual = await deriveLegacyPbkdf2(password, salt, iterations);
    return constantTimeEquals(actual, expected);
  } catch {
    return false;
  }
}

export async function verifyCredential(password: string, algorithm: string, verifier: string): Promise<boolean> {
  if (!normalizePassword(password) || typeof algorithm !== "string" || typeof verifier !== "string") return false;
  if (algorithm === CURRENT_CREDENTIAL_ALGORITHM) return verifyScrypt(password, verifier);
  if (algorithm === LEGACY_PBKDF2_ALGORITHM) return verifyLegacyPbkdf2(password, verifier);
  return false;
}

export async function sha256Hex(value: string): Promise<string> {
  const bytes = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return toHex(bytes);
}

export function createSessionToken(): string {
  const random = crypto.getRandomValues(new Uint8Array(32));
  return `${SESSION_TOKEN_PREFIX}${toHex(random)}`;
}

export function isSessionToken(value: unknown): value is string {
  return typeof value === "string" && /^cyid_[0-9a-f]{64}$/.test(value);
}
