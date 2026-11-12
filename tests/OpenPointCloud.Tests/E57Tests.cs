using System.Text;

namespace OpenPointCloud.Tests;

public class E57Tests
{
    static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    static (double X, double Y, double Z) P(double[] xyz, int i) => (xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);

    static void Near((double X, double Y, double Z) expected, (double X, double Y, double Z) actual, int digits = 9)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    static E57Field[] Xyz(params (double X, double Y, double Z)[] p) => new[]
    {
        E57Field.Double("cartesianX", p.Select(v => v.X).ToArray()),
        E57Field.Double("cartesianY", p.Select(v => v.Y).ToArray()),
        E57Field.Double("cartesianZ", p.Select(v => v.Z).ToArray()),
    };

    [Fact]
    public void Reads_a_libE57Format_file_with_two_scans()
    {
        // Scan 1: grid 20 x 20 x 20, step 0.1 m, scaled integers, colour, rotated 90 degrees about Z, moved.
        // Scan 2: 100 spherical points, intensity, every tenth point invalid, moved 10 m in X.
        var (xyz, rgb) = E57.Read(Data("libe57-two-scans.e57"));
        Assert.Equal(8000 + 90, rgb.Length);
        int i = 1 * 400 + 2 * 20 + 3;
        Near((155000 - 0.2, 463000 + 0.1, 2 + 0.3), P(xyz, i));
        Assert.Equal(0x0C1824, rgb[i]);

        double r = 1.01, az = 0.05, el = 0.1;
        Near((10 + r * Math.Cos(el) * Math.Cos(az), r * Math.Cos(el) * Math.Sin(az), r * Math.Sin(el)), P(xyz, 8000), 5);
        Assert.Equal(0x020202, rgb[8000]);
        Assert.Equal(0xFFFFFF, rgb[^1]);
    }

    [Fact]
    public void Reads_16_bit_colour_from_las2e57()
    {
        var (_, rgb) = E57.Read(Data("las2e57-colour.e57"));
        Assert.Equal(153, rgb.Length);
        Assert.Equal(new[] { 0x0000FF, 0x00FF00, 0xFF0000 }, rgb.Distinct().Order());
    }

    [Fact]
    public void Decodes_bit_widths_from_0_to_64_across_packets()
    {
        using var t = new TestE57();
        var x = Enumerable.Range(0, 500).Select(i => (long)(i % 2)).ToArray();                  // 1 bit
        var y = Enumerable.Range(0, 500).Select(i => (long)(i * 7919 % 8000) - 3000).ToArray();   // 13 bits
        var z = Enumerable.Range(0, 500).Select(i => (i % 3 - 1) * (1L << 40) + i).ToArray();    // 64 bits
        var path = t.Write("bits.e57", new E57Scan(new[]
        {
            E57Field.Int("cartesianX", 0, 1, x),
            E57Field.Int("cartesianY", -3000, 4999, y),
            E57Field.Int64("cartesianZ", z),
            E57Field.Int("cartesianInvalidState", 0, 0, new long[500]), // 0 bits
        }, Split: 7));
        var (xyz, _) = E57.Read(path);
        Assert.Equal(500, xyz.Length / 3);
        for (int i = 0; i < 500; i++) Assert.Equal((x[i], y[i], z[i]), ((long)xyz[i * 3], (long)xyz[i * 3 + 1], (long)xyz[i * 3 + 2]));
    }

    [Fact]
    public void Reads_scaled_integers_and_single_floats()
    {
        using var t = new TestE57();
        var path = t.Write("scaled.e57", new E57Scan(new[]
        {
            E57Field.Scaled("cartesianX", -1_000_000, 1_000_000, 0.001, 155000, 1234, -5678),
            E57Field.Single("cartesianY", 1.5f, -2.25f),
            E57Field.Double("cartesianZ", 0.1, 0.2),
        }));
        var (xyz, _) = E57.Read(path);
        Assert.Equal(new[] { 155001.234, 1.5, 0.1, 154994.322, -2.25, 0.2 }, xyz.Select(v => Math.Round(v, 6)));
    }

