using System.ComponentModel.DataAnnotations;

namespace PersonalWorkbench.Models;

public sealed class CryptoTextRequest
{
    [Required, MaxLength(100_000)]
    public string? Text { get; set; }
}

public sealed class EncodingRequest
{
    [Required(AllowEmptyStrings = true), MaxLength(100_000)]
    public string? Text { get; set; }

    [Required, RegularExpression("^(encode|decode)$", ErrorMessage = "操作必须是 encode 或 decode。")]
    public string? Operation { get; set; }
}

public sealed class AesRequest
{
    [Required, MaxLength(100_000)] public string? Text { get; set; }
    [Required] public string? KeyBase64 { get; set; }
    [Required] public string? IvBase64 { get; set; }
    [Required] public string? Operation { get; set; }
}

public sealed class RsaRequest
{
    [Required, MaxLength(100_000)] public string? Text { get; set; }
    [Required] public string? KeyPem { get; set; }
    [Required] public string? Operation { get; set; }
}

public sealed class Sm2Request
{
    [Required, MaxLength(100_000)] public string? Text { get; set; }
    [Required, MinLength(64)] public string? KeyHex { get; set; }
    [Required] public string? Operation { get; set; }
}

public sealed class Sm4Request
{
    [Required, MaxLength(100_000)] public string? Text { get; set; }
    [Required, MinLength(32), MaxLength(32)] public string? KeyHex { get; set; }
    [Required, MinLength(32), MaxLength(32)] public string? IvHex { get; set; }
    [Required] public string? Operation { get; set; }
}

public sealed record CryptoResult(string Result);
public sealed record AesKeyResponse(string KeyBase64, string IvBase64);
public sealed record RsaKeyResponse(string PublicKeyPem, string PrivateKeyPem);
public sealed record Sm2KeyResponse(string PublicKeyHex, string PrivateKeyHex);
public sealed record Sm4KeyResponse(string KeyHex, string IvHex);
