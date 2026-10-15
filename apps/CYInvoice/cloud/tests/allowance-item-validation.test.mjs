import assert from "node:assert/strict";
import {build} from "esbuild";
const out=await build({entryPoints:["src/allowance-item-validation.ts"],bundle:true,
  write:false,platform:"node",format:"esm",target:"node24"});
const {validateAllowanceOriginalItems:check}=await import("data:text/javascript;base64,"+
 Buffer.from(out.outputFiles[0].text).toString("base64"));
const official={
 invoiceNumber:"AB12345678",invoiceDate:20261015,detailVat:0,
 invoiceTaxAmount:5,invoiceGross:105,status:99,cancelled:false,
 allowanceSummaryPresent:true,existingAllowanceCount:0,
 officialItems:[{description:"Service",quantity:"2",unit_price:"50",
    amount:"100",tax_type:1}]
};
const draft={
 AllowanceNumber:"",AllowanceDate:"20261015",
 ProductItem:[{OriginalInvoiceNumber:"AB12345678",OriginalInvoiceDate:20261015,
   OriginalDescription:"Service",Quantity:1,UnitPrice:"50",Amount:"50",
   Tax:2,TaxType:1}],
 TotalAmount:"50",TaxAmount:2
};
const verify=(d=draft,o=official,gross=52)=>check(JSON.stringify(d),o,"AB12345678",gross);
assert.deepEqual(verify(),{fullyMatched:true,
 reason:"SOURCE_ITEMS_MATCHED_NO_PRIOR_ALLOWANCE",verifiedLines:1});
const fails=(d,o,gross,expected)=>assert.equal(verify(d,o,gross).reason,expected);
fails(draft,{...official,detailVat:1},52,"ORIGINAL_TAX_BASIS_NOT_VERIFIED");
fails(draft,{...official,detailVat:null},52,"ORIGINAL_TAX_BASIS_NOT_VERIFIED");
fails(draft,{...official,allowanceSummaryPresent:false},52,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,existingAllowanceCount:1},52,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,status:91},52,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,cancelled:true},52,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,invoiceDate:null},52,"OFFICIAL_DATE_MISSING");
fails(draft,{...official,officialItems:[]},52,"OFFICIAL_ITEMS_MISSING");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalInvoiceNumber:"XY12345678"}]},
  official,52,"REQUEST_ORIGINAL_INVOICE_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalInvoiceDate:20261014}]},
  official,52,"REQUEST_ORIGINAL_INVOICE_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalDescription:"Other"}]},
  official,52,"REQUEST_ITEM_NOT_IN_ORIGINAL");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Quantity:3}]},
  official,52,"REQUEST_EXCEEDS_ORIGINAL_ITEM");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Amount:105}],TotalAmount:105},
  official,107,"REQUEST_EXCEEDS_ORIGINAL_ITEM");
fails({...draft,TaxAmount:3},official,52,"REQUEST_TOTALS_MISMATCH");
fails(draft,{...official,invoiceTaxAmount:1},52,"REQUEST_EXCEEDS_INVOICE_BALANCE");
fails(draft,{...official,invoiceGross:50},52,"REQUEST_EXCEEDS_INVOICE_BALANCE");
fails(draft,{...official,officialItems:[...official.officialItems,...official.officialItems]},
  52,"OFFICIAL_DUPLICATE_ITEM");
assert.equal(verify({...draft,ProductItem:[draft.ProductItem[0],draft.ProductItem[0]],
 TotalAmount:100,TaxAmount:4},official,104).fullyMatched,true,
 "Multiple claims for one original line must accumulate without over-claiming");
fails({...draft,ProductItem:[draft.ProductItem[0],draft.ProductItem[0],
 draft.ProductItem[0]],TotalAmount:150,TaxAmount:6},
 official,156,"REQUEST_EXCEEDS_ORIGINAL_ITEM");
// Decimal precision is exact to seven places (no Number rounding).
const decOfficial={...official,invoiceTaxAmount:0,invoiceGross:2,
  officialItems:[{description:"Tiny",quantity:"1.0000000",
    unit_price:"0.3333333",amount:"0.3333333",tax_type:3}]};
const decDraft={...draft,ProductItem:[{OriginalInvoiceNumber:"AB12345678",
  OriginalInvoiceDate:20261015,OriginalDescription:"Tiny",Quantity:"1.0000000",
  UnitPrice:"0.3333333",Amount:"0.3333333",Tax:0,TaxType:3}],
 TotalAmount:"0.3333333",TaxAmount:0};
fails(decDraft,decOfficial,1,"REQUEST_TOTALS_MISMATCH");
assert.equal(verify(draft,{...official,officialItems:[
 {description:"Service",quantity:"2",unit_price:"50.00000001",
 amount:"100",tax_type:1}]},52).fullyMatched,false);
console.log("Allowance item verifier: exact seven-decimal net line matching and all fail-closed cases passed");
