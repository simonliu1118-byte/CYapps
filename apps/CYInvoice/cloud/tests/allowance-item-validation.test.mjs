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
   Tax:3,TaxType:1}],
 TotalAmount:"50",TaxAmount:3
};
const verify=(d=draft,o=official,gross=53)=>check(JSON.stringify(d),o,"AB12345678",gross);
assert.deepEqual(verify(),{fullyMatched:true,
 reason:"SOURCE_ITEMS_MATCHED_NO_PRIOR_ALLOWANCE",verifiedLines:1});
const fails=(d,o,gross,expected)=>assert.equal(verify(d,o,gross).reason,expected);
// Gross original source totals can be independently checked without
// authorizing the net-priced g0401 claim or any remaining balance.
const grossOriginal={...official,detailVat:1,officialItems:[
  {...official.officialItems[0],unit_price:"52.5",amount:"105"}
]};
assert.deepEqual(verify(draft,grossOriginal,53),{
  fullyMatched:false,reason:"ORIGINAL_GROSS_SOURCE_VERIFIED_MANUAL_ONLY",
  verifiedLines:0
});
fails(draft,{...grossOriginal,detailVat:"1"},53,
  "ORIGINAL_GROSS_SOURCE_VERIFIED_MANUAL_ONLY");
fails(draft,{...grossOriginal,invoiceTaxAmount:0},53,
  "ORIGINAL_GROSS_TAX_SPLIT_UNAVAILABLE");
fails(draft,{...grossOriginal,invoiceGross:104},53,
  "ORIGINAL_GROSS_TOTAL_MISMATCH");
fails(draft,{...grossOriginal,invoiceTaxAmount:6},53,
  "ORIGINAL_GROSS_TAX_ARITHMETIC_UNVERIFIED");
fails(draft,{...grossOriginal,officialItems:[{...grossOriginal.officialItems[0],
  unit_price:"49"}]},53,"ORIGINAL_GROSS_ITEMS_UNVERIFIED");
fails(draft,{...grossOriginal,officialItems:[
  grossOriginal.officialItems[0],grossOriginal.officialItems[0]],
  invoiceGross:210,invoiceTaxAmount:10},53,"ORIGINAL_GROSS_ITEMS_UNVERIFIED");
const mixedGross={...grossOriginal,invoiceGross:155,
  officialItems:[...grossOriginal.officialItems,
    {description:"Exempt",quantity:1,unit_price:50,amount:50,tax_type:3}]};
fails(draft,mixedGross,53,"ORIGINAL_GROSS_SOURCE_VERIFIED_MANUAL_ONLY");
fails(draft,{...grossOriginal,existingAllowanceCount:1},53,
  "OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...grossOriginal,detailVat:null},53,
  "ORIGINAL_TAX_BASIS_NOT_VERIFIED");
fails(draft,{...official,detailVat:null},53,"ORIGINAL_TAX_BASIS_NOT_VERIFIED");
fails(draft,{...official,allowanceSummaryPresent:false},53,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,existingAllowanceCount:1},53,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,status:91},53,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,cancelled:true},53,"OFFICIAL_STATE_NOT_SAFE");
fails(draft,{...official,invoiceDate:null},53,"OFFICIAL_DATE_MISSING");
fails(draft,{...official,officialItems:[]},53,"OFFICIAL_ITEMS_MISSING");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalInvoiceNumber:"XY12345678"}]},
  official,53,"REQUEST_ORIGINAL_INVOICE_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalInvoiceDate:20261014}]},
  official,53,"REQUEST_ORIGINAL_INVOICE_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],OriginalDescription:"Other"}]},
  official,53,"REQUEST_ITEM_NOT_IN_ORIGINAL");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Quantity:3}]},
  official,53,"REQUEST_LINE_ARITHMETIC_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Quantity:3,Amount:150,Tax:8}],
  TotalAmount:150,TaxAmount:8},official,158,"REQUEST_EXCEEDS_ORIGINAL_ITEM");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Amount:105}],TotalAmount:105},
  official,108,"REQUEST_LINE_ARITHMETIC_MISMATCH");
