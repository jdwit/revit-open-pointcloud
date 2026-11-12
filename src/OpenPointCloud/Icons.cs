using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace OpenPointCloud;

/// <summary>Ribbon icons. The button uses the Revit "Point Cloud" icon. The add-in icon is the fallback.</summary>
static class Icons
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Screen scale of the Revit window: 1, 1.5 or 2.</summary>
    public static double Scale(UIControlledApplication app)
    {
        double s = 1;
        try { s = GetDpiForWindow(app.MainWindowHandle) / 96.0; } catch { }
        return s >= 1.75 ? 2 : s >= 1.25 ? 1.5 : 1;
    }

    /// <summary>
    /// Loads the icon drawn for this screen scale. Size is in device-independent pixels (16 or 32).
    /// The method sets the DPI so that one image pixel is one screen pixel. This keeps the icon sharp.
    /// </summary>
    public static BitmapSource Load(int size, double scale)
    {
        var asm = typeof(Icons).Assembly;
        using var s = asm.GetManifestResourceStream($"icon{(int)(size * scale)}.png")
            ?? asm.GetManifestResourceStream($"icon{size}.png")!;
        var frame = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var (w, px) = Pixels(frame);
        return Make(w, px, size);
    }

    /// <summary>
    /// Uses the icon of the Revit "Point Cloud" button (Insert tab) for the button.
    /// The add-in reads the icon from Revit at start-up, so the repo has no Autodesk images.
    /// If the add-in cannot find the Revit button, the button keeps the add-in icon.
    /// </summary>
    public static void UseRevitPointCloudIcon(PushButton button)
    {
        try
        {
            var item = FindRibbonItem("Insert", "ID_POINT_CLOUD");
            if (item == null) { Log.Write("Icon: Revit Point Cloud button not found."); return; }
            if (item.LargeImage is ImageSource large) button.LargeImage = large;
            if (item.Image is ImageSource small) button.Image = small;
        }
        catch (Exception e) { Log.Write("Icon: " + e.Message); }
    }

    static dynamic? FindRibbonItem(string tabId, string itemId)
    {
        var cm = Type.GetType("Autodesk.Windows.ComponentManager, AdWindows");
        dynamic? ribbon = cm?.GetProperty("Ribbon")?.GetValue(null);
        if (ribbon == null) return null;
        foreach (dynamic tab in ribbon.Tabs)
        {
            if ((string)tab.Id != tabId) continue;
            foreach (dynamic panel in tab.Panels)
                foreach (dynamic item in Flatten(panel.Source.Items))
                    if ((string?)item.Id == itemId) return item;
        }
        return null;
    }

    static IEnumerable<dynamic> Flatten(System.Collections.IEnumerable items)
    {
        foreach (dynamic item in items)
        {
            yield return item;
            System.Collections.IEnumerable? sub = null;
            try { sub = item.Items; } catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            if (sub != null) foreach (var s in Flatten(sub)) yield return s;
        }
    }

    static (int width, byte[] bgra) Pixels(BitmapSource src)
    {
        var c = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[c.PixelWidth * c.PixelHeight * 4];
        c.CopyPixels(px, c.PixelWidth * 4, 0);
        return (c.PixelWidth, px);
    }

    static BitmapSource Make(int width, byte[] bgra, int size)
    {
        double dpi = 96.0 * width / size;
        var bmp = BitmapSource.Create(width, width, dpi, dpi, PixelFormats.Bgra32, null, bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }
}
