using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Crypto;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/crypto")]
public sealed class CryptoController(CryptoService crypto) : ControllerBase
{
    [HttpPost("md5")]
    public ActionResult<CryptoResult> Md5(CryptoTextRequest request) => Execute(() => new CryptoResult(crypto.Md5(request.Text!)));

    [HttpPost("base64")]
    public ActionResult<CryptoResult> Base64(EncodingRequest request) => Execute(() => new CryptoResult(crypto.Base64(request)));

    [HttpPost("url")]
    public ActionResult<CryptoResult> UrlEncoding(EncodingRequest request) => Execute(() => new CryptoResult(crypto.Url(request)));

    [HttpPost("aes")]
    public ActionResult<CryptoResult> Aes(AesRequest request) => Execute(() => new CryptoResult(crypto.Aes(request)));

    [HttpGet("aes/key")]
    public ActionResult<AesKeyResponse> AesKey() => Ok(crypto.GenerateAesKey());

    [HttpPost("rsa")]
    public ActionResult<CryptoResult> Rsa(RsaRequest request) => Execute(() => new CryptoResult(crypto.Rsa(request)));

    [HttpGet("rsa/key")]
    public ActionResult<RsaKeyResponse> RsaKey() => Ok(crypto.GenerateRsaKey());

    [HttpPost("sm2")]
    public ActionResult<CryptoResult> Sm2(Sm2Request request) => Execute(() => new CryptoResult(crypto.Sm2(request)));

    [HttpGet("sm2/key")]
    public ActionResult<Sm2KeyResponse> Sm2Key() => Ok(crypto.GenerateSm2Key());

    [HttpPost("sm4")]
    public ActionResult<CryptoResult> Sm4(Sm4Request request) => Execute(() => new CryptoResult(crypto.Sm4(request)));

    [HttpGet("sm4/key")]
    public ActionResult<Sm4KeyResponse> Sm4Key() => Ok(crypto.GenerateSm4Key());

    private ActionResult<T> Execute<T>(Func<T> action)
    {
        try { return Ok(action()); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidCipherTextException exception) { return BadRequest(new { message = $"加密操作失败：{exception.Message}" }); }
    }
}
