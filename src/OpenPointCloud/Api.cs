using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace OpenPointCloud;

/// <summary>Facts about a PLY point cloud. Coordinates are in meters, in the coordinates of the file.</summary>
public sealed record CloudInfo(
    string File, long Bytes, int Points, bool HasColour,
    double[] Min, double[] Max, double[] Size, bool IndexFromCache);

/// <summary>Result of a link. ElementId is the point cloud instance.</summary>
public sealed record LinkResult(long ElementId, CloudInfo Cloud, double Seconds)
{
    /// <summary>The result as JSON, for scripts and agents.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Api.Json);
}

/// <summary>
/// Public API for scripts and agents. The API shows no dialogs.
/// Call it in a Revit API context, for example from pyRevit or an external command.
/// </summary>
public static class Api
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Links a binary PLY as a point cloud. The first link makes the index next to the PLY.</summary>
    public static LinkResult Link(Document doc, string plyPath)
    {
        var ply = Path.GetFullPath(plyPath);
        if (!File.Exists(ply)) throw new FileNotFoundException("PLY file not found.", ply);
        bool cached = Index.IsCurrent(ply);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var id = App.Link(doc, ply);
        clock.Stop();
        return new LinkResult(id.Value, Inspect(ply, cached), Math.Round(clock.Elapsed.TotalSeconds, 2));
    }

    /// <summary>Reads the facts of a PLY. Makes the index if necessary. Needs no Revit document.</summary>
    public static CloudInfo Inspect(string plyPath) => Inspect(Path.GetFullPath(plyPath), Index.IsCurrent(Path.GetFullPath(plyPath)));

    internal static CloudInfo Inspect(string ply, bool cached)
    {
        if (!Index.IsCurrent(ply)) Index.Build(ply);
        var index = Index.Load(ply);
        var min = new double[3];
        var max = new double[3];
        var size = new double[3];
        for (int a = 0; a < 3; a++)
        {
            min[a] = Math.Round(index.Origin[a] + index.Min[a], 3);
            max[a] = Math.Round(index.Origin[a] + index.Max[a], 3);
            size[a] = Math.Round(max[a] - min[a], 3);
        }
        return new CloudInfo(Path.GetFileName(ply), new FileInfo(ply).Length, index.Points.Length,
            HasColour(index.Points), min, max, size, cached);
    }

    /// <summary>True if the points have more than one colour. A sample of 1000 points is enough.</summary>
    internal static bool HasColour(Pt[] points)
    {
        if (points.Length == 0) return false;
        int first = points[0].Color;
        int step = Math.Max(1, points.Length / 1000);
        for (int i = 0; i < points.Length; i += step)
            if (points[i].Color != first) return true;
        return false;
    }
}
