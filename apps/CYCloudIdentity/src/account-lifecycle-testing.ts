// The 2026-10-15 testing exception is retired. Keep these source-owned
// compatibility indicators until a coordinated consumer contract removes them.
// Verified-account deletion is forbidden by the normal server-side delete path.
export const ACCOUNT_LIFECYCLE_TESTING = {
  allowVerifiedDeletion: false,
  recordCreateDeleteAudit: true,
} as const;
