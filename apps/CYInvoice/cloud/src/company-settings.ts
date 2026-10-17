// Staged V3.0 CYID company settings. The HTTP route is disabled by default
// until an explicitly approved original-D1 migration and feature enablement.
// Decrypted credentials are returned only by explicit SUPER_ADMIN provisioning
// to the same authenticated Device; normal reads never include secrets.
export type SecretChange =
  | { action: "keep" }
  | { action: "clear" }
  | { action: "replace"; value: string };

export interface CompanySettingsMutation {
  workspaceId: string;
  deviceId: string;
  expectedRevision: number; // zero means the first card
  sellerInvoice: string;
  amegoAppKey: SecretChange;
  moExcelPassword: SecretChange;
}
export interface CurrentCyIdSuperAdmin {
  workspaceId: string;
  employeeId: string;
  workspaceRole: "SUPER_ADMIN" | "ADMIN" | "USER";
  isWorkspaceSuperAdmin: boolean;
  emailVerified: boolean;
}
export interface CompanySettingsAuthority {
  revalidate(): Promise<CurrentCyIdSuperAdmin | null>;
}
export interface CompanySettingsStatus {
  sellerInvoice: string;
  revision: number;
  hasAmegoAppKey: boolean;
  hasMoExcelPassword: boolean;
}
interface CompanyRow {
  seller_invoice: string;
  revision: number;
  amego_app_key_ciphertext: string | null;
  mo_excel_password_ciphertext: string | null;
}

const te = new TextEncoder();
const td = new TextDecoder();
// BufferSource expects ArrayBuffer-backed views in the Workers TS DOM types.
const utf8 = (s: string): Uint8Array<ArrayBuffer> => new Uint8Array(te.encode(s));
const hex = (b: Uint8Array): string =>
  Array.from(b, c => c.toString(16).padStart(2, "0")).join("");
