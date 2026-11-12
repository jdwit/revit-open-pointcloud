namespace OpenPointCloud.Tests;

public class PlyTests
{
    [Fact]
    public void Reads_float_points_and_colour()
    {
        using var t = new TestPly();
        var path = t.Write("a.ply", new[] { (1.0, 2.0, 3.0), (4.5, 5.5, 6.5) }, new (byte, byte, byte)[] { (255, 0, 0), (1, 2, 3) });
        var (xyz, rgb) = Ply.Read(path);
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.5, 5.5, 6.5 }, xyz);
        Assert.Equal(new[] { 0xFF0000, 0x010203 }, rgb);
    }

    [Fact]
    public void Reads_double_points_with_full_precision()
    {
        using var t = new TestPly();
        var path = t.Write("d.ply", new[] { (155000.12345, 463000.6789, 1.0001) }, type: "double");
        var (xyz, _) = Ply.Read(path);
        Assert.Equal(155000.12345, xyz[0], 9);
        Assert.Equal(463000.6789, xyz[1], 9);
    }

    [Fact]
    public void Reads_int_points()
    {
        using var t = new TestPly();
        var (xyz, _) = Ply.Read(t.Write("i.ply", new[] { (7.0, -8.0, 9.0) }, type: "int"));
        Assert.Equal(new[] { 7.0, -8.0, 9.0 }, xyz);
    }

    [Fact]
    public void Uses_grey_when_the_file_has_no_colour()
    {
        using var t = new TestPly();
        var (_, rgb) = Ply.Read(t.Write("g.ply", new[] { (0.0, 0.0, 0.0) }));
        Assert.Equal(0x808080, rgb[0]);
    }

    [Fact]
    public void Rejects_ascii_ply()
    {
        using var t = new TestPly();
        var path = t.Write("ascii.ply", new[] { (0.0, 0.0, 0.0) }, format: "ascii");
        var e = Assert.Throws<InvalidDataException>(() => Ply.Read(path));
        Assert.Contains("binary little-endian", e.Message);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_ply()
    {
        using var t = new TestPly();
        var path = Path.Combine(t.Folder, "not.ply");
        File.WriteAllText(path, "hello\nend_header\n");
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    [Fact]
    public void Rejects_a_header_without_end()
    {
        using var t = new TestPly();
        var path = Path.Combine(t.Folder, "open.ply");
        File.WriteAllText(path, "ply\nformat binary_little_endian 1.0\n");
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    static string Raw(TestPly t, string name, string header, byte[] body)
    {
        var path = Path.Combine(t.Folder, name);
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(header).Concat(body).ToArray());
        return path;
    }

    [Fact]
    public void Rejects_list_properties_on_vertices()
    {
        using var t = new TestPly();
        var path = Raw(t, "list.ply", "ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty list uchar int idx\nend_header\n", new byte[8]);
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    [Fact]
    public void Rejects_unknown_types()
    {
        using var t = new TestPly();
        var path = Raw(t, "type.ply", "ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty half x\nend_header\n", new byte[8]);
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    [Fact]
    public void Rejects_coordinates_in_a_small_type()
    {
        using var t = new TestPly();
        var path = Raw(t, "uchar.ply",
            "ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty uchar x\nproperty uchar y\nproperty uchar z\nend_header\n", new byte[3]);
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    [Fact]
    public void Rejects_a_file_without_coordinates()
    {
        using var t = new TestPly();
        var path = Raw(t, "noxyz.ply", "ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty float a\nend_header\n", new byte[4]);
        Assert.Throws<InvalidDataException>(() => Ply.Read(path));
    }

    [Fact]
    public void Reads_vertices_and_skips_a_face_element_after_them()
    {
        using var t = new TestPly();
        var body = new List<byte>();
        foreach (var v in new[] { 1f, 2f, 3f }) body.AddRange(BitConverter.GetBytes(v));
        body.AddRange(new byte[] { 0x00, 0x7F, 0x10, 0x00 }); // extra short and two bytes of normal data
        var path = Raw(t, "mesh.ply",
            "ply\r\nformat binary_little_endian 1.0\r\ncomment made by a test\r\nelement vertex 1\r\nproperty float x\r\nproperty float y\r\nproperty float z\r\n" +
            "property short s\r\nproperty uint8 u\r\nproperty int8 i\r\nelement face 0\r\nproperty list uchar int vertex_indices\r\nend_header\r\n",
            body.ToArray());
        var (xyz, _) = Ply.Read(path);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, xyz);
    }

    [Fact]
    public void Reports_progress_to_one()
    {
        using var t = new TestPly();
        var path = t.Write("p.ply", TestPly.Grid(5));
        double last = 0;
        Ply.Read(path, f => last = f);
        Assert.Equal(1.0, last);
    }
}
