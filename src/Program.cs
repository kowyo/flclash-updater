using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
namespace FlClashUpdater;
internal static class Program {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint flags);
    [STAThread] static int Main(string[] args) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        try {
            if (args.Length == 1 && args[0] == "--version") { Console.WriteLine(typeof(Program).Assembly.GetName().Version); return 0; }
            // Read-only checks and visual previews do not acquire the install mutex.
            bool preview = args.Length == 2 && args[0] == "--preview";
            bool verifyUi = args.Length == 2 && args[0] == "--verify-ui";
            bool check = args.Length == 2 && args[0] == "--check-only";
            bool download = args.Length == 2 && args[0] == "--download-only";
            bool once = args.Length == 1 && args[0] == "--update-once";
            if (args.Length != 0 && !preview && !verifyUi && !check && !download && !once) throw new ArgumentException("参数：--update-once、--check-only <报告路径>、--download-only <报告路径>。");
            if (check) return RunCommand(args, false, false);
            using var mutex = new Mutex(false, @"Local\FlClashUpdater-" + WindowsIdentity.GetCurrent().User.Value);
            bool inspection = preview || verifyUi;
            bool acquired = inspection;
            if (!inspection) { try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; } }
            if (!acquired) { if (args.Length == 0) MessageBoxW(IntPtr.Zero, "已有更新任务在运行。", "FlClash 更新器", 0x40); return 2; }
            try {
                if (download || once) return RunCommand(args, download, once);
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(init => {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(args);
                });
                return 0;
            } finally { if (!inspection) mutex.ReleaseMutex(); }
        } catch (Exception e) {
            Engine.Log("失败：" + e);
            if (args.Length == 0) MessageBoxW(IntPtr.Zero, e.Message, "FlClash 更新器", 0x10);
            return 1;
        }
    }
    static int RunCommand(string[] args, bool download, bool once) {
        Installation installed = Engine.Detect();
        if (once && installed == null) { Engine.Log("未找到已有安装，本次更新跳过。"); return 0; }
        string arch = installed == null ? Engine.NativeArchitecture() : installed.Architecture;
        Settings settings = Engine.LoadSettings();
        Release release = Engine.Fetch(settings, CancellationToken.None);
        Asset asset = Engine.SelectAsset(release, arch);
        CheckResult result = Engine.Check(installed, release, asset, arch);
        Engine.Log("本机 " + result.current + "；最新 " + result.latest);
        if (download || (once && result.updateAvailable)) {
            result.download = Engine.Download(asset, settings, (s, p) => { if (p % 10 == 0) Engine.Log(s); }, CancellationToken.None);
            if (once) Engine.Install(result.download, installed, release, asset, (s, p) => Engine.Log(s));
        }
        if (!once) File.WriteAllText(Path.GetFullPath(args[1]), Engine.Json.Serialize(result));
        return 0;
    }
}
