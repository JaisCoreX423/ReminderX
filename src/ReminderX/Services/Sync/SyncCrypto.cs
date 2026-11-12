using System.Security.Cryptography;
using System.Text;

namespace ReminderX.Services.Sync;

/// <summary>
/// End-to-end encryption for synced records: AES-256-GCM with the vault key that only travels in
/// the pairing QR. Wire format (base64url, no padding): nonce(12) | ciphertext | tag(16).
/// The record id and kind are bound as associated data, so a ciphertext can't be replayed onto a
/// different record. The Android app implements the same format.
/// </summary>
public static class SyncCrypto
{
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    public static string Encrypt(byte[] key, string kind, string id, string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length),
            output.AsSpan(NonceSize + plain.Length, TagSize), AssociatedData(kind, id));
        return Base64Url.Encode(output);
    }

    /// <summary>Throws <see cref="CryptographicException"/> when the key is wrong or the data was altered.</summary>
    public static string Decrypt(byte[] key, string kind, string id, string data)
    {
        var input = Base64Url.Decode(data);
        if (input.Length < NonceSize + TagSize) throw new CryptographicException("Record too short");

        var cipherLength = input.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, cipherLength),
            input.AsSpan(NonceSize + cipherLength, TagSize), plain, AssociatedData(kind, id));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] AssociatedData(string kind, string id) => Encoding.UTF8.GetBytes($"{kind}:{id}");
}

public static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
