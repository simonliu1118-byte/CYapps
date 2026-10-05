import assert from "node:assert/strict";
import test from "node:test";

import {
  createCredentialVerifier,
  createSessionToken,
  CURRENT_CREDENTIAL_ALGORITHM,
  isSessionToken,
  normalizePassword,
  sha256Hex,
  verifyCredential,
} from "../dist/crypto.js";

test("current credential verifier uses scrypt and authenticates correctly", async () => {
  const verifier = await createCredentialVerifier("CorrectPass123!");
  assert.equal(CURRENT_CREDENTIAL_ALGORITHM, "scrypt");
  assert.match(verifier, /^scrypt\$16384\$8\$1\$[0-9a-f]{32}\$[0-9a-f]{64}$/);
  assert.equal(await verifyCredential("CorrectPass123!", "scrypt", verifier), true);
  assert.equal(await verifyCredential("WrongPass123!", "scrypt", verifier), false);
});

test("password length is restricted to 8 through 16 characters", async () => {
  assert.equal(normalizePassword("1234567"), null);
  assert.equal(normalizePassword("12345678"), "12345678");
  assert.equal(normalizePassword("1234567890123456"), "1234567890123456");
  assert.equal(normalizePassword("12345678901234567"), null);
  assert.equal(normalizePassword("密碼測試一二三四"), "密碼測試一二三四");
  await assert.rejects(() => createCredentialVerifier("1234567"), /credential bounds/);
  await assert.rejects(() => createCredentialVerifier("12345678901234567"), /credential bounds/);
});

test("legacy PBKDF2 verifier fails closed outside the Workers production ceiling", async () => {
  assert.equal(
    await verifyCredential(
      "password123",
      "pbkdf2-sha256",
      "pbkdf2-sha256$210000$00112233445566778899aabbccddeeff$00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
    ),
    false,
  );
});

test("malformed and unsupported credential verifiers fail closed", async () => {
  assert.equal(await verifyCredential("password123", "unknown", "plaintext$password123"), false);
  assert.equal(await verifyCredential("password123", "scrypt", "scrypt$2$1$1$00$00"), false);
});

test("session tokens are opaque random values and only their hash needs storage", async () => {
  const first = createSessionToken();
  const second = createSessionToken();
  assert.equal(isSessionToken(first), true);
  assert.equal(isSessionToken(second), true);
  assert.notEqual(first, second);
  assert.match(await sha256Hex(first), /^[0-9a-f]{64}$/);
  assert.notEqual(await sha256Hex(first), await sha256Hex(second));
});
