using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MuniClaw.Core.Integrations;

public sealed class LocalEncryptedSecretStore : IOpenBaoClient
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly string _secretsDirectoryPath;
    private readonly byte[] _masterKey;

    public string SecretsDirectoryPath => _secretsDirectoryPath;

    public LocalEncryptedSecretStore(string secretsDirectoryPath, byte[] masterKey)
    {
        if (string.IsNullOrWhiteSpace(secretsDirectoryPath))
        {
            throw new ArgumentException("Secrets directory path cannot be null or empty.", nameof(secretsDirectoryPath));
        }

        if (masterKey == null || masterKey.Length != 32)
        {
            throw new ArgumentException("Master key must be exactly 32 bytes (256 bits) for AES-256.", nameof(masterKey));
        }

        _secretsDirectoryPath = Path.GetFullPath(secretsDirectoryPath);
        _masterKey = (byte[])masterKey.Clone();
        Directory.CreateDirectory(_secretsDirectoryPath);
    }

    public LocalEncryptedSecretStore(string secretsDirectoryPath, string masterKeyOrPassphrase)
        : this(secretsDirectoryPath, DeriveKey(masterKeyOrPassphrase))
    {
    }

    public Task StoreSecretAsync(string path, IReadOnlyDictionary<string, string> secretData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretData);

        var filePath = ResolveSecretFilePath(path);
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(secretData);
        var plaintext = Encoding.UTF8.GetBytes(json);

        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(_masterKey, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // Layout: [Nonce (12)][Tag (16)][Ciphertext (N)]
        byte[] payload = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        var tempFile = Path.Combine(dir, $".tmp_{Guid.NewGuid():N}");
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using (var fs = new FileStream(tempFile, options))
                {
                    fs.Write(payload);
                }
                File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            else
            {
                File.WriteAllBytes(tempFile, payload);
            }

            File.Move(tempFile, filePath, overwrite: true);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        return Task.CompletedTask;
    }


    public Task<Dictionary<string, string>?> GetSecretAsync(string path, CancellationToken ct = default)
    {
        var filePath = ResolveSecretFilePath(path);
        if (!File.Exists(filePath))
        {
            if (File.Exists(filePath + ".enc"))
            {
                filePath += ".enc";
            }
            else
            {
                return Task.FromResult<Dictionary<string, string>?>(null);
            }
        }

        byte[] payload;
        try
        {
            payload = File.ReadAllBytes(filePath);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Dictionary<string, string>?>(null);
        }

        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Corrupted secret payload: data length is insufficient to contain nonce and tag.");
        }

        byte[] nonce = payload[..NonceSizeBytes];
        byte[] tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        byte[] ciphertext = payload[(NonceSizeBytes + TagSizeBytes)..];

        byte[] plaintext = new byte[ciphertext.Length];
        using (var aesGcm = new AesGcm(_masterKey, TagSizeBytes))
        {
            // Decrypt will throw CryptographicException if the tag does not match or key is incorrect
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        var json = Encoding.UTF8.GetString(plaintext);
        var secretDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return Task.FromResult<Dictionary<string, string>?>(secretDict);
    }

    public Task DeleteSecretAsync(string path, CancellationToken ct = default)
    {
        var filePath = ResolveSecretFilePath(path);
        SecureDeleteIfExists(filePath);
        SecureDeleteIfExists(filePath + ".enc");

        return Task.CompletedTask;
    }

    private static void SecureDeleteIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var length = new FileInfo(filePath).Length;
            if (length > 0)
            {
                var zeros = new byte[length];
                File.WriteAllBytes(filePath, zeros);
            }
        }
        catch
        {
            // best-effort overwrite before deletion
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // best-effort delete
        }
    }

    private string ResolveSecretFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Secret path cannot be null or empty.", nameof(path));
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith("v1/secret/data/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["v1/secret/data/".Length..];
        }
        else if (normalized.StartsWith("secret/data/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["secret/data/".Length..];
        }
        else if (normalized.StartsWith("secret/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["secret/".Length..];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (seg is ".." or ".")
            {
                throw new ArgumentException("Path cannot contain relative navigation segments.", nameof(path));
            }
        }

        var combined = Path.Combine(_secretsDirectoryPath, Path.Combine(segments));
        var fullPath = Path.GetFullPath(combined);
        var fullBase = Path.GetFullPath(_secretsDirectoryPath);
        if (!fullPath.StartsWith(fullBase, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Secret path traversal outside allowed directory.");
        }

        return fullPath;
    }

    private static byte[] DeriveKey(string masterKeyOrPassphrase)
    {
        if (string.IsNullOrWhiteSpace(masterKeyOrPassphrase))
        {
            throw new ArgumentException("Master key or passphrase cannot be null or empty.", nameof(masterKeyOrPassphrase));
        }

        // Check if input is a 32-byte Base64 string (44 characters)
        if (masterKeyOrPassphrase.Length == 44)
        {
            try
            {
                var raw = Convert.FromBase64String(masterKeyOrPassphrase);
                if (raw.Length == 32)
                {
                    return raw;
                }
            }
            catch
            {
                // Fall back to hash derivation
            }
        }

        // Check if input is a 32-byte Hex string (64 characters)
        if (masterKeyOrPassphrase.Length == 64)
        {
            try
            {
                var raw = Convert.FromHexString(masterKeyOrPassphrase);
                if (raw.Length == 32)
                {
                    return raw;
                }
            }
            catch
            {
                // Fall back to hash derivation
            }
        }

        // Cryptographic key derivation via SHA-256 for passphrases
        return SHA256.HashData(Encoding.UTF8.GetBytes(masterKeyOrPassphrase));
    }
}
