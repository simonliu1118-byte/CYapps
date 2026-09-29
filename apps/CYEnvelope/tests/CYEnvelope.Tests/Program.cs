using CYEnvelope;
using System.IO;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Check(Postal.Infer("高雄市新興區中正三路") == "800", "高雄新興區");
Check(Postal.Infer("臺北市大安區復興南路") == "106", "臺北大安區");
Check(Postal.Infer("台北市中正區") == "100", "台/臺 variants");
Check(Postal.Infer("未知的地址") is null, "unknown postal code should remain unchanged");
foreach (var (address, expected) in new[]
{
    ("彰化縣員林市中山路", "510"), ("彰化縣彰化市中山路", "500"), ("臺北市中山區中正路", "104"),
    ("臺南市東區大學路", "701"), ("嘉義市西區中山路", "600"), ("新竹市東區光復路", "300"),
    ("連江縣南竿鄉", "209"), ("金門縣金城鎮", "893"), ("10617臺北市大安區", "106"),
    ("彰化縣某某路", null), ("東區中山路", null)
})
    Check(Postal.Infer(address) == expected, $"postal {address} -> {Postal.Infer(address)}, want {expected}");
Check(Postal.SameAddress("台北市 中正區　重慶南路", "臺北市中正區重慶南路"), "address comparison ignores 台/臺 and spaces");
foreach (var (input, expected) in new[]
{
    ("0912345678", "0912-345-678"),
    ("02 2345 6789 分機123", "02-23456789 #123"),
    ("07-1234567", "07-1234567"),
    ("03 431 3366", "03-4313366"),
    ("04-22223333", "04-22223333"),
    ("04-7222151", "04-7222151"),
    ("037-322150", "037-322150"),
    ("049-2222106", "049-2222106"),
    ("089-322216", "089-322216"),
    ("0836-22381", "0836-22381"),
    ("0800-000-123", "0800-000-123"),
    ("+886 2 2381 0435", "02-23810435"),
    ("(02)23810435#9", "02-23810435 #9"),
    ("12345", "12345")
})
    Check(PhoneFormatting.Format(input) == expected, $"phone {input} -> {PhoneFormatting.Format(input)}, want {expected}");