    [Fact]
    public void Applies_the_pose_of_each_scan()
    {
        using var t = new TestE57();
        // A quaternion that is not normalised: (2, 0, 0, 2) is 90 degrees about Z.
        var path = t.Write("pose.e57",
            new E57Scan(Xyz((1, 0, 0)), TestE57.Pose(2, 0, 0, 2, 100, 200, 300)),
            new E57Scan(Xyz((1, 2, 3)), TestE57.Pose(1, 0, 0, 0, -1, -1, -1)));
        var (xyz, _) = E57.Read(path);
        Near((100, 201, 300), P(xyz, 0));
        Near((0, 1, 2), P(xyz, 1));
    }

    [Fact]
    public void Converts_spherical_coordinates()
    {
        using var t = new TestE57();
        var path = t.Write("sph.e57", new E57Scan(new[]
        {
            E57Field.Double("sphericalRange", 2, 3),
            E57Field.Double("sphericalAzimuth", Math.PI / 2, 0),
            E57Field.Double("sphericalElevation", 0, Math.PI / 6),
        }));
        var (xyz, _) = E57.Read(path);
        Near((0, 2, 0), P(xyz, 0));
        Near((3 * Math.Cos(Math.PI / 6), 0, 1.5), P(xyz, 1));
    }

    [Fact]
    public void Skips_invalid_points()
    {
        using var t = new TestE57();
        var path = t.Write("invalid.e57", new E57Scan(Xyz((1, 1, 1), (2, 2, 2), (3, 3, 3))
            .Append(E57Field.Int("cartesianInvalidState", 0, 2, 1, 0, 2)).ToArray()));
        var (xyz, rgb) = E57.Read(path);
        Assert.Single(rgb);
        Near((2, 2, 2), P(xyz, 0));
    }

    [Fact]
    public void Uses_intensity_as_grey_without_valid_colour()
    {
        using var t = new TestE57();
        var path = t.Write("grey.e57",
            new E57Scan(Xyz((0, 0, 0), (1, 1, 1)).Concat(new[]
            {
                E57Field.Int("colorRed", 0, 255, 255, 255), E57Field.Int("colorGreen", 0, 255, 0, 0),
                E57Field.Int("colorBlue", 0, 255, 0, 0), E57Field.Int("isColorInvalid", 0, 1, 0, 1),
                E57Field.Int("intensity", 0, 2047, 0, 1024),
            }).ToArray()),
            new E57Scan(Xyz((2, 2, 2)).Append(E57Field.Single("intensity", 0.5f)).ToArray(),
                "<intensityLimits type=\"Structure\"><intensityMinimum type=\"Float\">0</intensityMinimum>" +
                "<intensityMaximum type=\"Float\">2</intensityMaximum></intensityLimits>"),
            new E57Scan(Xyz((3, 3, 3))));
        var (_, rgb) = E57.Read(path);
        Assert.Equal(new[] { 0xFF0000, 0x808080, 0x404040, 0x808080 }, rgb);
    }

    [Fact]
    public void Skips_index_packets_empty_packets_and_empty_scans()
    {
        using var t = new TestE57();
        var path = t.Write("packets.e57",
            new E57Scan(Xyz(), RecordCount: 0),
            new E57Scan(Xyz((1, 2, 3)), OtherPackets: true));
        var (xyz, _) = E57.Read(path);
        Assert.Equal(new[] { 1.0, 2, 3 }, xyz);
    }

    [Fact]
    public void Reports_progress_to_the_end()
    {
        using var t = new TestE57();
        var path = t.Write("progress.e57", new E57Scan(Xyz((1, 2, 3), (4, 5, 6))));
        var seen = new List<double>();
        E57.Read(path, seen.Add);
        Assert.Equal(1.0, seen[^1]);
    }

