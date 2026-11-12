using System.IO;

namespace OpenPointCloud;

/// <summary>Reads the points of a point cloud file. The file extension selects the reader.</summary>
public static class PointFile
{
    /// <summary>Filter for a file dialog.</summary>
    public const string DialogFilter = "Point cloud (*.ply;*.e57)|*.ply;*.e57";

    /// <summary>Returns the coordinates (x, y, z for each point, in meters) and the colour (0xRRGGBB) of each point.</summary>
    public static (double[] Xyz, int[] Rgb) Read(string path, Action<double>? progress = null) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ply" => Ply.Read(path, progress),
            ".e57" => E57.Read(path, progress),
            _ => throw new InvalidDataException("The add-in reads only PLY and E57 files."),
        };
}
