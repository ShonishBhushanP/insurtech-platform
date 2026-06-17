namespace InsurTech.Claims.Domain.ValueObjects;

/// <summary>Money value object — major-unit decimal + ISO-4217 currency (API spec §2).</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Of(decimal amount, string currency = "INR")
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be non-negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO-4217 code.", nameof(currency));
        return new Money(decimal.Round(amount, 2), currency.ToUpperInvariant());
    }
}
