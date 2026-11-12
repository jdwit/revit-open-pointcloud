using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenPointCloud.Tests;

/// <summary>A prototype field: the XML element and the raw values that the bitpack codec writes.</summary>
sealed record E57Field(string Name, string Xml, int Bits, ulong[] Raw)
{
    static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static E57Field Single(string name, params float[] v) =>
        new(name, $"<{name} type=\"Float\" precision=\"single\"/>", 32, v.Select(x => (ulong)(uint)BitConverter.SingleToInt32Bits(x)).ToArray());

    public static E57Field Double(string name, params double[] v) =>
        new(name, $"<{name} type=\"Float\"/>", 64, v.Select(x => (ulong)BitConverter.DoubleToInt64Bits(x)).ToArray());

    public static E57Field Int(string name, long min, long max, params long[] v) =>
        new(name, $"<{name} type=\"Integer\" minimum=\"{min}\" maximum=\"{max}\"/>", Width(min, max), v.Select(x => (ulong)(x - min)).ToArray());

    /// <summary>Integer without minimum and maximum: the full 64-bit range.</summary>
    public static E57Field Int64(string name, params long[] v) =>
        new(name, $"<{name} type=\"Integer\"/>", 64, v.Select(x => unchecked((ulong)(x - long.MinValue))).ToArray());

    public static E57Field Scaled(string name, long min, long max, double scale, double offset, params long[] raw) =>
        new(name, $"<{name} type=\"ScaledInteger\" minimum=\"{min}\" maximum=\"{max}\" scale=\"{N(scale)}\" offset=\"{N(offset)}\"/>",
            Width(min, max), raw.Select(x => (ulong)(x - min)).ToArray());

    static int Width(long min, long max) => max == min ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(max - min));
}

/// <summary>A scan for the test writer. Split is the number of bytes of each bytestream in one data packet.</summary>
sealed record E57Scan(E57Field[] Fields, string Extra = "", int Split = 1 << 12, long? RecordCount = null,
    string Codecs = "", bool OtherPackets = false);

/// <summary>Writes small E57 files for the tests: pages with CRC-32C, an XML section and bitpack data packets.</summary>
sealed class TestE57 : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("opc-e57-").FullName;

    public static string Pose(double w, double x, double y, double z, double tx, double ty, double tz) =>
        $"<pose type=\"Structure\"><rotation type=\"Structure\">{F("w", w)}{F("x", x)}{F("y", y)}{F("z", z)}</rotation>" +
        $"<translation type=\"Structure\">{F("x", tx)}{F("y", ty)}{F("z", tz)}</translation></pose>";

    static string F(string name, double v) => $"<{name} type=\"Float\">{v.ToString("R", CultureInfo.InvariantCulture)}</{name}>";

    public string Write(string name, params E57Scan[] scans)
    {
        var data = new List<byte>(new byte[48]);
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<e57Root type=\"Structure\" xmlns=\"http://www.astm.org/COMMIT/E57/2010-e57-v1.0\">")
            .Append("<formatName type=\"String\"><![CDATA[ASTM E57 3D Imaging Data File]]></formatName><data3D type=\"Vector\" allowHeterogeneousChildren=\"1\">");
        foreach (var s in scans)
        {
            long records = s.RecordCount ?? s.Fields[0].Raw.Length;
            long section = data.Count;
            data.AddRange(new byte[32]);
            var packets = Packets(s);
            var sectionBytes = packets.ToArray();
            long dataStart = section + 32;
            data.AddRange(sectionBytes);
            var header = new byte[32];
            header[0] = 1;
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), (ulong)(data.Count - section));
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)Physical(dataStart));
            for (int i = 0; i < 32; i++) data[(int)section + i] = header[i];
            xml.Append("<vectorChild type=\"Structure\">").Append(s.Extra)
                .Append($"<points type=\"CompressedVector\" fileOffset=\"{Physical(section)}\" recordCount=\"{records}\"><prototype type=\"Structure\">");
            foreach (var f in s.Fields) xml.Append(f.Xml);
            xml.Append("</prototype><codecs type=\"Vector\" allowHeterogeneousChildren=\"1\">").Append(s.Codecs).Append("</codecs></points></vectorChild>");
        }
        xml.Append("</data3D></e57Root>");
        long xmlStart = data.Count;
        var xmlBytes = Encoding.UTF8.GetBytes(xml.ToString());
        data.AddRange(xmlBytes);

        int pages = (data.Count + 1019) / 1020;
        var h = new byte[48];
        "ASTM-E57"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(16), (ulong)pages * 1024);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(24), (ulong)Physical(xmlStart));
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(32), (ulong)xmlBytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(40), 1024);
        for (int i = 0; i < 48; i++) data[i] = h[i];

        var file = new byte[pages * 1024];
        for (int p = 0; p < pages; p++)
        {
            int n = Math.Min(1020, data.Count - p * 1020);
            data.CopyTo(p * 1020, file, p * 1024, n);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(p * 1024 + 1020), Crc32C.Compute(file.AsSpan(p * 1024, 1020)));
        }
        string path = Path.Combine(Folder, name);
        File.WriteAllBytes(path, file);
        return path;
    }

    // Data packets. Each packet takes up to Split bytes of each bytestream, so values cross packet borders.
    static List<byte> Packets(E57Scan s)
    {
        var streams = s.Fields.Select(Pack).ToArray();
        var at = new int[streams.Length];
        var result = new List<byte>();
        if (s.OtherPackets)
        {
            result.AddRange(new byte[] { 0, 0, 15, 0 }.Concat(new byte[12])); // index packet, 16 bytes
            result.AddRange(new byte[] { 2, 0, 7, 0, 0, 0, 0, 0 });             // empty packet, 8 bytes
        }
        while (at.Select((a, i) => a < streams[i].Length).Any(x => x))
        {
            var lengths = streams.Select((b, i) => Math.Min(s.Split, b.Length - at[i])).ToArray();
            int length = 6 + 2 * streams.Length + lengths.Sum();
            int padded = (length + 3) / 4 * 4;
            var p = new byte[padded];
            p[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), (ushort)(padded - 1));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), (ushort)streams.Length);
            int o = 6 + 2 * streams.Length;
            for (int i = 0; i < streams.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6 + 2 * i), (ushort)lengths[i]);
                Array.Copy(streams[i], at[i], p, o, lengths[i]);
                o += lengths[i];
                at[i] += lengths[i];
            }
            result.AddRange(p);
        }
        return result;
    }

    // Bitpack: values follow each other from the least significant bit, with no padding.
    static byte[] Pack(E57Field f)
    {
        var bytes = new byte[(f.Raw.Length * (long)f.Bits + 7) / 8];
        long bit = 0;
        foreach (var v in f.Raw)
            for (int b = 0; b < f.Bits; b++, bit++)
                if (((v >> b) & 1) != 0) bytes[bit >> 3] |= (byte)(1 << (int)(bit & 7));
        return bytes;
    }

    static long Physical(long logical) => logical / 1020 * 1024 + logical % 1020;

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}
