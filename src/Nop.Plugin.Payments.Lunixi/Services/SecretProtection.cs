using System.Security.Cryptography;
using System.Text;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class SecretProtection
{
    private readonly byte[] key;
    public SecretProtection(string encodedKey)
    {
        try { key = Convert.FromBase64String(encodedKey); }
        catch (FormatException) { throw new InvalidOperationException("LUNIXI_NOP_ENCRYPTION_KEY must be a base64 server key."); }
        if (key.Length != 32) throw new InvalidOperationException("LUNIXI_NOP_ENCRYPTION_KEY must decode to 32 bytes.");
    }
    public string Protect(string plain, string purpose)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var input = Encoding.UTF8.GetBytes(plain); var cipher = new byte[input.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, input, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        return "v1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Unprotect(string value, string purpose)
    {
        try
        {
            if (!value.StartsWith("v1:", StringComparison.Ordinal)) throw new CryptographicException();
            var bytes = Convert.FromBase64String(value[3..]); if (bytes.Length < 28) throw new CryptographicException();
            var result = new byte[bytes.Length - 28]; using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), result, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(result);
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        { throw new InvalidOperationException("Encrypted Lunixi settings could not be authenticated. Restore the matching server key."); }
    }
    public override string ToString() => "SecretProtection { Key = [REDACTED] }";
}
