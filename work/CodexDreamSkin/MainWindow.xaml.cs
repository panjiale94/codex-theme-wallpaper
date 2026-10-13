using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexDreamSkin;

public partial class MainWindow : Window
{
    private readonly WorkerClient controller;
    private readonly ThemeSettingsStore settingsStore;
    private readonly ThemeSettings settings;
    private readonly List<ThemePreset> presets;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool sourceReady;
    private bool initializing = true;
    private bool busy;
    private bool closing;
    private bool mayClose;

    public MainWindow()
    {
        string? profileOverride = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--profile-dir="))?[14..];
        controller = new WorkerClient(profileOverride);
        settingsStore = new ThemeSettingsStore(profileOverride);
        settings = settingsStore.Load();
        InitializeComponent();
        presets = ThemeCatalog.Create(); ThemeList.ItemsSource = presets;
        OpacitySlider.Value = settings.Panel; MotionSlider.Value = settings.Motion;
        AutoStartCheckBox.IsChecked = settings.AutoStart;
        var preset = presets.FirstOrDefault(x => x.Id == settings.PresetId) ?? presets[0];
        if (settings.PresetId == "custom")
        {
            SelectedPreview.Source = presets[0].Thumbnail;
            MediaName.Text = Path.GetFileName(settings.CustomMediaPath ?? "自定义素材");
            MediaDescription.Text = "上次导入的本地素材";
        }
        else { ThemeList.SelectedItem = preset; PreparePreset(preset); }
        initializing = false; UpdateState();
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            if (settings.Enabled && !busy) await ApplySkinAsync(false);
        };
        Loaded += Window_Loaded;
        Closing += Window_Closing;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (settings.PresetId == "custom" && settings.CustomMediaPath is not null)
        {
            try { await PrepareCustomAsync(settings.CustomMediaPath); }
            catch (Exception ex) { StatusText.Text = "保存的素材无法读取，但仍可点击关闭效果。" + ex.Message; return; }
        }
        if (settings.Enabled) await ApplySkinAsync(false);
    }

    private void PreparePreset(ThemePreset preset)
    {
        settings.PresetId = preset.Id; settings.CustomMediaPath = null; sourceReady = true;
        SelectedPreview.Source = preset.Thumbnail; MediaName.Text = preset.Name; MediaDescription.Text = preset.Description;
    }

    private async void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || ThemeList.SelectedItem is not ThemePreset preset) return;
        PreparePreset(preset);
        if (preset.Id.StartsWith("center-")) MotionSlider.Value = 100;
        SaveSettings();
        if (settings.Enabled) await ApplySkinAsync(false);
    }

    private Task PrepareCustomAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("文件已移动或删除。", path);
        if (file.Length > 100 * 1024 * 1024) throw new InvalidOperationException("请选择小于 100 MB 的背景素材。");
        string mime = file.Extension.ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4", ".webm" => "video/webm", ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "image/png"
        };
        bool isVideo = mime.StartsWith("video/");
        settings.PresetId = "custom"; settings.CustomMediaPath = file.FullName; sourceReady = true;
        ThemeList.SelectedItem = null;
        MediaName.Text = file.Name; MediaDescription.Text = isVideo ? "本地视频 · 静音循环 · 原生动态" : "本地图片 / 动图";
        if (!isVideo)
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 420; bitmap.UriSource = new Uri(file.FullName); bitmap.EndInit(); bitmap.Freeze();
            SelectedPreview.Source = bitmap;
        }
        return Task.CompletedTask;
    }

    private async void ChooseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Codex 内部背景",
            Filter = "图片、动图、视频|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.mp4;*.webm", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await ImportMediaAsync(dialog.FileName);
    }

    private async void ImportDesktop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "")?.ToString() ?? "";
            string configPath = Path.Combine(steam, "steamapps", "common", "wallpaper_engine", "config.json");
            if (!File.Exists(configPath)) throw new InvalidOperationException("没有检测到 Wallpaper Engine，请直接导入本地视频。");
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            string? wallpaperPath = null;
            foreach (var user in config.RootElement.EnumerateObject())
            {
                if (user.Value.ValueKind != JsonValueKind.Object || !user.Value.TryGetProperty("general", out var general) ||
                    !general.TryGetProperty("wallpaperconfig", out var wallpaper) || !wallpaper.TryGetProperty("selectedwallpapers", out var monitors)) continue;
                foreach (var monitor in monitors.EnumerateObject())
                    if (monitor.Value.TryGetProperty("file", out var file)) { wallpaperPath = file.GetString(); break; }
                if (wallpaperPath is not null) break;
            }
            if (wallpaperPath is null || Path.GetExtension(wallpaperPath).ToLowerInvariant() is not (".mp4" or ".webm"))
                throw new InvalidOperationException("当前桌面使用场景型壁纸，不能直接作为视频导入。请选择本地 MP4 / WebM，或使用主题库中的不知火动效。");
            await ImportMediaAsync(wallpaperPath);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task ImportMediaAsync(string path)
    {
        try { await PrepareCustomAsync(path); SaveSettings(); if (settings.Enabled) await ApplySkinAsync(false); }
        catch (Exception ex) { StatusText.Text = "素材加载失败：" + ex.Message; }
    }

    private void SettingsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is null || MotionValue is null || OpacitySlider is null || MotionSlider is null) return;
        OpacityValue.Text = $"{OpacitySlider.Value:F0}%"; MotionValue.Text = $"{MotionSlider.Value:F0}%";
        if (initializing) return;
        SaveSettings(); debounce.Stop(); debounce.Start();
    }

    private void SaveSettings()
    {
        settings.Panel = OpacitySlider.Value; settings.Motion = MotionSlider.Value;
        settings.AutoStart = AutoStartCheckBox.IsChecked == true;
        settingsStore.Save(settings); UpdateState();
    }

    private void UpdateState() => StateText.Text = settings.Enabled ? "已启用 · 退出工具继续" : "效果已关闭";


    private async void Start_Click(object sender, RoutedEventArgs e) => await ApplySkinAsync(true);

    private void BoostMotion_Click(object sender, RoutedEventArgs e) => MotionSlider.Value = 100;

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (settings.Enabled) await ApplySkinAsync(false);
    }

    private void SetBusy(bool value)
    {
        busy = value; StartButton.IsEnabled = !value; StopButton.IsEnabled = !value;
        ThemeList.IsEnabled = !value; OpacitySlider.IsEnabled = !value; MotionSlider.IsEnabled = !value;
        AutoStartCheckBox.IsEnabled = !value;
    }

    private async Task ApplySkinAsync(bool allowLaunch)
    {
        if (busy || closing || !sourceReady) return;
        SetBusy(true);
        try
        {
            int windows = await controller.ApplyAsync(allowLaunch);
            if (!settings.Enabled) { settings.Enabled = true; SaveSettings(); }
            UpdateState();
            StatusText.Text = windows > 0 ? $"已应用“{MediaName.Text}”（{windows} 个窗口）。轻量后台维持效果，可以关闭设置窗口。" :
                "自动恢复已就绪。请从“Codex（动态皮肤）”快捷方式打开 Codex；设置窗口可以关闭。";
        }
        catch (Exception ex) { settings.Enabled = settingsStore.Load().Enabled; UpdateState(); StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true); debounce.Stop();
        try
        {
            await controller.RestoreAsync(); settings.Enabled = false; SaveSettings();
            StatusText.Text = "效果已关闭，Codex 已恢复原来的界面主题。以后打开工具也不会自动启用。";
        }
        catch (Exception ex) { StatusText.Text = "关闭失败：" + ex.Message; }
        finally { SetBusy(false); }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (mayClose) return;
        if (closing) return;
        closing = true; debounce.Stop();
        try
        {
            // 前台没有 CDP 连接；无需等待后台操作，可以立即关闭窗口。
            if (!busy) SaveSettings();
        }
        catch (Exception) { /* 页面皮肤由 Codex 保留；关闭管理器不撤销它。 */ }
        finally { mayClose = true; }
    }
}
