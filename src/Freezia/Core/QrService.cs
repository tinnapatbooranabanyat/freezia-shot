using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Freezia.Capture;
using ZXing;
using ZXing.Common;

namespace Freezia.Core;

/// <summary>Reads QR codes and barcodes in an image with ZXing.Net (offline).</summary>
public static class QrService
{
    public sealed record Code(string Format, string Text);

    public static Task<List<Code>> ReadAsync(PixelImage image) => Task.Run(() =>
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true },
        };
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source) ?? Array.Empty<Result>();
        return results
            .Where(r => !string.IsNullOrEmpty(r.Text))
            .GroupBy(r => r.Text)
            .Select(g => new Code(g.First().BarcodeFormat.ToString(), g.Key))
            .ToList();
    });

    public static bool IsLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
