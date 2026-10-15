import assert from "node:assert/strict";
import test from "node:test";
import { verifyProductionCore } from "../scripts/verify-production-core.mjs";

function fixture(patch = {}) {
  const plain = values => Object.entries(values).map(([name,text])=>({type:"plain_text",name,text}));
  const provider={success:true,result:{bindings:plain({APP_ENV:"production",CYACC_SERVICE_BINDING_TARGET:"identity-test-prod",CYACC_APPLICATION_ID:"APP_ACCOUNTING",CYACC_WORKSPACE_ID:"workspace-test",CORE_ACCOUNT_APPLICATION_ID:"APP_CORE",ACCOUNT_PORTAL_URL:"https://portal.example.test/"})}};
  const core={success:true,result:{bindings:[...plain({IDENTITY_APPLICATION_ID:"APP_CORE",IDENTITY_WORKSPACE_ID:patch.workspace??"workspace-test",IDENTITY_CONSUMER_VERSION:patch.version??"1.0.5"}),{type:"service",name:"IDENTITY",service:patch.service??"identity-test-prod"}]}};
  const health={ok:true,data:{service:"cyweb",database:patch.database??"ok",identityConsumerVersion:patch.healthVersion??patch.version??"1.0.5"}};
  return {provider,workerName:"identity-test-prod",account:"synthetic-account",token:"synthetic-token",consumerVersion:"1.0.6",minimumVersion:"1.0.0",async fetchImpl(url,init={}){
    url=String(url);
    if(url.startsWith("https://portal.example.test/")){assert.equal(init.headers,undefined);return Response.json(health);}
    assert.ok(url.startsWith("https://api.cloudflare.com/client/v4/accounts/synthetic-account/"));
    assert.equal(init.headers.authorization,"Bearer synthetic-token");
    if(url.includes("/workers/domains?"))return Response.json({success:true,result:patch.noDomain?[]:[{hostname:"portal.example.test",service:"portal-test",environment:"production"}]});
    assert.ok(url.endsWith("/workers/scripts/portal-test/settings"));return Response.json(core);
  }};
}
test("routine provider release resolves the managed portal and requires exact scope and an adopted supported contract",async()=>{
  await verifyProductionCore(fixture());
});
for(const [name,patch] of [["below supported minimum",{version:"0.9.9"}],["future unsupported contract",{version:"1.0.7"}],["invalid contract",{version:"1.0.x"}],["mismatched declaration",{healthVersion:"1.0.4"}],["development authority",{service:"identity-test-dev"}],["foreign Workspace",{workspace:"workspace-other"}],["unhealthy D1",{database:"unavailable"}],["missing domain",{noDomain:true}]])test(`routine release rejects ${name}`,async()=>{
  await assert.rejects(verifyProductionCore(fixture(patch)));
});

for(const version of ["1.0.0","1.0.4","1.0.5","1.0.6"])test(`backward-compatible provider accepts supported core contract ${version}`,async()=>{
  await verifyProductionCore(fixture({version}));
});
test("raising minimum still rejects an older supported-by-previous-provider consumer",async()=>{
  await assert.rejects(verifyProductionCore({...fixture({version:"1.0.4"}),minimumVersion:"1.0.5"}));
});
