namespace CYERPAutoInput;

/// <summary>
/// How MO店+ orders get into ERP with their tracking numbers (user, 2026-10-13). The example
/// sheets are drawn from made-up data and their numbers are pixelated anyway.
/// </summary>
internal sealed class MoImportHelpForm : HelpForm
{
    public MoImportHelpForm() : base("MO店+訂單匯入說明", new Size(960, 720))
    {
        Heading("流程");
        Body("1. 在 MO店+ 後台「未出貨」頁按「匯出報表」，下載還沒產生運送資訊的訂單匯出檔（Excel，有密碼）。這一份就是 ①。");
        Example("mo-export-before.png");
        Body("2. 用這一份去 CYInvoice 開發票。");
        Body("3. 回「未出貨」頁按「批次出貨」（新竹、全家、7-11 各自產生寄件單）。");
        Body("4. 出貨後到「配送中」頁再按一次「匯出報表」。這一份就是 ②，物流單號已經填好。");
        Example("mo-export-after.png");
        Body("5. 在本程式按「MO店+」，把 ① 和 ② 放進視窗（選擇檔案或直接拖曳），按「下一步」。");
        Body("6. 預覽裡物流單號已經帶好，填上發票資料（客戶全名有統編時會自動查）。勾選要輸入的訂單（預設已勾好；已輸入過的訂單不能勾），按「開始輸入」：勾一張單獨輸入，勾多張為批次，完成後列出結果清單。");

        Heading("① 出貨前的訂單匯出檔");
        Body("物流單號是空的。這一份裡的訂單就是這次要輸入 ERP 的訂單，一張都不會多。");
        Example("mo-before.png");

        Heading("② 出貨後的訂單匯出檔");
        Body("物流單號已經填好（新竹 10 碼、全家 11 碼、7-11 英文字母開頭）。MO店+ 會把以前的訂單一起列出來，沒關係：程式只依訂單編號找 ① 的訂單（圖中綠底），其他的不會用到。");
        Example("mo-after.png");

        Heading("其他");
        Body("・檔名不拘；兩個檔放錯格或一起拖進來都可以，程式看物流單號有沒有填自動分成 ① 和 ②。");
        Body("・不需要寄件單 PDF，放進來會提醒。");
        Body("・② 裡找不到、沒有物流單號、或同一張訂單有兩個不同物流單號時，物流單號留空並列在預覽下方，請自己填，程式不會猜。");
        Body("・沒有 ② 也可以繼續，物流單號空白，之後回補。");
        Body("・檔案密碼使用「設定 › 平台匯入 › MO店+」的匯出檔密碼；檔案只在這台電腦讀取，不會上傳。");
        Body("・勾兩張以上（批次）需要在設定勾選自動儲存。已由本程式輸入儲存過的訂單在預覽顯示「已輸入過」，不能再勾；需要人工處理的訂單以紅字顯示、預設不勾，原因列在預覽下方。");
        Finish();
    }
}
