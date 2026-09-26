namespace CYEnvelope;

public static class FormatGeometry
{
    // Rotate field centres with the sheet. The frame keeps a tall black box
    // because its content must remain vertical in either feed orientation.
    public static void Rotate(EnvelopeFormat format)
    {
        var oldWidth = format.WidthMm;
        var oldHeight = format.HeightMm;
        static RectMm Clockwise(RectMm r, double h) =>
            new(h - r.Y - r.Height, r.X, r.Height, r.Width);
        static RectMm CounterClockwise(RectMm r, double w) =>
            new(r.Y, w - r.X - r.Width, r.Height, r.Width);
        var clockwise = !format.Landscape;
        RectMm Change(RectMm r) => clockwise ? Clockwise(r, oldHeight) : CounterClockwise(r, oldWidth);
        foreach (var field in new[] { format.Recipient, format.Address, format.Phone, format.PostalCode })
            field.Rect = Change(field.Rect);
        var postal = format.PostalCode.Rect;
        format.PostalCode.Rect = new RectMm(postal.X + postal.Width / 2 - 20,
            postal.Y + postal.Height / 2 - 4, 40, 8);
        foreach (var field in new[] { format.Recipient, format.Address, format.Phone })
            field.Vertical = !field.Vertical;
        var frame = Change(format.Frame);
        var centreX = frame.X + frame.Width / 2;
        var centreY = frame.Y + frame.Height / 2;
        format.Frame = new RectMm(centreX - 6, centreY - 15, 12, 30);
        foreach (var item in format.Delivery)
        {
            var (x, y) = clockwise ? (oldHeight - item.Y, item.X) : (item.Y, oldWidth - item.X);
            item.X = x; item.Y = y;
        }
        format.WidthMm = oldHeight;
        format.HeightMm = oldWidth;
        format.Landscape = !format.Landscape;
    }
}
