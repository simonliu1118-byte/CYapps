export interface Env {
  DB: D1Database;
  APP_ENV?: string;
  API_VERSION?: string;
  SESSION_TTL_SECONDS?: string;
}

export interface IdentityPrincipal {
  workspaceId: string;
  employeeId: string;
  employeeNo: string;
  displayName: string;
  isWorkspaceSuperAdmin: boolean;
  groupKeys: string[];
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