var path = Path.Combine(Path.GetTempPath(), "CYEnvelope-tests-" + Guid.NewGuid().ToString("N"), "Data", "CYEnvelope.db");
try
{
    var repository = new Repository(path);
    Check(repository.Formats().Count == 1, "initial format");
    var contact = new Contact { Name = "測試對象" };
    contact.Addresses.Add(new ContactAddress { Label = "公司", Value = "高雄市新興區", PostalCode = "800" });
    contact.Addresses.Add(new ContactAddress { Label = "住家", Value = "台北市中正區", PostalCode = "100" });
    contact.Phones.Add(new ContactPhone { Number = "0912-345-678", Extension = "9" });
    contact.LastAddressId = contact.Addresses[1].Id;
    contact.Addresses[1].LastPhoneId = contact.Phones[0].Id;
    contact.LastDeliveryIds.Add("delivery-1");
    repository.SaveContact(contact);
    var reloaded = new Repository(path).Contacts().Single();
    Check(reloaded.Addresses.Count == 2 && reloaded.Phones.Count == 1, "multiple addresses and phones");
    Check(reloaded.Addresses[1].LastPhoneId == reloaded.Phones[0].Id, "last phone per address");
    Check(reloaded.LastDeliveryIds.SequenceEqual(["delivery-1"]), "last mail option");
    var format = repository.Formats().Single();
    var originalRecipient = (format.Recipient.Rect.X, format.Recipient.Rect.Y,
        format.Recipient.Rect.Width, format.Recipient.Rect.Height);
    FormatGeometry.Rotate(format);
    Check(format.Landscape && format.WidthMm == 222 && format.HeightMm == 105,
        "landscape rotates the sheet dimensions");
    FormatGeometry.Rotate(format);
    Check(!format.Landscape && format.WidthMm == 105 && format.HeightMm == 222 &&
        originalRecipient == (format.Recipient.Rect.X, format.Recipient.Rect.Y,
            format.Recipient.Rect.Width, format.Recipient.Rect.Height),
        "portrait roundtrip preserves the recipient coordinates");
    var originalPostal = (format.PostalCode.Rect.X, format.PostalCode.Rect.Y, format.PostalCode.Rect.Width, format.PostalCode.Rect.Height);
    FormatGeometry.Rotate(format); FormatGeometry.Rotate(format);
    Check(originalPostal == (format.PostalCode.Rect.X, format.PostalCode.Rect.Y, format.PostalCode.Rect.Width, format.PostalCode.Rect.Height),
        "portrait roundtrip preserves postal boxes");
    format.Recipient.Rect.X += 1;
    repository.SaveFormat(format);
    var reopened = new Repository(path).Formats().Single();
    Check(reopened.Recipient.Rect.X == format.Recipient.Rect.X, "user-adjusted built-in format survives reopening");
    var preview = EnvelopeRenderer.Draw(format, new PrintData { Recipient = "測試對象", PostalCode = "800" }, true);
    var output = EnvelopeRenderer.Draw(format, new PrintData { Recipient = "測試對象", PostalCode = "800" }, false);
    Check(preview.ContentBounds.Width > 0 && output.ContentBounds.Width > 0, "shared renderer output");
    // Default 15K layout, left to right: recipient in the printed frame, then phone, then address.
    var fresh = new EnvelopeFormat();
    const double MmPerPoint = 25.4 / 72;
    var recipientLayout = EnvelopeRenderer.Layout("王小明", fresh.Recipient).Glyphs;
    var recipientCentre = recipientLayout.Average(g => g.X) + 24 * MmPerPoint / 2;
    Check(Math.Abs(recipientCentre - 53) < .5, $"recipient centred in the printed frame (centre {recipientCentre:0.0} mm, frame 53 mm)");
    var addressLayout = EnvelopeRenderer.Layout("高雄市新興區中正三路一號", fresh.Address).Glyphs;
    Check(addressLayout.Min(g => g.Y) - recipientLayout.Min(g => g.Y) >= 4,
        "first address glyph starts lower than the first recipient glyph");
    Check(recipientLayout.Min(g => g.Y) < 111, "recipient stays in the upper half of the frame");
    var phoneLayout = EnvelopeRenderer.Layout("0912-345-678", fresh.Phone).Glyphs;
    Check(phoneLayout.Min(g => g.X) >= 69 && phoneLayout.Max(g => g.X) + 10 * MmPerPoint <= addressLayout.Min(g => g.X),
        "phone sits right of the recipient frame and left of the address");
    Check(phoneLayout.Min(g => g.Y) == addressLayout.Min(g => g.Y), "phone shares the address's top edge");
    Check(EnvelopeRenderer.Overflows(fresh, new PrintData { Recipient = "王小明", Address = "高雄市新興區中正三路一號", Phone = "0912-345-678 #123" }).Count == 0,
        "default layout fits ordinary data");

    Check(EnvelopeRenderer.DrawCalibration(fresh).ContentBounds.Width > 0, "calibration sheet renders");

    // Layouts shipped by earlier builds upgrade only while unedited.
    var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "legacy", "CYEnvelope.db");
    EnvelopeFormat Build3() => new()
    {
        Id = "format-15k",
        Recipient = new() { Rect = new(39, 62, 26, 134), FontSize = 24 },
        Address = new() { Rect = new(72, 60, 27, 140), FontSize = 14, Columns = 2 },
        Phone = new() { Rect = new(28, 98, 8, 104), FontSize = 10 },
        PostalCode = new() { Rect = new(49, 31, 24, 9), FontSize = 12, Vertical = false }
    };
    var legacy = new Repository(legacyPath);
    legacy.SaveFormat(Build3());
    Check(new Repository(legacyPath).Formats().Single().Recipient.CenterHorizontally, "unedited Build 3 layout upgrades to the current defaults");
    var edited = Build3(); edited.Address.Rect.X += 1;
    legacy.SaveFormat(edited);
    Check(new Repository(legacyPath).Formats().Single().Address.Rect.X == 73, "edited layout is kept as the user left it");

    var builtIn = new EnvelopeFormat();
    Check(EnvelopeRenderer.Overflows(builtIn, new PrintData { Recipient = "王小明", Address = "高雄市新興區中正三路1號" }).Count == 0,
        "ordinary envelope fits");
    var longAddress = string.Concat(Enumerable.Repeat("高雄市新興區中正三路", 8));
    Check(EnvelopeRenderer.Overflows(builtIn, new PrintData { Recipient = "王小明", Address = longAddress })
        .SequenceEqual(["地址"]), "overflowing address is reported instead of silently cut");
    var shifted = EnvelopeRenderer.Draw(builtIn, new PrintData { Recipient = "王小明" }, false,
        printerOrigin: new System.Windows.Vector(20, 10));
    var unshifted = EnvelopeRenderer.Draw(builtIn, new PrintData { Recipient = "王小明" }, false);
    Check(Math.Abs(unshifted.ContentBounds.X - shifted.ContentBounds.X - 20) < .01 &&
          Math.Abs(unshifted.ContentBounds.Y - shifted.ContentBounds.Y - 10) < .01,
        "printer imageable origin is removed from paper coordinates");
}
finally
{
    try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, true); } catch (IOException) { }
}
Console.WriteLine("CYEnvelope core checks passed.");
