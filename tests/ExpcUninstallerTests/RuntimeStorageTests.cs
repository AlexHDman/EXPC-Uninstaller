using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;
[TestClass]
[DoNotParallelize]
public sealed class RuntimeStorageTests
{
    [TestMethod]
    [DataRow(@"C:\Users\Test User\AppData\Local")]
    [DataRow(@"D:\Пользователь\Local")]
    public void ExactPerUserRoot(string localAppData) =>
        Assert.AreEqual(Path.Combine(localAppData, "EXPC", "Uninstaller"), RuntimeStorage.Resolve(localAppData));

    [TestMethod]
    [DataRow(null, false)] [DataRow("", false)] [DataRow("0", false)]
    [DataRow("false", false)] [DataRow("true", false)] [DataRow("1", true)]
    public void DiagnosticsRequireExplicitOptIn(string? value, bool enabled) =>
        Assert.AreEqual(enabled, DiagnosticLog.IsEnabled(value));

    [TestMethod]
    public void DisabledDiagnosticsDoNotSerializeOrTouchDisk()
    {
        DiagnosticLog.Append(false, "invalid\0path", "startup-metrics.jsonl", () => throw new Exception("Must not serialize"));
    }

    [TestMethod]
    public void LogsGoOnlyToUserLogsAndPreserveExistingData()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "storage-fixture-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(fixture, "Desktop");
        var logs = Path.Combine(RuntimeStorage.Resolve(Path.Combine(fixture, "LocalAppData")), "Logs");
        Directory.CreateDirectory(desktop);
        var settings = Path.Combine(desktop, "settings.fixture");
        File.WriteAllText(settings, "existing-user-data-fixture");
        try
        {
            DiagnosticLog.Append(false, logs, "startup-metrics.jsonl", () => "disabled");
            Assert.IsFalse(Directory.Exists(logs));
            DiagnosticLog.Append(true, logs, "startup-metrics.jsonl", () => "{\"stage\":\"fixture\"}");
            Assert.AreEqual("{\"stage\":\"fixture\"}" + Environment.NewLine, File.ReadAllText(Path.Combine(logs, "startup-metrics.jsonl")));
            Assert.IsFalse(File.Exists(Path.Combine(desktop, "startup-metrics.jsonl")));
            Assert.AreEqual("existing-user-data-fixture", File.ReadAllText(settings));
        }
        finally
        {
            // Exact fixture files only. Never touch actual user profiles/data.
            if (File.Exists(Path.Combine(logs, "startup-metrics.jsonl"))) File.Delete(Path.Combine(logs, "startup-metrics.jsonl"));
            File.Delete(settings);
            foreach (var dir in Directory.EnumerateDirectories(fixture, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(dir);
            Directory.Delete(fixture);
        }
    }
    [TestMethod]
    public void OptionalLogFailureDoesNotBlockApplication()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "storage-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "sentinel");
        try { DiagnosticLog.Append(true, file, "startup-metrics.jsonl", () => "fixture"); Assert.AreEqual("sentinel", File.ReadAllText(file)); }
        finally { File.Delete(file); }
    }
    [TestMethod]
    public void LegacyImportPreservesOriginalsAndDoesNotOverwriteCurrentData()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "migration-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(fixture, "Desktop");
        var target = Path.Combine(fixture, "User", "Data");
        Directory.CreateDirectory(Path.Combine(legacy, "local-data", "registry-backups"));
        File.WriteAllText(Path.Combine(legacy, "local-data", "settings.json"), "old settings");
        File.WriteAllText(Path.Combine(legacy, "local-data", "registry-backups", "test.reg"), "backup fixture");
        File.WriteAllText(Path.Combine(legacy, "accounts.expc-vault"), "encrypted fixture bytes");
        Directory.CreateDirectory(Path.Combine(target, "local-data"));
        File.WriteAllText(Path.Combine(target, "local-data", "settings.json"), "current settings");
        var original = Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).ToDictionary(p => p, SelfUpdatePolicy.Hash);
        try
        {
            UserDataMigration.Ensure(legacy, target);
            Assert.AreEqual("current settings", File.ReadAllText(Path.Combine(target, "local-data", "settings.json")));
            Assert.AreEqual("encrypted fixture bytes", File.ReadAllText(Path.Combine(target, "accounts.expc-vault")));
            Assert.AreEqual("backup fixture", File.ReadAllText(Path.Combine(target, "local-data", "registry-backups", "test.reg")));
            UserDataMigration.Ensure(legacy, target);
            Assert.AreEqual(original.Count, Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).Length);
            foreach (var item in original) Assert.AreEqual(item.Value, SelfUpdatePolicy.Hash(item.Key));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (var dir in Directory.GetDirectories(fixture, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(dir);
            Directory.Delete(fixture);
        }
    }
    [TestMethod]
    public void OriginDoesNotChangeCacheOrDiagnostics()
    {
        var old = Environment.GetEnvironmentVariable("EXPC_PORTABLE_ORIGIN");
        var cache = RuntimeStorage.Cache; var logs = RuntimeStorage.Logs;
        try
        {
            Environment.SetEnvironmentVariable("EXPC_PORTABLE_ORIGIN", @"Z:\External portable\EXPCdelete.exe");
            Assert.AreEqual(cache, RuntimeStorage.Cache); Assert.AreEqual(logs, RuntimeStorage.Logs);
            Assert.AreEqual(RuntimeStorage.Root, PortablePaths.Root);
            Assert.AreEqual(Path.Combine(RuntimeStorage.Data, "local-data"), PortableData.DirectoryPath);
            Assert.AreEqual(Path.Combine(cache, "SelfUpdate"), RuntimeStorage.UpdateCache);
        }
        finally { Environment.SetEnvironmentVariable("EXPC_PORTABLE_ORIGIN", old); }
    }
}
