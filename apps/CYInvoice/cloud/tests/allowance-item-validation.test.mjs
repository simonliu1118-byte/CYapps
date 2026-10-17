import assert from "node:assert/strict";
import {build} from "esbuild";
const out=await build({entryPoints:["src/allowance-item-validation.ts"],
  bundle:true,write:false,platform:"node",format:"esm",target:"node24"});
const {validateAllowanceOriginalItems:check,remainingOfficialGross:remaining}=
 await import("data:text/javascript;base64,"+
   Buffer.from(out.outputFiles[0].text).toString("base64"));
const official={
 invoiceNumber:"AB12345678",invoiceDate:20261015,detailVat:1,
 invoiceTaxAmount:5,invoiceGross:105,status:99,cancelled:false,
 allowanceSummaryPresent:true,existingAllowanceCount:0,
 officialItems:null,allowanceHistory:[]
};
const draft={
 AllowanceNumber:"",AllowanceDate:"20261015",ProductItem:[
  {OriginalInvoiceNumber:"AB12345678",OriginalInvoiceDate:20261015,
   OriginalDescription:"User edited description",Quantity:1,
   UnitPrice:50,Amount:50,Tax:3,TaxType:1}
 ],TotalAmount:50,TaxAmount:3
};
const verify=(d=draft,o=official,g=53)=>
 check(JSON.stringify(d),o,"AB12345678",g);
assert.deepEqual(verify(),{
 fullyMatched:true,reason:"REQUEST_TOTALS_WITHIN_REMAINING",verifiedLines:1
});
assert.equal(verify(draft,{...official,detailVat:0}).fullyMatched,true);
assert.equal(verify(draft,{...official,officialItems:[]}).fullyMatched,true);
assert.equal(verify({...draft,ProductItem:[
 {...draft.ProductItem[0],OriginalDescription:"Custom item"}]}).fullyMatched,true);
const history=[{
 allowanceNumber:"3821061800001",allowanceDate:"20261015",
 metadataComplete:true,uploadStatus:99,cancelDate:"0",
 totalAmount:"40",taxAmount:"2"
}];
const after={...official,existingAllowanceCount:1,allowanceHistory:history};
assert.equal(remaining(after),63n*10000000n);
assert.equal(verify(draft,after,53).fullyMatched,true,
 "Existing effective allowances do not block a partial refund by themselves");
assert.equal(verify(draft,after,64).reason,"REQUEST_EXCEEDS_REMAINING");
const voided={...after,allowanceHistory:[
 {...history[0],cancelDate:"20261016"}]};
assert.equal(remaining(voided),105n*10000000n);
assert.equal(verify(draft,voided,53).fullyMatched,true);
for(const [source,reason]of [
 [{...official,allowanceSummaryPresent:false},"OFFICIAL_HISTORY_UNVERIFIED"],
 [{...official,status:91},"OFFICIAL_STATE_NOT_SAFE"],
 [{...official,cancelled:true},"OFFICIAL_STATE_NOT_SAFE"],
 [{...after,allowanceHistory:[]},"OFFICIAL_HISTORY_UNVERIFIED"],
 [{...after,allowanceHistory:[{...history[0],uploadStatus:91}]},
  "OFFICIAL_HISTORY_UNVERIFIED"],
 [{...after,allowanceHistory:[{...history[0],taxAmount:null}]},
  "OFFICIAL_HISTORY_UNVERIFIED"],
 [{...after,existingAllowanceCount:2,allowanceHistory:[...history,...history]},
  "OFFICIAL_HISTORY_UNVERIFIED"],
 [{...after,invoiceGross:10},"OFFICIAL_HISTORY_UNVERIFIED"]
]){
 assert.equal(verify(draft,source,53).reason,reason);
}
assert.equal(verify({...draft,TaxAmount:4}).reason,"REQUEST_TOTALS_MISMATCH");
assert.equal(verify({...draft,ProductItem:[
 {...draft.ProductItem[0],Quantity:2}]}).fullyMatched,true,
 "An edited quantity is the employee's decision, not an original-item gate");
assert.equal(verify({...draft,ProductItem:[
 {...draft.ProductItem[0],Quantity:0}]}).reason,"REQUEST_ITEM_INCOMPLETE");
assert.equal(verify({...draft,ProductItem:[
 {...draft.ProductItem[0],OriginalInvoiceNumber:"XY12345678"}]}).reason,
 "REQUEST_ORIGINAL_INVOICE_MISMATCH");
assert.equal(verify(draft,{...official,invoiceDate:null}).reason,
 "OFFICIAL_DATE_MISSING");
console.log("Allowance gross ceiling: original minus effective history, editable lines and unknowns closed");
