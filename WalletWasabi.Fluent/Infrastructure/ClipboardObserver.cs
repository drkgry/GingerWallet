using System.Reactive.Concurrency;
using System.Reactive.Linq;
using NBitcoin;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Helpers;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Fluent.Infrastructure;

internal class ClipboardObserver
{
	private readonly IObservable<Amount> _balances;

	public ClipboardObserver(IObservable<Amount> balances)
	{
		_balances = balances;
	}

	public IObservable<string?> ClipboardFiatContentChanged(IScheduler scheduler)
	{
		return ApplicationHelper.ClipboardTextChanged(scheduler)
			.CombineLatest(_balances.Select(x => x.Fiat).Switch(), ParseToFiat)
			.Select(money => money?.ToString("0.00"));
	}

	public IObservable<string?> ClipboardBtcContentChanged(IScheduler scheduler)
	{
		return ApplicationHelper.ClipboardTextChanged(scheduler)
			.CombineLatest(_balances.Select(x => x.Btc), ParseToMoney);
	}

	public static decimal? ParseToFiat(string? text)
	{
		if (text is null)
		{
			return null;
		}

		if (CurrencyInput.TryCorrectAmount(text, out var corrected))
		{
			text = corrected;
		}

		return decimal.TryParse(text, CurrencyInput.InvariantNumberFormat, out var n) ? n : (decimal?)default;
	}

	public static decimal? ParseToFiat(string? text, decimal balanceFiat)
	{
		return ParseToFiat(text)
			.Ensure(n => n <= balanceFiat)
			.Ensure(n => n >= 1)
			.Ensure(n => n.CountDecimalPlaces() <= 2);
	}

	public static Money? ParseToMoney(string? text)
	{
		if (text is null)
		{
			return null;
		}

		if (CurrencyInput.TryCorrectBitcoinAmount(text, out var corrected))
		{
			text = corrected;
		}

		return Money.TryParse(text, out var n) ? n : default;
	}

	public static string? ParseToMoney(string? text, Money balance)
	{
		// Ignore paste if there are invalid characters
		if (text is null || !CurrencyInput.RegexValidCharsOnly().IsMatch(text))
		{
			return null;
		}

		if (CurrencyInput.TryCorrectBitcoinAmount(text, out var corrected))
		{
			text = corrected;
		}

		var money = ParseToMoney(text).Ensure(m => m <= balance);
		return money?.ToDecimal(MoneyUnit.BTC).FormattedBtcExactFractional(text);
	}
}
