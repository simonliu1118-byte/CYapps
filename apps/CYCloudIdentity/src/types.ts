export interface Env {
  DB: D1Database;
  LOGIN_RATE_LIMITER?: RateLimit;
  APP_ENV?: string;
  API_VERSION?: string;
  SESSION_TTL_SECONDS?: string;
  EMAIL_PROVIDER?: string;
  EMAIL_FROM?: string;
  EMAIL_DAILY_BUDGET?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  OTP_PEPPER?: string;
  BOOTSTRAP_SECRET?: string;
  CORE_ACCOUNT_APPLICATION_ID?: string;
  ACCOUNT_PORTAL_URL?: string;
}

export type WorkspaceRole = "USER" | "ADMIN" | "SUPER_ADMIN";

/** @deprecated Compatibility alias while existing consumers cut over to workspaceRole. */
export type ApplicationRoleKey = WorkspaceRole;

export interface IdentityPrincipal {
  workspaceId: string;
  employeeId: string;
  employeeNo: string;
  displayName: string;
  workspaceRole: WorkspaceRole;
  isIdentityAdmin: boolean;
  emailVerified: boolean;
  isWorkspaceSuperAdmin: boolean;
  /** @deprecated Legacy descriptive data. New authorization must not depend on Groups. */
  groupKeys: string[];
  /** @deprecated Compatibility alias. Consumers should read workspaceRole. */
  applicationRoleKey?: ApplicationRoleKey;
  credentialVersion: number;
  employeeRevision: number;
}

export interface IdentitySessionView {
  token: string;
  expiresAt: string;
}

export interface LoginSuccess {
  principal: IdentityPrincipal;
  session: IdentitySessionView;
}

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };
