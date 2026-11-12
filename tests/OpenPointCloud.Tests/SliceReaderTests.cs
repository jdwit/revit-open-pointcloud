namespace OpenPointCloud.Tests;

public class SliceReaderTests
{
    // 2 x 2 x 1 cells of 1 m, 1500 points per cell: more than five slices per cell.
    static Index MakeIndex(TestPly t)
    {
        var rnd = new Random(3);
        var pts = new List<(double, double, double)>();
        for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
                for (int i = 0; i < 1500; i++)
                    pts.Add((cx + 0.05 + rnd.NextDouble() * 0.9, cy + 0.05 + rnd.NextDouble() * 0.9, 0.05 + rnd.NextDouble() * 0.9));
        var path = t.Write("cells.ply", pts.ToArray());
        Index.Build(path);
        return Index.Load(path);
    }

    static List<(int, bool, int)> All(Index ix) => ix.Cells.Select((c, i) => (i, false, c.Count)).ToList();

    static List<Pt> ReadAll(SliceReader r, int buffer)
    {
        var all = new List<Pt>();
        var buf = new Pt[buffer];
        int n;
        while ((n = r.Read(buf)) > 0) all.AddRange(buf.Take(n));
        return all;
    }

    [Fact]
    public void Reads_every_point_once()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        var read = ReadAll(new SliceReader(ix, All(ix), (_, _) => { }, _ => true), 1000);
        Assert.Equal(ix.Points.Length, read.Count);
        Assert.Equal(ix.Points.Length, read.Distinct().Count());
    }

    [Fact]
    public void Buffer_size_does_not_change_the_result()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        var big = ReadAll(new SliceReader(ix, All(ix), (_, _) => { }, _ => true), 100000);
        var small = ReadAll(new SliceReader(ix, All(ix), (_, _) => { }, _ => true), 7);
        Assert.Equal(big, small);
    }

    [Fact]
    public void First_round_takes_a_slice_from_every_cell()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        var reader = new SliceReader(ix, All(ix), (_, _) => { }, _ => true);
        var buf = new Pt[SliceReader.Slice * ix.Cells.Length];
        Assert.Equal(buf.Length, reader.Read(buf));
        foreach (var c in ix.Cells)
            Assert.Equal(SliceReader.Slice, buf.Count(p => p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY && p.Y <= c.MaxY));
    }

    [Fact]
    public void Partial_cells_use_the_point_test_and_prepare_each_slice()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        int prepared = 0;
        var todo = ix.Cells.Select((c, i) => (i, true, c.Count)).ToList();
        var read = ReadAll(new SliceReader(ix, todo, (_, _) => prepared++, p => p.Z < 0.5f), 500);
        Assert.All(read, p => Assert.True(p.Z < 0.5f));
        Assert.Equal(ix.Points.Count(p => p.Z < 0.5f), read.Count);
        int slices = ix.Cells.Sum(c => (c.Count + SliceReader.Slice - 1) / SliceReader.Slice);
        Assert.Equal(slices, prepared);
    }

    [Fact]
    public void Take_limits_the_points_per_cell()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        var todo = ix.Cells.Select((c, i) => (i, false, 300)).ToList();
        var read = ReadAll(new SliceReader(ix, todo, (_, _) => { }, _ => true), 1000);
        Assert.Equal(300 * ix.Cells.Length, read.Count);
    }

    [Fact]
    public void Density_sets_take_and_zero_means_all()
    {
        var cell = new Cell(0, 0, 0, 0.3048f, 0.3048f, 1, 0, 5000);
        Assert.Equal(5000, SliceReader.Take(cell, 0));
        // The footprint is one square foot (float rounding can add one point).
        Assert.InRange(SliceReader.Take(cell, 10), 10, 11);
        Assert.Equal(5000, SliceReader.Take(cell, 1e9));
    }

    [Fact]
    public void Empty_list_reads_nothing()
    {
        using var t = new TestPly();
        var ix = MakeIndex(t);
        Assert.Equal(0, new SliceReader(ix, new List<(int, bool, int)>(), (_, _) => { }, _ => true).Read(new Pt[10]));
    }
}
