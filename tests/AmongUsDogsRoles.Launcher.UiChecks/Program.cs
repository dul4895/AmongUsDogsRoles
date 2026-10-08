using AmongUsDogsRoles.Launcher;
using System.Drawing.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
    }

    private static void Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new LauncherForm();
        // Render without showing a window, touching player data, or making requests.
        _ = form.Handle;
        foreach (var control in Descendants(form)) _ = control.Handle;
        var path = Path.GetFullPath(args.Length == 0 ? "artifacts/launcher-preview.png" : args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (var size in new[] { new Size(740, 470), new Size(680, 490) })
        {
            form.ClientSize = size;
            form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"-{size.Width}.png"), ImageFormat.Png);
            foreach (var button in Descendants(form).OfType<Button>())
            {
                var rectangle = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                if (!form.ClientRectangle.Contains(rectangle)) throw new Exception($"Button clipped: {button.Text}, {rectangle}, client {form.ClientRectangle}");
            }
        }
        Console.WriteLine("PASS: launcher window renders at default and narrow sizes; every button fits.");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
}
