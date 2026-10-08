using System.Diagnostics;
using System.Text.Json;

namespace AmongUsDogsRoles.Launcher;

internal sealed class LauncherForm : Form
{
    private Installation? installation;
    private readonly string selectionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmongUsDogsRoles", "selected-copy.txt");
    private readonly ReleaseClient releases = new();
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill, ReadOnly = true, PlaceholderText = "Browse to your existing copied mod folder" };
    private readonly Label installedLabel = new() { AutoSize = true, Text = "Choose your mod folder to begin." };
    private readonly Label latestLabel = new() { AutoSize = true, Text = "Latest release: checking…" };
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "Ready.", AutoEllipsis = true, MinimumSize = new Size(0, 60) };
    private readonly Button browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Button play = new() { Text = "Update && Play", AutoSize = true, Height = 44, BackColor = Color.FromArgb(211, 238, 219) };
    private readonly Button offline = new() { Text = "Play installed", AutoSize = true };
    private readonly Button check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button local = new() { Text = "Install test package…", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private CancellationTokenSource? operation;
    private InstalledRelease? installed;

    public LauncherForm()
    {
        Text = "AmongUsDogsRoles Launcher";
        ClientSize = new Size(740, 470);
        MinimumSize = new Size(680, 490);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(246, 248, 250);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Among Us, with the dogs", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { Text = "Choose your existing mod copy once, then use Update & Play.", AutoSize = true, UseMnemonic = false });
        layout.Controls.Add(new Label { Text = "Your copied mod folder (separate from Steam)", AutoSize = true, Margin = new Padding(0, 18, 0, 6) });
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(gamePath, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        layout.Controls.Add(pathRow);
        layout.Controls.Add(installedLabel);
        layout.Controls.Add(latestLabel);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 16, 0, 10) };
        buttons.Controls.AddRange([play, offline, check, local, cancel]);
        layout.Controls.Add(buttons);
        layout.Controls.Add(new Label { Text = "Launch through Steam for vanilla; use this launcher for your modded copy.\nKeep Steam running. Everyone needs matching mod and game versions.", AutoSize = true });
        var folderLink = new LinkLabel { Text = "Open mod folder", AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        folderLink.LinkClicked += (_, _) =>
        {
            if (installation is not null && Directory.Exists(installation.Root))
                Process.Start(new ProcessStartInfo(installation.Root) { UseShellExecute = true });
        };
        layout.Controls.Add(folderLink);
        layout.Controls.Add(status);
        Controls.Add(layout);

        browse.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Choose your existing copied mod folder, outside Steam's library", UseDescriptionForTitle = true };
            if (Directory.Exists(gamePath.Text)) dialog.InitialDirectory = gamePath.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await RunAsync(_ => { SelectCopy(dialog.SelectedPath); return Task.CompletedTask; });
        };
        check.Click += async (_, _) => await RunAsync(CheckAsync);
        play.Click += async (_, _) => await RunAsync(UpdateAndPlayAsync);
        offline.Click += async (_, _) => await RunAsync(_ =>
        {
            SelectedCopy().Launch();
            status.Text = "Game started. Keep Steam running.";
            return Task.CompletedTask;
        });
        local.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Choose launcher-release.json beside the test ZIP", Filter = "Launcher release manifest|launcher-release.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await RunAsync(token => InstallLocalAsync(dialog.FileName, token));
        };
        cancel.Click += (_, _) => { status.Text = "Cancelling…"; operation?.Cancel(); };
        Shown += async (_, _) =>
        {
            await RunAsync(async token =>
            {
                if (File.Exists(selectionPath)) SelectCopy(File.ReadAllText(selectionPath));
                await CheckAsync(token);
            });
        };
        FormClosing += (_, e) =>
        {
            if (operation is not null)
            {
                e.Cancel = true;
                operation.Cancel();
                status.Text = "Cancelling safely… Close the launcher once this finishes.";
            }
        };
        FormClosed += (_, _) => releases.Dispose();
    }

    private Installation SelectedCopy() => installation ?? throw new InvalidOperationException("Choose your copied mod folder using Browse first.");

    private void SelectCopy(string folder)
    {
        var selected = new Installation(folder);
        var state = selected.ReadInstalled();
        selected.ValidateExistingCopy();
        Directory.CreateDirectory(Path.GetDirectoryName(selectionPath)!);
        File.WriteAllText(selectionPath, selected.Root);
        installation = selected;
        installed = state;
        gamePath.Text = selected.Root;
        RefreshInstalled();
        status.Text = "Copied mod folder selected. Use Play installed to launch it, or Update & Play to update it.";
    }

    private void RefreshInstalled()
    {
        installed = installation?.ReadInstalled();
        installedLabel.Text = installed is null ? "Existing copy selected · version checked when updating" : $"Installed: mod {installed.Release.Version} · Among Us {installed.Release.GameVersion}";
    }

    private async Task CheckAsync(CancellationToken token)
    {
        status.Text = "Checking the latest stable GitHub release…";
        var latest = await releases.LatestAsync(token);
        latestLabel.Text = $"Latest stable: mod {latest.Manifest.Version} · Steam {latest.Manifest.GameVersion}";
        status.Text = installed?.Release.Sha256.Equals(latest.Manifest.Sha256, StringComparison.OrdinalIgnoreCase) == true
            ? "You are up to date."
            : "Press Update & Play to install the latest stable release.";
    }

    private async Task UpdateAndPlayAsync(CancellationToken token)
    {
        var selected = SelectedCopy();
        Installation.EnsureGameClosed();
        status.Text = "Checking for updates…";
        var latest = await releases.LatestAsync(token);
        latestLabel.Text = $"Latest stable: mod {latest.Manifest.Version} · Steam {latest.Manifest.GameVersion}";
        var progress = new Progress<string>(message => status.Text = message);
        if (installed is null || !installed.Release.Sha256.Equals(latest.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            installed = await selected.InstallAsync(latest.Manifest,
                (path, ct) => releases.DownloadAsync(latest, path, progress, ct), progress, token);
        token.ThrowIfCancellationRequested();
        RefreshInstalled();
        selected.Launch();
        status.Text = "Game started. Keep Steam running.";
    }

    private async Task InstallLocalAsync(string manifestPath, CancellationToken token)
    {
        var selected = SelectedCopy();
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(manifestPath, token), ReleaseManifest.Json)
            ?? throw new InvalidDataException("Empty manifest.");
        manifest.Validate();
        var zip = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.AssetName);
        var progress = new Progress<string>(message => status.Text = message);
        installed = await selected.InstallAsync(manifest, async (target, ct) =>
        {
            await using var input = File.OpenRead(zip);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, ct);
        }, progress, token);
        RefreshInstalled();
        status.Text = $"Test package {manifest.Version} installed. Use Play installed to try it.";
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (operation is not null) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        SetBusy(true);
        try { await action(cancellation.Token); }
        catch (OperationCanceledException)
        {
            status.Text = cancellation.IsCancellationRequested
                ? "Cancelled. You can use Play installed to launch your current copy."
                : "The request timed out. Try again, or play your installed version.";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            latestLabel.Text = "Latest stable: check not completed";
            MessageBox.Show(this, ex.Message, "AmongUsDogsRoles", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { operation = null; SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in new Control[] { gamePath, browse, play, check, local }) control.Enabled = !busy;
        offline.Enabled = !busy && installation is not null;
        play.Enabled = !busy && installation is not null;
        local.Enabled = !busy && installation is not null;
        cancel.Enabled = busy;
        UseWaitCursor = busy;
    }
}
