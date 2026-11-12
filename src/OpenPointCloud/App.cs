using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace OpenPointCloud;

/// <summary>Registers the engine at startup and adds the "Link point cloud" button.</summary>
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        Register();
        var panel = app.CreateRibbonPanel("Open Point Cloud");
        double scale = Icons.Scale(app);
        var button = (PushButton)panel.AddItem(new PushButtonData("LinkPly", "Link point cloud",
            typeof(App).Assembly.Location, typeof(LinkCommand).FullName)
        {
            ToolTip = "Link a PLY or E57 point cloud. The first link makes an index next to the file.",
            Image = Icons.Load(16, scale),
            LargeImage = Icons.Load(32, scale),
        });
        // The Revit ribbon is complete only after start-up.
        app.ControlledApplication.ApplicationInitialized += (_, _) => Icons.UseRevitPointCloudIcon(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    /// <summary>Registers the point cloud engine. A second call replaces the engine.</summary>
    public static void Register()
    {
        if (PointCloudEngineRegistry.GetSupportedEngines().Contains(CloudEngine.Id))
            PointCloudEngineRegistry.UnregisterPointCloudEngine(CloudEngine.Id);
        PointCloudEngineRegistry.RegisterPointCloudEngine(CloudEngine.Id, new CloudEngine(), true);
    }

    /// <summary>Points an existing cloud instance to another point cloud file. The instance keeps its position.</summary>
    public static ElementId Relink(Document doc, ElementId instance, string path, Action<string, double>? progress = null)
    {
        if (!Index.IsCurrent(path)) Index.Build(path, progress: progress);
        using var t = new Transaction(doc, "Relink point cloud");
        t.Start();
        var type = PointCloudType.Create(doc, CloudEngine.Id, path);
        doc.GetElement(instance).ChangeTypeId(type.Id);
        t.Commit();
        return type.Id;
    }

    /// <summary>Makes the index if necessary, then links the point cloud file in the document. Returns the instance id.</summary>
    public static ElementId Link(Document doc, string path, Action<string, double>? progress = null)
    {
        if (!Index.IsCurrent(path)) Index.Build(path, progress: progress);
        using var t = new Transaction(doc, "Link point cloud");
        t.Start();
        var type = PointCloudType.Create(doc, CloudEngine.Id, path);
        var o = Index.Load(path).Origin;
        var inst = PointCloudInstance.Create(doc, type.Id, Transform.CreateTranslation(new XYZ(o[0], o[1], o[2]) / 0.3048));
        t.Commit();
        return inst.Id;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class LinkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var uidoc = data.Application.ActiveUIDocument;
        if (uidoc == null) return Result.Cancelled;
        using var dlg = new OpenFileDialog { Filter = PointFile.DialogFilter, Title = "Link point cloud" };
        if (dlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;
        var doc = uidoc.Document;
        var path = OfferProjectCopy(doc, dlg.FileName);

        ElementId id;
        bool cached = Index.IsCurrent(path);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var form = new ProgressForm())
        {
            if (!cached) form.Show();
            id = App.Link(doc, path, form.Report);
        }
        clock.Stop();

        // Show the modeller where the cloud is: select it, zoom to it and give a short summary.
        uidoc.Selection.SetElementIds(new List<ElementId> { id });
        uidoc.ShowElements(id);
        ShowStatus(doc, id, path, cached, clock.Elapsed.TotalSeconds);
        return Result.Succeeded;
    }

    // Short status after the link: points, colour, size, position, index and time.
    static void ShowStatus(Document doc, ElementId id, string path, bool cached, double seconds)
    {
        var info = Api.Inspect(path, cached);
        var bb = doc.GetElement(id).get_BoundingBox(null);
        const double m = 0.3048;
        var td = new TaskDialog("Open Point Cloud")
        {
            MainInstruction = "Point cloud linked",
            MainContent =
                $"File: {info.File} ({info.Bytes / 1048576.0:N0} MB)\n" +
                $"Points: {info.Points:N0}{(info.HasColour ? "" : ", no colour (Revit shows them grey)")}\n" +
                $"Size: {info.Size[0]:F1} x {info.Size[1]:F1} x {info.Size[2]:F1} m\n" +
                $"Position: X {bb.Min.X * m:F1} to {bb.Max.X * m:F1} m, Y {bb.Min.Y * m:F1} to {bb.Max.Y * m:F1} m, Z {bb.Min.Z * m:F1} to {bb.Max.Z * m:F1} m\n" +
                (cached ? $"Index: from cache. Link time: {seconds:F1} s" : $"Index: new. Index and link time: {seconds:F1} s"),
        };
        td.Show();
    }

    // A point cloud file in Downloads, on the desktop or in a temp folder breaks the link later.
    // Offer to copy it to the folder of the Revit model.
    static string OfferProjectCopy(Document doc, string path)
    {
        if (string.IsNullOrEmpty(doc.PathName)) return path;
        string full = Path.GetFullPath(path);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] temporary = {
            Path.Combine(home, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.GetTempPath() };
        if (!temporary.Any(t => full.StartsWith(Path.GetFullPath(t), StringComparison.OrdinalIgnoreCase))) return path;

        string dir = Path.GetDirectoryName(doc.PathName)!;
        string target = Path.Combine(dir, Path.GetFileName(path));
        var td = new TaskDialog("Link point cloud")
        {
            MainInstruction = "Copy the point cloud to the project folder?",
            MainContent = $"The file is in a temporary place:\n{full}\n\nCopy to:\n{target}",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes,
        };
        if (td.Show() != TaskDialogResult.Yes) return path;
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(full).Length) File.Copy(full, target, true);
        if (File.Exists(Index.AlignPath(full))) File.Copy(Index.AlignPath(full), Index.AlignPath(target), true);
        return target;
    }
}

sealed class ProgressForm : System.Windows.Forms.Form
{
    readonly System.Windows.Forms.ProgressBar _bar = new() { Dock = DockStyle.Bottom, Maximum = 100 };
    readonly System.Windows.Forms.Label _label = new() { Dock = DockStyle.Fill, Text = "Index point cloud" };

    public ProgressForm()
    {
        Text = "Link point cloud";
        Width = 400;
        Height = 110;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_label);
        Controls.Add(_bar);
    }

    public void Report(string step, double fraction)
    {
        if (!Visible) return;
        _label.Text = "Index point cloud: " + step;
        _bar.Value = (int)(Math.Clamp(fraction, 0, 1) * 100);
        System.Windows.Forms.Application.DoEvents();
    }
}
