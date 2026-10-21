using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class CryptoService
{
    private static readonly SecureRandom Random = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Org.BouncyCastle.Crypto.Parameters.ECDomainParameters Sm2Domain = CreateSm2Domain();

    public string Md5(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public string Base64(EncodingRequest request)
    {
        return IsEncode(request.Operation)
            ? Convert.ToBase64String(StrictUtf8.GetBytes(request.Text!))
            : DecodeUtf8(DecodeBase64(request.Text, "待解码文本"), "Base64");
    }

    public string Url(EncodingRequest request)
    {
        var text = request.Text!;
        if (IsEncode(request.Operation)) return Uri.EscapeDataString(text);

        var result = new StringBuilder();
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != '%')
            {
                result.Append(text[index++]);
                continue;
            }

            var bytes = new List<byte>();
            while (index < text.Length && text[index] == '%')
            {
                if (index + 2 >= text.Length || !byte.TryParse(text.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    throw new ArgumentException("URL 编码无效：百分号后必须是两位十六进制字符。", nameof(request));
                }
                bytes.Add(value);
                index += 3;
            }
            result.Append(DecodeUtf8(bytes.ToArray(), "URL"));
        }
        return result.ToString();
    }

    public string Aes(AesRequest request)
    {
        var key = DecodeBase64(request.KeyBase64, "AES 密钥");
        var iv = DecodeBase64(request.IvBase64, "AES IV");
        if (key.Length is not (16 or 24 or 32) || iv.Length != 16)
        {
            throw new ArgumentException("AES 密钥必须是 16/24/32 字节，IV 必须是 16 字节。", nameof(request));
        }

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return IsEncrypt(request.Operation) ? Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(request.Text!), iv)) : Encoding.UTF8.GetString(aes.DecryptCbc(DecodeBase64(request.Text, "AES 密文"), iv));
    }

    public AesKeyResponse GenerateAesKey()
    {
        var key = RandomBytes(32);
        var iv = RandomBytes(16);
        return new(Convert.ToBase64String(key), Convert.ToBase64String(iv));
    }

    public string Rsa(RsaRequest request)
    {
        using var rsa = RSA.Create();
        if (IsEncrypt(request.Operation))
        {
            rsa.ImportFromPem(request.KeyPem);
            return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(request.Text!), RSAEncryptionPadding.OaepSHA256));
        }

        rsa.ImportFromPem(request.KeyPem);
        return Encoding.UTF8.GetString(rsa.Decrypt(DecodeBase64(request.Text, "RSA 密文"), RSAEncryptionPadding.OaepSHA256));
    }

    public RsaKeyResponse GenerateRsaKey()
    {
        using var rsa = RSA.Create(2048);
        return new(rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    public string Sm2(Sm2Request request)
    {
        var engine = new SM2Engine();
        if (IsEncrypt(request.Operation))
        {
            var point = Sm2Domain.Curve.DecodePoint(DecodeHex(request.KeyHex, "SM2 公钥"));
            engine.Init(true, new ParametersWithRandom(new ECPublicKeyParameters(point, Sm2Domain), Random));
            return Convert.ToHexString(engine.ProcessBlock(Encoding.UTF8.GetBytes(request.Text!), 0, Encoding.UTF8.GetByteCount(request.Text!))).ToLowerInvariant();
        }

        var privateKey = new ECPrivateKeyParameters(new BigInteger(1, DecodeHex(request.KeyHex, "SM2 私钥")), Sm2Domain);
        engine.Init(false, privateKey);
        var cipherText = DecodeHex(request.Text, "SM2 密文");
        return Encoding.UTF8.GetString(engine.ProcessBlock(cipherText, 0, cipherText.Length));
    }

    public Sm2KeyResponse GenerateSm2Key()
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(Sm2Domain, Random));
        var pair = generator.GenerateKeyPair();
        var privateKey = (ECPrivateKeyParameters)pair.Private;
        var publicKey = (ECPublicKeyParameters)pair.Public;
        return new(Convert.ToHexString(publicKey.Q.Normalize().GetEncoded(false)).ToLowerInvariant(), privateKey.D.ToString(16).PadLeft(64, '0'));
    }

    public string Sm4(Sm4Request request)
    {
        var key = DecodeHex(request.KeyHex, "SM4 密钥");
        var iv = DecodeHex(request.IvHex, "SM4 IV");
        if (key.Length != 16 || iv.Length != 16)
        {
            throw new ArgumentException("SM4 密钥和 IV 都必须是 16 字节（32 位十六进制）。", nameof(request));
        }

        var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new SM4Engine()));
        cipher.Init(IsEncrypt(request.Operation), new ParametersWithIV(new KeyParameter(key), iv));
        var input = IsEncrypt(request.Operation) ? Encoding.UTF8.GetBytes(request.Text!) : DecodeBase64(request.Text, "SM4 密文");
        var output = cipher.DoFinal(input);
        return IsEncrypt(request.Operation) ? Convert.ToBase64String(output) : Encoding.UTF8.GetString(output);
    }

    public Sm4KeyResponse GenerateSm4Key() => new(Convert.ToHexString(RandomBytes(16)).ToLowerInvariant(), Convert.ToHexString(RandomBytes(16)).ToLowerInvariant());

    private static bool IsEncrypt(string? operation) => string.Equals(operation, "encrypt", StringComparison.OrdinalIgnoreCase);

    private static bool IsEncode(string? operation) => operation switch
    {
        "encode" => true,
        "decode" => false,
        _ => throw new ArgumentException("操作必须是 encode 或 decode。", nameof(operation))
    };

    private static string DecodeUtf8(byte[] bytes, string name)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new ArgumentException($"{name}解码结果不是有效的 UTF-8 文本。", nameof(bytes)); }
    }

    private static byte[] DecodeBase64(string? value, string name)
    {
        try { return Convert.FromBase64String(value ?? string.Empty); }
        catch (FormatException) { throw new ArgumentException($"{name}必须是有效的 Base64。", name); }
    }

    private static byte[] DecodeHex(string? value, string name)
    {
        try { return Hex.Decode(value ?? string.Empty); }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { throw new ArgumentException($"{name}必须是有效的十六进制。", name, exception); }
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.NextBytes(bytes);
        return bytes;
    }

    private static Org.BouncyCastle.Crypto.Parameters.ECDomainParameters CreateSm2Domain()
    {
        var curve = Org.BouncyCastle.Crypto.EC.CustomNamedCurves.GetByName("sm2p256v1");
        return new(curve.Curve, curve.G, curve.N, curve.H);
    }
}
