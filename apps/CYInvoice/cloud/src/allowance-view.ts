import {loadEncryptedCloudAllowance,allowanceParticipantText,type AllowanceActorSource,type CloudAllowanceContext} from "./allowance-frozen-store";
import {loadCloudAllowanceVoidForManager} from "./allowance-void-applications";
import {decryptCompanyBusinessData} from "./company-settings";
import {parseAllowanceFinancialJson} from "./allowance-decimal";

type Table={title:string;columns:string[];rows:string[][]};
type Target={workspaceId:string;applicationId:string;deviceId:string};
export async function allowanceView(db:D1Database,target:Target,ctx:CloudAllowanceContext,
 operation:"issue"|"void",access:AllowanceActorSource|{deviceRead:true}){
 const isVoid=operation==="void";
 const frozen=isVoid?await loadCloudAllowanceVoidForManager(db,target,ctx,access):await loadEncryptedCloudAllowance(db,target,ctx,access);
 const table=isVoid?"allowance_void_requests":"allowance_applications";
 const state=await db.prepare(`SELECT created_at,execution_state,last_error,returned_at,return_ciphertext FROM ${table}
  WHERE workspace_id=?1 AND application_id=?2`).bind(target.workspaceId,target.applicationId)
  .first<{created_at:string;execution_state:string;last_error:string;returned_at:string|null;return_ciphertext:string|null}>();
 if(!state)throw new Error("NOT_FOUND");
 // The caller's active Device may view post-approval progress on this same company;
 // pending management still uses the existing employee/manager authorization.
 if("deviceRead" in access&&["open","review"].includes(frozen.status)&&!state.returned_at)
  throw new Error("MANAGER_REQUIRED");
 const approvalTable=isVoid?"allowance_void_approvals":"allowance_execution_approvals";
 const grant=await db.prepare(`SELECT manager_actor_id,authority_snapshot,approved_at FROM ${approvalTable}
  WHERE workspace_id=?1 AND application_id=?2`).bind(target.workspaceId,target.applicationId)
  .first<{manager_actor_id:string;authority_snapshot:string|null;approved_at:string}>();
 const ledger=isVoid?null:await db.prepare(`SELECT allowance_number,status FROM allowance_number_ledger
  WHERE workspace_id=?1 AND application_id=?2 ORDER BY attempt DESC LIMIT 1`).bind(target.workspaceId,target.applicationId)
  .first<{allowance_number:string;status:string}>();
 const events=await db.prepare(`SELECT stage,provider_code,recorded_at,error_code FROM allowance_execution_events
  WHERE workspace_id=?1 AND application_id=?2 AND operation=?3 ORDER BY event_id DESC LIMIT 100`)
  .bind(target.workspaceId,target.applicationId,operation).all<{stage:string;provider_code:number|null;recorded_at:string;error_code:string}>();
 const response=events.results.find(event=>event.stage==="send_response");
 const accepted=response?.provider_code===0||response?.provider_code===4050141;
 const returned=!!state.returned_at||frozen.status==="withdrawn";
 const progress=returned?"已退回":state.execution_state==="completed"?(isVoid?"已作廢":"已完成"):
  state.execution_state==="problem"?"處理未完成":!grant?"待主管核准":accepted?(isVoid?"光貿已接收，等待作廢處理":"光貿已接收，等待處理"):
  ledger?.status==="sending"?"送出結果待確認，正在依原單號查證":ledger?"已配號，正在送交光貿":"主管已核准，程式自動處理中";
 const rows:Array<{label:string;value:string}>=[];const tables:Table[]=[];
 const add=(label:string,value:unknown)=>rows.push({label,value:value===null||value===undefined||value===""?"－":String(value)});
 add("處理狀態",progress);add("原發票",frozen.originalInvoiceNumber);
 add("折讓單號",isVoid?(frozen as Awaited<ReturnType<typeof loadCloudAllowanceVoidForManager>>).allowanceNumber:ledger?.allowance_number??"尚未配號");
 add("申請員工",allowanceParticipantText(frozen.requesterActorId,frozen.requester));add("申請時間",state.created_at);add(isVoid?"作廢原因":"折讓原因",frozen.reason);
 let reviewer="尚未核准";
 if(grant){reviewer=allowanceParticipantText(grant.manager_actor_id);
  if(grant.authority_snapshot){const saved=JSON.parse(grant.authority_snapshot);if(saved.employeeId===grant.manager_actor_id&&saved.employeeNo&&saved.displayName)reviewer=`${saved.employeeNo} ${saved.displayName}`;}}
 add("覆核主管",reviewer);add("核准時間",grant?.approved_at);
 const sent=events.results.find(event=>event.stage==="send_response"||event.stage==="send_prepared"||event.stage==="send_started");
 add("送出時間",sent?.recorded_at);add("最後查證時間",events.results[0]?.recorded_at);
 if(response)add("光貿受理回覆",accepted?"已明確接收":response.provider_code===null?"回覆不明，依原號查證":"尚無明確受理回覆");
 if(state.last_error){add("問題原因",state.last_error);add("下一步",grant?"保留原申請與原號，由程式接續查證；不可退回或重新送出":"尚未核准，請主管確認申請或填寫原因退回");}
 if(state.return_ciphertext){
  const returned=JSON.parse(await decryptCompanyBusinessData(ctx.encryptionKeyHex,target.workspaceId,"allowance-return/"+operation+"/"+target.applicationId,state.return_ciphertext));
  add("退回原因",returned.reason);add("退回主管",returned.participant??"姓名未取得");add("退回時間",state.returned_at);
 }
 let template:string|undefined;
 if(!isVoid)template=(frozen as Awaited<ReturnType<typeof loadEncryptedCloudAllowance>>).unnumberedTemplateJson;
 else{
  const original=await db.prepare(`SELECT application_id FROM allowance_number_ledger WHERE workspace_id=?1 AND environment=?2 AND allowance_number=?3
   AND status IN ('sending','confirmed') LIMIT 1`).bind(target.workspaceId,ctx.environment,(frozen as Awaited<ReturnType<typeof loadCloudAllowanceVoidForManager>>).allowanceNumber)
   .first<{application_id:string}>();
  if(original){const issued=await loadEncryptedCloudAllowance(db,{...target,applicationId:original.application_id},ctx,{deviceRead:true});template=issued.unnumberedTemplateJson;}
 }
 if(template){
  const request=parseAllowanceFinancialJson(template) as Record<string,unknown>;
  add(isVoid?"原折讓未稅金額":"本次未稅金額",request.TotalAmount);add(isVoid?"原折讓稅額":"本次稅額",request.TaxAmount);
  if(!isVoid)add("本次含稅折讓",(frozen as Awaited<ReturnType<typeof loadEncryptedCloudAllowance>>).requestedGross);
  const items=request.ProductItem as Array<Record<string,unknown>>;
  tables.push({title:isVoid?"原折讓商品":"本次折讓商品",columns:["品名","數量","單價","稅別","未稅金額","稅額"],
   rows:items.map(item=>["OriginalDescription","Quantity","UnitPrice","TaxType","Amount","Tax"].map(key=>String(item[key]??"－")))});
 }else add("原折讓商品","本機尚無原折讓商品快照，保留已知原單資料");
 return {applicationId:target.applicationId,operation,status:state.returned_at?"withdrawn":frozen.status,revision:frozen.revision,
  executionState:state.execution_state,allowanceNumber:rows.find(row=>row.label==="折讓單號")?.value,
  rows,tables,requestHash:"",officialSnapshotHash:"",canWithdraw:!state.returned_at&&!grant&&["open","review"].includes(frozen.status)};
}
