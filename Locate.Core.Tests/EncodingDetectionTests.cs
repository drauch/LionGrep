using System.Text;
using NUnit.Framework;

namespace Locate.Core.Tests;

public class EncodingDetectionTests
{
    [Test]
    public void NoBom_FallsBackToUtf8()
    {
        var result = EncodingDetection.Detect("hello"u8);
        Assert.That(result.Encoding, Is.InstanceOf<UTF8Encoding>());
        Assert.That(result.BomLength, Is.EqualTo(0));
    }

    [Test]
    public void Utf8Bom_Detected()
    {
        ReadOnlySpan<byte> input = [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.Encoding, Is.EqualTo(Encoding.UTF8));
        Assert.That(result.BomLength, Is.EqualTo(3));
    }

    [Test]
    public void Utf16LeBom_Detected()
    {
        ReadOnlySpan<byte> input = [0xFF, 0xFE, (byte)'h', 0x00];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.Encoding.CodePage, Is.EqualTo(Encoding.Unicode.CodePage));
        Assert.That(result.BomLength, Is.EqualTo(2));
    }

    [Test]
    public void Utf16BeBom_Detected()
    {
        ReadOnlySpan<byte> input = [0xFE, 0xFF, 0x00, (byte)'h'];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.Encoding.CodePage, Is.EqualTo(Encoding.BigEndianUnicode.CodePage));
        Assert.That(result.BomLength, Is.EqualTo(2));
    }

    [Test]
    public void Utf32LeBom_Detected()
    {
        ReadOnlySpan<byte> input = [0xFF, 0xFE, 0x00, 0x00, (byte)'h', 0x00, 0x00, 0x00];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.Encoding.CodePage, Is.EqualTo(Encoding.UTF32.CodePage));
        Assert.That(result.BomLength, Is.EqualTo(4));
    }

    [Test]
    public void Utf32BeBom_Detected()
    {
        ReadOnlySpan<byte> input = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, (byte)'h'];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.BomLength, Is.EqualTo(4));
    }

    [Test]
    public void Utf16LeBom_Disambiguated_FromUtf32LeBom()
    {
        // Without enough bytes for UTF-32 detection, falls through to UTF-16 LE.
        ReadOnlySpan<byte> input = [0xFF, 0xFE, (byte)'h'];
        var result = EncodingDetection.Detect(input);
        Assert.That(result.Encoding.CodePage, Is.EqualTo(Encoding.Unicode.CodePage));
        Assert.That(result.BomLength, Is.EqualTo(2));
    }

    [Test]
    public void EmptyInput_ReturnsUtf8NoBom()
    {
        var result = EncodingDetection.Detect(ReadOnlySpan<byte>.Empty);
        Assert.That(result.Encoding, Is.InstanceOf<UTF8Encoding>());
        Assert.That(result.BomLength, Is.EqualTo(0));
    }
}
