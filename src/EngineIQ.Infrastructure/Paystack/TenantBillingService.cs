using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Infrastructure.Paystack;

public sealed class TenantBillingService : ITenantBillingService
{
    private readonly ITenantRepository _tenants;
    private readonly IPaystackClient _paystack;
    private readonly PaystackOptions _options;
    private readonly ILogger<TenantBillingService> _logger;

    public TenantBillingService(
        ITenantRepository tenants,
        IPaystackClient paystack,
        IOptions<PaystackOptions> options,
        ILogger<TenantBillingService> logger)
    {
        _tenants = tenants;
        _paystack = paystack;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProvisionCustomerAfterRegisterAsync(
        Guid tenantId,
        string email,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        var billing = await _tenants.GetBillingRowAsync(tenantId, cancellationToken);
        if (billing is null)
            return;

        if (InternalTenantBilling.BypassesPaystack(billing.BillingStatus))
        {
            _logger.LogInformation("Skipping Paystack customer for internal tenant {TenantId}.", tenantId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(billing.PaystackCustomerCode))
            return;

        if (!_paystack.IsConfigured)
        {
            _logger.LogWarning("Paystack not configured; tenant {TenantId} remains without customer_code.", tenantId);
            return;
        }

        var customerCode = await _paystack.CreateCustomerAsync(email.Trim(), companyName.Trim(), cancellationToken);
        await _tenants.UpdatePaystackCustomerCodeAsync(tenantId, customerCode, cancellationToken);
        _logger.LogInformation("Paystack customer {CustomerCode} linked to tenant {TenantId}.", customerCode, tenantId);
    }

    public async Task<TenantBillingSnapshot?> GetBillingAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var row = await _tenants.GetBillingRowAsync(tenantId, cancellationToken);
        if (row is null)
            return null;

        return new TenantBillingSnapshot(
            row.TenantId,
            row.Plan,
            row.BillingStatus,
            row.TrialEndsAt,
            row.PaystackCustomerCode,
            row.PaystackSubscriptionCode,
            PaystackRequired: !InternalTenantBilling.BypassesPaystack(row.BillingStatus));
    }

    public async Task<SubscriptionCheckoutResult> StartSubscriptionCheckoutAsync(
        Guid tenantId,
        string plan,
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        var row = await RequirePaystackBillableAsync(tenantId, cancellationToken);
        if (!PlanCatalog.IsKnownProductPlan(plan))
            throw new InvalidOperationException("unknown_plan");

        var paystackPlanCode = ResolvePaystackPlanCode(plan);
        if (string.IsNullOrWhiteSpace(paystackPlanCode))
            throw new InvalidOperationException("paystack_plan_not_configured");

        if (string.IsNullOrWhiteSpace(row.ContactEmail))
            throw new InvalidOperationException("missing_contact_email");

        var init = await _paystack.InitializeSubscriptionCheckoutAsync(
            row.ContactEmail,
            paystackPlanCode,
            callbackUrl,
            cancellationToken);

        return new SubscriptionCheckoutResult(init.Reference, init.AuthorizationUrl);
    }

    public async Task<SubscriptionConfirmResult> ConfirmSubscriptionAsync(
        Guid tenantId,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var row = await RequirePaystackBillableAsync(tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(reference))
            return new SubscriptionConfirmResult(false, null, null, "missing_reference");

        var verify = await _paystack.VerifyTransactionAsync(reference.Trim(), cancellationToken);
        if (!verify.Success)
            return new SubscriptionConfirmResult(false, null, null, "payment_not_successful");

        var subscriptionCode = verify.SubscriptionCode ?? row.PaystackSubscriptionCode;
        if (string.IsNullOrWhiteSpace(subscriptionCode))
            return new SubscriptionConfirmResult(false, null, null, "missing_subscription_code");

        var productPlan = MapPaystackPlanToProduct(verify.PlanCode) ?? row.Plan;
        await _tenants.ApplyPaidPlanAsync(
            tenantId,
            productPlan,
            PlanCatalog.DefaultFeatureFlagsJson(productPlan),
            BillingStatuses.Active,
            subscriptionCode,
            verify.CustomerCode ?? row.PaystackCustomerCode,
            cancellationToken);

        return new SubscriptionConfirmResult(true, BillingStatuses.Active, subscriptionCode, null);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(
        Guid tenantId,
        string newPlan,
        CancellationToken cancellationToken = default)
    {
        var row = await RequirePaystackBillableAsync(tenantId, cancellationToken);
        if (!PlanCatalog.IsKnownProductPlan(newPlan))
            return new PlanChangeResult(false, null, "unknown_plan");

        if (string.IsNullOrWhiteSpace(row.PaystackSubscriptionCode))
            return new PlanChangeResult(false, null, "no_active_subscription");

        var paystackPlanCode = ResolvePaystackPlanCode(newPlan);
        if (string.IsNullOrWhiteSpace(paystackPlanCode))
            return new PlanChangeResult(false, null, "paystack_plan_not_configured");

        var updatedCode = await _paystack.UpdateSubscriptionPlanAsync(
            row.PaystackSubscriptionCode,
            paystackPlanCode,
            cancellationToken);

        var productPlan = newPlan.Trim();
        await _tenants.ApplyPaidPlanAsync(
            tenantId,
            productPlan,
            PlanCatalog.DefaultFeatureFlagsJson(productPlan),
            BillingStatuses.Active,
            updatedCode,
            row.PaystackCustomerCode,
            cancellationToken);

        return new PlanChangeResult(true, productPlan, null);
    }

    private async Task<TenantBillingRow> RequirePaystackBillableAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var row = await _tenants.GetBillingRowAsync(tenantId, cancellationToken)
                  ?? throw new InvalidOperationException("tenant_not_found");

        if (InternalTenantBilling.BypassesPaystack(row.BillingStatus))
            throw new InvalidOperationException("billing_not_required");

        if (!_paystack.IsConfigured)
            throw new InvalidOperationException("paystack_not_configured");

        return row;
    }

    private string ResolvePaystackPlanCode(string productPlan) =>
        _options.ResolvePlanCode(PlanCatalog.PaystackPlanKeySuffix(productPlan));

    private string? MapPaystackPlanToProduct(string? paystackPlanCode)
    {
        if (string.IsNullOrWhiteSpace(paystackPlanCode))
            return null;

        if (string.Equals(paystackPlanCode, _options.PlanStarter, StringComparison.OrdinalIgnoreCase))
            return PlanCatalog.Starter;
        if (string.Equals(paystackPlanCode, _options.PlanGrowth, StringComparison.OrdinalIgnoreCase))
            return PlanCatalog.Growth;
        if (string.Equals(paystackPlanCode, _options.PlanScale, StringComparison.OrdinalIgnoreCase))
            return PlanCatalog.Scale;

        return null;
    }
}
