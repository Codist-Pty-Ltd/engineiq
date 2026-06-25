using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Paystack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EngineIQ.API.Controllers;

[ApiController]
[Route("webhooks")]
public sealed class PaystackWebhookController : ControllerBase
{
    private readonly IPaystackWebhookProcessor _processor;
    private readonly PaystackOptions _options;
    private readonly ILogger<PaystackWebhookController> _logger;

    public PaystackWebhookController(
        IPaystackWebhookProcessor processor,
        IOptions<PaystackOptions> options,
        ILogger<PaystackWebhookController> logger)
    {
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Paystack signs the raw JSON body with HMAC-SHA512 using the account secret key
    /// (<c>x-paystack-signature</c>). Always returns 200 after signature validation.
    /// </summary>
    [HttpPost("paystack")]
    public async Task<IActionResult> Paystack(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payloadBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var signatureHeader = Request.Headers[PaystackWebhookSignatureValidator.SignatureHeaderName].FirstOrDefault();
        if (!PaystackWebhookSignatureValidator.Validate(payloadBody, signatureHeader, _options.SecretKey))
        {
            _logger.LogWarning("Paystack webhook signature validation failed.");
            return Unauthorized();
        }

        try
        {
            await _processor.ProcessAsync(payloadBody, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack webhook processing failed after signature validation.");
        }

        return Ok();
    }
}
