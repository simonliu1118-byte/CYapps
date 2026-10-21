import assert from "node:assert/strict";
import {build} from "esbuild";
import {readFileSync} from "node:fs";

// Synthetic, read-only report assessment. No real Device/CYID/D1/AMEGO IO.
const bundle=await build({entryPoints:["src/allowance-void-cutover-preflight.ts"],
  bundle:true,write:false,platform:"node",format:"esm",target:"node24"});
const {assessLocalVoidCutover:assess}=await import("data:text/javascript;base64,"+
  Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const good={
  workspaceId:"ws_void_test",environment:"test",sellerInvoice:"12345678",
  companyRevision:1,assessedAtUtc:"2026-10-21T09:30:00.000Z",
  externalInventoryVerified:true,unknownLegacyInstallationsExcluded:true,
  inventoryEvidenceSha256:"a".repeat(64),
  expectedInstallationIds:["local_install_A","local_install_B"],
  devices:["local_install_A","local_install_B"].map((installationId,i)=>({
    installationId,capturedAtUtc:"2026-10-21T09:20:00.000Z",
    evidenceSha256:(i?"c":"b").repeat(64),
    localG0501Disabled:true,durableQuarantineVerified:true,
    deviceStoppedOrMigrated:true,knownClaimCount:i,
    unresolvedClaimCount:0,historicalClaimsReconciled:true
  }))
};
function clone(){return structuredClone(good);}
async function block(name,mutator,expected){
  const item=clone();mutator(item);
  const result=await assess(item);
  assert.equal(result.status,"blocked",name);
  assert.ok(result.blockers.includes(expected),name+": "+result.blockers.join(", "));
  assert.equal(result.inventorySummarySha256,null,name);
  assert.equal(result.permitsCloudG0501,false,name);
  assert.equal(result.activatesCloudGate,false,name);
  assert.equal(result.provesUnknownOfflineDevicesAbsent,false,name);
}
const report=await assess(good);
assert.equal(report.status,"candidate_for_independent_review");
assert.deepEqual(report.blockers,[]);
assert.match(report.inventorySummarySha256,/^[0-9a-f]{64}$/);
assert.equal(report.assessedInstallations,2);
assert.equal(report.permitsCloudG0501,false);
assert.equal(report.activatesCloudGate,false);
assert.equal(report.provesUnknownOfflineDevicesAbsent,false);
// Device ordering must not change an assessment digest.
const reordered=clone();
reordered.devices.reverse();
reordered.expectedInstallationIds.reverse();
assert.equal((await assess(reordered)).inventorySummarySha256,
  report.inventorySummarySha256);
await block("Unknown offline Local installs",x=>x.unknownLegacyInstallationsExcluded=false,
 "UNKNOWN_LEGACY_INSTALLATIONS");
await block("No independent inventory",x=>x.externalInventoryVerified=false,
 "INVENTORY_NOT_INDEPENDENTLY_VERIFIED");
await block("Missing inventory digest",x=>x.inventoryEvidenceSha256="",
 "INVENTORY_NOT_INDEPENDENTLY_VERIFIED");
await block("Fake inventory digest",x=>x.inventoryEvidenceSha256="Z".repeat(64),
 "INVENTORY_NOT_INDEPENDENTLY_VERIFIED");
await block("Unlisted installation",x=>x.expectedInstallationIds.push("local_install_C"),
 "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
await block("Ghost device report",x=>x.devices[1].installationId="local_install_C",
 "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
await block("Duplicate inventory ID",x=>x.expectedInstallationIds[1]=x.expectedInstallationIds[0],
 "INVENTORY_MISSING_OR_DUPLICATED");
await block("Duplicate report ID",x=>x.devices[1].installationId=x.devices[0].installationId,
 "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
await block("No installations",x=>{x.expectedInstallationIds=[];x.devices=[]},
 "INVENTORY_MISSING_OR_DUPLICATED");
await block("No device report",x=>x.devices[0]=null,
 "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
await block("Unsigned/absent device fingerprint",x=>x.devices[0].evidenceSha256="",
 "INSTALLATION_EVIDENCE_MISSING_OR_DUPLICATED");
await block("Still enabled Local g0501",x=>x.devices[0].localG0501Disabled=false,
 "LOCAL_SEND_STILL_POSSIBLE");
await block("Quarantine not persisted",x=>x.devices[1].durableQuarantineVerified=false,
 "DURABLE_QUARANTINE_UNVERIFIED");
await block("Offline device still active",x=>x.devices[0].deviceStoppedOrMigrated=false,
 "DEVICE_NOT_STOPPED_OR_MIGRATED");
await block("Unresolved legacy void claim",x=>x.devices[1].unresolvedClaimCount=1,
 "UNRESOLVED_LOCAL_CLAIM");
await block("Bad claim count",x=>x.devices[0].knownClaimCount=-1,
 "UNRESOLVED_LOCAL_CLAIM");
await block("Bad claim range",x=>x.devices[0].unresolvedClaimCount=3,
 "UNRESOLVED_LOCAL_CLAIM");
await block("Unreviewed historical claims",x=>x.devices[1].historicalClaimsReconciled=false,
 "LOCAL_CLAIM_HISTORY_NOT_RECONCILED");
await block("Old device evidence",x=>x.devices[0].capturedAtUtc="2026-10-20T09:00:00.000Z",
 "EVIDENCE_STALE_OR_FUTURE");
await block("Future evidence",x=>x.devices[0].capturedAtUtc="2026-10-21T09:45:00.000Z",
 "EVIDENCE_STALE_OR_FUTURE");
await block("Invalid observation timestamp",x=>x.devices[0].capturedAtUtc="bad",
 "EVIDENCE_STALE_OR_FUTURE");
await block("Bad workspace",x=>x.workspaceId="bad",
 "INVALID_SCOPE");
await block("Wrong seller",x=>x.sellerInvoice="123",
 "INVALID_SCOPE");
await block("Missing company revision",x=>x.companyRevision=0,
 "INVALID_SCOPE");
await block("Bad cutover assessment time",x=>x.assessedAtUtc="2026-99-99",
 "INVALID_SCOPE");
const changeRevision=clone();changeRevision.companyRevision=2;
assert.notEqual((await assess(changeRevision)).inventorySummarySha256,
  report.inventorySummarySha256,"company revision must bind the digest");
// This preflight is NOT wired to Worker routes, and its digest cannot be used
// as a D1 cutover_manifest_hash or a self-attested source of global truth.
for(const path of ["src/app.ts","src/worker.ts","src/allowances-route.ts"]){
  const source=readFileSync(path,"utf8");
  assert.ok(!source.includes("allowance-void-cutover-preflight"),
    "no route may treat candidate reports as Cloud activation");
}
console.log("V3.2 D/E Local device inventory assessment: 25 synthetic fail-closed cases, no authority/activation");
