using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
namespace FlClashUpdater;
public sealed partial class MainWindow : Window {
    readonly string[] args;
    CancellationTokenSource cancellation;
    bool loaded, busy, installing, closeRequested, closed;
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    public MainWindow(string[] args) {
        this.args = args;
        InitializeComponent();
        Title = "";
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, true);
        RootPanel.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(SyncTitleBarColors);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(780 * scale), (int)(650 * scale)));
        AppWindow.Closing += (_, e) => {
            if (!busy) return;
            e.Cancel = true;
            if (installing) { StatusText.Text = "正在安装，请稍候…"; return; }
            closeRequested = true; cancellation?.Cancel();
        };
        Closed += (_, _) => { closed = true; cancellation?.Cancel(); };
        try { ProxyBox.Text = Engine.LoadSettings().Proxy; RefreshInstalled(); }
        catch (Exception e) { ShowError(e); }
    }
    async void OnLoaded(object sender, RoutedEventArgs e) {
        if (loaded) return; loaded = true;
        SyncTitleBarColors();
        if (args.Length == 2 && args[0] == "--preview") {
            try { await Task.Delay(500); await RenderPreview(args[1]); }
            catch (Exception ex) { Engine.Log("预览失败：" + ex); }
            finally { Close(); }
            return;
        }
        if (args.Length == 2 && args[0] == "--verify-ui") {
            try {
                await Task.Delay(3500);
                if (busy || closed || LatestVersion.Text != "—" || !RetryButton.IsEnabled || CancelButton.IsEnabled || (AppWindow.Presenter is OverlappedPresenter p && !p.HasTitleBar) || Title.Length != 0 || AppWindow.TitleBar.IconShowOptions != IconShowOptions.HideIconAndSystemMenu)
                    throw new InvalidOperationException("窗口没有保持手动操作状态。");
                var background = ((Microsoft.UI.Xaml.Media.SolidColorBrush)RootPanel.Background).Color;
                int expected = background.R | (background.G << 8) | (background.B << 16);
                int result = DwmGetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 35, out int actual, sizeof(int));
                if (result == 0 && actual != expected) throw new InvalidOperationException("原生标题栏颜色与界面不一致。");
                File.WriteAllText(Path.GetFullPath(args[1]), "{\"manualStartup\":true,\"windowRemainedOpen\":true,\"checkButtonEnabled\":true,\"nativeTitleBarPreserved\":true,\"titleBarMatchesTheme\":true,\"titleAndIconHidden\":true}");
            } catch (Exception ex) { Engine.Log("界面验证失败：" + ex); }
            finally { Close(); }
        }
    }
    void RefreshInstalled() {
        Installation i = Engine.Detect();
        CurrentVersion.Text = i == null ? "未安装" : i.Version.Split('+')[0];
        ArchitectureText.Text = (i == null ? Engine.NativeArchitecture() : i.Architecture) == "arm64" ? "ARM64" : "x64";
        InstallPathText.Text = i == null ? "未检测到安装版 FlClash" : i.Directory;
    }
    void Append(string text) { LogBox.Text += DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine; Engine.Log(text); }
    void ShowError(Exception e) { StatusText.Text = "更新未完成"; Notice.Severity = InfoBarSeverity.Error; Notice.Title = e.Message; Notice.IsOpen = true; Append(e.Message); }
    void SetBusy(bool value) { busy = value; RetryButton.IsEnabled = !value; CancelButton.IsEnabled = value && !installing; ProxyBox.IsEnabled = !value; }
    void Progress(string text, int percent) {
        DispatcherQueue.TryEnqueue(() => {
            if (closed) return;
            DownloadProgress.IsIndeterminate = false; DownloadProgress.Value = percent;
            StatusText.Text = text.StartsWith("下载中") ? text : text.StartsWith("校验") ? "正在校验安装包…" : text.StartsWith("正在安装") ? "正在安装…" : text;
        });
    }
    async Task RunUpdate() {
        if (busy || closed) return;
        cancellation = new CancellationTokenSource(); SetBusy(true); Notice.IsOpen = false;
        DownloadProgress.IsIndeterminate = true; StatusText.Text = "正在检查更新…";
        try {
            Settings settings = new() { Proxy = ProxyBox.Text.Trim() }; Engine.SaveSettings(settings);
            Append("获取 GitHub 最新正式版本");
            var result = await Task.Run(() => {
                Installation installed = Engine.Detect();
                string arch = installed == null ? Engine.NativeArchitecture() : installed.Architecture;
                Release release = Engine.Fetch(settings, cancellation.Token);
                Asset asset = Engine.SelectAsset(release, arch);
                return (installed, release, asset, check: Engine.Check(installed, release, asset, arch));
            });
            LatestVersion.Text = result.release.tag_name.TrimStart('v', 'V');
            Append("本机 " + result.check.current + "；最新 " + result.check.latest);
            if (result.check.updateAvailable) {
                StatusText.Text = "正在下载安装包…";
                string file = await Task.Run(() => Engine.Download(result.asset, settings, Progress, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                installing = true; CancelButton.IsEnabled = false; DownloadProgress.IsIndeterminate = true;
                StatusText.Text = "正在安装…";
                Notice.Severity = InfoBarSeverity.Informational; Notice.Title = "安装时 FlClash 会短暂关闭"; Notice.IsOpen = true;
                Append("校验通过，启动官方安装器");
                await Task.Run(() => Engine.Install(file, result.installed, result.release, result.asset, Progress));
                StatusText.Text = "更新完成"; Append("安装后版本复查通过");
            } else StatusText.Text = "已是最新版本";
            DownloadProgress.IsIndeterminate = false; DownloadProgress.Value = 100;
            Notice.IsOpen = false; RefreshInstalled();
        } catch (OperationCanceledException) { StatusText.Text = "已取消"; Append("本次操作已取消"); DownloadProgress.IsIndeterminate = false; }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { StatusText.Text = "安装已取消"; Append("管理员授权已取消"); DownloadProgress.IsIndeterminate = false; }
        catch (Exception e) { ShowError(e); DownloadProgress.IsIndeterminate = false; }
        finally { installing = false; SetBusy(false); cancellation.Dispose(); cancellation = null; }
        if (closeRequested && !closed) { Close(); return; }
    }
    async void Retry(object sender, RoutedEventArgs e) => await RunUpdate();
    void Cancel(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    void SyncTitleBarColors() {
        if (closed) return;
        bool dark = RootPanel.ActualTheme == ElementTheme.Dark;
        var background = (RootPanel.Background as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color ?? (dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var secondary = dark ? Windows.UI.Color.FromArgb(255, 166, 166, 166) : Windows.UI.Color.FromArgb(255, 110, 110, 110);
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) {
            var titleBar = AppWindow.TitleBar;
            titleBar.BackgroundColor = titleBar.InactiveBackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = secondary;
            titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonForegroundColor = titleBar.ButtonHoverForegroundColor = titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = secondary;
            titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 48, 48, 48) : Windows.UI.Color.FromArgb(255, 232, 232, 232);
            titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 60, 60, 60) : Windows.UI.Color.FromArgb(255, 220, 220, 220);
        }
        IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int darkMode = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        int caption = background.R | (background.G << 8) | (background.B << 16);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
    }
    void SaveProxy(object sender, RoutedEventArgs e) {
        try { Engine.SaveSettings(new Settings { Proxy = ProxyBox.Text.Trim() }); Append("代理设置已保存"); }
        catch (Exception ex) { ShowError(ex); }
    }
    void OpenLogs(object sender, RoutedEventArgs e) { Directory.CreateDirectory(Engine.Root); Process.Start(new ProcessStartInfo(Engine.Root) { UseShellExecute = true }); }
    async Task RenderPreview(string path) {
        StatusText.Text = "准备就绪"; DownloadProgress.IsIndeterminate = false;
        RetryButton.IsEnabled = true; CancelButton.IsEnabled = false;
        await Task.Delay(150);
        RenderTargetBitmap bitmap = new(); await bitmap.RenderAsync(RootPanel);
        var pixels = await bitmap.GetPixelsAsync();
        StorageFile file = await StorageFile.GetFileFromPathAsync(CreateFile(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
    static string CreateFile(string path) { string full = Path.GetFullPath(path); File.WriteAllBytes(full, Array.Empty<byte>()); return full; }
}
