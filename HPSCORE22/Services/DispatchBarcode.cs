// Services/DispatchBarcode.cs
// Barcodes of the dispatch label, same library (ZXing.Net) and settings as YA:
//   PrintLebel.aspx    -> CODE-128, 300 x 80, margin 2, pure barcode (no text under it)
//   PrintLebelPOS.aspx -> QR code,  150 x 150, margin 1
// Returned as "data:image/svg+xml;base64,..." for an <img src>.
using System.Text;
using ZXing;
using ZXing.Common;

namespace AlphaCare.Services
{
    public static class DispatchBarcode
    {
        public static string Code128(string text) => Svg(text, BarcodeFormat.CODE_128, 300, 80, 2, pure: true);

        public static string Qr(string text) => Svg(text, BarcodeFormat.QR_CODE, 150, 150, 1, pure: false);

        private static string Svg(string text, BarcodeFormat format, int width, int height, int margin, bool pure)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var writer = new BarcodeWriterSvg
            {
                Format = format,
                Options = new EncodingOptions { Width = width, Height = height, Margin = margin, PureBarcode = pure }
            };
            var svg = writer.Write(text.Trim());
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.Content));
        }
    }
}
