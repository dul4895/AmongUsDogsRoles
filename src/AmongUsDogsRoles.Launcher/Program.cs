namespace AmongUsDogsRoles.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\AmongUsDogsRolesLauncher", out var first);
        if (!first)
        {
            MessageBox.Show("The AmongUsDogsRoles launcher is already open.", "AmongUsDogsRoles");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}
