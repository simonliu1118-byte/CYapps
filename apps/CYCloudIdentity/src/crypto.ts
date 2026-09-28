import { pbkdf2 as nodePbkdf2 } from "node:crypto";

const PBKDF2_ALGORITHM = "pbkdf2-sha256";
export const DEFAULT_PBKDF2_ITERATIONS = 210_000;
const MIN_PBKDF2_ITERATIONS = 100_000;
const MAX_PBKDF2_ITERATIONS = 2_000_000;
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

async function derivePbkdf2(password: string, salt: Uint8Array, iterations: number): Promise<Uint8Array> {
  const passwordBytes = new TextEncoder().encode(password);
  const saltCopy = Uint8Array.from(salt);
  return new Promise<Uint8Array>((resolve, reject) => {
    nodePbkdf2(passwordBytes, saltCopy, iterations, 32, "sha256", (error, derivedKey) => {
      if (error) {
        reject(error);
        return;
      }
      resolve(Uint8Array.from(derivedKey));
    });
  });
}

export function normalizePassword(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const length = Array.from(value).length;
  if (length < MIN_PASSWORD_LENGTH || length > MAX_PASSWORD_LENGTH) return null;
  return value;
}

export async function createCredentialVerifier(
  password: string,
  iterations = DEFAULT_PBKDF2_ITERATIONS,
): Promise<string> {
  if (!normalizePassword(password)) throw new Error("password does not satisfy credential bounds");
  if (!Number.isInteger(iterations) || iterations < MIN_PBKDF2_ITERATIONS || iterations > MAX_PBKDF2_ITERATIONS)
    throw new Error("PBKDF2 iterations are outside accepted bounds");

  const salt = crypto.getRandomValues(new Uint8Array(16));
  const derived = await derivePbkdf2(password, salt, iterations);
  return `${PBKDF2_ALGORITHM}$${iterations}$${toHex(salt)}$${toHex(derived)}`;
}

export async function verifyCredential(password: string, verifier: string): Promise<boolean> {
  if (typeof verifier !== "string") return false;
  const parts = verifier.toLowerCase().split("$");
  if (parts.length !== 4 || parts[0] !== PBKDF2_ALGORITHM) return false;

  const iterations = Number.parseInt(parts[1], 10);
  const salt = fromHex(parts[2]);
  const expected = fromHex(parts[3]);
  if (!Number.isInteger(iterations) || iterations < MIN_PBKDF2_ITERATIONS || iterations > MAX_PBKDF2_ITERATIONS)
    return false;
  if (!salt || salt.length < 16 || salt.length > 64 || !expected || expected.length !== 32) return false;

  try {
    const actual = await derivePbkdf2(password, salt, iterations);
    return constantTimeEquals(actual, expected);
  } catch {
    return false;
  }
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
