using System.Globalization;
using System.Text;

namespace Inventory.Core;

public static class NativeOutput
{
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var sample = bytes.Take(1024).ToArray();
        if (sample.Length >= 4 && sample.Where((_, i) => i % 2 == 1).Count(x => x == 0) > sample.Length / 4)
            return Encoding.Unicode.GetString(bytes);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage).GetString(bytes);
        }
    }
}
