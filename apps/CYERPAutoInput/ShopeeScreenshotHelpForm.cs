namespace CYERPAutoInput;

/// <summary>
/// How to take the screenshots for the Shopee OCR input (user, 2026-10-11), with example
/// images whose order number, tracking number and item code are pixelated.
/// </summary>
internal sealed class ShopeeScreenshotHelpForm : HelpForm
{
    public ShopeeScreenshotHelpForm() : base("蝦皮訂單自動擷取輸入說明", new Size(1100, 760))
    {
        Heading("自動擷取（建議）");
        Body("1. 在瀏覽器開好蝦皮賣家中心的訂單詳情頁。");
        Body($"2. 在瀏覽器按 {BrowserHotkey.KeyName}（程式開著即可）；或在主畫面按「蝦皮 → 自動擷取輸入」後按「開始」。");
        Body("3. 程式會把頁面捲到最上方，再往下一段段截圖，捲完自動回到這個視窗並顯示辨識進度。擷取期間滑鼠暫時鎖定，按 Esc 立即停止。");
        Body("4. 判讀沒有問題就自動帶回表單；設定有勾「蝦皮自動擷取」時，會接著輸入 ERP、儲存並列印。");
        Body("5. 畫面中央的提示：綠色勾勾＝完成；黃色三角＝有項目要確認或已中斷，請看提示視窗；紅色禁止＝已按 Esc 停止。");

        Heading("有問題時：手動補截圖");
        Body("自動擷取讀不完整或判讀有問題時，會列出缺少或對不上的部分。不要的圖按紅色 × 移除，再用下面的方式補上，最後按「帶入表單」：");
        Body("・按「框選截圖」，拖曳框選後放開即加入，可以連續截，按 Esc 結束。");
        Body("・或按 Win＋Shift＋S 截圖後，在這個視窗按 Ctrl＋V 貼上（也可以拖曳圖片檔）。");
        Body("一張訂單需要下面三種截圖，順序不限：");

        Heading("① 訂單資訊：訂單編號＋運送資訊");
        Body("要包含「訂單編號」下面的編號，以及「包裹」那一行的物流方式和 # 開頭的物流單號。");
        Example("shopee-order.png");

        Heading("② 商品明細：表頭＋每個商品");
        Body("從表頭（編號／商品／單價／數量／小計）截到每個商品的「商品選項貨號」，右側的單價、數量、小計都要在畫面內。");
        Body("商品很多時分成多張截圖，可以重疊，重複的部分會自動去掉；但不能漏掉中間的商品。");
        Example("shopee-items.png");

        Heading("③ 買家實付金額");
        Body("從「訂單金額」截到「買家總支付金額」，包含運費、各種優惠券與蝦幣折抵。");
        Example("shopee-amounts.png");

        Heading("其他");
        Body("・截圖只在這台電腦辨識，不會保存或上傳。");
        Body("・辨識後會核對金額；對不上、缺明細或讀不到貨號時會列出來（能看出可能讀錯的數量時會直接指出），請對照截圖修正表單後再開始輸入。程式不會自行更改品號或數量。");
        Body("・帶回表單時，商品依品號由小到大排列，折價券、運費、促銷組合排在最後。");
        Body("・買家備註、賣家備註不在截圖內，請自行查看。");

        Finish();
    }
}
