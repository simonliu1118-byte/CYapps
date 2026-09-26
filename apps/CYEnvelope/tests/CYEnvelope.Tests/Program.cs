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
Check(PhoneFormatting.Format("0912345678") == "0912-345-678", "mobile formatting");
Check(PhoneFormatting.Format("02 2345 6789 分機123") == "02-23456789 #123", "telephone extension");

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
    var preview = EnvelopeRenderer.Draw(format, new PrintData { Recipient = "測試對象", PostalCode = "800" }, true);
    var output = EnvelopeRenderer.Draw(format, new PrintData { Recipient = "測試對象", PostalCode = "800" }, false);
    Check(preview.ContentBounds.Width > 0 && output.ContentBounds.Width > 0, "shared renderer output");
}
finally
{
    try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, true); } catch (IOException) { }
}
Console.WriteLine("CYEnvelope core checks passed.");
