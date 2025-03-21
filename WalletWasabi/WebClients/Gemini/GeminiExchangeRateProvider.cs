using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GingerCommon.Logging;
using WalletWasabi.Backend.Models;
using WalletWasabi.Interfaces;
using WalletWasabi.Tor.Http.Extensions;

namespace WalletWasabi.WebClients.Gemini;

public class GeminiExchangeRateProvider : IExchangeRateProvider
{
	public async Task<IEnumerable<ExchangeRate>> GetExchangeRateAsync(CancellationToken cancellationToken)
	{
		// Only used by the Backend.
#pragma warning disable RS0030 // Do not use banned APIs
		using var httpClient = new HttpClient
		{
			BaseAddress = new Uri("https://api.gemini.com")
		};
#pragma warning restore RS0030 // Do not use banned APIs

		var exchangeRates = new List<ExchangeRate>();

		var currenciesToFetch = new[]
		{
			"usd",
			"eur",
		};

		foreach (var currency in currenciesToFetch)
		{
			using var response = await httpClient.GetAsync($"/v1/pubticker/btc{currency}", cancellationToken).ConfigureAwait(false);
			using var content = response.Content;
			var data = await content.ReadAsJsonAsync<GeminiExchangeRateInfo>().ConfigureAwait(false);

			exchangeRates.Add(new ExchangeRate { Rate = data.Bid, Ticker = currency.ToUpper(CultureInfo.InvariantCulture) });
		}

		return exchangeRates;
	}

	private class GeminiExchangeRateInfo
	{
		public decimal Bid { get; set; }
	}
}
