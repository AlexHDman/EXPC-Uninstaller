using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpcUninstaller;

// Isolated acceptance helper, never shipped. Only random synthetic credentials.
internal static class VaultTransport
{
    public static int Send(string directory)
    {
        using var transport = RSA.Create(); transport.ImportFromPem(File.ReadAllText(Path.Combine(directory, "transport-public.pem")));
        var master = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using (var vault = new SecretVault(Path.Combine(directory, "transport.expc-vault")))
        { vault.Create(master); vault.SaveAccounts([new("fixture", "Synthetic transport fixture", "example.com", "fixture", secret)]); }
        var plain = Encoding.UTF8.GetBytes(master);
        try { File.WriteAllBytes(Path.Combine(directory, "master.encrypted"), transport.Encrypt(plain, RSAEncryptionPadding.OaepSHA256)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        File.WriteAllText(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(new { SecretDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), ProducerSandbox = Environment.UserName == "WDAGUtilityAccount" }));
        return 0;
    }
    public static int Reopen(string path)
    {
        using var vault = new SecretVault(path); var locked = !vault.Unlocked;
        var password = Console.ReadLine() ?? ""; vault.Unlock(password);
        Console.WriteLine(JsonSerializer.Serialize(new { StartedLocked = locked, Unlocked = vault.Unlocked, Accounts = vault.Accounts.Count })); return 0;
    }
    public static async Task<int> Receive(string directory)
    {
        using var transport = RSA.Create(3072);
        File.WriteAllText(Path.Combine(directory, "transport-public.pem"), transport.ExportSubjectPublicKeyInfoPem());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        while (!File.Exists(Path.Combine(directory, "ready.json"))) await Task.Delay(250, timeout.Token);
        var plain = transport.Decrypt(File.ReadAllBytes(Path.Combine(directory, "master.encrypted")), RSAEncryptionPadding.OaepSHA256);
        string master; try { master = Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        var copied = @"C:\EXPC-VaultAcceptance\accounts.expc-vault"; Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
        File.Copy(Path.Combine(directory, "transport.expc-vault"), copied, false);
        var original = File.ReadAllBytes(copied); bool wrong = false;
        using var vault = new SecretVault(copied);
        try { vault.Unlock(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))); } catch (CryptographicException) { wrong = true; }
        var intact = original.SequenceEqual(File.ReadAllBytes(copied)); vault.Unlock(master);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ready.json")));
        var matches = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vault.Accounts.Single().Password))) == expected.RootElement.GetProperty("SecretDigest").GetString();
        var protectedRequest = vault.ProtectedRequests; vault.Lock(); var locked = !vault.Unlocked && protectedRequest.IsCancellationRequested;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("vault-reopen"); start.ArgumentList.Add(copied);
        using var child = Process.Start(start)!; await child.StandardInput.WriteLineAsync(master); child.StandardInput.Close();
        var output = await child.StandardOutput.ReadToEndAsync(); await child.WaitForExitAsync(timeout.Token);
        using var reopened = JsonDocument.Parse(output);
        var restarted = child.ExitCode == 0 && reopened.RootElement.GetProperty("StartedLocked").GetBoolean() && reopened.RootElement.GetProperty("Unlocked").GetBoolean();
        var crossEnvironment = !expected.RootElement.GetProperty("ProducerSandbox").GetBoolean() && Environment.UserName == "WDAGUtilityAccount";
        var pass = wrong && intact && matches && locked && restarted && crossEnvironment;
        File.WriteAllText(@"C:\EXPC-Results\vault-transport.json", JsonSerializer.Serialize(new { pass, WrongPasswordRejected = wrong, OriginalUnchanged = intact, SecretMatches = matches, ManualLockCancels = locked, RestartedLocked = restarted, CrossEnvironment = crossEnvironment }));
        return pass ? 0 : 1;
    }
}
