export type TransactionalEmail = {
  to: string;
  subject: string;
  text: string;
  html?: string;
  tags?: ReadonlyArray<{ name: string; value: string }>;
};

export type EmailDeliveryResult = {
  provider: string;
  providerMessageId: string;
};

export interface EmailSender {
  send(message: TransactionalEmail): Promise<EmailDeliveryResult>;
}

export type EmailProviderConfig = {
  provider?: string;
  brevoApiKey?: string;
  resendApiKey?: string;
  from?: string;
};

const BREVO_EMAILS_URL = "https://api.brevo.com/v3/smtp/email";
const RESEND_EMAILS_URL = "https://api.resend.com/emails";
const MAX_EMAIL_LENGTH = 320;
const MAX_SUBJECT_LENGTH = 200;

export function createEmailSender(config: EmailProviderConfig): EmailSender {
  const provider = (config.provider ?? "brevo").trim().toLowerCase();
  const from = parseMailbox(config.from?.trim() ?? "");

  if (provider === "brevo") {
    const apiKey = config.brevoApiKey?.trim() ?? "";
    if (!apiKey) throw new Error("EMAIL_PROVIDER_NOT_CONFIGURED");
    return new BrevoEmailSender(apiKey, from);
  }

  if (provider === "resend") {
    const apiKey = config.resendApiKey?.trim() ?? "";
    if (!apiKey) throw new Error("EMAIL_PROVIDER_NOT_CONFIGURED");
    return new ResendEmailSender(apiKey, formatMailbox(from));
  }

  throw new Error("EMAIL_PROVIDER_UNSUPPORTED");
}

class BrevoEmailSender implements EmailSender {
  constructor(
    private readonly apiKey: string,
    private readonly from: Mailbox)
  {
  }

  async send(message: TransactionalEmail): Promise<EmailDeliveryResult> {
    const to = normalizeAddress(message.to);
    const subject = normalizeSubject(message.subject);
    const text = normalizeBody(message.text);
    const html = message.html === undefined ? undefined : normalizeBody(message.html);

    const response = await fetch(BREVO_EMAILS_URL, {
      method: "POST",
      headers: {
        "api-key": this.apiKey,
        "content-type": "application/json",
        accept: "application/json"
      },
      body: JSON.stringify({
        sender: {
          email: this.from.email,
          ...(this.from.name ? { name: this.from.name } : {})
        },
        to: [{ email: to }],
        subject,
        textContent: text,
        ...(html === undefined ? {} : { htmlContent: html })
      })
    });

    if (!response.ok) {
      // Never copy the provider response body into an exception or log. It can
      // contain recipient information and provider-side delivery diagnostics.
      throw new Error(`EMAIL_DELIVERY_FAILED_${response.status}`);
    }

    const payload: unknown = await response.json();
    if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
      throw new Error("EMAIL_DELIVERY_INVALID_RESPONSE");
    }

    const providerMessageId = (payload as { messageId?: unknown }).messageId;
    if (typeof providerMessageId !== "string" || providerMessageId.trim().length === 0) {
      throw new Error("EMAIL_DELIVERY_INVALID_RESPONSE");
    }

    return {
      provider: "brevo",
      providerMessageId: providerMessageId.trim()
    };
  }
}

class ResendEmailSender implements EmailSender {
  constructor(
    private readonly apiKey: string,
    private readonly from: string)
  {
  }

  async send(message: TransactionalEmail): Promise<EmailDeliveryResult> {
    const to = normalizeAddress(message.to);
    const subject = normalizeSubject(message.subject);
    const text = normalizeBody(message.text);
    const html = message.html === undefined ? undefined : normalizeBody(message.html);

    const response = await fetch(RESEND_EMAILS_URL, {
      method: "POST",
      headers: {
        authorization: `Bearer ${this.apiKey}`,
        "content-type": "application/json"
      },
      body: JSON.stringify({
        from: this.from,
        to: [to],
        subject,
        text,
        ...(html === undefined ? {} : { html }),
        ...(message.tags && message.tags.length !== 0 ? { tags: message.tags } : {})
      })
    });

    if (!response.ok) {
      // Deliberately do not include the provider response body: it can contain
      // recipient addresses or provider diagnostics that should not enter logs.
      throw new Error(`EMAIL_DELIVERY_FAILED_${response.status}`);
    }

    const payload: unknown = await response.json();
    if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
      throw new Error("EMAIL_DELIVERY_INVALID_RESPONSE");
    }

    const providerMessageId = (payload as { id?: unknown }).id;
    if (typeof providerMessageId !== "string" || providerMessageId.trim().length === 0) {
      throw new Error("EMAIL_DELIVERY_INVALID_RESPONSE");
    }

    return {
      provider: "resend",
      providerMessageId: providerMessageId.trim()
    };
  }
}

type Mailbox = {
  name: string;
  email: string;
};

function parseMailbox(value: string): Mailbox {
  const normalized = value.trim();
  if (!normalized || /[\r\n]/.test(normalized)) {
    throw new Error("EMAIL_SENDER_INVALID");
  }

  const match = /^(.*?)\s*<([^<>]+)>$/.exec(normalized);
  if (!match) {
    return { name: "", email: normalizeAddress(normalized) };
  }

  const name = match[1].trim();
  if (name.length > 120) throw new Error("EMAIL_SENDER_INVALID");
  return {
    name,
    email: normalizeAddress(match[2])
  };
}

function formatMailbox(mailbox: Mailbox): string {
  return mailbox.name ? `${mailbox.name} <${mailbox.email}>` : mailbox.email;
}

function normalizeAddress(value: string): string {
  const normalized = value.trim();
  if (normalized.length < 3 || normalized.length > MAX_EMAIL_LENGTH || /[\r\n]/.test(normalized)) {
    throw new Error("EMAIL_RECIPIENT_INVALID");
  }

  const at = normalized.lastIndexOf("@");
  if (at <= 0 || at === normalized.length - 1) {
    throw new Error("EMAIL_RECIPIENT_INVALID");
  }
  return normalized;
}

function normalizeSubject(value: string): string {
  const normalized = value.trim();
  if (normalized.length < 1 || normalized.length > MAX_SUBJECT_LENGTH || /[\r\n]/.test(normalized)) {
    throw new Error("EMAIL_SUBJECT_INVALID");
  }
  return normalized;
}

function normalizeBody(value: string): string {
  if (value.length < 1 || value.length > 50_000) {
    throw new Error("EMAIL_BODY_INVALID");
  }
  return value;
}
