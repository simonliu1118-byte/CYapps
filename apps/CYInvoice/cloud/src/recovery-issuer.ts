import { createEmailSender, type EmailSender } from "./email";
import { CyIdError, type CyIdEnv } from "./cyid";
import {
  withAuthorizedCyWebRecovery, type CyWebRecoveryRequest,
  type CyWebRecoveryAuthority,
} from "./recovery-authority";

/**
 * Private, app-scoped issuance owner. The caller is a named Worker RPC, never
 * an HTTP route or a device-authentication bypass.
 *
 * Only the email transport ever sees a newly generated plaintext invitation
 * code. D1 receives its irreversible SHA-256 hash; no raw code, password,
 * CYID token, or destination email is persisted in this business D1.
 */
export interface RecoveryIssuerEnv extends CyIdEnv {
  DB: D1Database;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

export interface RecoveryIssueResult {
  status: "sent" | "pending" | "revoked" | "failed";
  invitationId?: string;
  expiresAt?: string;
}
type RecoveryRow = {
  invitation_id: string;
  workspace_id: string;
  issued_by_cyid_employee_id: string | null;
  issuer_credential_version: number | null;
  issuer_employee_revision: number | null;
  issuer_channel: string;
  purpose: string;
  delivery_state: string;
  expires_at: string;
  revoked_at: string | null;
  consumed_at: string | null;
};
const TEN_MINUTES_MS = 10 * 60 * 1000;

function code(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(20));
  return [...bytes].map(byte => byte.toString(16).padStart(2, "0")).join("");
}
async function digest(codeValue: string): Promise<string> {
  const data = new TextEncoder().encode(codeValue);
  const digestBytes = new Uint8Array(await crypto.subtle.digest("SHA-256", data));
  return [...digestBytes].map(byte => byte.toString(16).padStart(2, "0")).join("");
}
function emailSender(env: RecoveryIssuerEnv): EmailSender {
  try {
    return createEmailSender({
      provider: env.EMAIL_PROVIDER, brevoApiKey: env.BREVO_API_KEY,
      resendApiKey: env.RESEND_API_KEY, from: env.EMAIL_FROM,
    });
  } catch {
    throw new CyIdError(503, "RECOVERY_EMAIL_NOT_CONFIGURED");
  }
}
async function existingOperation(
  db: D1Database, actor: CyWebRecoveryAuthority,
): Promise<RecoveryRow | null> {
  return db.prepare(`SELECT invitation_id, workspace_id, issued_by_cyid_employee_id,
      issuer_credential_version, issuer_employee_revision, issuer_channel,
      purpose, delivery_state, expires_at, revoked_at, consumed_at
    FROM device_invitations
    WHERE workspace_id = ?1 AND recovery_operation_id = ?2 LIMIT 1`)
    .bind(actor.workspaceId, actor.operationId).first<RecoveryRow>();
}
function sameIssuer(row: RecoveryRow, actor: CyWebRecoveryAuthority): void {
  if (row.workspace_id !== actor.workspaceId || row.issuer_channel !== "CYWEB"
      || row.purpose !== "RECOVERY" || row.issued_by_cyid_employee_id !== actor.employeeId
      || row.issuer_credential_version !== actor.credentialVersion
      || row.issuer_employee_revision !== actor.employeeRevision)
    throw new CyIdError(409, "RECOVERY_OPERATION_CONFLICT");
}
function publicState(row: RecoveryRow): RecoveryIssueResult {
  // A consumed invitation is not an administrative revocation. Only a
  // persisted revoked_at is proof that CYWEB may report "revoked".
  if (row.consumed_at) return { status: "failed" };
  if (row.revoked_at) return { status: "revoked" };
  if (row.expires_at <= new Date().toISOString() || row.delivery_state === "failed")
    return { status: "failed" };
  if (row.delivery_state === "sent")
    return { status: "sent", invitationId: row.invitation_id, expiresAt: row.expires_at };
  return { status: "pending" };
}
async function recheckActor(
  verifyNow: () => Promise<CyWebRecoveryAuthority>,
  original: CyWebRecoveryAuthority,
): Promise<CyWebRecoveryAuthority> {
  const current = await verifyNow();
  if (current.employeeId !== original.employeeId || current.workspaceId !== original.workspaceId
      || current.identityWorkspaceId !== original.identityWorkspaceId
      || current.credentialVersion !== original.credentialVersion
      || current.employeeRevision !== original.employeeRevision
      || current.email !== original.email)
    throw new CyIdError(403, "RECOVERY_AUTHORITY_CHANGED");
  return current;
}

