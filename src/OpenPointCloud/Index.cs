using System.IO;
using System.Runtime.InteropServices;

namespace OpenPointCloud;

/// <summary>Point in the cache and in the Revit buffer. Layout is equal to Revit CloudPoint.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Pt
{
    public float X, Y, Z;
    public int Color;
}

/// <summary>Cell of the uniform grid. The cache keeps the points of a cell in random order.</summary>
public readonly record struct Cell(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ, int Start, int Count);

/// <summary>Spatial index of a PLY file. The cache file is next to the PLY.</summary>
public sealed class Index
{
    const int Magic = 0x49435052; // "RPCI"
    const int Version = 3;

    public string Source { get; }
    public double CellSize { get; }
    public Cell[] Cells { get; }
    public Pt[] Points { get; }
    public float[] Min { get; } = new float[3];
    public float[] Max { get; } = new float[3];

    /// <summary>Origin in meters. The cache keeps the points relative to it, so float precision stays at 0.1 mm.</summary>
    public double[] Origin { get; }

    Index(string source, double cellSize, double[] origin, Cell[] cells, Pt[] points)
    {
        Source = source;
        CellSize = cellSize;
        Origin = origin;
        Cells = cells;
        Points = points;
        for (int a = 0; a < 3; a++) { Min[a] = float.MaxValue; Max[a] = float.MinValue; }
        foreach (var c in cells)
        {
            Min[0] = Math.Min(Min[0], c.MinX); Min[1] = Math.Min(Min[1], c.MinY); Min[2] = Math.Min(Min[2], c.MinZ);
            Max[0] = Math.Max(Max[0], c.MaxX); Max[1] = Math.Max(Max[1], c.MaxY); Max[2] = Math.Max(Max[2], c.MaxZ);
        }
    }

    public static string CachePath(string ply) => ply + ".opc";

    /// <summary>Optional sidecar with the move "dx dy dz" in meters. The index applies it to all points.</summary>
    public static string AlignPath(string ply) => ply + ".align";

    static double[] ReadAlign(string ply)
    {
        var p = AlignPath(ply);
        if (!File.Exists(p)) return new double[3];
        var v = File.ReadAllText(p).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (v.Length != 3) throw new InvalidDataException("Align file needs three numbers: dx dy dz.");
        return v;
    }

    static long AlignStamp(string ply) => File.Exists(AlignPath(ply)) ? File.GetLastWriteTimeUtc(AlignPath(ply)).Ticks : 0;

    /// <summary>Returns true if the cache exists and matches the source file.</summary>
    public static bool IsCurrent(string ply)
    {
        var cache = CachePath(ply);
        if (!File.Exists(cache)) return false;
        var src = new FileInfo(ply);
        using var r = new BinaryReader(File.OpenRead(cache));
        return r.ReadInt32() == Magic && r.ReadInt32() == Version
            && r.ReadInt64() == src.Length && r.ReadInt64() == src.LastWriteTimeUtc.Ticks && r.ReadInt64() == AlignStamp(ply);
    }

    /// <summary>Reads the PLY, sorts the points into cells and writes the cache.</summary>
    public static void Build(string ply, double cellSize = 1.0, Action<string, double>? progress = null)
    {
        var (xyz, rgb) = Ply.Read(ply, f => progress?.Invoke("Read", f));
        long n = rgb.Length;
        var align = ReadAlign(ply);
        for (long i = 0; i < n; i++)
            for (int a = 0; a < 3; a++) xyz[i * 3 + a] += align[a];
        var min = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
        for (long i = 0; i < n; i++)
            for (int a = 0; a < 3; a++) min[a] = Math.Min(min[a], xyz[i * 3 + a]);
        var origin = min.Select(m => Math.Floor(m / 100) * 100).ToArray();

        // Cell key per point, then sort the point order by key.
        var key = new long[n];
        var order = new int[n];
        for (long i = 0; i < n; i++)
        {
            long cx = (long)((xyz[i * 3] - min[0]) / cellSize);
            long cy = (long)((xyz[i * 3 + 1] - min[1]) / cellSize);
            long cz = (long)((xyz[i * 3 + 2] - min[2]) / cellSize);
            key[i] = (cx << 42) | (cy << 21) | cz;
            order[i] = (int)i;
        }
        progress?.Invoke("Sort", 0.5);
        Array.Sort(key, order);

        var cells = new List<Cell>();
        var pts = new Pt[n];
        var rnd = new Random(1);
        int s = 0;
        while (s < n)
        {
            int e = s;
            while (e < n && key[e] == key[s]) e++;
            // Random order inside the cell, so the first k points are an even sample.
            for (int i = e - 1; i > s; i--)
            {
                int j = rnd.Next(s, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            float[] lo = { float.MaxValue, float.MaxValue, float.MaxValue };
            float[] hi = { float.MinValue, float.MinValue, float.MinValue };
            for (int i = s; i < e; i++)
            {
                long o = order[i];
                var p = new Pt { X = (float)(xyz[o * 3] - origin[0]), Y = (float)(xyz[o * 3 + 1] - origin[1]), Z = (float)(xyz[o * 3 + 2] - origin[2]), Color = rgb[o] };
                pts[i] = p;
                lo[0] = Math.Min(lo[0], p.X); lo[1] = Math.Min(lo[1], p.Y); lo[2] = Math.Min(lo[2], p.Z);
                hi[0] = Math.Max(hi[0], p.X); hi[1] = Math.Max(hi[1], p.Y); hi[2] = Math.Max(hi[2], p.Z);
            }
            cells.Add(new Cell(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], s, e - s));
            s = e;
        }
        progress?.Invoke("Write", 0.9);

        var src = new FileInfo(ply);
        var tmp = CachePath(ply) + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Magic); w.Write(Version); w.Write(src.Length); w.Write(src.LastWriteTimeUtc.Ticks); w.Write(AlignStamp(ply));
            w.Write(cellSize); w.Write(origin[0]); w.Write(origin[1]); w.Write(origin[2]); w.Write(cells.Count); w.Write(n);
            foreach (var c in cells)
            {
                w.Write(c.MinX); w.Write(c.MinY); w.Write(c.MinZ);
                w.Write(c.MaxX); w.Write(c.MaxY); w.Write(c.MaxZ);
                w.Write(c.Start); w.Write(c.Count);
            }
            w.Write(MemoryMarshal.AsBytes(pts.AsSpan()));
        }
        File.Move(tmp, CachePath(ply), true);
        Forget(ply);
        progress?.Invoke("Done", 1.0);
    }

    static readonly Dictionary<string, Index> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads the cache once per Revit session.</summary>
    public static Index Load(string ply)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(ply, out var ix)) return ix;
            using var r = new BinaryReader(File.OpenRead(CachePath(ply)));
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version) throw new InvalidDataException("Cache format is not valid.");
            r.ReadInt64(); r.ReadInt64(); r.ReadInt64();
            double cellSize = r.ReadDouble();
            var origin = new[] { r.ReadDouble(), r.ReadDouble(), r.ReadDouble() };
            int nc = r.ReadInt32();
            long n = r.ReadInt64();
            var cells = new Cell[nc];
            for (int i = 0; i < nc; i++)
                cells[i] = new Cell(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
                    r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), r.ReadInt32());
            var pts = new Pt[n];
            r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(pts.AsSpan()));
            ix = new Index(ply, cellSize, origin, cells, pts);
            Loaded[ply] = ix;
            return ix;
        }
    }

    public static void Forget(string ply)
    {
        lock (Loaded) Loaded.Remove(ply);
    }
}
