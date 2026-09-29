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
    // Centre the columns/lines in Rect (recipient inside its printed frame) instead of anchoring them.
    public bool CenterHorizontally { get; set; }
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
    // Vertical layout, left to right: printed recipient frame (X 37-69), phone, address at the right.
    // Recipient is centred in the frame in the upper half; the address's first glyph starts 5 mm
    // lower than the recipient's, and the phone shares the address's top edge.
    public TextPlacement Recipient { get; set; } = new() { Rect = new(37, 64, 32, 130), FontSize = 24, CenterHorizontally = true };
    public TextPlacement Address { get; set; } = new() { Rect = new(87, 69, 12, 135), FontSize = 14, Columns = 2 };
    public TextPlacement Phone { get; set; } = new() { Rect = new(77, 69, 9, 110), FontSize = 10 };
    public TextPlacement PostalCode { get; set; } = new() { Rect = new(49, 31, 24, 9), FontSize = 12, Vertical = false };
    public RectMm Frame { get; set; } = new(7, 95, 12, 30);
    public List<DeliveryPlacement> Delivery { get; set; } =
    [
        new() { Id = "delivery-1", Label = "平信", X = 7, Y = 64 },
        new() { Id = "delivery-2", Label = "限時", X = 7, Y = 68 },
        new() { Id = "delivery-3", Label = "掛號", X = 7, Y = 72 },
        new() { Id = "delivery-4", Label = "限時掛號", X = 7, Y = 76 },
        new() { Id = "delivery-5", Label = "印刷品", X = 7, Y = 80 },
        new() { Id = "delivery-6", Label = "航空", X = 7, Y = 84 },
        new() { Id = "delivery-7", Label = "其他", X = 7, Y = 88 }
    ];
}

public sealed class AppSettings
{
    public string SelectedFormatId { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public bool DirectEntry { get; set; }
    public List<string> FrameTexts { get; set; } = ["內附對帳單"];
}
