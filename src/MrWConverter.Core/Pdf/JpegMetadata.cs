namespace MrWConverter.Core.Pdf;

/// <summary>
/// Takes EXIF, XMP and comments out of a JPEG without touching its picture data, so that
/// a photo put in a PDF keeps its quality but not where and when it was taken.
/// </summary>
internal static class JpegMetadata
{
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte Exif = 0xE1;
    private const byte PhotoshopIrb = 0xED;
    private const byte Comment = 0xFE;

    public static byte[] Without(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != Marker || jpeg[1] != StartOfImage)
        {
            throw new InvalidDataException("This is not a JPEG.");
        }

        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);

        var position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == Marker)
        {
            var kind = jpeg[position + 1];

            // Fill bytes before a marker carry nothing.
            if (kind == Marker)
            {
                position++;
                continue;
            }

            if (kind == StartOfScan)
            {
                break;
            }

            var length = 2 + (jpeg[position + 2] << 8 | jpeg[position + 3]);
            if (position + length > jpeg.Length)
            {
                throw new InvalidDataException("The JPEG is cut short.");
            }

            if (kind is not (Exif or PhotoshopIrb or Comment))
            {
                output.Write(jpeg, position, length);
            }

            position += length;
        }

        output.Write(jpeg, position, jpeg.Length - position);
        return output.ToArray();
    }
}
