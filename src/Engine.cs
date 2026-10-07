using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace FlClashUpdater;
public sealed class JsonCodec {
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}

    public class Asset { public string name; public long size; public string digest; public string browser_download_url; }
    public class Release { public string tag_name; public bool draft; public bool prerelease; public Asset[] assets; }
    public class Installation { public string Directory; public string Version; public string Architecture; public string Exe { get { return Path.Combine(Directory, "FlClash.exe"); } } }
    public class Settings { public string Proxy = ""; }
    public class CheckResult { public string current; public string latest; public string architecture; public string directory; public bool updateAvailable; public string asset; public string download; }

    public static class Engine {
        public const string ReleasesUrl = "https://github.com/chen08209/FlClash/releases";
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlClashUpdater");
        public static readonly JsonCodec Json = new JsonCodec();
        static readonly object LogLock = new object();
        public static void Log(string s) {
            lock (LogLock) {
                System.IO.Directory.CreateDirectory(Root);
                string file = Path.Combine(Root, "updater.log");
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024) File.Move(file, Path.Combine(Root, "updater-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".log"));
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine, Encoding.UTF8);
            }
        }
        public static Settings LoadSettings() {
            string file = Path.Combine(Root, "settings.json");
            return File.Exists(file) ? Json.Deserialize<Settings>(File.ReadAllText(file, Encoding.UTF8)) : new Settings();
        }
        public static void SaveSettings(Settings s) {
            ValidateProxy(s.Proxy);
            System.IO.Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "settings.json"), Json.Serialize(s), Encoding.UTF8);
        }
        static void ValidateProxy(string proxy) {
            if (String.IsNullOrWhiteSpace(proxy)) return;
            Uri uri;
            if (!Uri.TryCreate(proxy.Trim(), UriKind.Absolute, out uri) || uri.Scheme != "http" || String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("代理请填写 HTTP 地址，例如 http://127.0.0.1:7890；留空使用 Windows 系统代理。");
        }
        public static Version ParseVersion(string s) {
            Match m = Regex.Match(s ?? "", @"^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?(?:\+[^\s]+)?$", RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("无法识别版本号：" + s);
            return new Version(Int32.Parse(m.Groups[1].Value), Int32.Parse(m.Groups[2].Value), Int32.Parse(m.Groups[3].Value), m.Groups[4].Success ? Int32.Parse(m.Groups[4].Value) : 0);
        }
        public static string ReadArchitecture(string exe) {
            using (BinaryReader r = new BinaryReader(File.OpenRead(exe))) {
                if (r.ReadUInt16() != 0x5A4D) throw new InvalidOperationException("FlClash.exe 不是有效的 Windows 程序。");
                r.BaseStream.Position = 0x3c;
                int offset = r.ReadInt32();
                if (offset < 64 || offset > r.BaseStream.Length - 6) throw new InvalidOperationException("FlClash.exe 的 PE 文件头损坏。");
                r.BaseStream.Position = offset;
                if (r.ReadUInt32() != 0x4550) throw new InvalidOperationException("FlClash.exe 的 PE 文件头无效。");
                ushort machine = r.ReadUInt16();
                if (machine == 0x8664) return "amd64";
                if (machine == 0xAA64) return "arm64";
                throw new InvalidOperationException("当前 FlClash 架构不受支持；仅支持 x64 / ARM64。");
            }
        }
        [StructLayout(LayoutKind.Sequential)] struct SystemInfo { public ushort arch; public ushort reserved; public uint page; public IntPtr min, max, mask; public uint count, type, gran; public ushort level, revision; }
        [DllImport("kernel32.dll")] static extern void GetNativeSystemInfo(out SystemInfo info);
        public static string NativeArchitecture() {
            SystemInfo info; GetNativeSystemInfo(out info);
            if (info.arch == 9) return "amd64";
            if (info.arch == 12) return "arm64";
            throw new InvalidOperationException("本程序仅支持 Windows x64 / ARM64。");
        }
        public static Installation Detect() {
            List<Installation> found = new List<Installation>();
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) {
                    if (uninstall == null) continue;
                    foreach (string key in uninstall.GetSubKeyNames()) {
                        using (RegistryKey item = uninstall.OpenSubKey(key)) {
                            string name = Convert.ToString(item.GetValue("DisplayName", ""));
                            if (!Regex.IsMatch(name, @"^FlClash(?:\s|$)", RegexOptions.IgnoreCase)) continue;
                            string dir = Convert.ToString(item.GetValue("InstallLocation", "")).Trim().Trim('"').TrimEnd('\\');
                            if (String.IsNullOrEmpty(dir)) {
                                Match m = Regex.Match(Convert.ToString(item.GetValue("UninstallString", "")), "^\"([^\"]+)\"");
                                if (m.Success) dir = Path.GetDirectoryName(m.Groups[1].Value);
                            }
                            if (String.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "FlClash.exe"))) continue;
                            dir = Path.GetFullPath(dir);
                            if (found.Any(x => String.Equals(x.Directory, dir, StringComparison.OrdinalIgnoreCase))) continue;
                            string version = FileVersionInfo.GetVersionInfo(Path.Combine(dir, "FlClash.exe")).ProductVersion;
                            if (String.IsNullOrEmpty(version)) version = Convert.ToString(item.GetValue("DisplayVersion", ""));
                            ParseVersion(version);
                            found.Add(new Installation { Directory = dir, Version = version, Architecture = ReadArchitecture(Path.Combine(dir, "FlClash.exe")) });
                        }
                    }
                }
            }
            if (found.Count > 1) throw new InvalidOperationException("发现多个 FlClash 安装目录，请保留一个安装版本后再更新，避免覆盖错误目录。");
            return found.SingleOrDefault();
        }
        static HttpWebRequest Request(string url, Settings settings, int timeout) {
            ValidateProxy(settings.Proxy);
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "FlClashUpdater/1.0";
            r.Timeout = timeout; r.ReadWriteTimeout = timeout;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!String.IsNullOrWhiteSpace(settings.Proxy)) r.Proxy = new WebProxy(settings.Proxy.Trim());
            return r;
        }
        static Exception NetworkError(WebException ex) {
            HttpWebResponse r = ex.Response as HttpWebResponse;
            if (r != null && ((int)r.StatusCode == 403 || (int)r.StatusCode == 429))
                return new InvalidOperationException("GitHub 拒绝请求或 API 频率受限，请稍后重试。", ex);
            return new InvalidOperationException("无法连接 GitHub，请检查网络或在代理栏填写 HTTP 代理地址。" + ex.Message, ex);
        }
        public static Release Fetch(Settings settings, CancellationToken token) {
            for (int attempt = 0; ; attempt++) {
                token.ThrowIfCancellationRequested();
                HttpWebRequest request = Request("https://api.github.com/repos/chen08209/FlClash/releases/latest", settings, 30000);
                request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                try {
                    using (token.Register(request.Abort))
                    using (WebResponse response = request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                        Release release = Json.Deserialize<Release>(reader.ReadToEnd());
                        if (release == null || release.draft || release.prerelease) throw new InvalidOperationException("GitHub 未返回可用的正式版本。");
                        ParseVersion(release.tag_name);
                        return release;
                    }
                } catch (WebException ex) {
                    token.ThrowIfCancellationRequested();
                    if (attempt >= 2) throw NetworkError(ex);
                    if (token.WaitHandle.WaitOne((attempt + 1) * 1500)) token.ThrowIfCancellationRequested();
                }
            }
        }
        public static Asset SelectAsset(Release release, string architecture) {
            string version = release.tag_name.TrimStart('v', 'V');
            string expected = "FlClash-" + version + "-windows-" + architecture + "-setup.exe";
            Asset[] matches = (release.assets ?? new Asset[0]).Where(a => a.name == expected).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("此版本没有唯一匹配的官方 Windows 安装包：" + expected);
            Asset asset = matches[0];
            if (asset.browser_download_url != "https://github.com/chen08209/FlClash/releases/download/" + release.tag_name + "/" + expected)
                throw new InvalidOperationException("安装包下载地址不属于指定的官方 Release。");
            if (asset.size <= 0 || asset.size > 1024L * 1024 * 1024) throw new InvalidOperationException("安装包大小无效。");
            if (!Regex.IsMatch(asset.digest ?? "", @"^sha256:[a-fA-F0-9]{64}$")) throw new InvalidOperationException("该安装包没有 GitHub SHA-256 摘要，已停止自动安装。");
            return asset;
        }
        public static CheckResult Check(Installation installed, Release release, Asset asset, string arch) {
            return new CheckResult { current = installed == null ? "未安装" : installed.Version, latest = release.tag_name,
                architecture = arch, directory = installed == null ? "" : installed.Directory,
                updateAvailable = installed == null || ParseVersion(release.tag_name) > ParseVersion(installed.Version), asset = asset.name };
        }
        public static string Hash(string path) {
            using (SHA256 sha = SHA256.Create()) using (FileStream s = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
        public static void Verify(string path, Asset asset) {
            if (new FileInfo(path).Length != asset.size) throw new InvalidOperationException("下载文件大小不匹配，已停止安装。");
            if (!String.Equals(Hash(path), asset.digest.Substring(7), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SHA-256 校验失败，已停止安装。");
        }
        public static string Download(Asset asset, Settings settings, Action<string, int> progress, CancellationToken token) {
            string dir = Path.Combine(Root, "downloads"); System.IO.Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, asset.name), partial = file + ".part";
            if (File.Exists(file)) {
                try { Verify(file, asset); progress("使用已校验的缓存安装包。", 100); return file; }
                catch (InvalidOperationException) { File.Delete(file); }
            }
            for (int attempt = 0; ; attempt++) {
                token.ThrowIfCancellationRequested();
                HttpWebRequest request = Request(asset.browser_download_url, settings, 60000);
                try {
                    using (token.Register(request.Abort))
                    using (WebResponse response = request.GetResponse())
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        byte[] buffer = new byte[131072]; long total = 0; int count; int last = -1;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0) {
                            token.ThrowIfCancellationRequested(); total += count;
                            if (total > asset.size) throw new InvalidOperationException("下载文件超过声明大小，已停止。");
                            output.Write(buffer, 0, count);
                            int percent = (int)(total * 100 / asset.size);
                            if (percent != last) { last = percent; progress("下载中 " + percent + "%（" + (total / 1048576.0).ToString("F1") + " MB）", percent); }
                        }
                    }
                    progress("校验 SHA-256…", 100); Verify(partial, asset); File.Move(partial, file); return file;
                } catch (WebException ex) {
                    token.ThrowIfCancellationRequested();
                    if (attempt >= 2) throw NetworkError(ex);
                    progress("下载连接中断，正在重试…", 0);
                    if (token.WaitHandle.WaitOne((attempt + 1) * 2000)) token.ThrowIfCancellationRequested();
                } finally { if (File.Exists(partial)) File.Delete(partial); }
            }
        }
        public static string Quote(string s) {
            if (s == null || s.IndexOf('"') >= 0 || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0) throw new ArgumentException("无效的命令行参数。");
            return "\"" + s.TrimEnd('\\') + "\"";
        }
        public static bool IsAdmin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        static bool WasRunning(Installation installed) {
            if (installed == null) return false;
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("FlClash")) {
                using (p) {
                    try { if (p.SessionId == session && String.Equals(p.MainModule.FileName, installed.Exe, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            return false;
        }
        public static void Install(string installer, Installation old, Release release, Asset asset, Action<string, int> progress) {
            Verify(installer, asset);
            bool restart = WasRunning(old);
            string log = Path.Combine(Root, "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            string args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NORESTARTAPPLICATIONS /LOG=" + Quote(log);
            if (old != null) args += " /DIR=" + Quote(old.Directory);
            progress("正在安装；FlClash 会暂时关闭，代理连接可能中断。", 100);
            ProcessStartInfo start = new ProcessStartInfo(installer, args) { UseShellExecute = true };
            if (!IsAdmin()) start.Verb = "runas";
            using (Process p = Process.Start(start)) { p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException("官方安装器返回代码 " + p.ExitCode + "。请查看：" + log); }
            Installation updated = Detect();
            if (updated == null || ParseVersion(updated.Version) < ParseVersion(release.tag_name) || (old != null && !String.Equals(old.Directory, updated.Directory, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("安装器已退出，但未确认目标目录更新成功。请查看：" + log);
            if (restart) {
                try { Process.Start(new ProcessStartInfo(updated.Exe) { UseShellExecute = true, WorkingDirectory = updated.Directory }); }
                catch (Exception ex) { Engine.Log("更新已完成，但启动 FlClash 失败：" + ex.Message); }
            }
            progress("更新成功：" + updated.Version + (restart ? "；已尝试重新启动 FlClash。" : "。"), 100);
        }
    }
