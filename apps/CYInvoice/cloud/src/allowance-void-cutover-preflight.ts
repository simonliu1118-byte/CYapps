// V3.2 D/E cutover preflight: PURE / READ-ONLY / NOT AN AUTHORITY.
// A local device report may be stale, spoofed, or omit an unknown installation.
// This assessment NEVER activates the D1 cloud_exclusive gate, sends g0501,
// or proves that all offline legacy clients have been accounted for.
// An independently verified inventory and controlled handoff remain mandatory.
export interface LocalVoidCutoverDeviceEvidence {
  installationId: string;
  capturedAtUtc: string;
  evidenceSha256: string;
  localG0501Disabled: boolean;
  durableQuarantineVerified: boolean;
  deviceStoppedOrMigrated: boolean;
  knownClaimCount: number;
  unresolvedClaimCount: number;
  historicalClaimsReconciled: boolean;
}
export interface LocalVoidCutoverPreflightInput {
  workspaceId: string;
  environment: "test" | "production";
  sellerInvoice: string;
  companyRevision: number;
  assessedAtUtc: string;
  externalInventoryVerified: boolean;
  unknownLegacyInstallationsExcluded: boolean;
  inventoryEvidenceSha256: string;
  expectedInstallationIds: string[];
  devices: LocalVoidCutoverDeviceEvidence[];
}
export type CutoverBlocker =
  | "INVALID_SCOPE"
  | "INVENTORY_NOT_INDEPENDENTLY_VERIFIED"
  | "UNKNOWN_LEGACY_INSTALLATIONS"
  | "INVENTORY_MISSING_OR_DUPLICATED"
  | "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED"
  | "EVIDENCE_STALE_OR_FUTURE"
  | "LOCAL_SEND_STILL_POSSIBLE"
  | "DURABLE_QUARANTINE_UNVERIFIED"
  | "DEVICE_NOT_STOPPED_OR_MIGRATED"
  | "UNRESOLVED_LOCAL_CLAIM"
  | "LOCAL_CLAIM_HISTORY_NOT_RECONCILED";
export interface LocalVoidCutoverAssessment {
  status: "blocked" | "candidate_for_independent_review";
  blockers: CutoverBlocker[];
  inventorySummarySha256: string | null;
  assessedInstallations: number;
  // Always false, including when every snapshot looks acceptable.
  permitsCloudG0501: false;
  activatesCloudGate: false;
  provesUnknownOfflineDevicesAbsent: false;
}
const HASH=/^[0-9a-f]{64}$/;
const ID=/^[A-Za-z0-9_-]{6,100}$/;
const MAX_SNAPSHOT_AGE_MS=60*60*1000; // engineering preflight freshness only
const validCount=(n:number)=>Number.isSafeInteger(n)&&n>=0;
const time=(value:string):number=>{
  if(typeof value!=="string"||!/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,3})?Z$/.test(value))
    return NaN;
  const ms=Date.parse(value);
  return Number.isFinite(ms)?ms:NaN;
};
function canonicalDevice(id:string):string {
  // Deliberately not tied to CYID Device IDs; a legacy Local installation
  // might not have been registered with Cloud at all.
  return typeof id==="string"&&ID.test(id)?id:"";
}
export async function assessLocalVoidCutover(
  input:LocalVoidCutoverPreflightInput
):Promise<LocalVoidCutoverAssessment>{
  const errors=new Set<CutoverBlocker>();
  const assessed=time(input?.assessedAtUtc);
  if(!input||!ID.test(input.workspaceId)||
    (input.environment!=="test"&&input.environment!=="production")||
    !/^\d{8}$/.test(input.sellerInvoice)||
    !Number.isSafeInteger(input.companyRevision)||input.companyRevision<1||
    !Number.isFinite(assessed))
    errors.add("INVALID_SCOPE");
  if(input?.externalInventoryVerified!==true||!HASH.test(input?.inventoryEvidenceSha256??""))
    errors.add("INVENTORY_NOT_INDEPENDENTLY_VERIFIED");
  if(input?.unknownLegacyInstallationsExcluded!==true)
    errors.add("UNKNOWN_LEGACY_INSTALLATIONS");
  const expected=Array.isArray(input?.expectedInstallationIds)?input.expectedInstallationIds:[];
  const devices=Array.isArray(input?.devices)?input.devices:[];
  const expectedIds=expected.map(canonicalDevice);
  const foundIds=devices.map(d=>canonicalDevice(d?.installationId));
  if(expectedIds.length===0||expectedIds.some(id=>!id)||
    new Set(expectedIds).size!==expectedIds.length)
    errors.add("INVENTORY_MISSING_OR_DUPLICATED");
  if(devices.length!==expectedIds.length||foundIds.some(id=>!id)||
    new Set(foundIds).size!==foundIds.length||
    expectedIds.some(id=>!foundIds.includes(id))||
    foundIds.some(id=>!expectedIds.includes(id)))
    errors.add("INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
  for(const d of devices){
    if(!d){
      errors.add("INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
      errors.add("EVIDENCE_STALE_OR_FUTURE");
      continue;
    }
    if(!HASH.test(d.evidenceSha256??""))
      errors.add("INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
    const captured=time(d.capturedAtUtc);
    if(!Number.isFinite(assessed)||!Number.isFinite(captured)||
      captured>assessed||assessed-captured>MAX_SNAPSHOT_AGE_MS)
      errors.add("EVIDENCE_STALE_OR_FUTURE");
    if(d?.localG0501Disabled!==true)errors.add("LOCAL_SEND_STILL_POSSIBLE");
    if(d?.durableQuarantineVerified!==true)errors.add("DURABLE_QUARANTINE_UNVERIFIED");
    if(d?.deviceStoppedOrMigrated!==true)errors.add("DEVICE_NOT_STOPPED_OR_MIGRATED");
    if(!validCount(d?.knownClaimCount)||!validCount(d?.unresolvedClaimCount)||
      d.unresolvedClaimCount>d.knownClaimCount||d.unresolvedClaimCount>0)
      errors.add("UNRESOLVED_LOCAL_CLAIM");
    if(d?.historicalClaimsReconciled!==true)
      errors.add("LOCAL_CLAIM_HISTORY_NOT_RECONCILED");
  }
  const blockers=Array.from(errors).sort();
  // Only an unprivileged, reproducible assessment digest. NOT a signature,
  // D1 cutover_manifest_hash, device attestation, or send authorization.
  let inventorySummarySha256:string|null=null;
  if(blockers.length===0){
    const summary={
      version:1,workspaceId:input.workspaceId,environment:input.environment,
      sellerInvoice:input.sellerInvoice,companyRevision:input.companyRevision,
      assessedAtUtc:input.assessedAtUtc,
      inventoryEvidenceSha256:input.inventoryEvidenceSha256,
      devices:devices.map(d=>({
        installationId:d.installationId,evidenceSha256:d.evidenceSha256,
        capturedAtUtc:d.capturedAtUtc,knownClaimCount:d.knownClaimCount
      })).sort((a,b)=>a.installationId.localeCompare(b.installationId,"en"))
    };
    const digest=await crypto.subtle.digest("SHA-256",
      new TextEncoder().encode(JSON.stringify(summary)));
    inventorySummarySha256=Array.from(new Uint8Array(digest),
      b=>b.toString(16).padStart(2,"0")).join("");
  }
  return {
    status:blockers.length===0?"candidate_for_independent_review":"blocked",
    blockers,inventorySummarySha256,assessedInstallations:devices.length,
    permitsCloudG0501:false,activatesCloudGate:false,
    provesUnknownOfflineDevicesAbsent:false
  };
}
