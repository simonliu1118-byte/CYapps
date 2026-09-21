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
  resendApiKey?: string;
  from?: string;
};

const RESEND_EMAILS_URL = "https://api.resend.com/emails";
const MAX_EMAIL_LENGTH = 320;
const MAX_SUBJECT_LENGTH = 200;

export function createEmailSender(config: EmailProviderConfig): EmailSender {
  const provider = (config.provider ?? "resend").trim().toLowerCase();
  if (provider !== "resend") {
    throw new Error("EMAIL_PROVIDER_UNSUPPORTED");
  }

  const apiKey = config.resendApiKey?.trim() ?? "";
  const from = config.from?.trim() ?? "";
  if (!apiKey || !from) {
    throw new Error("EMAIL_PROVIDER_NOT_CONFIGURED");
  }

  return new ResendEmailSender(apiKey, from);
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
