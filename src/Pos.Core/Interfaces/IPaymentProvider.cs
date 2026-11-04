namespace Pos.Core.Interfaces;

public interface IPaymentProvider
{
    string Method { get; }
    Task<PaymentOutcome> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}

public sealed record PaymentRequest(decimal Amount, string? CustomerPhone, string? Reference);

public sealed record PaymentOutcome(bool Success, string Status, string? TransactionRef, string? FailureReason);
