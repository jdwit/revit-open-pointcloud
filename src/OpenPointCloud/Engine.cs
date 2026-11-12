using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;

namespace OpenPointCloud;

/// <summary>Revit point cloud engine for PLY files. The identifier of a cloud is the path of the PLY.</summary>
public sealed class PlyEngine : IPointCloudEngine
{
    public const string Id = "ply";

    public IPointCloudAccess CreatePointCloudAccess(string identifier)
    {
        try
        {
            // Make a new index only if the PLY is present. Without the PLY, the cache is enough.
            if (File.Exists(identifier) && !Index.IsCurrent(identifier)) Index.Build(identifier);
            return new PlyAccess(Index.Load(identifier));
        }
        catch (Exception e)
        {
            Log.Write($"Cannot open {identifier}: {e.Message}");
            throw;
        }
    }

    public void Free() { }
}

public sealed class PlyAccess : IPointCloudAccess
{
    const double MetersToFeet = 1.0 / 0.3048;
    readonly Index _ix;

    public PlyAccess(Index ix) => _ix = ix;

    public string GetName() => Path.GetFileName(_ix.Source);
    public PointCloudColorEncoding GetColorEncoding() => PointCloudColorEncoding.ARGB;
    // Revit ignores this offset (tested 12-11-2026). App.Link puts the origin in the instance transform.
    public XYZ GetOffset() => XYZ.Zero;
    public double GetUnitsToFeetConversionFactor() => MetersToFeet;

    public Outline GetExtent() =>
        new(new XYZ(_ix.Min[0], _ix.Min[1], _ix.Min[2]), new XYZ(_ix.Max[0], _ix.Max[1], _ix.Max[2]));

    public IPointSetIterator CreatePointSetIterator(PointCloudFilter filter, ElementId viewId) =>
        new PlyIterator(_ix, filter, 0);

    public IPointSetIterator CreatePointSetIterator(PointCloudFilter filter, double density, ElementId viewId) =>
        new PlyIterator(_ix, filter, density);

    // Revit uses this call for snapping: a small filter around the cursor.
    public int ReadPoints(PointCloudFilter filter, ElementId viewId, IntPtr buffer, int size) =>
        new PlyIterator(_ix, filter, 0).ReadPoints(buffer, size);

    public void Free() { }
}

/// <summary>Gives the points of the cells that the Revit filter accepts.</summary>
public sealed class PlyIterator : IPointSetIterator
{
    readonly SliceReader _reader;

    public PlyIterator(Index ix, PointCloudFilter filter, double density)
    {
        var todo = new List<(int Cell, bool Partial, int Take)>();
        for (int i = 0; i < ix.Cells.Length; i++)
        {
            var c = ix.Cells[i];
            int test = filter.TestCell(new XYZ(c.MinX, c.MinY, c.MinZ), new XYZ(c.MaxX, c.MaxY, c.MaxZ));
            if (test < 0) continue;
            todo.Add((i, test == 0, SliceReader.Take(c, density)));
        }
        _reader = new SliceReader(ix, todo,
            (c, n) => filter.PrepareForCell(new XYZ(c.MinX, c.MinY, c.MinZ), new XYZ(c.MaxX, c.MaxY, c.MaxZ), n),
            p => filter.TestPoint(new CloudPoint(p.X, p.Y, p.Z, p.Color)));
    }

    public unsafe int ReadPoints(IntPtr buffer, int size) => _reader.Read(new Span<Pt>((void*)buffer, size));

    public void Free() { }
}

/// <summary>
/// Reads the points of a list of cells in rounds. Each round takes the next slice of every cell.
/// Revit can stop reading at any point, so the points read so far are always an even sample.
/// The class has no Revit types, so tests can use it without Revit.
/// </summary>
public sealed class SliceReader
{
    public const int Slice = 256;

    readonly Index _ix;
    readonly List<(int Cell, bool Partial, int Take)> _todo;
    readonly Action<Cell, int> _prepare;
    readonly Func<Pt, bool> _test;
    int _t, _p, _round;

    /// <param name="todo">Cells to read. Partial cells need a test per point. Take is the number of points to read.</param>
    /// <param name="prepare">Called before the points of a partial cell, with the number of points in the slice.</param>
    /// <param name="test">Point test for partial cells.</param>
    public SliceReader(Index ix, IEnumerable<(int Cell, bool Partial, int Take)> todo, Action<Cell, int> prepare, Func<Pt, bool> test)
    {
        _ix = ix;
        _todo = todo.ToList();
        _prepare = prepare;
        _test = test;
    }

    /// <summary>Number of points to read from a cell. Density is points per square foot of cell footprint. Zero or less means all points.</summary>
    public static int Take(Cell c, double density)
    {
        if (density <= 0) return c.Count;
        const double f = 1.0 / 0.3048;
        double area = Math.Max((c.MaxX - c.MinX) * f, 0.1) * Math.Max((c.MaxY - c.MinY) * f, 0.1);
        return (int)Math.Min(c.Count, Math.Ceiling(density * area));
    }

    /// <summary>Fills the buffer. Returns the number of points. Zero means the end.</summary>
    public int Read(Span<Pt> dst)
    {
        int n = 0;
        while (n < dst.Length && _todo.Count > 0)
        {
            var (ci, partial, take) = _todo[_t];
            var c = _ix.Cells[ci];
            int from = _round * Slice, to = Math.Min(take, from + Slice);
            if (_p == 0 && partial) _prepare(c, to - from);
            while (n < dst.Length && from + _p < to)
            {
                var p = _ix.Points[c.Start + from + _p++];
                if (partial && !_test(p)) continue;
                dst[n++] = p;
            }
            if (from + _p < to) break;
            _p = 0;
            if (to >= take) _todo.RemoveAt(_t);
            else _t++;
            if (_t >= _todo.Count) { _t = 0; _round++; }
        }
        return n;
    }
}

static class Log
{
    static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "revit-open-pointcloud", "log.txt");

    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.AppendAllText(File_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
