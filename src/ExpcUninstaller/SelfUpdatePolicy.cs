using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace ExpcUninstaller;

internal sealed record SelfUpdateManifest(int Schema, string Version, string Asset, long Bytes, string Sha256,
    string Architecture, string RuntimeMinimum, string WindowsMinimum);
internal static class SelfUpdatePolicy
{
    internal const string Repository = "AlexHDman/EXPC-Uninstaller";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    internal static Version ParseVersion(string text) => Regex.IsMatch(text, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$")
        && Version.TryParse(text, out var v) ? v : throw new InvalidDataException("Invalid stable version");
    internal static void Validate(SelfUpdateManifest m)
    {
        _ = ParseVersion(m.Version);
        if (m.Schema != 1 || m.Asset != "EXPCdelete.exe" || m.Architecture != "win-x64" || m.Bytes is < 1024 or > 200_000_000
            || !Regex.IsMatch(m.Sha256, "^[a-fA-F0-9]{64}$") || m.RuntimeMinimum != "10.0.12" || m.WindowsMinimum != "10.0.18362")
            throw new InvalidDataException("Incompatible update manifest");
    }
    internal static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    internal static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked update paths are not supported");
    }
    internal static void Verify(string path, SelfUpdateManifest manifest)
    {
        Validate(manifest); NoLinks(path);
        if (new FileInfo(path).Length != manifest.Bytes || !Hash(path).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update size/SHA256 mismatch");
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Not an executable");
        reader.BaseStream.Position = 0x3c; var offset = reader.ReadInt32();
        if (offset < 64 || offset > reader.BaseStream.Length - 6) throw new InvalidDataException("Invalid PE");
        reader.BaseStream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Expected x64 PE");
        var version = FileVersionInfo.GetVersionInfo(path);
        if ($"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}" != manifest.Version)
            throw new InvalidDataException("Executable version differs from manifest");
        reader.BaseStream.Position = offset + 24 + 112 + 4 * 8; // PE32+ certificate directory.
        var certificateOffset = reader.ReadUInt32(); var certificateSize = reader.ReadUInt32();
        if (certificateOffset != 0 || certificateSize != 0) VerifySignature(path);
    }
    private static void VerifySignature(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, Flags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) throw new InvalidDataException("Authenticode signature could not be trusted");
        }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile
    { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    { public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint State; public IntPtr StateData, Url; public uint Flags, Context; public IntPtr Signature; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr owner, ref Guid action, ref TrustData data);
    internal static Uri AssetUri(string version, string name) => new($"https://github.com/{Repository}/releases/download/v{ParseVersion(version)}/{name}");
    internal static void RequireAssetUrl(string url, string version, string name)
    {
        if (!string.Equals(url, AssetUri(version, name).AbsoluteUri, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected release asset URL");
    }
}

internal sealed record ReplacementRequest(string Origin, string Download, string OldHash, SelfUpdateManifest Manifest,
    int AppPid, long AppStart, int LauncherPid, long LauncherStart, string ReadyEvent);
internal static class Replacement
{
    internal static void Wait(int pid, long start, int milliseconds)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks != start) return; // PID reused: original already ended.
            if (!process.WaitForExit(milliseconds)) throw new TimeoutException("Old instance has not exited; replacement cancelled");
        }
        catch (ArgumentException) { }
    }
    internal static string Swap(ReplacementRequest request)
    {
        SelfUpdatePolicy.NoLinks(request.Origin); SelfUpdatePolicy.Verify(request.Download, request.Manifest);
        if (!SelfUpdatePolicy.Hash(request.Origin).Equals(request.OldHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original executable changed; replacement cancelled");
        var stage = request.Origin + ".expc-new-" + Guid.NewGuid().ToString("N");
        var backup = request.Origin + ".expc-rollback-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(request.Download, stage, false);
            SelfUpdatePolicy.Verify(stage, request.Manifest);
            File.Replace(stage, request.Origin, backup);
            if (!SelfUpdatePolicy.Hash(backup).Equals(request.OldHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Rollback hash mismatch; backup retained");
            return backup;
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    internal static void Rollback(string origin, string backup, string oldHash, string newHash)
    {
        SelfUpdatePolicy.NoLinks(origin); SelfUpdatePolicy.NoLinks(backup);
        if (!SelfUpdatePolicy.Hash(backup).Equals(oldHash, StringComparison.OrdinalIgnoreCase)
            || !SelfUpdatePolicy.Hash(origin).Equals(newHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Rollback conflict; files retained");
        File.Replace(backup, origin, null); // Never restarts automatically, so rollback cannot loop.
    }
}
