using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.Intrinsics.X86;
using System.Xml;
using System.Xml.Linq;

namespace OpenPointCloud;

/// <summary>
/// Reads the 3D scans of an E57 file (ASTM E2807) and applies the pose of each scan.
/// The reader supports Cartesian and spherical coordinates, colour, intensity and the bitpack codec.
/// </summary>
public static class E57
{
    public static (double[] Xyz, int[] Rgb) Read(string path, Action<double>? progress = null)
    {
        using var file = new PagedFile(path);
        List<Scan> scans;
        try { scans = ReadScans(file); }
        catch (Exception e) when (e is XmlException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The E57 XML section is not valid. " + e.Message, e);
        }
        long total = scans.Sum(s => s.Count);
        if (total * 3 > Array.MaxLength) throw new InvalidDataException("The E57 file has too many points.");
        var o = new Output(total);
        long before = 0;
        foreach (var s in scans)
        {
            long start = before;
            ReadScan(file, s, o, done => progress?.Invoke(total == 0 ? 1 : (double)(start + done) / total));
            before += s.Count;
        }
        return o.Result();
    }

    // Header (48 bytes): signature, major, minor, physical length, XML offset, XML length, page size.
    static List<Scan> ReadScans(PagedFile file)
    {
        Span<byte> h = stackalloc byte[48];
        file.Read(0, h);
        if (!h[..8].SequenceEqual("ASTM-E57"u8)) throw new InvalidDataException("File is not an E57 file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(h[8..]) != 1)
            throw new InvalidDataException("The add-in reads only E57 version 1.");
        if (BinaryPrimitives.ReadUInt64LittleEndian(h[16..]) != (ulong)file.Length)
            throw new InvalidDataException("The E57 file is not complete.");
        if (BinaryPrimitives.ReadUInt64LittleEndian(h[40..]) != PagedFile.PageSize)
            throw new InvalidDataException("The E57 page size is not 1024.");
        long xmlOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(h[24..]);
        long xmlLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(h[32..]);
        if (xmlLength <= 0 || xmlLength > int.MaxValue) throw new InvalidDataException("The E57 XML section is not valid.");

        var xml = new byte[xmlLength];
        file.Read(PagedFile.Logical(xmlOffset), xml);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XElement root;
        using (var reader = XmlReader.Create(new MemoryStream(xml), settings)) root = XElement.Load(reader);

        var data3D = Child(root, "data3D");
        var scans = data3D?.Elements().Select(ParseScan).ToList() ?? new List<Scan>();
        if (scans.Count == 0) throw new InvalidDataException("The E57 file has no 3D scans.");
        return scans;
    }

    static Scan ParseScan(XElement scan)
    {
        var points = Child(scan, "points") ?? throw new InvalidDataException("An E57 scan has no points.");
        var prototype = Child(points, "prototype") ?? throw new InvalidDataException("An E57 scan has no prototype.");
        var codecs = Child(points, "codecs");
        if (codecs != null && codecs.Elements().SelectMany(c => c.Elements()).Any(e => e.Name.LocalName is not ("inputs" or "bitPackCodec")))
            throw new InvalidDataException("The add-in reads only the E57 bitpack codec.");

        var fields = new List<Field>();
        AddFields(prototype, fields);
        var s = new Scan(fields, Long(points, "recordCount"), Long(points, "fileOffset"), Pose(Child(scan, "pose")));
        if (s.Has("cartesianX") && s.Has("cartesianY") && s.Has("cartesianZ")) s.Cartesian = true;
        else if (!(s.Has("sphericalRange") && s.Has("sphericalAzimuth") && s.Has("sphericalElevation")))
            throw new InvalidDataException("An E57 scan has no Cartesian or spherical coordinates.");

        var colour = Child(scan, "colorLimits");
        s.Red = Limits(s.Field("colorRed"), colour, "colorRed");
        s.Green = Limits(s.Field("colorGreen"), colour, "colorGreen");
        s.Blue = Limits(s.Field("colorBlue"), colour, "colorBlue");
        s.Intensity = Limits(s.Field("intensity"), Child(scan, "intensityLimits"), "intensity");
        return s;
    }

    // Each terminal node of the prototype is one bytestream, in document order.
    static void AddFields(XElement node, List<Field> fields)
    {
        foreach (var e in node.Elements())
        {
            string type = (string?)e.Attribute("type") ?? "";
            switch (type)
            {
                case "Structure": AddFields(e, fields); break;
                case "Float":
                    bool single = (string?)e.Attribute("precision") == "single";
                    fields.Add(new Field(e.Name.LocalName, single ? Kind.Single : Kind.Double, single ? 32 : 64, 0, 1, 0,
                        OptionalDouble(e, "minimum"), OptionalDouble(e, "maximum")));
                    break;
                case "Integer":
                case "ScaledInteger":
                    long min = OptionalLong(e, "minimum") ?? long.MinValue;
                    long max = OptionalLong(e, "maximum") ?? long.MaxValue;
                    if (max < min) throw new InvalidDataException($"E57 field {e.Name.LocalName} has a maximum less than its minimum.");
                    double scale = OptionalDouble(e, "scale") ?? 1, offset = OptionalDouble(e, "offset") ?? 0;
                    bool range = e.Attribute("minimum") != null && e.Attribute("maximum") != null;
                    fields.Add(new Field(e.Name.LocalName, Kind.Integer, BitLength(unchecked((ulong)(max - min))), min, scale, offset,
                        range ? min * scale + offset : null, range ? max * scale + offset : null));
                    break;
                default:
                    throw new InvalidDataException($"The add-in cannot read the E57 field {e.Name.LocalName} of type {type}.");
            }
        }
    }

    // Range of colour or intensity: from the scan, then from the field, then a default.
    // An integer range has one step more than max - min, so 0..65535 maps to 0..255 as v / 256.
    static (double Lo, double Width)? Limits(Field? f, XElement? limits, string name)
    {
        if (f == null) return null;
        bool steps = f.Kind == Kind.Integer && f.Scale == 1 && f.Offset == 0;
        double? lo = Value(Child(limits, name + "Minimum")) ?? f.Lo;
        double? hi = Value(Child(limits, name + "Maximum")) ?? f.Hi;
        if (lo is double a && hi is double b && b > a) return (a, b - a + (steps ? 1 : 0));
        return steps ? (0, 256) : (0, 1);
    }

    static double[] Pose(XElement? pose)
    {
        var r = Child(pose, "rotation");
        double w = Value(Child(r, "w")) ?? 1, x = Value(Child(r, "x")) ?? 0, y = Value(Child(r, "y")) ?? 0, z = Value(Child(r, "z")) ?? 0;
        double n = Math.Sqrt(w * w + x * x + y * y + z * z);
        if (n == 0) { w = 1; n = 1; }
        w /= n; x /= n; y /= n; z /= n;
        var t = Child(pose, "translation");
        return new[]
        {
            1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y), Value(Child(t, "x")) ?? 0,
            2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x), Value(Child(t, "y")) ?? 0,
            2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y), Value(Child(t, "z")) ?? 0,
        };
    }

    // Compressed vector section header (32 bytes): id, reserved, logical length, data offset, index offset.
    static void ReadScan(PagedFile file, Scan s, Output o, Action<long> progress)
    {
        if (s.Count == 0) return;
        long section = PagedFile.Logical(s.Offset);
        Span<byte> h = stackalloc byte[32];
        file.Read(section, h);
        if (h[0] != 1) throw new InvalidDataException("An E57 scan has no valid compressed vector section.");
        long end = section + (long)BinaryPrimitives.ReadUInt64LittleEndian(h[8..]);
        long pos = PagedFile.Logical((long)BinaryPrimitives.ReadUInt64LittleEndian(h[16..]));

        var streams = s.Fields.Select(_ => new BitStream()).ToArray();
        var packet = new byte[1 << 16];
        var values = s.Fields.Select(_ => new double[Chunk]).ToArray();
        long done = 0, reported = 0;
        while (true)
        {
            long ready = s.Count - done;
            for (int i = 0; i < streams.Length; i++)
                if (s.Fields[i].Bits > 0) ready = Math.Min(ready, streams[i].Bits / s.Fields[i].Bits);
            if (ready > 0)
            {
                Decode(s, streams, values, ready, o);
                done += ready;
                if (done - reported >= 1 << 20 || done == s.Count) progress(reported = done);
            }
            if (done == s.Count) return;
            if (pos + 4 > end) throw new InvalidDataException("An E57 scan has fewer points than its header gives.");

            // Packet header: type, flags, logical length - 1. Type 1 is data, 0 is index, 2 is empty.
            file.Read(pos, packet.AsSpan(0, 4));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)) + 1;
            if (pos + length > end) throw new InvalidDataException("An E57 data packet is not valid.");
            if (packet[0] == 1)
            {
                file.Read(pos, packet.AsSpan(0, length));
                int count = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4));
                if (count != streams.Length) throw new InvalidDataException("An E57 data packet has the wrong number of bytestreams.");
                int at = 6 + 2 * count;
                for (int i = 0; i < count; i++)
                {
                    int n = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6 + 2 * i));
                    if (at + n > length) throw new InvalidDataException("An E57 data packet is not valid.");
                    streams[i].Append(packet.AsSpan(at, n));
                    at += n;
                }
            }
            else if (packet[0] > 2) throw new InvalidDataException("An E57 packet has an unknown type.");
            pos += length;
        }
    }

    const int Chunk = 1 << 14;

    static void Decode(Scan s, BitStream[] streams, double[][] values, long n, Output o)
    {
        for (long from = 0; from < n; from += Chunk)
        {
            int m = (int)Math.Min(Chunk, n - from);
            for (int i = 0; i < streams.Length; i++)
            {
                var f = s.Fields[i];
                var dst = values[i];
                var bs = streams[i];
                for (int k = 0; k < m; k++)
                    dst[k] = f.Kind switch
                    {
                        Kind.Single => BitConverter.Int32BitsToSingle((int)bs.Take(32)),
                        Kind.Double => BitConverter.Int64BitsToDouble((long)bs.Take(64)),
                        _ => unchecked((long)((ulong)f.Min + bs.Take(f.Bits))) * f.Scale + f.Offset,
                    };
            }
            for (int k = 0; k < m; k++) o.Add(s, values, k);
        }
    }

    static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    // Value of a Float, Integer or ScaledInteger node. An empty node is zero.
    static double? Value(XElement? e)
    {
        if (e == null) return null;
        string text = e.Value.Trim();
        double v = text.Length == 0 ? 0 : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if ((string?)e.Attribute("type") == "ScaledInteger")
            v = v * (OptionalDouble(e, "scale") ?? 1) + (OptionalDouble(e, "offset") ?? 0);
        return v;
    }

    static double? OptionalDouble(XElement e, string name) =>
        e.Attribute(name) is { } a ? double.Parse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    static long? OptionalLong(XElement e, string name) =>
        e.Attribute(name) is { } a ? long.Parse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;

    static long Long(XElement e, string name) =>
        OptionalLong(e, name) ?? throw new InvalidDataException($"An E57 compressed vector has no {name}.");

    static int BitLength(ulong range) => range == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(range);

    enum Kind { Single, Double, Integer }

    sealed record Field(string Name, Kind Kind, int Bits, long Min, double Scale, double Offset, double? Lo, double? Hi);

    sealed class Scan
    {
        public Scan(List<Field> fields, long count, long offset, double[] pose)
        {
            Fields = fields;
            Count = count;
            Offset = offset;
            Pose = pose;
            int Slot(string name) => fields.FindIndex(f => f.Name == name);
            (CX, CY, CZ, CInvalid) = (Slot("cartesianX"), Slot("cartesianY"), Slot("cartesianZ"), Slot("cartesianInvalidState"));
            (SR, SA, SE, SInvalid) = (Slot("sphericalRange"), Slot("sphericalAzimuth"), Slot("sphericalElevation"), Slot("sphericalInvalidState"));
            (R, G, B, CInvalidColour) = (Slot("colorRed"), Slot("colorGreen"), Slot("colorBlue"), Slot("isColorInvalid"));
            (I, IInvalid) = (Slot("intensity"), Slot("isIntensityInvalid"));
        }

        public List<Field> Fields { get; }
        public long Count { get; }
        public long Offset { get; }
        public double[] Pose { get; }
        public bool Cartesian { get; set; }
        public (double Lo, double Width)? Red, Green, Blue, Intensity;

        // Index of each known field in Fields, or -1.
        public readonly int CX, CY, CZ, CInvalid, SR, SA, SE, SInvalid, R, G, B, CInvalidColour, I, IInvalid;

        public bool Has(string name) => Fields.Exists(f => f.Name == name);
        public Field? Field(string name) => Fields.Find(f => f.Name == name);
    }

    // Collects the points of all scans in file coordinates.
    sealed class Output
    {
        readonly double[] _xyz;
        readonly int[] _rgb;
        long _n;

        public Output(long capacity)
        {
            _xyz = new double[capacity * 3];
            _rgb = new int[capacity];
        }

        public void Add(Scan s, double[][] v, int k)
        {
            double Get(int slot) => slot >= 0 ? v[slot][k] : 0;
            double x, y, z;
            if (s.Cartesian)
            {
                if (Get(s.CInvalid) != 0) return;
                (x, y, z) = (Get(s.CX), Get(s.CY), Get(s.CZ));
            }
            else
            {
                if (Get(s.SInvalid) != 0) return;
                double r = Get(s.SR), az = Get(s.SA), el = Get(s.SE);
                (x, y, z) = (r * Math.Cos(el) * Math.Cos(az), r * Math.Cos(el) * Math.Sin(az), r * Math.Sin(el));
            }
            var m = s.Pose;
            _xyz[_n * 3] = m[0] * x + m[1] * y + m[2] * z + m[3];
            _xyz[_n * 3 + 1] = m[4] * x + m[5] * y + m[6] * z + m[7];
            _xyz[_n * 3 + 2] = m[8] * x + m[9] * y + m[10] * z + m[11];

            int rgb = 0x808080;
            if (s.Red is { } lr && s.Green is { } lg && s.Blue is { } lb && Get(s.CInvalidColour) == 0)
                rgb = (Byte(Get(s.R), lr) << 16) | (Byte(Get(s.G), lg) << 8) | Byte(Get(s.B), lb);
            else if (s.Intensity is { } li && Get(s.IInvalid) == 0)
                rgb = Byte(Get(s.I), li) * 0x010101;
            _rgb[_n++] = rgb;
        }

        static int Byte(double v, (double Lo, double Width) l) => (int)Math.Clamp(Math.Floor((v - l.Lo) / l.Width * 256), 0, 255);

        public (double[] Xyz, int[] Rgb) Result()
        {
            if (_n == _rgb.Length) return (_xyz, _rgb);
            return (_xyz.AsSpan(0, (int)(_n * 3)).ToArray(), _rgb.AsSpan(0, (int)_n).ToArray());
        }
    }

    /// <summary>A bytestream of a compressed vector. The bitpack codec writes each value from the least significant bit.</summary>
    sealed class BitStream
    {
        byte[] _buf = new byte[1 << 12];
        int _len;
        long _bit;

        public long Bits => _len * 8L - _bit;

        public void Append(ReadOnlySpan<byte> data)
        {
            int drop = (int)(_bit >> 3);
            if (drop > 0)
            {
                Buffer.BlockCopy(_buf, drop, _buf, 0, _len - drop);
                _len -= drop;
                _bit -= drop * 8L;
            }
            // Keep 9 spare bytes, so that Take can always read 8 bytes plus 1.
            if (_len + data.Length + 9 > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + data.Length + 9));
            data.CopyTo(_buf.AsSpan(_len));
            _len += data.Length;
        }

        public ulong Take(int width)
        {
            if (width == 0) return 0;
            int i = (int)(_bit >> 3), shift = (int)(_bit & 7);
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(i)) >> shift;
            if (shift + width > 64) v |= (ulong)_buf[i + 8] << (64 - shift);
            _bit += width;
            return width == 64 ? v : v & ((1UL << width) - 1);
        }
    }
}

