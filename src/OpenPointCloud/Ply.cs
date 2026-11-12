using System.IO;
using System.Text;

namespace OpenPointCloud;

/// <summary>Reads the vertices of a binary little-endian PLY file.</summary>
public static class Ply
{
    public static (double[] Xyz, int[] Rgb) Read(string path, Action<double>? progress = null)
    {
        using var fs = File.OpenRead(path);
        var (count, stride, offsets, types, bodyStart) = ReadHeader(fs);
        if (!offsets.ContainsKey("x") || !offsets.ContainsKey("y") || !offsets.ContainsKey("z"))
            throw new InvalidDataException("PLY has no x, y or z property.");
        bool hasRgb = offsets.ContainsKey("red") && offsets.ContainsKey("green") && offsets.ContainsKey("blue");

        var xyz = new double[count * 3];
        var rgb = new int[count];
        fs.Position = bodyStart;
        const int block = 1 << 16;
        var buf = new byte[block * stride];
        long done = 0;
        while (done < count)
        {
            int n = (int)Math.Min(block, count - done);
            fs.ReadExactly(buf, 0, n * stride);
            for (int i = 0; i < n; i++)
            {
                int b = i * stride;
                long v = done + i;
                xyz[v * 3] = Num(buf, b + offsets["x"], types["x"]);
                xyz[v * 3 + 1] = Num(buf, b + offsets["y"], types["y"]);
                xyz[v * 3 + 2] = Num(buf, b + offsets["z"], types["z"]);
                rgb[v] = hasRgb
                    ? (buf[b + offsets["red"]] << 16) | (buf[b + offsets["green"]] << 8) | buf[b + offsets["blue"]]
                    : 0x808080;
            }
            done += n;
            progress?.Invoke((double)done / count);
        }
        return (xyz, rgb);
    }

    static (long Count, int Stride, Dictionary<string, int> Offsets, Dictionary<string, string> Types, long BodyStart)
        ReadHeader(Stream s)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        while (true)
        {
            int c = s.ReadByte();
            if (c < 0) throw new InvalidDataException("PLY header has no end_header.");
            if (c == '\n')
            {
                string line = sb.ToString().TrimEnd('\r');
                sb.Clear();
                lines.Add(line);
                if (line == "end_header") break;
            }
            else sb.Append((char)c);
        }
        if (lines[0] != "ply") throw new InvalidDataException("File is not a PLY file.");
        if (!lines.Any(l => l.StartsWith("format binary_little_endian")))
            throw new InvalidDataException("The add-in reads only binary little-endian PLY.");

        long count = 0;
        int stride = 0;
        bool inVertex = false;
        var offsets = new Dictionary<string, int>();
        var types = new Dictionary<string, string>();
        foreach (var l in lines)
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "element")
            {
                inVertex = p[1] == "vertex";
                if (inVertex) count = long.Parse(p[2]);
                else if (count > 0) break;
            }
            else if (p[0] == "property" && inVertex)
            {
                if (p[1] == "list") throw new InvalidDataException("The add-in cannot read list properties on vertices.");
                offsets[p[2]] = stride;
                types[p[2]] = p[1];
                stride += Size(p[1]);
            }
        }
        return (count, stride, offsets, types, s.Position);
    }

    static int Size(string t) => t switch
    {
        "char" or "uchar" or "int8" or "uint8" => 1,
        "short" or "ushort" or "int16" or "uint16" => 2,
        "int" or "uint" or "float" or "int32" or "uint32" or "float32" => 4,
        "double" or "float64" => 8,
        _ => throw new InvalidDataException("Unknown PLY type " + t),
    };

    static double Num(byte[] b, int o, string t) => t switch
    {
        "float" or "float32" => BitConverter.ToSingle(b, o),
        "double" or "float64" => BitConverter.ToDouble(b, o),
        "int" or "int32" => BitConverter.ToInt32(b, o),
        _ => throw new InvalidDataException("Coordinate type " + t + " is not supported."),
    };
}
