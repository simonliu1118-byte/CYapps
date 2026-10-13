import { CyIdError } from "./cyid";
import type { CyWebRecoveryAuthority } from "./recovery-authority";

/**
 * A RECOVERY claim is not an ordinary JOIN: accepting one new Device
 * invalidates every other active Device Token for the same business Workspace
 * inside the exact same D1 batch/transaction.
 *
 * The issuer's private RPC, the CYWEB front end, and Windows transport remain
 * separate concerns. This module is internal until those owners exist.
 */
export interface RecoveryClaimInput {
  code: string;
  deviceToken: string;
  deviceDisplayName: string;
  clientVersion: string;
  requestId: string;
}
export interface RecoveryClaimResult {
  recovered: boolean;
  workspaceId: string;
  deviceId: string;
  deviceDisplayName: string;
}
export interface RecoveryClaimEnv { DB: D1Database; }

type Invitation = {
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
  consumed_by_device_id: string | null;
};
type ClaimedDevice = {
  device_id: string;
  workspace_id: string;
  display_name: string;
  invitation_id: string | null;
  status: string;
};

async function sha256(value: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return Array.from(digest, n => n.toString(16).padStart(2, "0")).join("");
}
function checkInput(value: RecoveryClaimInput): void {
  if (!value || typeof value !== "object"
      || typeof value.code !== "string" || !/^[0-9a-f]{40}$/i.test(value.code)
      || typeof value.deviceToken !== "string" || !/^cydev_[0-9a-f]{64}$/i.test(value.deviceToken)
      || typeof value.deviceDisplayName !== "string" ||
      value.deviceDisplayName.trim().length < 1 || value.deviceDisplayName.trim().length > 120
      || /[\x00-\x1f\x7f]/.test(value.deviceDisplayName)
      || typeof value.clientVersion !== "string" || value.clientVersion.trim().length < 1
      || value.clientVersion.trim().length > 64 || /[\x00-\x1f\x7f]/.test(value.clientVersion)
      || typeof value.requestId !== "string" || !/^[A-Za-z0-9._:-]{1,128}$/.test(value.requestId))
    throw new CyIdError(400, "INVALID_RECOVERY_CLAIM");
}
function checkIssuer(i: Invitation | null, actor: CyWebRecoveryAuthority): asserts i is Invitation {
  if (!i || i.workspace_id !== actor.workspaceId || i.issuer_channel !== "CYWEB"
      || i.purpose !== "RECOVERY" || i.issued_by_cyid_employee_id !== actor.employeeId
      || i.issuer_credential_version !== actor.credentialVersion
      || i.issuer_employee_revision !== actor.employeeRevision)
    throw new CyIdError(409, "RECOVERY_INVITATION_INVALID");
}

/**
 * verifyNow must come from withAuthorizedCyWebRecovery or an equivalently
 * revalidated CYInvoice app-scoped SUPER_ADMIN session, never browser claims.
 * Caller must retain the same protected pending token for result-loss retries.
 */
