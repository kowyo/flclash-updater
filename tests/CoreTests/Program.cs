using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using FlClashUpdater;
public static class Tests {
    static int assertions;
    static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); assertions++; Console.WriteLine("PASS: " + name); }
    static void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { assertions++; Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name); }
    public static int Main(string[] args) {
        string dir = args[0]; Directory.CreateDirectory(dir);
        Assert(Engine.ParseVersion("0.8.99") > Engine.ParseVersion("0.8.96+2026081701"), "installed build metadata and new release");
        Assert(Engine.ParseVersion("v0.8.100") > Engine.ParseVersion("v0.8.99"), "numeric version ordering");
        Assert(Engine.ParseVersion("0.8.99+1") == Engine.ParseVersion("v0.8.99"), "same release with metadata");
        Reject(delegate { Engine.ParseVersion("v0.8.100-beta.1"); }, "reject unsupported prerelease version");
        Release release = Engine.Json.Deserialize<Release>(File.ReadAllText(args[1]));
        Asset a = Engine.SelectAsset(release, "amd64"), arm = Engine.SelectAsset(release, "arm64");
        Assert(a.name.EndsWith("amd64-setup.exe") && arm.name.EndsWith("arm64-setup.exe"), "select installer for each architecture");
        CheckResult same = Engine.Check(new Installation { Directory = dir, Version = "0.8.99+2026100301" }, release, a, "amd64");
        Assert(!same.updateAvailable, "same version skips installation");
        Assert(!Engine.Check(new Installation { Directory = dir, Version = "0.9.0" }, release, a, "amd64").updateAvailable, "newer local version never downgraded");
        Assert(Engine.Check(null, release, a, "amd64").updateAvailable, "fresh manual install available");
        string original = a.browser_download_url;
        a.browser_download_url = "https://example.com/installer.exe";
        Reject(delegate { Engine.SelectAsset(release, "amd64"); }, "reject nonofficial download URL"); a.browser_download_url = original;
        string digest = a.digest; a.digest = null;
        Reject(delegate { Engine.SelectAsset(release, "amd64"); }, "reject absent digest"); a.digest = digest;
        Asset[] assets = release.assets; release.assets = new Asset[0];
        Reject(delegate { Engine.SelectAsset(release, "amd64"); }, "reject missing installer"); release.assets = new[] { a, a };
        Reject(delegate { Engine.SelectAsset(release, "amd64"); }, "reject ambiguous installers"); release.assets = assets;
        string file = Path.Combine(dir, "test.bin"); File.WriteAllText(file, "verified bytes", Encoding.ASCII);
        Asset fixture = new Asset { size = new FileInfo(file).Length, digest = "sha256:" + Engine.Hash(file) };
        Engine.Verify(file, fixture); Assert(true, "valid SHA-256 and size accepted");
        File.WriteAllText(file, "corrupt! bytes", Encoding.ASCII);
        Reject(delegate { Engine.Verify(file, fixture); }, "same-size corrupt download rejected");
        File.WriteAllText(file, "short", Encoding.ASCII);
        Reject(delegate { Engine.Verify(file, fixture); }, "truncated download rejected");
        foreach (ushort machine in new ushort[] { 0x8664, 0xAA64, 0x014c }) {
            using (BinaryWriter w = new BinaryWriter(File.Create(file))) { w.Write((ushort)0x5A4D); w.BaseStream.Position = 0x3c; w.Write(64); w.BaseStream.Position = 64; w.Write((uint)0x4550); w.Write(machine); }
            if (machine == 0x014c) Reject(delegate { Engine.ReadArchitecture(file); }, "unsupported x86 rejected");
            else Assert(Engine.ReadArchitecture(file) == (machine == 0x8664 ? "amd64" : "arm64"), "PE architecture " + machine);
        }
        Console.WriteLine("All " + assertions + " assertions passed."); return 0;
    }
}