/// <summary>
/// E57 file in pages of 1024 bytes. Each page has 1020 bytes of data and a CRC-32C checksum.
/// A logical offset counts only the data bytes.
/// </summary>
sealed class PagedFile : IDisposable
{
    public const int PageSize = 1024;
    const int Data = 1020;

    readonly FileStream _fs;
    readonly byte[] _page = new byte[PageSize];
    long _loaded = -1;

    public PagedFile(string path)
    {
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        if (_fs.Length == 0 || _fs.Length % PageSize != 0) throw new InvalidDataException("The E57 file is not complete.");
    }

    public long Length => _fs.Length;

    public static long Logical(long physical) => physical / PageSize * Data + physical % PageSize;

    public void Read(long logical, Span<byte> dst)
    {
        while (dst.Length > 0)
        {
            Load(logical / Data);
            int at = (int)(logical % Data), n = Math.Min(Data - at, dst.Length);
            _page.AsSpan(at, n).CopyTo(dst);
            dst = dst[n..];
            logical += n;
        }
    }

    void Load(long page)
    {
        if (page == _loaded) return;
        if ((page + 1) * PageSize > _fs.Length) throw new InvalidDataException("The E57 file is not complete.");
        _fs.Position = page * PageSize;
        _fs.ReadExactly(_page);
        if (Crc32C.Compute(_page.AsSpan(0, Data)) != BinaryPrimitives.ReadUInt32BigEndian(_page.AsSpan(Data)))
            throw new InvalidDataException($"E57 page {page} has a bad checksum. The file has damage.");
        _loaded = page;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>CRC-32C (Castagnoli). Uses the SSE 4.2 instruction if the processor has it.</summary>
static class Crc32C
{
    static readonly uint[] Table = MakeTable();

    public static uint Compute(ReadOnlySpan<byte> data) => Compute(data, Sse42.X64.IsSupported);

    internal static uint Compute(ReadOnlySpan<byte> data, bool hardware)
    {
        uint crc = ~0u;
        int i = 0;
        if (hardware)
        {
            ulong c = crc;
            for (; i + 8 <= data.Length; i += 8) c = Sse42.X64.Crc32(c, BinaryPrimitives.ReadUInt64LittleEndian(data[i..]));
            crc = (uint)c;
            for (; i < data.Length; i++) crc = Sse42.Crc32(crc, data[i]);
        }
        else
        {
            for (; i < data.Length; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0x82F63B78 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
