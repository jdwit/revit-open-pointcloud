namespace OpenPointCloud.Tests;

public class ApiTests
{
    [Fact]
    public void Inspect_gives_points_size_and_position_in_file_coordinates()
    {
        using var t = new TestPly();
        var pts = new[] { (155000.0, 463000.0, 1.0), (155024.0, 463027.0, 31.0), (155010.0, 463010.0, 5.0) };
        var path = t.Write("info.ply", pts, type: "double");
        var info = Api.Inspect(path);
        Assert.Equal("info.ply", info.File);
        Assert.Equal(3, info.Points);
        Assert.False(info.IndexFromCache);
        Assert.Equal(new[] { 155000.0, 463000.0, 1.0 }, info.Min);
        Assert.Equal(new[] { 155024.0, 463027.0, 31.0 }, info.Max);
        Assert.Equal(new[] { 24.0, 27.0, 30.0 }, info.Size);
        Assert.True(Api.Inspect(path).IndexFromCache);
    }

    [Fact]
    public void Inspect_detects_colour()
    {
        using var t = new TestPly();
        var pts = TestPly.Grid(4);
        var colours = pts.Select((_, i) => ((byte)(i % 255), (byte)0, (byte)0)).ToArray();
        Assert.True(Api.Inspect(t.Write("rgb.ply", pts, colours)).HasColour);
        Assert.False(Api.Inspect(t.Write("grey.ply", pts)).HasColour);
    }

    [Fact]
    public void Link_result_is_json_with_camel_case_names()
    {
        var info = new CloudInfo("a.ply", 10, 2, true, new[] { 0.0, 0, 0 }, new[] { 1.0, 1, 1 }, new[] { 1.0, 1, 1 }, false);
        var json = new LinkResult(42, info, 0.5).ToJson();
        Assert.Contains("\"elementId\":42", json);
        Assert.Contains("\"hasColour\":true", json);
        Assert.Contains("\"seconds\":0.5", json);
    }

    [Fact]
    public void Inspect_of_a_missing_file_fails()
    {
        Assert.ThrowsAny<IOException>(() => Api.Inspect(Path.Combine(Path.GetTempPath(), "no-such-file.ply")));
    }
}
