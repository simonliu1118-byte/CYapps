import {decryptCompanyBusinessData} from "./company-settings";
import {resumeApprovedCloudAllowances,type AllowanceExecutionEnv} from "./allowance-one-shot-coordinator";
import { readDeviceSelfStatus } from "./device-self-status";
import { employeeSnapshot } from "./employee-authority";
import { usesCyId, cyIdBinding, cyIdDevice, CyIdError, type CyIdEnv } from "./cyid";

interface Env extends AllowanceExecutionEnv { API_VERSION: string }

export async function handleRuntimeSync(request: Request, env: Env, context?:ExecutionContext): Promise<Response | null> {
  if (request.method !== "POST" || new URL(request.url).pathname !== "/v1/runtime/sync") return null;
  const requestId = crypto.randomUUID();
  const deviceResponse = await readDeviceSelfStatus(request, env, requestId);
  if (!deviceResponse.ok) return deviceResponse;
  const self = await deviceResponse.json<Record<string, unknown>>();
  const result: Record<string, unknown> = { ok: true, device: { deviceId: self.deviceId, workspaceId: self.workspaceId,
    status: self.status, workspaceStatus: self.workspaceStatus, revokedAt: self.revokedAt } };
  const respond = () => Response.json(result, { headers: { "cache-control": "no-store" } });
  // Return only terminal self-state. Provider state must never hide an explicit revoke.
  if (self.status === "revoked" || self.workspaceStatus !== "active") return respond();
  let body: { synchronize?: boolean; authorities?: unknown[]; allowanceIds?:string[]; allowanceIssueCursor?:string; allowanceVoidCursor?:string;allowanceEventCursor?:number };
  try { body = await request.json(); } catch { return Response.json({ ok: false }, { status: 400 }); }
  if (!body || typeof body.synchronize !== "boolean" || !Array.isArray(body.authorities) || body.authorities.length > 10_000)
    return Response.json({ ok: false }, { status: 400 });
  try {
    if (usesCyId(env)) {
      const binding = cyIdBinding(env);
      await cyIdDevice(request, env);
      result.binding = { provider: "CYID", ...binding };
      if(body.synchronize&&env.ALLOWANCE_API_ENABLED==="true"&&Number(env.SCHEMA_VERSION)>=21){
        const work=resumeApprovedCloudAllowances(env,binding.workspaceId).catch(()=>console.warn("ALLOWANCE_RECOVERY_STORAGE_FAILED"));
        if(context)context.waitUntil(work);else await work;
        const ids=Array.isArray(body.allowanceIds)?body.allowanceIds.filter(x=>typeof x==="string"&&/^[A-Za-z0-9_-]{8,100}$/.test(x)).slice(0,100):[];
        const cursor=(value:unknown)=>typeof value==="string"&&/^[A-Za-z0-9_-]{8,100}$/.test(value)?value:"";
        const columns="application_id,environment,status,execution_state,last_error,request_id"+(Number(env.SCHEMA_VERSION)>=23?
          ",returned_at AS returnedAt,(SELECT allowance_number FROM allowance_number_ledger l WHERE l.application_id=a.application_id ORDER BY attempt DESC LIMIT 1) AS allowanceNumber,(SELECT provider_code FROM allowance_execution_events e WHERE e.application_id=a.application_id AND e.stage='send_response' ORDER BY event_id DESC LIMIT 1) AS providerCode":"");
        // Known operations are checked directly. Independent indexed cursors prevent
        // one operation kind or a long history from hiding another device's work.
        const known=await env.DB.prepare(`SELECT ${columns},'issue' AS operation FROM allowance_applications a
          WHERE workspace_id=?1 AND application_id IN (SELECT value FROM json_each(?2))
          UNION ALL SELECT ${columns},'void' AS operation FROM allowance_void_requests a
          WHERE workspace_id=?1 AND application_id IN (SELECT value FROM json_each(?2)) LIMIT 100`)
          .bind(binding.workspaceId,JSON.stringify(ids)).all<Record<string,unknown>>();
        const issue=await env.DB.prepare(`SELECT ${columns},'issue' AS operation FROM allowance_applications a
          WHERE workspace_id=?1 AND application_id>?2 ORDER BY application_id LIMIT 25`)
          .bind(binding.workspaceId,cursor(body.allowanceIssueCursor)).all<Record<string,unknown>>();
        const voids=await env.DB.prepare(`SELECT ${columns},'void' AS operation FROM allowance_void_requests a
          WHERE workspace_id=?1 AND application_id>?2 ORDER BY application_id LIMIT 25`)
          .bind(binding.workspaceId,cursor(body.allowanceVoidCursor)).all<Record<string,unknown>>();
        result.allowances=[...new Map([...known.results,...issue.results,...voids.results]
          .map(row=>[`${row.operation}:${row.application_id}`,row])).values()];
        // Display references only, in two bounded indexed reads. No official API queries or new state owner.
        const projections=result.allowances as Array<Record<string,unknown>>;
        if(env.COMPANY_SETTINGS_KEY_HEX&&projections.length){
          const keys=JSON.stringify(projections.map(x=>x.application_id));
          const references=await Promise.all([
            env.DB.prepare(`SELECT f.application_id,f.request_hash,f.template_ciphertext AS ciphertext,'issue' AS operation
              FROM allowance_frozen_requests f WHERE f.workspace_id=?1
              AND f.application_id IN (SELECT value FROM json_each(?2)) LIMIT 150`).bind(binding.workspaceId,keys).all<Record<string,string>>(),
            env.DB.prepare(`SELECT application_id,request_hash,frozen_ciphertext AS ciphertext,'void' AS operation
              FROM allowance_void_requests WHERE workspace_id=?1
              AND application_id IN (SELECT value FROM json_each(?2)) LIMIT 150`).bind(binding.workspaceId,keys).all<Record<string,string>>()
          ]);
          for(const ref of references.flatMap(x=>x.results) as Array<Record<string,string>>){
            const row=projections.find(x=>x.application_id===ref.application_id&&x.operation===ref.operation);
            if(!row)continue;
            try{
              const field=(ref.operation==="issue"?"allowance-frozen/":"allowance-void/")+ref.application_id+"/"+ref.request_hash;
              const frozen=JSON.parse(await decryptCompanyBusinessData(env.COMPANY_SETTINGS_KEY_HEX,binding.workspaceId,field,ref.ciphertext));
              const number=ref.operation==="void"?frozen.originalInvoiceNumber:JSON.parse(frozen.templateJson).ProductItem?.[0]?.OriginalInvoiceNumber;
              if(typeof number==="string"&&/^[A-Z]{2}\d{8}$/.test(number))row.originalInvoiceNumber=number;
              if(ref.operation==="void"&&typeof frozen.allowanceNumber==="string")row.allowanceNumber=frozen.allowanceNumber;
            }catch{row.referenceError="ALLOWANCE_REFERENCE_UNAVAILABLE";}
          }
        }
        for(const row of projections){
          if(row.returnedAt)row.status="withdrawn";
          row.progress=row.status==="withdrawn"?"已退回":row.execution_state==="completed"?(row.operation==="void"?"已作廢":"已完成"):
            row.execution_state==="problem"?"處理未完成":row.status==="open"||row.status==="review"?"待主管核准":
            row.providerCode===0||row.providerCode===4050141?(row.operation==="void"?"光貿已接收，等待作廢處理":"光貿已接收，等待處理"):
            row.allowanceNumber?"正在依原單號送出／查證":"主管已核准，程式自動配號中";
          delete row.providerCode;delete row.returnedAt;
        }
        const after=Number.isSafeInteger(body.allowanceEventCursor)&&Number(body.allowanceEventCursor)>=0?Number(body.allowanceEventCursor):0;
        const events=await env.DB.prepare(`SELECT event_id,application_id,operation,stage,attempt,provider_code,http_status,response_received,request_id,error_code,recorded_at
          FROM allowance_execution_events WHERE workspace_id=?1 AND event_id>?2 ORDER BY event_id LIMIT 100`).bind(binding.workspaceId,after).all<{event_id:number}>();
        result.allowanceEvents=events.results;
        result.allowanceEventCursor=events.results.at(-1)?.event_id??after;
        result.allowanceCursors={issue:issue.results.length===25?issue.results.at(-1)!.application_id:"",
          void:voids.results.length===25?voids.results.at(-1)!.application_id:""};
      }
      if (body.synchronize) {
        if (!env.IDENTITY_AUTHORITY) throw new CyIdError(503, "IDENTITY_CONFIGURATION_INVALID");
        const sync = await env.IDENTITY_AUTHORITY.invalidateCache({ workspaceId: binding.identityWorkspaceId,
          applicationId: binding.applicationId, authorities: body.authorities }) as Record<string, unknown>;
        if (!sync || sync.workspaceId !== binding.identityWorkspaceId || sync.applicationId !== binding.applicationId
            || typeof sync.available !== "boolean" || !Array.isArray(sync.invalidated)
            || sync.invalidated.some(id => typeof id !== "string")) throw new CyIdError(502, "IDENTITY_RESPONSE_INVALID");
        result.permissions = { ok: sync.available, invalidated: sync.invalidated,
          ...(sync.available ? {} : { code: "ACCESS_DENIED" }) };
      } else {
        const health = await env.IDENTITY!.fetch(new Request("https://cyid.private/v1/health", { signal: AbortSignal.timeout(4000) }));
        const status = await health.json<Record<string, unknown>>();
        if (!health.ok || status.identity !== "ready") throw new Error("IDENTITY_UNAVAILABLE");
      }
    } else {
      result.binding = { provider: "BUILT_IN", workspaceId: self.workspaceId };
      if (body.synchronize) {
        const response = await employeeSnapshot(request, env, requestId);
        const snapshot = await response.json<Record<string, unknown>>();
        result.permissions = response.ok ? { ok: true, employeeSnapshot: snapshot.employeeSnapshot }
          : { ok: false, code: (snapshot.error as { code?: string })?.code ?? "IDENTITY_UNAVAILABLE" };
      }
    }
  } catch (error) {
    result.permissions = { ok: false, code: error instanceof CyIdError ? error.code : "IDENTITY_UNAVAILABLE" };
  }
  // Check even when the provider failed while revocation was occurring.
  const final = await readDeviceSelfStatus(request, env, requestId);
  if (!final.ok) return final;
  const latest = await final.json<Record<string, unknown>>();
  if (latest.status === "revoked" || latest.workspaceStatus !== "active") {
    result.device = { deviceId: latest.deviceId, workspaceId: latest.workspaceId, status: latest.status,
      workspaceStatus: latest.workspaceStatus, revokedAt: latest.revokedAt };
    delete result.binding;
    delete result.permissions;
    delete result.allowances;
    delete result.allowanceCursors;
    delete result.allowanceEvents;
    delete result.allowanceEventCursor;
  }
  return respond();
}
