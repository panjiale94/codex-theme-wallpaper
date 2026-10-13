using System.Windows;
using System.Security.Cryptography;
using System.Text;

namespace CodexDreamSkin;

public partial class App : Application
{
    private Mutex? singleInstance;
    private bool ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        string name = "Local\\CodexDreamSkin.SingleInstance";
        string? profile = e.Args.FirstOrDefault(x => x.StartsWith("--profile-dir="))?[14..];
        if (profile is not null) profile = WorkerIdentity.ProfileDirectory(profile);
        if (profile is not null) name += "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)))[..12];
        bool background = e.Args.Contains("--background");
        if (background) { name += ".Worker"; ShutdownMode = ShutdownMode.OnExplicitShutdown; }
        singleInstance = new Mutex(true, name, out bool created);
        ownsMutex = created;
        if (!created)
        {
            if (!background) MessageBox.Show("Codex 动态皮肤已经在运行。", "Codex 动态皮肤",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        if (background) RunWorker(profile);
        else { MainWindow = new MainWindow(); MainWindow.Show(); }
    }

    private async void RunWorker(string? profile)
    {
        try { await new SkinWorker(profile).RunAsync(); }
        catch (Exception ex) { SkinLog.Write("后台退出：" + ex); }
        finally { Dispatcher.Invoke(Shutdown); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsMutex)
            singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
