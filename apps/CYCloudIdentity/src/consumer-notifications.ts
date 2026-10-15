import { createEmailSender, type EmailSender } from "./email";
import { reserveEmailBudget, settleEmailBudget } from "./email-budget";
import type { Env } from "./types";

export interface BackupHealthNotification {
  workspaceId: string;
  applicationId: string;
  type: "backup_health_failed";
  eventId: string;
}

// Called only by the named private Worker RPC capability. No public route,
// caller-selected recipient/body, Identity grant or app business data is accepted.
export async function notifySuperAdmin(env: Env, input: BackupHealthNotification,
  options: { now?: Date; sender?: EmailSender } = {}) {
  if (!input || typeof input.workspaceId !== "string" || !/^[A-Za-z0-9._:-]{5,80}$/.test(input.workspaceId)
      || typeof input.applicationId !== "string" || !/^[A-Z0-9_-]{2,64}$/.test(input.applicationId)
      || input.type !== "backup_health_failed" || typeof input.eventId !== "string"
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.eventId)
      || Object.keys(input).some(key => !["workspaceId", "applicationId", "type", "eventId"].includes(key)))
    throw new Error("NOTIFICATION_REQUEST_INVALID");
  const now = options.now ?? new Date(), timestamp = now.toISOString();
  const scope = await env.DB.prepare(`SELECT e.email_normalized, a.display_name
    FROM workspaces w JOIN workspace_applications wa ON wa.workspace_id = w.workspace_id
    JOIN applications a ON a.application_id = wa.application_id
    JOIN employees e ON e.employee_id = w.super_admin_employee_id AND e.workspace_id = w.workspace_id
    WHERE w.workspace_id = ?1 AND a.application_id = ?2 AND w.status = 'active'
      AND wa.enabled = 1 AND a.status = 'active' AND e.enabled = 1 AND e.email_verified_at IS NOT NULL`)
    .bind(input.workspaceId, input.applicationId).first<{ email_normalized: string; display_name: string }>();
  if (!scope) throw new Error("NOTIFICATION_AUTHORITY_UNAVAILABLE");
  const prior = await env.DB.prepare(`SELECT event_id, sent_at FROM consumer_notifications
    WHERE workspace_id = ?1 AND application_id = ?2 AND notification_type = ?3`)
    .bind(input.workspaceId, input.applicationId, input.type).first<{ event_id: string; sent_at: string | null }>();
  if (prior?.event_id === input.eventId && prior.sent_at) return { status: "already_delivered" as const };
  const lease = crypto.randomUUID();
  const claimed = await env.DB.prepare(`INSERT INTO consumer_notifications
    (workspace_id, application_id, notification_type, event_id, lease_token, lease_until, sent_at, updated_at)
    VALUES (?1, ?2, ?3, ?4, ?5, ?6, NULL, ?7)
    ON CONFLICT(workspace_id, application_id, notification_type) DO UPDATE SET
      event_id = excluded.event_id, lease_token = excluded.lease_token,
      lease_until = excluded.lease_until, sent_at = NULL, updated_at = excluded.updated_at
    WHERE consumer_notifications.lease_until <= ?7
      AND (consumer_notifications.sent_at IS NULL OR consumer_notifications.sent_at <= ?8)
    RETURNING lease_token`)
    .bind(input.workspaceId, input.applicationId, input.type, input.eventId, lease,
      new Date(now.getTime() + 120_000).toISOString(), timestamp,
      new Date(now.getTime() - 3_600_000).toISOString()).first();
  if (!claimed) return { status: "deferred" as const };
  let reservation;
  let sent = false;
  try {
    const sender = options.sender ?? createEmailSender({ provider: env.EMAIL_PROVIDER,
      brevoApiKey: env.BREVO_API_KEY, resendApiKey: env.RESEND_API_KEY, from: env.EMAIL_FROM });
    reservation = await reserveEmailBudget(env, input.workspaceId, now);
    await sender.send({ to: scope.email_normalized,
      subject: "CY 程式備份異常通知",
      text: `${scope.display_name} 的備份自動檢查發現異常。請登入該程式查看警告及備份／復原狀態，由維運人員確認原因。\n本通知不包含帳務資料，也不會自動還原或切換帳本。` });
    sent = true;
    await env.DB.prepare(`UPDATE consumer_notifications SET sent_at = ?1, lease_token = '', lease_until = '', updated_at = ?1
      WHERE workspace_id = ?2 AND application_id = ?3 AND notification_type = ?4 AND lease_token = ?5`)
      .bind(timestamp, input.workspaceId, input.applicationId, input.type, lease).run();
    return { status: "delivered" as const };
  } catch {
    // Provider diagnostics may include personal data. Only a stable code crosses RPC.
    throw new Error("NOTIFICATION_DELIVERY_FAILED");
  } finally {
    if (reservation) await settleEmailBudget(env, reservation, sent, now);
    if (!sent) await env.DB.prepare(`UPDATE consumer_notifications SET lease_token = '', lease_until = '', updated_at = ?1
      WHERE workspace_id = ?2 AND application_id = ?3 AND notification_type = ?4 AND lease_token = ?5`)
      .bind(timestamp, input.workspaceId, input.applicationId, input.type, lease).run();
  }
}