export async function claimRecoveryAndRevokeOldDevices(
  env: RecoveryClaimEnv,
  input: RecoveryClaimInput,
  verifyNow: () => Promise<CyWebRecoveryAuthority>,
): Promise<RecoveryClaimResult> {
  checkInput(input);
  const codeHash = await sha256(input.code.toLowerCase());
  const tokenHash = await sha256(input.deviceToken.toLowerCase());
  const actor = await verifyNow();
  const invitation = await env.DB.prepare(`SELECT invitation_id, workspace_id,
      issued_by_cyid_employee_id, issuer_credential_version, issuer_employee_revision,
      issuer_channel, purpose, delivery_state, expires_at, revoked_at, consumed_at,
      consumed_by_device_id
    FROM device_invitations WHERE code_hash = ?1 LIMIT 1`)
    .bind(codeHash).first<Invitation>();
  checkIssuer(invitation, actor);
  const existing = await env.DB.prepare(`SELECT device_id, workspace_id, display_name,
      status, invitation_id FROM devices WHERE token_hash = ?1 LIMIT 1`)
    .bind(tokenHash).first<ClaimedDevice>();
  if (existing) {
    if (existing.workspace_id !== actor.workspaceId || existing.status !== "active"
        || existing.invitation_id !== invitation.invitation_id
        || invitation.consumed_by_device_id !== existing.device_id || !invitation.consumed_at)
      throw new CyIdError(409, "RECOVERY_TOKEN_CONFLICT");
    // Lost-response readback: never revoke all Devices *again* or add another.
    await verifyNow();
    return { recovered: true, workspaceId: actor.workspaceId,
      deviceId: existing.device_id, deviceDisplayName: existing.display_name };
  }
  const now = new Date().toISOString();
  if (invitation.delivery_state !== "sent" || invitation.revoked_at || invitation.consumed_at
      || invitation.expires_at <= now)
    throw new CyIdError(409, "RECOVERY_INVITATION_UNAVAILABLE");

  // Recheck *after* reading current invitation and immediately before D1 write.
  const fresh = await verifyNow();
  if (fresh.employeeId !== actor.employeeId || fresh.workspaceId !== actor.workspaceId
      || fresh.credentialVersion !== actor.credentialVersion
      || fresh.employeeRevision !== actor.employeeRevision)
    throw new CyIdError(403, "RECOVERY_AUTHORITY_CHANGED");

  const id = `dev_${crypto.randomUUID()}`;
  const workspace = actor.workspaceId;
  try {
    // D1 batch is transactional. A guard CHECK failure or UNIQUE violation
    // rolls back the insert, invitation consume, full-fleet revocation,
    // all outstanding JOIN/RECOVERY grants, and every success audit together.
    // Avoid post-batch "changes != 1" guards: they cannot roll back a commit.
    await env.DB.batch([
      env.DB.prepare(`INSERT INTO cyweb_recovery_claim_guard(passed)
        SELECT CASE WHEN EXISTS (
          SELECT 1 FROM device_invitations i
          JOIN workspaces w ON w.workspace_id = i.workspace_id AND w.status = 'active'
          JOIN cyid_storage_profile c ON c.workspace_id = w.workspace_id AND c.profile = 'CYID_ONLY'
          JOIN cyweb_recovery_storage_profile p ON p.workspace_id = w.workspace_id
            AND p.profile = 'CYWEB_RECOVERY'
          WHERE i.invitation_id = ?1 AND i.workspace_id = ?2 AND i.code_hash = ?3
            AND i.issuer_channel = 'CYWEB' AND i.purpose = 'RECOVERY'
            AND i.issued_by_cyid_employee_id = ?4
            AND i.issuer_credential_version = ?5 AND i.issuer_employee_revision = ?6
            AND i.delivery_state = 'sent' AND i.revoked_at IS NULL
            AND i.consumed_at IS NULL AND i.expires_at > ?7
        ) AND NOT EXISTS (SELECT 1 FROM devices WHERE token_hash = ?8)
        THEN 1 ELSE 0 END`)
        .bind(invitation.invitation_id, workspace, codeHash, fresh.employeeId,
          fresh.credentialVersion, fresh.employeeRevision, now, tokenHash),
      env.DB.prepare(`INSERT INTO devices (device_id, workspace_id, display_name, token_hash,
          token_created_at, client_version, status, invitation_id, employee_authority_state,
          employee_transition_completed_at, paired_at, created_at, updated_at)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'active', ?7, 'cloud', ?5, ?5, ?5, ?5)`)
        .bind(id, workspace, input.deviceDisplayName.trim(), tokenHash, now,
          input.clientVersion.trim(), invitation.invitation_id),
      env.DB.prepare(`UPDATE device_invitations SET consumed_at = ?1,
          consumed_by_device_id = ?2
        WHERE invitation_id = ?3 AND workspace_id = ?4`)
        .bind(now, id, invitation.invitation_id, workspace),
      env.DB.prepare(`UPDATE devices SET status = 'revoked',
          revoked_at = ?1, updated_at = ?1
        WHERE workspace_id = ?2 AND device_id <> ?3 AND status = 'active'`)
        .bind(now, workspace, id),
      env.DB.prepare(`INSERT INTO security_audit_events (
          event_id, workspace_id, event_type, outcome, actor_employee_id,
          target_device_id, invitation_id, reason_code, request_id, occurred_at,
          actor_provider, issuer_channel, purpose)
        SELECT 'evt_' || lower(hex(randomblob(16))), ?1, 'device_revoked', 'success',
          ?2, d.device_id, ?3, 'RECOVERY_CUTOVER', ?4, ?5,
          'CYID', 'CYWEB', 'RECOVERY'
        FROM devices d WHERE d.workspace_id = ?1 AND d.device_id <> ?6
          AND d.status = 'revoked' AND d.revoked_at = ?5`)
        .bind(workspace, fresh.employeeId, invitation.invitation_id, input.requestId, now, id),
      env.DB.prepare(`UPDATE device_invitations SET revoked_at = ?1
        WHERE workspace_id = ?2 AND invitation_id <> ?3
          AND consumed_at IS NULL AND revoked_at IS NULL`)
        .bind(now, workspace, invitation.invitation_id),
      env.DB.prepare(`UPDATE device_pairing_codes SET revoked_at = ?1
        WHERE workspace_id = ?2 AND used_at IS NULL AND revoked_at IS NULL`)
        .bind(now, workspace),
      env.DB.prepare(`INSERT INTO security_audit_events (
          event_id, workspace_id, event_type, outcome, actor_employee_id,
          target_device_id, invitation_id, request_id, occurred_at,
          actor_provider, issuer_channel, purpose)
        VALUES (?1, ?2, 'recovery_device_joined', 'success', ?3, ?4,
          ?5, ?6, ?7, 'CYID', 'CYWEB', 'RECOVERY')`)
        .bind(`evt_${crypto.randomUUID()}`, workspace, fresh.employeeId,
          id, invitation.invitation_id, input.requestId, now),
      env.DB.prepare(`INSERT INTO cyweb_recovery_claim_guard(passed)
        SELECT CASE WHEN EXISTS (
          SELECT 1 FROM devices WHERE device_id = ?1 AND workspace_id = ?2
            AND status = 'active' AND invitation_id = ?3
        ) AND EXISTS (
          SELECT 1 FROM device_invitations WHERE invitation_id = ?3 AND workspace_id = ?2
            AND consumed_by_device_id = ?1 AND consumed_at = ?4
        ) AND NOT EXISTS (
          SELECT 1 FROM devices WHERE workspace_id = ?2 AND device_id <> ?1 AND status = 'active'
        ) AND NOT EXISTS (
          SELECT 1 FROM device_invitations WHERE workspace_id = ?2
            AND invitation_id <> ?3 AND consumed_at IS NULL AND revoked_at IS NULL
        ) AND NOT EXISTS (
          SELECT 1 FROM device_pairing_codes WHERE workspace_id = ?2
            AND used_at IS NULL AND revoked_at IS NULL
        ) THEN 1 ELSE 0 END`)
        .bind(id, workspace, invitation.invitation_id, now),
      env.DB.prepare("DELETE FROM cyweb_recovery_claim_guard"),
    ]);
  } catch {
    // A lost D1 response may have committed. Windows must probe with the SAME
    // protected pending Token; never generate another Token or retry blindly.
    throw new CyIdError(503, "RECOVERY_RESULT_UNKNOWN");
  }
  return { recovered: false, workspaceId: workspace, deviceId: id,
    deviceDisplayName: input.deviceDisplayName.trim() };
}
