using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExpcUninstaller;

internal sealed record VaultAccount(string Id, string Label, string Host, string Username, string Password);
internal sealed class SecretVault(string path) : IDisposable
{
    private const int Iterations = 600_000;
    private static readonly byte[] Magic = "EXPCVLT1"u8.ToArray();
    private byte[]? _key;
    private byte[]? _salt;
    private byte[]? _fingerprint;
    private readonly List<VaultAccount> _accounts = [];
    private CancellationTokenSource _requests = new();
    private bool _disposed;
    public bool Exists => File.Exists(path);
    public bool Unlocked => _key != null;
    public CancellationToken ProtectedRequests => _requests.Token;
    public event Action? StateChanged;
    public IReadOnlyList<VaultAccount> Accounts => Unlocked ? _accounts.AsReadOnly() : throw new InvalidOperationException(L.T("VaultLocked"));
    private static byte[] Derive(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    private static void Clear(byte[]? value) { if (value != null) CryptographicOperations.ZeroMemory(value); }
    public void Create(string password)
    {
        if (Exists) throw new IOException(L.T("VaultExists"));
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(32); var key = Derive(password, salt);
        try
        {
            var bytes = Encrypt(key, salt, []); Write(bytes, true);
            Adopt(key, salt, [], bytes); key = [];
        }
        finally { Clear(key); }
    }
    public void Unlock(string password)
    {
        if (Unlocked) return;
        var bytes = Read(); var salt = Header(bytes); var key = Derive(password, salt);
        try { var accounts = Decrypt(bytes, key); Adopt(key, salt, accounts, bytes); key = []; }
        finally { Clear(key); }
    }
    private byte[] Read()
    {
        if (new FileInfo(path).Length is < 72 or > 2_000_000) throw new CryptographicException(L.T("VaultInvalid"));
        return File.ReadAllBytes(path);
    }
    private static byte[] Header(byte[] data)
    {
        if (data.Length < 72 || !data.AsSpan(0, 8).SequenceEqual(Magic) || BitConverter.ToInt32(data, 8) != Iterations)
            throw new CryptographicException(L.T("VaultInvalid"));
        return data[12..44];
    }
    private static List<VaultAccount> Decrypt(byte[] data, byte[] key)
    {
        var plain = new byte[data.Length - 72];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(data.AsSpan(44, 12), data.AsSpan(72), data.AsSpan(56, 16), plain, data.AsSpan(0, 44));
            return JsonSerializer.Deserialize<List<VaultAccount>>(plain) ?? throw new CryptographicException(L.T("VaultInvalid"));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new CryptographicException(L.T("VaultInvalid")); }
        finally { Clear(plain); }
    }
    private static byte[] Encrypt(byte[] key, byte[] salt, IReadOnlyList<VaultAccount> accounts)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(accounts);
        try
        {
            var data = new byte[72 + plain.Length]; Magic.CopyTo(data, 0); BitConverter.GetBytes(Iterations).CopyTo(data, 8); salt.CopyTo(data, 12);
            RandomNumberGenerator.Fill(data.AsSpan(44, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(data.AsSpan(44, 12), plain, data.AsSpan(72), data.AsSpan(56, 16), data.AsSpan(0, 44));
            return data;
        }
        finally { Clear(plain); }
    }
    private void Adopt(byte[] key, byte[] salt, List<VaultAccount> accounts, byte[] bytes)
    {
        Clear(_key); _key = key; _salt = salt; _fingerprint = SHA256.HashData(bytes);
        _accounts.Clear(); _accounts.AddRange(accounts); StateChanged?.Invoke();
    }
    private void Write(byte[] bytes, bool create)
    {
        // No fallback directory and no plaintext temporary file. Same-directory atomic rename.
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        using var writerLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            if (create) File.Move(temp, path, false);
            else
            {
                // Reject replacement/corruption since unlock, including a second EXPC process.
                if (_fingerprint == null || !CryptographicOperations.FixedTimeEquals(_fingerprint, SHA256.HashData(Read())))
                    throw new IOException(L.T("VaultChanged"));
                File.Replace(temp, path, path + ".bak");
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void SaveAccounts(IEnumerable<VaultAccount> accounts)
    {
        if (_key == null || _salt == null) throw new InvalidOperationException(L.T("VaultLocked"));
        var values = accounts.ToList();
        if (values.Count > 100 || values.Select(x => x.Id).Distinct().Count() != values.Count || values.Any(x =>
            x.Id.Length is 0 or > 100 || x.Label.Length > 200 || x.Username.Length > 500 || x.Password.Length > 4096
            || Uri.CheckHostName(x.Host) != UriHostNameType.Dns || x.Host != x.Host.ToLowerInvariant() || !x.Host.Contains('.')))
            throw new FormatException(L.T("AccountInvalid"));
        var bytes = Encrypt(_key, _salt, values); Write(bytes, false);
        _accounts.Clear(); _accounts.AddRange(values); _fingerprint = SHA256.HashData(bytes);
    }
    public void ChangePassword(string oldPassword, string newPassword)
    {
        if (!Unlocked) throw new InvalidOperationException(L.T("VaultLocked"));
        ValidatePassword(newPassword);
        var original = Read(); var old = Derive(oldPassword, Header(original));
        try { _ = Decrypt(original, old); } finally { Clear(old); }
        var salt = RandomNumberGenerator.GetBytes(32); var key = Derive(newPassword, salt);
        try { var bytes = Encrypt(key, salt, _accounts); Write(bytes, false); Adopt(key, salt, _accounts.ToList(), bytes); key = []; }
        finally { Clear(key); }
    }
    private static void ValidatePassword(string password)
    { if (password.Length is < 12 or > 1024) throw new ArgumentException(L.T("PasswordLength")); }
    public void Lock()
    {
        if (_disposed) return;
        _requests.Cancel(); _requests.Dispose(); _requests = new CancellationTokenSource();
        Clear(_key); Clear(_salt); Clear(_fingerprint); _key = _salt = _fingerprint = null;
        // Managed strings cannot be reliably erased; never intern, log, or persist these strings in plaintext.
        _accounts.Clear(); StateChanged?.Invoke();
    }
    public void Dispose() { if (_disposed) return; Lock(); _disposed = true; _requests.Dispose(); }
}