    [Fact]
    public void Rejects_a_damaged_page()
    {
        using var t = new TestE57();
        var path = t.Write("crc.e57", new E57Scan(Xyz((1, 2, 3))));
        var bytes = File.ReadAllBytes(path);
        bytes[100] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => E57.Read(path)).Message);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_complete()
    {
        using var t = new TestE57();
        var path = t.Write("cut.e57", new E57Scan(Xyz(Enumerable.Range(0, 200).Select(i => (i * 1.0, 0.0, 0.0)).ToArray())));
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^1024]);
        Assert.Contains("not complete", Assert.Throws<InvalidDataException>(() => E57.Read(path)).Message);
    }

    [Fact]
    public void Rejects_a_file_without_signature()
    {
        using var t = new TestE57();
        var path = Path.Combine(t.Folder, "text.e57");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(new string('x', 1024)));
        Assert.Throws<InvalidDataException>(() => E57.Read(path));
    }

    [Fact]
    public void Rejects_a_scan_with_fewer_points_than_its_header()
    {
        using var t = new TestE57();
        var path = t.Write("short.e57", new E57Scan(Xyz((1, 2, 3)), RecordCount: 2));
        Assert.Contains("fewer points", Assert.Throws<InvalidDataException>(() => E57.Read(path)).Message);
    }

    [Theory]
    [InlineData("codec")]
    [InlineData("string")]
    [InlineData("no-xyz")]
    public void Rejects_data_that_it_cannot_read(string kind)
    {
        using var t = new TestE57();
        var scan = kind switch
        {
            "codec" => new E57Scan(Xyz((1, 2, 3)), Codecs: "<vectorChild type=\"Structure\"><zipCodec type=\"Structure\"/></vectorChild>"),
            "string" => new E57Scan(Xyz((1, 2, 3)).Append(new E57Field("name", "<name type=\"String\"/>", 8, new ulong[] { 65 })).ToArray()),
            _ => new E57Scan(new[] { E57Field.Double("intensity", 1) }),
        };
        var path = t.Write(kind + ".e57", scan);
        Assert.Throws<InvalidDataException>(() => E57.Read(path));
    }

    [Fact]
    public void Crc32C_hardware_and_software_give_the_check_value()
    {
        var data = "123456789"u8;
        Assert.Equal(0xE3069283u, Crc32C.Compute(data, hardware: false));
        if (System.Runtime.Intrinsics.X86.Sse42.X64.IsSupported) Assert.Equal(0xE3069283u, Crc32C.Compute(data, hardware: true));
    }

    [Fact]
    public void The_same_cloud_as_PLY_and_as_E57_gives_the_same_index()
    {
        var pts = TestPly.Grid(8, 155000, 463000, 1, 0.3);
        var colours = pts.Select((_, i) => ((byte)i, (byte)(i * 3), (byte)(i * 7))).ToArray();
        using var ply = new TestPly();
        using var e57 = new TestE57();
        var a = ply.Write("same.ply", pts, colours, type: "double");
        var b = e57.Write("same.e57", new E57Scan(Xyz(pts).Concat(new[]
        {
            E57Field.Int("colorRed", 0, 255, colours.Select(c => (long)c.Item1).ToArray()),
            E57Field.Int("colorGreen", 0, 255, colours.Select(c => (long)c.Item2).ToArray()),
            E57Field.Int("colorBlue", 0, 255, colours.Select(c => (long)c.Item3).ToArray()),
        }).ToArray(), Split: 100));
        Index.Build(a);
        Index.Build(b);
        var ia = Index.Load(a);
        var ib = Index.Load(b);
        Assert.Equal(ia.Origin, ib.Origin);
        Assert.Equal(ia.Cells, ib.Cells);
        Assert.Equal(ia.Points, ib.Points);
        Assert.True(File.Exists(b + ".opc"));
        Assert.True(Api.Inspect(b).HasColour);
    }

    [Fact]
    public void Index_rejects_a_cloud_without_valid_points()
    {
        using var t = new TestE57();
        var path = t.Write("none.e57", new E57Scan(Xyz((1, 1, 1)).Append(E57Field.Int("cartesianInvalidState", 0, 2, 2)).ToArray()));
        Assert.Contains("no points", Assert.Throws<InvalidDataException>(() => Index.Build(path)).Message);
    }

    [Fact]
    public void Point_file_rejects_other_extensions()
    {
        Assert.Throws<InvalidDataException>(() => PointFile.Read("cloud.xyz"));
    }
}
