using System.Security.Cryptography;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class VaultTests
{
    private static string PathFor() => Path.Combine(Path.GetTempPath(), "EXPC-vault-fixture-" + Guid.NewGuid().ToString("N"), "accounts.vault");
    private static string Password() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    [TestMethod] public void PortableRoundTripNeverWritesPlaintextSecrets()
    {
        var path = PathFor(); var password = Password(); var secret = Password();
        using (var vault = new SecretVault(path)) { vault.Create(password); vault.SaveAccounts([new("id", "Fixture", "login.1c.ru", "fixture-user", secret)]); }
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(secret));
        var copied = PathFor(); Directory.CreateDirectory(Path.GetDirectoryName(copied)!); File.Copy(path, copied);
        using var portable = new SecretVault(copied); Assert.IsFalse(portable.Unlocked); portable.Unlock(password);
        Assert.AreEqual(secret, portable.Accounts.Single().Password);
    }
    [TestMethod] public void WrongPasswordAndCorruptionNeverOverwrite()
    {
        var path = PathFor(); var password = Password(); using (var vault = new SecretVault(path)) vault.Create(password);
        var original = File.ReadAllBytes(path);
        using var opened = new SecretVault(path);
        Assert.Throws<CryptographicException>(() => opened.Unlock(Password())); CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        var corrupt = original.ToArray(); corrupt[^1] ^= 1; File.WriteAllBytes(path, corrupt);
        Assert.Throws<CryptographicException>(() => opened.Unlock(password)); CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path)); Assert.IsFalse(opened.Unlocked);
        Assert.Throws<IOException>(() => opened.Create(Password())); CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
    }
    [TestMethod] public void NewNonceForEverySaveAndEncryptedBackup()
    {
        var path = PathFor(); using var vault = new SecretVault(path); vault.Create(Password()); var before = File.ReadAllBytes(path);
        vault.SaveAccounts([]); var after = File.ReadAllBytes(path);
        Assert.IsFalse(before.AsSpan(44, 12).SequenceEqual(after.AsSpan(44, 12)));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path + ".bak"));
    }
    [TestMethod] public void LockCancelsProtectedRequestsAndNewProcessStartsLocked()
    {
        var path = PathFor(); using var vault = new SecretVault(path); var password = Password(); vault.Create(password);
        var token = vault.ProtectedRequests; vault.Lock(); Assert.IsTrue(token.IsCancellationRequested); Assert.IsFalse(vault.Unlocked);
        Assert.Throws<InvalidOperationException>(() => _ = vault.Accounts);
        using var other = new SecretVault(path); Assert.IsFalse(other.Unlocked); other.Unlock(password); Assert.IsTrue(other.Unlocked);
    }
    [TestMethod] public void PasswordChangeAuthenticatesOldPasswordAndKeepsAccounts()
    {
        var path = PathFor(); using var vault = new SecretVault(path); var old = Password(); var next = Password(); vault.Create(old);
        var before = File.ReadAllBytes(path); Assert.Throws<CryptographicException>(() => vault.ChangePassword(Password(), next)); CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        vault.ChangePassword(old, next); vault.Lock(); Assert.Throws<CryptographicException>(() => vault.Unlock(old)); vault.Unlock(next); Assert.IsTrue(vault.Unlocked);
    }
    [TestMethod] public void ExternalModificationAndUnsupportedFormatAreRejected()
    {
        var path = PathFor(); using var vault = new SecretVault(path); var password = Password(); vault.Create(password);
        var bytes = File.ReadAllBytes(path); bytes[8] ^= 1; File.WriteAllBytes(path, bytes);
        Assert.Throws<IOException>(() => vault.SaveAccounts([])); vault.Lock(); Assert.Throws<CryptographicException>(() => vault.Unlock(password));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }
    [TestMethod] public void ConcurrentUnlockedWriterCannotClobberNewerData()
    {
        var path = PathFor(); var password = Password(); using var first = new SecretVault(path); first.Create(password);
        using var second = new SecretVault(path); second.Unlock(password); first.SaveAccounts([new("one", "One", "example.com", "fixture", Password())]);
        Assert.Throws<IOException>(() => second.SaveAccounts([]));
    }
    [TestMethod] public void UnwritableDestinationDoesNotCreateElsewhere()
    {
        var file = PathFor(); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture");
        using var vault = new SecretVault(Path.Combine(file, "vault")); Assert.Throws<IOException>(() => vault.Create(Password())); Assert.IsFalse(vault.Unlocked);
    }
}
