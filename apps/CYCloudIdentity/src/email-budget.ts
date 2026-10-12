import { globalEmailDailyCeiling, loadWorkspaceSecurityPolicy } from "./security-policy";
import type { Env } from "./types";

export interface EmailBudgetReservation {
  usageDate: string;
  workspaceId: string | null;
}

/** Both Email flows reserve from the same global and Workspace daily counters. */
export async function reserveEmailBudget(
  env: Env, workspaceId: string | null, now: Date,
  workspaceLimit?: number | null,
): Promise<EmailBudgetReservation> {
  const limit = workspaceId && workspaceLimit === undefined
    ? (await loadWorkspaceSecurityPolicy(env, workspaceId)).emailDailyLimit
    : workspaceLimit;
  const reservation = { usageDate: now.toISOString().slice(0, 10), workspaceId: null as string | null };
  const global = await env.DB.prepare(
    `INSERT INTO email_delivery_budget(usage_date_utc, reserved_count, sent_count, updated_at)
     VALUES(?1, 1, 0, ?2)
     ON CONFLICT(usage_date_utc) DO UPDATE SET reserved_count = reserved_count + 1, updated_at = excluded.updated_at
     WHERE email_delivery_budget.sent_count + email_delivery_budget.reserved_count < ?3
     RETURNING reserved_count`
  ).bind(reservation.usageDate, now.toISOString(), globalEmailDailyCeiling(env)).first();
  if (!global) throw new Error("EMAIL_DAILY_BUDGET_EXHAUSTED");
  try {
    if (workspaceId && limit != null) {
      const workspace = await env.DB.prepare(
        `INSERT INTO workspace_email_delivery_budget(workspace_id, usage_date_utc, reserved_count, sent_count, updated_at)
         VALUES(?1, ?2, 1, 0, ?3)
         ON CONFLICT(workspace_id, usage_date_utc) DO UPDATE SET reserved_count = reserved_count + 1, updated_at = excluded.updated_at
         WHERE workspace_email_delivery_budget.sent_count + workspace_email_delivery_budget.reserved_count < ?4
         RETURNING reserved_count`
      ).bind(workspaceId, reservation.usageDate, now.toISOString(), limit).first();
      if (!workspace) throw new Error("WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED");
      reservation.workspaceId = workspaceId;
    }
    return reservation;
  } catch (error) {
    await settleEmailBudget(env, reservation, false);
    throw error;
  }
}

/** Settle once per reservation; both counters commit in one D1 transaction. */
export async function settleEmailBudget(
  env: Env, reservation: EmailBudgetReservation, sent: boolean, now = new Date(),
): Promise<void> {
  const statements = [env.DB.prepare(
    `UPDATE email_delivery_budget
        SET reserved_count = CASE WHEN reserved_count > 0 THEN reserved_count - 1 ELSE 0 END,
            sent_count = sent_count + ?2, updated_at = ?3
      WHERE usage_date_utc = ?1`
  ).bind(reservation.usageDate, sent ? 1 : 0, now.toISOString())];
  if (reservation.workspaceId) statements.push(env.DB.prepare(
    `UPDATE workspace_email_delivery_budget
        SET reserved_count = CASE WHEN reserved_count > 0 THEN reserved_count - 1 ELSE 0 END,
            sent_count = sent_count + ?3, updated_at = ?4
      WHERE workspace_id = ?1 AND usage_date_utc = ?2`
  ).bind(reservation.workspaceId, reservation.usageDate, sent ? 1 : 0, now.toISOString()));
  await env.DB.batch(statements);
}
