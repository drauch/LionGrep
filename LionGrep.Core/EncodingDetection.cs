using System.Text;

namespace LionGrep.Core;

public readonly record struct DetectedEncoding(Encoding Encoding, int BomLength);

public static class EncodingDetection
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static DetectedEncoding Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4)
        {
            if (head[0] == 0xFF && head[1] == 0xFE && head[2] == 0x00 && head[3] == 0x00)
                return new(new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
            if (head[0] == 0x00 && head[1] == 0x00 && head[2] == 0xFE && head[3] == 0xFF)
                return new(new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }

        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            return new(Encoding.UTF8, 3);

        if (head.Length >= 2)
        {
            if (head[0] == 0xFF && head[1] == 0xFE)
                return new(Encoding.Unicode, 2);
            if (head[0] == 0xFE && head[1] == 0xFF)
                return new(Encoding.BigEndianUnicode, 2);
        }

        return new(Utf8NoBom, 0);
    }
}