/**
 * Only the first successful INSERT of a *new* operation ID can send email.
 * A repeated request with the same operation ID is READ-ONLY even if the
 * previous response or provider result was lost.
 */
export async function issueCyWebRecoveryInvitation(
  env: RecoveryIssuerEnv, raw: CyWebRecoveryRequest,
  senderOverride?: EmailSender,
): Promise<RecoveryIssueResult> {
  return withAuthorizedCyWebRecovery(env, raw, async verifyNow => {
    const actor = await verifyNow();
    const previous = await existingOperation(env.DB, actor);
    if (previous) {
      sameIssuer(previous, actor);
      return publicState(previous);
    }
    const sender = senderOverride ?? emailSender(env);
    await recheckActor(verifyNow, actor);

    const secretCode = code();
    const codeHash = await digest(secretCode);
    const invitationId = `inv_${crypto.randomUUID()}`;
    const createdAt = new Date().toISOString();
    const expiresAt = new Date(Date.parse(createdAt) + TEN_MINUTES_MS).toISOString();

    // No client-supplied Workspace/issuer/device. The second profile and active
    // workspace are verified by the same execution-time SQL that writes.
    // Unique(workspace, operationId) and unique(open RECOVERY grant/workspace)
    // prevent retries and concurrent different operations from emailing twice.
    let inserted: D1Result;
    try {
      inserted = await env.DB.prepare(`INSERT INTO device_invitations (
          invitation_id, workspace_id, code_hash, issued_by_device_id,
          issued_by_employee_id, issued_by_cyid_employee_id, delivery_state,
          expires_at, created_at, issuer_channel, purpose,
          issuer_credential_version, issuer_employee_revision, recovery_operation_id
        )
        SELECT ?1, w.workspace_id, ?2, NULL, NULL, ?3, 'pending', ?4, ?5,
          'CYWEB', 'RECOVERY', ?6, ?7, ?8
        FROM workspaces w
        JOIN cyid_storage_profile cp ON cp.workspace_id = w.workspace_id
          AND cp.profile = 'CYID_ONLY'
        JOIN cyweb_recovery_storage_profile rp ON rp.workspace_id = w.workspace_id
          AND rp.profile = 'CYWEB_RECOVERY'
        WHERE w.workspace_id = ?9 AND w.status = 'active'`)
        .bind(invitationId, codeHash, actor.employeeId, expiresAt, createdAt,
          actor.credentialVersion, actor.employeeRevision, actor.operationId,
          actor.workspaceId).run();
    } catch {
      // Concurrent same-op caller may have won. Never auto-send a second email.
      const concurrent = await existingOperation(env.DB, actor);
      if (concurrent) {
        sameIssuer(concurrent, actor);
        return publicState(concurrent);
      }
      throw new CyIdError(409, "RECOVERY_ALREADY_PENDING");
    }
    if (inserted.meta.changes !== 1)
      throw new CyIdError(409, "RECOVERY_STORAGE_NOT_READY");

    // If authorization was revoked after the pending insert, leave that code
    // forever non-claimable; no email is sent.
    await recheckActor(verifyNow, actor);
    try {
      await sender.send({
        to: actor.email,
        subject: "CYInvoice 裝置災難復原邀請",
        text: `您正在為 CYInvoice 啟動公司級裝置災難復原。\n請在新電腦的 CYInvoice Windows 復原畫面輸入以下一次性邀請碼：\n${secretCode}\n有效至：${expiresAt}\n\n重要：只有新裝置完成復原時，原 Workspace 所有舊裝置才會立即撤銷，舊裝置 Token 將失效。若非本人操作，請立即聯絡管理員撤銷邀請。\n此郵件並不代表復原已完成。`,
        tags: [{ name: "purpose", value: "cyinvoice_recovery" }],
      });
    } catch {
      // An HTTP timeout can happen *after* the provider accepted the email.
      // Do not mark sent, mark definitely failed, or replay the same code:
      // pending remains non-claimable until privately investigated/revoked.
      return { status: "pending" };
    }
    // Revalidate immediately before the only state transition that makes a
    // delivered code claimable. A lost DB write stays pending, never retried.
    await recheckActor(verifyNow, actor);
    const sentAt = new Date().toISOString();
    try {
      await env.DB.batch([
        env.DB.prepare(`INSERT INTO cyweb_recovery_issuer_guard(passed)
          SELECT CASE WHEN EXISTS (
            SELECT 1 FROM device_invitations i
            JOIN workspaces w ON w.workspace_id = i.workspace_id AND w.status = 'active'
            JOIN cyid_storage_profile p ON p.workspace_id = w.workspace_id
              AND p.profile = 'CYID_ONLY'
            JOIN cyweb_recovery_storage_profile r ON r.workspace_id = w.workspace_id
              AND r.profile = 'CYWEB_RECOVERY'
            WHERE i.invitation_id = ?1 AND i.workspace_id = ?2
              AND i.recovery_operation_id = ?3 AND i.issued_by_cyid_employee_id = ?4
              AND i.issuer_credential_version = ?5 AND i.issuer_employee_revision = ?6
              AND i.issuer_channel = 'CYWEB' AND i.purpose = 'RECOVERY'
              AND i.delivery_state = 'pending' AND i.revoked_at IS NULL
              AND i.consumed_at IS NULL AND i.expires_at > ?7
          ) THEN 1 ELSE 0 END`)
          .bind(invitationId, actor.workspaceId, actor.operationId, actor.employeeId,
            actor.credentialVersion, actor.employeeRevision, sentAt),
        env.DB.prepare(`UPDATE device_invitations SET delivery_state='sent', sent_at=?1
          WHERE invitation_id = ?2 AND workspace_id = ?3 AND delivery_state='pending'
            AND consumed_at IS NULL AND revoked_at IS NULL AND expires_at > ?1`)
          .bind(sentAt, invitationId, actor.workspaceId),
        env.DB.prepare(`INSERT INTO security_audit_events (
            event_id, workspace_id, event_type, outcome, actor_employee_id,
            invitation_id, request_id, occurred_at, actor_provider, issuer_channel, purpose
          ) VALUES (?1, ?2, 'recovery_invitation_issued', 'success', ?3,
            ?4, ?5, ?6, 'CYID', 'CYWEB', 'RECOVERY')`)
          .bind(`evt_${crypto.randomUUID()}`, actor.workspaceId, actor.employeeId,
            invitationId, actor.operationId, sentAt),
        env.DB.prepare("DELETE FROM cyweb_recovery_issuer_guard"),
      ]);
    } catch {
      // A lost commit response might mean sent was committed.
      // The caller must query the original operation; never issue another.
      return { status: "pending" };
    }
    return { status: "sent", invitationId, expiresAt };
  });
}