function bytes(value: string): Uint8Array<ArrayBuffer> {
  if (value.length % 2 || !/^[0-9a-f]+$/i.test(value))
    throw new Error("COMPANY_CRYPTO_FORMAT_INVALID");
  const out = new Uint8Array(new ArrayBuffer(value.length / 2));
  for (let i = 0; i < out.length; i++) out[i] = parseInt(value.slice(i*2,i*2+2),16);
  return out;
}
async function keyFromSecret(keyHex: string): Promise<CryptoKey> {
  if (typeof keyHex !== "string" || !/^[0-9a-f]{64}$/i.test(keyHex))
    throw new Error("COMPANY_ENCRYPTION_NOT_CONFIGURED");
  return crypto.subtle.importKey("raw", bytes(keyHex), {name:"AES-GCM"}, false,
    ["encrypt","decrypt"]);
}
function context(workspaceId: string, field: string): Uint8Array<ArrayBuffer> {
  // Prevent ciphertext copying across workspace and across secret slots.
  return utf8("CYInvoice/company-settings/v1/"+workspaceId+"/"+field);
}
async function seal(key: CryptoKey, scope: string, field: string, plaintext: string): Promise<string> {
  const iv = crypto.getRandomValues(new Uint8Array(new ArrayBuffer(12)));
  const result = await crypto.subtle.encrypt({
    name:"AES-GCM",iv,additionalData:context(scope,field),tagLength:128
  },key,utf8(plaintext));
  return "v1:"+hex(iv)+":"+hex(new Uint8Array(result));
}
async function open(key: CryptoKey, scope: string, field: string, payload: string): Promise<string> {
  const parts = payload.split(":");
  if (parts.length!==3 || parts[0]!=="v1" || !/^[0-9a-f]{24}$/.test(parts[1])
      || !/^[0-9a-f]+$/.test(parts[2]) || parts[2].length<32)
    throw new Error("COMPANY_CIPHERTEXT_INVALID");
  try {
    const result = await crypto.subtle.decrypt({
      name:"AES-GCM",iv:bytes(parts[1]),additionalData:context(scope,field),tagLength:128
    },key,bytes(parts[2]));
    return td.decode(result);
  } catch { throw new Error("COMPANY_CIPHERTEXT_AUTH_FAILED"); }
}
function assertScope(workspaceId: string, deviceId: string): void {
  if (!/^ws_[0-9a-zA-Z_-]{3,100}$/.test(workspaceId)
      || !/^dev_[0-9a-f-]{36}$/.test(deviceId))
    throw new Error("COMPANY_SCOPE_INVALID");
}
function validChange(value: SecretChange, maxLength: number, preserveExactValue = false): boolean {
  if (!value || !["keep","clear","replace"].includes(value.action)) return false;
  if (value.action === "replace")
    return typeof value.value === "string" && value.value.length>0
      && value.value.length<=maxLength && !/[\u0000-\u001f\u007f]/u.test(value.value)
      // SettingsStore.SetProductionAppKey trims the AMEGO key before DPAPI.
      // Reject input that would be changed silently after provisioning.
      && (!preserveExactValue || value.value === value.value.trim());
  return !("value" in value);
}
function superAdmin(p: CurrentCyIdSuperAdmin | null, workspaceId: string): p is CurrentCyIdSuperAdmin {
  return !!p && p.workspaceId === workspaceId && p.workspaceRole === "SUPER_ADMIN"
    && p.isWorkspaceSuperAdmin === true && p.emailVerified === true
    && typeof p.employeeId === "string" && p.employeeId.length>=1
    && p.employeeId.length<=100;
}
async function state(db: D1Database, workspaceId: string): Promise<CompanyRow | null> {
  return db.prepare("SELECT seller_invoice,revision,amego_app_key_ciphertext,mo_excel_password_ciphertext "+
    "FROM company_settings WHERE workspace_id=?1 LIMIT 1").bind(workspaceId).first<CompanyRow>();
}
function status(row: CompanyRow): CompanySettingsStatus {
  return {sellerInvoice:row.seller_invoice,revision:row.revision,
    hasAmegoAppKey:!!row.amego_app_key_ciphertext,
    hasMoExcelPassword:!!row.mo_excel_password_ciphertext};
}
// Safe metadata only. A normal ADMIN can read card status; never the stored envelopes.
export async function getCompanySettingsStatus(db: D1Database, workspaceId: string,
  authority: CompanySettingsAuthority): Promise<CompanySettingsStatus | null> {
  const p=await authority.revalidate();
  if (!p || p.workspaceId!==workspaceId || !["ADMIN","SUPER_ADMIN"].includes(p.workspaceRole))
    throw new Error("COMPANY_STATUS_AUTH_REQUIRED");
  const row=await state(db,workspaceId);
  return row ? status(row) : null;
}
export async function changeCompanySettings(db: D1Database,
  input: CompanySettingsMutation, authority: CompanySettingsAuthority,
  encryptionKeyHex: string): Promise<CompanySettingsStatus> {
  assertScope(input.workspaceId,input.deviceId);
  if (!Number.isSafeInteger(input.expectedRevision) || input.expectedRevision<0
    || !/^[0-9]{8}$/.test(input.sellerInvoice)
    || !validChange(input.amegoAppKey,512,true) || !validChange(input.moExcelPassword,256))
    throw new Error("COMPANY_SETTINGS_INVALID");
  const before=await authority.revalidate();
  if (!superAdmin(before,input.workspaceId)) throw new Error("COMPANY_SUPER_ADMIN_REQUIRED");
  const previous=await state(db,input.workspaceId);
  if ((previous?.revision ?? 0)!==input.expectedRevision)
    throw new Error("COMPANY_SETTINGS_STALE_REVISION");
  // Never insert a partly configured company card because a blank field
  // silently discarded a previously saved secret. Every change is explicit.
  const key=await keyFromSecret(encryptionKeyHex);
  const credential=async (change: SecretChange, existing: string | null, field: string) =>
    change.action==="keep" ? existing : change.action==="clear" ? null :
      seal(key,input.workspaceId,field,change.value);
  const appKey=await credential(input.amegoAppKey,previous?.amego_app_key_ciphertext??null,"amego");
  const moPassword=await credential(input.moExcelPassword,previous?.mo_excel_password_ciphertext??null,"mo");
  const current=await authority.revalidate();
  if (!superAdmin(current,input.workspaceId) || current.employeeId!==before.employeeId)
    throw new Error("COMPANY_AUTHORITY_CHANGED");
  const at=new Date().toISOString();
  const fields=[
    ...(previous?.seller_invoice===input.sellerInvoice?[]:["sellerInvoice"]),
    ...(input.amegoAppKey.action==="keep"?[]:["amegoAppKey"]),
    ...(input.moExcelPassword.action==="keep"?[]:["moExcelPassword"]),
  ];
  if (fields.length===0) throw new Error("COMPANY_NO_CHANGES");
  const guardedDevice = "EXISTS (SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id "+
    "WHERE d.device_id=?6 AND d.workspace_id=?1 AND d.status='active' "+
    "AND d.employee_authority_state='cloud' AND w.status='active')";
  const sql=input.expectedRevision===0
    ? "INSERT INTO company_settings (workspace_id,seller_invoice,amego_app_key_ciphertext,"+
      "mo_excel_password_ciphertext,revision,changed_by_employee_id,changed_by_device_id,changed_at) "+
      "SELECT ?1,?2,?3,?4,1,?5,?6,?7 WHERE "+guardedDevice+
      " AND NOT EXISTS (SELECT 1 FROM company_settings WHERE workspace_id=?1)"
    : "UPDATE company_settings SET seller_invoice=?2,amego_app_key_ciphertext=?3,"+
      "mo_excel_password_ciphertext=?4,revision=revision+1,changed_by_employee_id=?5,"+
      "changed_by_device_id=?6,changed_at=?7 WHERE workspace_id=?1 AND revision=?8 AND "+guardedDevice;
  // D1.batch commits the card and audit together or not at all.
  const params=[input.workspaceId,input.sellerInvoice,appKey,moPassword,
    before.employeeId,input.deviceId,at];
  const update=db.prepare(sql).bind(...(input.expectedRevision===0?params:[...params,input.expectedRevision]));
  const audit=db.prepare("INSERT INTO company_settings_audit_events "+
    "(event_id,workspace_id,revision,actor_employee_id,actor_device_id,changed_fields,created_at) "+
    "SELECT ?1,?2,?3,?4,?5,?6,?7 WHERE EXISTS "+
    "(SELECT 1 FROM company_settings WHERE workspace_id=?2 AND revision=?3 "+
    "AND changed_by_employee_id=?4 AND changed_by_device_id=?5 AND changed_at=?7)")
    .bind("csa_"+crypto.randomUUID(),input.workspaceId,input.expectedRevision+1,
      before.employeeId,input.deviceId,JSON.stringify(fields),at);
  let results: D1Result[];
  try { results=await db.batch([update,audit]); }
  catch { throw new Error("COMPANY_MUTATION_UNKNOWN_READBACK_REQUIRED"); }
  if (results[0].meta.changes!==1 || results[1].meta.changes!==1)
    throw new Error("COMPANY_SETTINGS_CONFLICT_OR_DEVICE_REVOKED");
  return {sellerInvoice:input.sellerInvoice,revision:input.expectedRevision+1,
    hasAmegoAppKey:!!appKey,hasMoExcelPassword:!!moPassword};
}
// Explicit SUPER_ADMIN action from an authenticated, active CYID Device only.
// This is the one controlled transport used to install credentials into
// Windows DPAPI; never call it from status/get or an ordinary USER session.
export async function provisionCompanySettingsToDevice(
  db:D1Database, workspaceId:string,deviceId:string,
  authority:CompanySettingsAuthority, encryptionKeyHex:string) {
  assertScope(workspaceId,deviceId);
  const before=await authority.revalidate();
  if(!superAdmin(before,workspaceId))throw new Error("COMPANY_SUPER_ADMIN_REQUIRED");
  const row=await state(db,workspaceId);
  if(!row?.amego_app_key_ciphertext||!row.mo_excel_password_ciphertext)
    throw new Error("COMPANY_PROVISION_INCOMPLETE");
  const key=await keyFromSecret(encryptionKeyHex);
  const amegoAppKey=await open(key,workspaceId,"amego",row.amego_app_key_ciphertext);
  const moExcelPassword=await open(key,workspaceId,"mo",row.mo_excel_password_ciphertext);
  if(!amegoAppKey.trim()||!moExcelPassword.trim())
    throw new Error("COMPANY_PROVISION_INCOMPLETE");
  const after=await authority.revalidate();
  if(!superAdmin(after,workspaceId)||after.employeeId!==before.employeeId)
    throw new Error("COMPANY_AUTHORITY_CHANGED");
  // Recheck original active Device and card revision after decryption;
  // revoked Devices or concurrently edited cards cannot receive old secrets.
  const active=await db.prepare("SELECT 1 AS permitted FROM devices d JOIN workspaces w"+
    " ON w.workspace_id=d.workspace_id WHERE d.device_id=?1 AND d.workspace_id=?2"+
    " AND d.status='active' AND d.employee_authority_state='cloud'"+
    " AND w.status='active' LIMIT 1").bind(deviceId,workspaceId)
      .first<{permitted:number}>();
  if(!active?.permitted)throw new Error("COMPANY_DEVICE_REVOKED");
  const latest=await state(db,workspaceId);
  if(!latest||latest.revision!==row.revision||
    latest.seller_invoice!==row.seller_invoice||
    latest.amego_app_key_ciphertext!==row.amego_app_key_ciphertext||
    latest.mo_excel_password_ciphertext!==row.mo_excel_password_ciphertext)
    throw new Error("COMPANY_PROVISION_CHANGED");
  return {sellerInvoice:row.seller_invoice,revision:row.revision,
    amegoAppKey,moExcelPassword};
}

