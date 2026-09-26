using System.Text.Json.Serialization;

namespace CYEnvelope;

public sealed class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<ContactAddress> Addresses { get; set; } = [];
    public List<ContactPhone> Phones { get; set; } = [];
    public string LastAddressId { get; set; } = "";
    public List<string> LastDeliveryIds { get; set; } = [];
}

public sealed class ContactAddress
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "地址1";
    public string Value { get; set; } = "";
    public string Note { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string LastPhoneId { get; set; } = "";
}

public sealed class ContactPhone
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Note { get; set; } = "";
    [JsonIgnore] public string Display => string.IsNullOrEmpty(Extension) ? Number : $"{Number} #{Extension}";
}

public sealed class PrintData
{
    public string Recipient { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public List<string> DeliveryIds { get; set; } = [];
    public bool ShowFrame { get; set; } = true;
    public string FrameText { get; set; } = "內附對帳單";
}

public sealed class RectMm
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public RectMm() { }
    public RectMm(double x, double y, double width, double height) => (X, Y, Width, Height) = (x, y, width, height);
}

public sealed class TextPlacement
{
    public RectMm Rect { get; set; } = new();
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "DFKai-SB";
    public bool Vertical { get; set; } = true;
    public int Columns { get; set; } = 1;
}

public sealed class DeliveryPlacement
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class EnvelopeFormat
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "15K 標準信封";
    public double WidthMm { get; set; } = 105;
    public double HeightMm { get; set; } = 222;
    public bool Landscape { get; set; }
    public bool IsDefault { get; set; } = true;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public TextPlacement Recipient { get; set; } = new() { Rect = new(43, 49, 18, 142), FontSize = 24 };
    public TextPlacement Address { get; set; } = new() { Rect = new(64, 46, 29, 148), FontSize = 14, Columns = 2 };
    public TextPlacement Phone { get; set; } = new() { Rect = new(32, 59, 8, 132), FontSize = 10 };
    public TextPlacement PostalCode { get; set; } = new() { Rect = new(52, 21, 40, 8), FontSize = 12, Vertical = false };
    public RectMm Frame { get; set; } = new(7, 95, 12, 30);
    public List<DeliveryPlacement> Delivery { get; set; } =
    [
        new() { Id = "delivery-1", Label = "平信", X = 8.2, Y = 63 },
        new() { Id = "delivery-2", Label = "限時", X = 8.2, Y = 67 },
        new() { Id = "delivery-3", Label = "掛號", X = 8.2, Y = 71 },
        new() { Id = "delivery-4", Label = "限時掛號", X = 8.2, Y = 75 },
        new() { Id = "delivery-5", Label = "印刷品", X = 8.2, Y = 79 },
        new() { Id = "delivery-6", Label = "航空", X = 8.2, Y = 83 },
        new() { Id = "delivery-7", Label = "其他", X = 8.2, Y = 87 }
    ];
}

public sealed class AppSettings
{
    public string SelectedFormatId { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public bool DirectEntry { get; set; }
    public List<string> FrameTexts { get; set; } = ["內附對帳單"];
}
