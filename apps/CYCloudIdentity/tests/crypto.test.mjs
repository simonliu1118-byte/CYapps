import assert from "node:assert/strict";
import test from "node:test";

import {
  createCredentialVerifier,
  createSessionToken,
  isSessionToken,
  sha256Hex,
  verifyCredential,
} from "../dist/crypto.js";

test("credential verifier accepts the correct password and rejects a wrong password", async () => {
  const verifier = await createCredentialVerifier("correct horse battery staple", 100_000);
  assert.match(verifier, /^pbkdf2-sha256\$100000\$[0-9a-f]{32}\$[0-9a-f]{64}$/);
  assert.equal(await verifyCredential("correct horse battery staple", verifier), true);
  assert.equal(await verifyCredential("wrong password", verifier), false);
});

test("malformed credential verifier fails closed", async () => {
  assert.equal(await verifyCredential("password123", "plaintext$password123"), false);
  assert.equal(await verifyCredential("password123", "pbkdf2-sha256$2$00$00"), false);
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
