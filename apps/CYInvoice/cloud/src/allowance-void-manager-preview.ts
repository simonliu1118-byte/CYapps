// V3.2-E: trusted CYID manager-only signed read-only proof.
 // A preview is NOT an approval grant, sending claim, or g0501 permission.
import {loadCloudAllowanceVoidForManager} from "./allowance-void-applications";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialVoidTarget} from "./amego-allowance-void-proof";
import type {AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";

export interface CloudVoidManagerPreview {
 applicationId:string;revision:1;requestHash:string;
 allowanceNumber:string;originalInvoiceNumber:string;reason:string;
 officialSnapshotHash:string;officialCheckedAtUtc:string;
 canApproveWithoutFurtherReview:false;
 mayPostG0501:false;
}
export async function previewCloudVoidForManager(
 db:D1Database,t:{workspaceId:string;applicationId:string;deviceId:string},
 ctx:CloudAllowanceContext,authority:AllowanceActorSource,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<CloudVoidManagerPreview>{
 // This returns unredacted fields only to the internal trusted owner. Never
 // echo the plaintext request or AMEGO credentials in an HTTP list/status.
 const first=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 if(first.status!=="open"||first.revision!==1)
  throw new Error("VOID_PREVIEW_APPLICATION_NOT_OPEN");
 const company=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==first.companyRevision||
  company.sellerInvoice!==first.sellerInvoice)
  throw new Error("VOID_PREVIEW_COMPANY_CHANGED");
 const proof=await queryOfficialVoidTarget({
  sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
  allowanceNumber:first.allowanceNumber,
  originalInvoiceNumber:first.originalInvoiceNumber
 },transport,clock);
 // Recheck CYID, active Device, Frozen ciphertext/HMAC and company AFTER
 // both read-only provider round trips. No stored grant or sending CAS here.
 const last=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 if(last.status!=="open"||last.revision!==1||
  last.requestHash!==first.requestHash||
  last.allowanceNumber!==first.allowanceNumber||
  last.originalInvoiceNumber!==first.originalInvoiceNumber)
  throw new Error("VOID_PREVIEW_CHANGED_DURING_OFFICIAL_CHECK");
 const current=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(current.revision!==company.revision||
  current.sellerInvoice!==company.sellerInvoice||
  current.amegoAppKey!==company.amegoAppKey)
  throw new Error("VOID_PREVIEW_COMPANY_CHANGED");
 const date=clock();
 const checked=Date.parse(proof.checkedAtUtc);
 if(!(date instanceof Date)||!Number.isFinite(date.valueOf())||
  !Number.isFinite(checked)||checked>date.valueOf()+1000||
  date.valueOf()-checked>60_000)
  throw new Error("VOID_PREVIEW_OFFICIAL_EVIDENCE_STALE");
 return {
  applicationId:t.applicationId,revision:1,requestHash:first.requestHash,
  allowanceNumber:first.allowanceNumber,
  originalInvoiceNumber:first.originalInvoiceNumber,reason:first.reason,
  officialSnapshotHash:proof.officialSnapshotHash,
  officialCheckedAtUtc:proof.checkedAtUtc,
  canApproveWithoutFurtherReview:false,mayPostG0501:false
 };
}
