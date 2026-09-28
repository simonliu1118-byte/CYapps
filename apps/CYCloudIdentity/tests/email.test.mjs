import assert from "node:assert/strict";
import test from "node:test";

import { createEmailSender } from "../dist/email.js";

test("Brevo adapter sends normalized transactional email without exposing configuration", async () => {
  const originalFetch = globalThis.fetch;
  let request;
  globalThis.fetch = async (url, init) => {
    request = { url: String(url), init };
    return new Response(JSON.stringify({ messageId: "synthetic-message-id" }), {
      status: 201,
      headers: { "content-type": "application/json" },
    });
  };
  try {
    const sender = createEmailSender({
      provider: "brevo",
      brevoApiKey: "synthetic-test-key",
      from: "CY Identity Test <sender@example.test>",
    });
    const result = await sender.send({
      to: "USER@EXAMPLE.TEST",
      subject: "Synthetic subject",
      text: "Synthetic body",
    });
    assert.equal(result.provider, "brevo");
    assert.equal(result.providerMessageId, "synthetic-message-id");
    assert.match(request.url, /^https:\/\/api\.brevo\.com\//);
    const body = JSON.parse(request.init.body);
    assert.equal(body.to[0].email, "user@example.test");
    assert.equal(body.sender.email, "sender@example.test");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("provider failure does not copy response body into the thrown error", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response("recipient-private-diagnostic", { status: 429 });
  try {
    const sender = createEmailSender({
      provider: "brevo",
      brevoApiKey: "synthetic-test-key",
      from: "sender@example.test",
    });
    await assert.rejects(
      () => sender.send({ to: "user@example.test", subject: "Synthetic", text: "Synthetic" }),
      error => {
        assert.equal(error.message, "EMAIL_DELIVERY_FAILED_429");
        assert.equal(error.message.includes("recipient-private-diagnostic"), false);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});
