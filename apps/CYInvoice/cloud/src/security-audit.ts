export type SecurityEventType =
  | "pairing_email" | "pairing_issued" | "pairing_verified" | "pairing_claim_denied" | "device_joined"
  | "invitation_issued" | "invitation_delivery_failed" | "invitation_revoked"
  | "invitation_verified" | "invitation_claim_denied";

export type SecurityEvent = {
  workspaceId: string;
  type: SecurityEventType;
  outcome: "success" | "denied" | "failed";
  requestId: string;
  actorDeviceId?: string;
  actorEmployeeId?: string;
  targetDeviceId?: string;
  pairingId?: string;
  invitationId?: string;
  reasonCode?: string;
};

export function securityEventStatement(db: D1Database, event: SecurityEvent): D1PreparedStatement {
  return db.prepare(
    `INSERT INTO security_audit_events (
       event_id, workspace_id, event_type, outcome, actor_device_id,
       actor_employee_id, target_device_id, pairing_id, invitation_id,
       reason_code, request_id, occurred_at
     ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)`
  ).bind(`evt_${crypto.randomUUID()}`, event.workspaceId, event.type, event.outcome,
    event.actorDeviceId ?? null, event.actorEmployeeId ?? null,
    event.targetDeviceId ?? null, event.pairingId ?? null,
    event.invitationId ?? null, event.reasonCode ?? null,
    event.requestId, new Date().toISOString());
}

export async function recordSecurityEvent(db: D1Database, event: SecurityEvent): Promise<void> {
  await securityEventStatement(db, event).run();
}
