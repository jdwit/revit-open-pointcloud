namespace OpenPointCloud.Tests;

public class IndexTests
{
    [Fact]
    public void Build_and_load_keep_all_points()
    {
        using var t = new TestPly();
        var pts = TestPly.Grid(12, step: 0.25);
        var path = t.Write("grid.ply", pts);
        Index.Build(path);
        var ix = Index.Load(path);
        Assert.Equal(pts.Length, ix.Points.Length);
        Assert.Equal(pts.Length, ix.Cells.Sum(c => c.Count));
    }

    [Fact]
    public void Cells_are_contiguous_and_contain_their_points()
    {
        using var t = new TestPly();
        var path = t.Write("cells.ply", TestPly.Grid(10, step: 0.3));
        Index.Build(path);
        var ix = Index.Load(path);
        int next = 0;
        foreach (var c in ix.Cells)
        {
            Assert.Equal(next, c.Start);
            next += c.Count;
            for (int i = c.Start; i < c.Start + c.Count; i++)
            {
                var p = ix.Points[i];
                Assert.InRange(p.X, c.MinX, c.MaxX);
                Assert.InRange(p.Y, c.MinY, c.MaxY);
                Assert.InRange(p.Z, c.MinZ, c.MaxZ);
            }
        }
        Assert.True(ix.Cells.Length > 1);
    }

    [Fact]
    public void Large_coordinates_keep_sub_millimetre_precision()
    {
        using var t = new TestPly();
        var pts = new[] { (155000.1234, 463000.5678, 2.0001), (155010.9876, 463005.4321, 3.5) };
        var path = t.Write("rd.ply", pts, type: "double");
        Index.Build(path);
        var ix = Index.Load(path);
        Assert.Equal(new[] { 155000.0, 463000.0, 0.0 }, ix.Origin);
        var found = ix.Points.Select(p => (ix.Origin[0] + p.X, ix.Origin[1] + p.Y, ix.Origin[2] + p.Z)).ToList();
        foreach (var (x, y, z) in pts)
            Assert.Contains(found, f => Math.Abs(f.Item1 - x) < 1e-4 && Math.Abs(f.Item2 - y) < 1e-4 && Math.Abs(f.Item3 - z) < 1e-4);
    }

    [Fact]
    public void Cache_is_current_until_the_ply_changes()
    {
        using var t = new TestPly();
        var path = t.Write("c.ply", TestPly.Grid(3));
        Assert.False(Index.IsCurrent(path));
        Index.Build(path);
        Assert.True(Index.IsCurrent(path));
        t.Write("c.ply", TestPly.Grid(4));
        Assert.False(Index.IsCurrent(path));
    }

    [Fact]
    public void Align_file_moves_the_points_and_makes_the_cache_old()
    {
        using var t = new TestPly();
        var path = t.Write("al.ply", new[] { (1.0, 1.0, 1.0) });
        Index.Build(path);
        File.WriteAllText(Index.AlignPath(path), "10 -20 0.5");
        Assert.False(Index.IsCurrent(path));
        Index.Build(path);
        var ix = Index.Load(path);
        var p = ix.Points[0];
        Assert.Equal(11.0, ix.Origin[0] + p.X, 4);
        Assert.Equal(-19.0, ix.Origin[1] + p.Y, 4);
        Assert.Equal(1.5, ix.Origin[2] + p.Z, 4);
    }

    [Fact]
    public void Bad_align_file_gives_a_clear_error()
    {
        using var t = new TestPly();
        var path = t.Write("bad.ply", new[] { (0.0, 0.0, 0.0) });
        File.WriteAllText(Index.AlignPath(path), "1 2");
        var e = Assert.Throws<InvalidDataException>(() => Index.Build(path));
        Assert.Contains("dx dy dz", e.Message);
    }

    [Fact]
    public void Points_in_a_cell_are_shuffled_so_a_prefix_is_an_even_sample()
    {
        using var t = new TestPly();
        // 1000 points on a line inside one cell (cell size 1 m).
        var pts = Enumerable.Range(0, 1000).Select(i => (i * 0.0009, 0.0, 0.0)).ToArray();
        var path = t.Write("line.ply", pts);
        Index.Build(path);
        var ix = Index.Load(path);
        Assert.Single(ix.Cells);
        var first = ix.Points.Take(100).Select(p => p.X).ToArray();
        // An even sample covers most of the line. A sorted prefix covers only the first tenth.
        Assert.True(first.Max() - first.Min() > 0.6, "The first 100 points cover only a small part of the cell.");
    }
}