export async function recoveryInvitationStatus(
  env: RecoveryIssuerEnv, raw: CyWebRecoveryRequest,
): Promise<RecoveryIssueResult> {
  return withAuthorizedCyWebRecovery(env, raw, async verifyNow => {
    const actor = await verifyNow();
    const previous = await existingOperation(env.DB, actor);
    if (!previous) return { status: "failed" };
    sameIssuer(previous, actor);
    return publicState(previous);
  });
}

/**
 * Admin cancellation of the SAME operation, used after ambiguous provider
 * response so a fresh operation can be explicitly authorized later. Any
 * current CYInvoice-app SUPER_ADMIN of the same fixed business workspace
 * may revoke an older issuer's pending invitation after a credential change.
 * This is revocation only: it NEVER revokes Devices or issues a new code.
 */
export async function revokeRecoveryInvitation(
  env: RecoveryIssuerEnv, raw: CyWebRecoveryRequest,
): Promise<RecoveryIssueResult> {
  return withAuthorizedCyWebRecovery(env, raw, async verifyNow => {
    const actor = await verifyNow();
    const row = await existingOperation(env.DB, actor);
    if (!row) throw new CyIdError(404, "RECOVERY_OPERATION_NOT_FOUND");
    if (row.workspace_id !== actor.workspaceId ||
        row.issuer_channel !== "CYWEB" || row.purpose !== "RECOVERY")
      throw new CyIdError(409, "RECOVERY_OPERATION_CONFLICT");
    if (row.consumed_at) return { status: "failed" };
    // Repeated explicit revoke and lost-response readback are idempotent.
    if (row.revoked_at) return { status: "revoked" };
    const fresh = await recheckActor(verifyNow, actor);
    const now = new Date().toISOString();
    // Expiry and delivery failure make the code unclaimable, but they do
    // NOT release the unique outstanding Workspace grant. Allow an explicit
    // SUPER_ADMIN cancellation of the original operation after expiry, with
    // the same SQL-time consumed/revoked guard and append-only Audit.
    // Read-only status remains "failed" until this explicit revoke commits.
    try {
      await env.DB.batch([
        env.DB.prepare(`INSERT INTO cyweb_recovery_issuer_guard(passed)
          SELECT CASE WHEN EXISTS (
            SELECT 1 FROM device_invitations i
            JOIN workspaces w ON w.workspace_id = i.workspace_id AND w.status='active'
            JOIN cyid_storage_profile cp ON cp.workspace_id = w.workspace_id AND cp.profile='CYID_ONLY'
            JOIN cyweb_recovery_storage_profile rp ON rp.workspace_id = w.workspace_id AND rp.profile='CYWEB_RECOVERY'
            WHERE i.invitation_id=?1 AND i.workspace_id=?2 AND i.recovery_operation_id=?3
              AND i.issuer_channel='CYWEB' AND i.purpose='RECOVERY'
              AND i.revoked_at IS NULL AND i.consumed_at IS NULL
          ) THEN 1 ELSE 0 END`)
          .bind(row.invitation_id, actor.workspaceId, actor.operationId),
        env.DB.prepare(`UPDATE device_invitations SET revoked_at=?1
          WHERE invitation_id=?2 AND workspace_id=?3
            AND issuer_channel='CYWEB' AND purpose='RECOVERY'
            AND consumed_at IS NULL AND revoked_at IS NULL`)
          .bind(now, row.invitation_id, fresh.workspaceId),
        env.DB.prepare(`INSERT INTO security_audit_events (
            event_id,workspace_id,event_type,outcome,actor_employee_id,
            invitation_id,reason_code,request_id,occurred_at,
            actor_provider,issuer_channel,purpose)
          VALUES (?1,?2,'recovery_invitation_revoked','success',?3,
            ?4,'ADMIN_REVOKED',?5,?6,'CYID','CYWEB','RECOVERY')`)
          .bind(`evt_${crypto.randomUUID()}`, actor.workspaceId, fresh.employeeId,
            row.invitation_id, actor.operationId, now),
        env.DB.prepare("DELETE FROM cyweb_recovery_issuer_guard"),
      ]);
    } catch {
      // Response lost could mean revoke committed; re-query same operation
      // through the authenticated status operation, never retry blindly.
      throw new CyIdError(503, "RECOVERY_RESULT_UNKNOWN");
    }
    return { status: "revoked" };
  });
}

