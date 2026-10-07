using System.Globalization;
using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// An amount in a currency, such as a service's price. A value object: two are equal when both
/// the amount and the currency are.
/// </summary>
/// <remarks>
/// Amounts have at most two decimals for every currency. That's wrong for the few without minor
/// units (JPY) or with three (BHD); fine for now, since prices are only shown, never charged.
/// </remarks>
sealed record Money
{
    public const decimal MaxAmount = 100_000m;

    // Every ISO 4217 code this machine knows a region for.
    static readonly HashSet<string> Currencies = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).ISOCurrencySymbol)
        .ToHashSet(StringComparer.Ordinal);

    Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);

    public decimal Amount { get; }

    /// <summary>The ISO 4217 code, such as PLN or EUR.</summary>
    public string Currency { get; }

    /// <exception cref="DomainValidationException">See <see cref="Create(decimal?, string?, DomainErrors)"/>.</exception>
    public static Money Create(decimal? amount, string? currency)
    {
        var errors = new DomainErrors();
        var money = Create(amount, currency, errors);
        errors.ThrowIfAny();
        return money!;
    }

    /// <summary>
    /// The money, or null after adding to <paramref name="errors"/> why it can't be: the amount is
    /// missing, negative, too big or has more than two decimals (under "price"), or the currency
    /// isn't an ISO 4217 code (under "currency"). For aggregates that check several things at once.
    /// </summary>
    public static Money? Create(decimal? amount, string? currency, DomainErrors errors)
    {
        var valid = true;
        if (amount is null)
        {
            errors.Add("price", "Enter a price; 0 if it's free.");
            valid = false;
        }
        else if (amount is < 0 or > MaxAmount)
        {
            errors.Add("price", $"Use an amount from 0 to {MaxAmount.ToString("N0", CultureInfo.InvariantCulture)}.");
            valid = false;
        }
        else if (decimal.Round(amount.Value, 2) != amount)
        {
            errors.Add("price", "Use at most two decimals.");
            valid = false;
        }
        if (currency is null || !IsCurrency(currency))
        {
            errors.Add("currency", "Choose a currency from the list.");
            valid = false;
        }
        return valid ? new Money(amount!.Value, currency!) : null;
    }


    public static bool IsCurrency(string code) => Currencies.Contains(code);
}
