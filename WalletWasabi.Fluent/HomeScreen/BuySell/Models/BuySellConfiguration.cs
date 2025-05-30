namespace WalletWasabi.Fluent.HomeScreen.BuySell.Models;

public record BuySellConfiguration(
	CountrySelection? BuyCountry = null,
	CountrySelection? SellCountry = null,
	CurrencyModel? BuyCurrency = null,
	CurrencyModel? SellCurrency = null,
	string? TestingAddress = null,
	string? BuyPaymentMethod = null);
