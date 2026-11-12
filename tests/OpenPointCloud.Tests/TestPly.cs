using System.Text;

namespace OpenPointCloud.Tests;

/// <summary>Writes small PLY files for the tests. Each test gets its own temporary folder.</summary>
sealed class TestPly : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("opc-test-").FullName;

    public string Write(string name, (double X, double Y, double Z)[] points, (byte R, byte G, byte B)[]? colours = null,
        string type = "float", string format = "binary_little_endian")
    {
        string path = Path.Combine(Folder, name);
        var header = new StringBuilder();
        header.Append($"ply\nformat {format} 1.0\nelement vertex {points.Length}\n");
        foreach (var a in new[] { "x", "y", "z" }) header.Append($"property {type} {a}\n");
        if (colours != null) header.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
        header.Append("end_header\n");
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes(header.ToString()));
        for (int i = 0; i < points.Length; i++)
        {
            foreach (var v in new[] { points[i].X, points[i].Y, points[i].Z })
            {
                if (type == "double") w.Write(v);
                else if (type == "int") w.Write((int)v);
                else w.Write((float)v);
            }
            if (colours != null) { w.Write(colours[i].R); w.Write(colours[i].G); w.Write(colours[i].B); }
        }
        return path;
    }

    public static (double X, double Y, double Z)[] Grid(int n, double x0 = 0, double y0 = 0, double z0 = 0, double step = 0.1)
    {
        var pts = new List<(double, double, double)>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                for (int k = 0; k < n; k++)
                    pts.Add((x0 + i * step, y0 + j * step, z0 + k * step));
        return pts.ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}