// INTERNAL trusted Worker gateway only, never a browser-facing response.
export async function loadAmegoCompanySettingsForGateway(db: D1Database,workspaceId: string,
  encryptionKeyHex: string) {
  const row=await state(db,workspaceId);
  if (!row || !row.amego_app_key_ciphertext) throw new Error("COMPANY_AMEGO_NOT_CONFIGURED");
  const key=await keyFromSecret(encryptionKeyHex);
  // Least privilege: the AMEGO caller must NOT receive the MO Excel password.
  // A future Windows DPAPI credential transfer needs its own protected contract.
  return {sellerInvoice:row.seller_invoice,revision:row.revision,
    amegoAppKey:await open(key,workspaceId,"amego",row.amego_app_key_ciphertext)};
}

// INTERNAL reusable crypto owner for an exceptional Work Item's full template.
// Uses the same injected KEK but distinct AAD for each Work Item and field.
export async function encryptCompanyBusinessData(keyHex: string, workspaceId: string,
  dataType: string, value: string): Promise<string> {
  return seal(await keyFromSecret(keyHex),workspaceId,dataType,value);
}
export async function decryptCompanyBusinessData(keyHex: string, workspaceId: string,
  dataType: string, envelope: string): Promise<string> {
  return open(await keyFromSecret(keyHex),workspaceId,dataType,envelope);
}
