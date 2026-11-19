namespace TinaClass.Contracts.Common;

/// <summary>A page of a list: <c>?page=&amp;pageSize=</c> (default 30, at most 100).</summary>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Amounts are whole minor units (fen or cents) so that every database stores them exactly; prices are per million tokens.</summary>
public static class Money
{
    /// <summary>The currency symbol of the template's <c>currency</c> option (<c>¥</c> for CNY, <c>$</c> for USD); set once at start by <see cref="Configure"/>.</summary>
    public static string Symbol { get; private set; } = "¥";

    /// <summary>The ISO code of the currency: <c>CNY</c> or <c>USD</c>.</summary>
    public static string Code { get; private set; } = "CNY";

    /// <summary>Picks the currency from the settings (<c>app.json</c> <c>currency</c>, the API's <c>App:Currency</c>); anything but USD is CNY.</summary>
    public static void Configure(string? code)
    {
        var usd = string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase);
        Code = usd ? "USD" : "CNY";
        Symbol = usd ? "$" : "¥";
    }

    public static string Format(long minor, int decimals = 2)
    {
        var value = minor / 100m;
        return Symbol + value.ToString(decimals == 2 ? "#,##0.00" : "#,##0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>The price of a request: minor units per million tokens times the tokens, rounded to a whole minor unit (half away from zero).</summary>
public static class Pricing
{
    public static long Cost(long priceIn, long priceOut, long priceCache, long tokensIn, long tokensOut, long tokensCache) =>
        (long)Math.Round(((priceIn * (decimal)tokensIn) + (priceOut * (decimal)tokensOut) + (priceCache * (decimal)tokensCache)) / 1_000_000m, MidpointRounding.AwayFromZero);
}