fails({...draft,TaxAmount:4},official,53,"REQUEST_TOTALS_MISMATCH");
fails(draft,{...official,invoiceTaxAmount:1},53,"OFFICIAL_TOTAL_ARITHMETIC_MISMATCH");
fails(draft,{...official,invoiceGross:50},53,"OFFICIAL_TOTAL_ARITHMETIC_MISMATCH");
// Totals alone are insufficient: official VAT must agree with the exact
// taxable original items. Previously, 100 + 10 = 110 could be accepted even
// though the documented narrow 5% path permits only tax 5.
fails(draft,{...official,invoiceTaxAmount:10,invoiceGross:110},53,
  "OFFICIAL_TAX_ARITHMETIC_UNVERIFIED");
fails(draft,{...official,invoiceTaxAmount:0,invoiceGross:100},53,
  "OFFICIAL_TAX_ARITHMETIC_UNVERIFIED");
const exemptOfficial={...official,invoiceTaxAmount:1,invoiceGross:101,
  officialItems:[{description:"Service",quantity:"2",unit_price:"50",
    amount:"100",tax_type:3}]};
fails(draft,exemptOfficial,53,"OFFICIAL_TAX_ARITHMETIC_UNVERIFIED");
fails(draft,{...official,officialItems:[...official.officialItems,...official.officialItems]},
  52,"OFFICIAL_DUPLICATE_ITEM");
fails({...draft,ProductItem:[draft.ProductItem[0],draft.ProductItem[0]],
 TotalAmount:100,TaxAmount:6},official,106,"REQUEST_TAX_ARITHMETIC_UNVERIFIED");
fails({...draft,ProductItem:[draft.ProductItem[0],draft.ProductItem[0],
 draft.ProductItem[0]],TotalAmount:150,TaxAmount:9},
 official,159,"REQUEST_EXCEEDS_ORIGINAL_ITEM");
// Decimal precision is exact to seven places (no Number rounding).
const decOfficial={...official,invoiceTaxAmount:0,invoiceGross:0.3333333,
  officialItems:[{description:"Tiny",quantity:"1.0000000",
    unit_price:"0.3333333",amount:"0.3333333",tax_type:3}]};
const decDraft={...draft,ProductItem:[{OriginalInvoiceNumber:"AB12345678",
  OriginalInvoiceDate:20261015,OriginalDescription:"Tiny",Quantity:"1.0000000",
  UnitPrice:"0.3333333",Amount:"0.3333333",Tax:0,TaxType:3}],
 TotalAmount:"0.3333333",TaxAmount:0};
fails(decDraft,decOfficial,1,"REQUEST_TOTALS_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],UnitPrice:"20"}]},
  official,53,"REQUEST_LINE_ARITHMETIC_MISMATCH");
fails(draft,{...official,officialItems:[{...official.officialItems[0],
  amount:"95"}]},53,"OFFICIAL_LINE_ARITHMETIC_MISMATCH");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Tax:2}],
  TaxAmount:2},official,52,"REQUEST_TAX_ARITHMETIC_UNVERIFIED");
fails({...draft,ProductItem:[{...draft.ProductItem[0],Tax:2.5}],
  TaxAmount:2.5},official,52,"REQUEST_TAX_ARITHMETIC_UNVERIFIED");
assert.equal(verify(draft,{...official,officialItems:[
 {description:"Service",quantity:"2",unit_price:"50.00000001",
 amount:"100",tax_type:1}]},52).fullyMatched,false);
// Exact seven-decimal, no binary float: amount = 0.3333333 * 3.
const tinyOff={...official,invoiceTaxAmount:0,invoiceGross:1,
 officialItems:[{description:"Tiny",quantity:3,unit_price:"0.3333333",
  amount:"0.9999999",tax_type:3}]};
const tinyReq={...draft,ProductItem:[{OriginalInvoiceNumber:"AB12345678",
 OriginalInvoiceDate:20261015,OriginalDescription:"Tiny",Quantity:3,
 UnitPrice:"0.3333333",Amount:"0.9999999",Tax:0,TaxType:3}],
 TotalAmount:"0.9999999",TaxAmount:0};
assert.equal(verify(tinyReq,tinyOff,1).fullyMatched,false,
 "Official totals that do not add up must not yield a positive match");
console.log("Allowance item verifier: seven-decimal line math, 5% VAT and conservative failures passed");
