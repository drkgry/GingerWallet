using System.Collections.Generic;
using NBitcoin;
using WalletWasabi.Helpers;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client;

public class IsRoundEconomicTests
{
	[Fact]
	public void IsRoundEconomicTest()
	{
		var dailyTimeFrame = TimeSpan.FromHours(Constants.CoinJoinFeeRateMedianTimeFrames[0]);
		var weeklyTimeFrame = TimeSpan.FromHours(Constants.CoinJoinFeeRateMedianTimeFrames[1]);
		var monthlyTimeFrame = TimeSpan.FromHours(Constants.CoinJoinFeeRateMedianTimeFrames[2]);

		// InvalidOperationException when target TimeFrame is not a key of the coinJoinFeeRateMedians Dictionary.
		Assert.Throws<InvalidOperationException>(() =>
			CoinJoinClient.IsRoundEconomic(new FeeRate(2m), new(), 0, dailyTimeFrame));

		// Shorter TimeFrame has lower FeeRate.
		var coinJoinFeeRateMedians = new Dictionary<TimeSpan, FeeRate>
		{
			{ dailyTimeFrame, new FeeRate(500m) },
			{ weeklyTimeFrame, new FeeRate(50m) },
			{ monthlyTimeFrame, new FeeRate(5m) }
		};

		Assert.True(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, dailyTimeFrame));
		Assert.True(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, weeklyTimeFrame));
		Assert.False(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, monthlyTimeFrame));

		// Longer TimeFrame has lower FeeRate.
		coinJoinFeeRateMedians = new Dictionary<TimeSpan, FeeRate>
		{
			{ dailyTimeFrame, new FeeRate(5m) },
			{ weeklyTimeFrame, new FeeRate(50m) },
			{ monthlyTimeFrame, new FeeRate(500m) }
		};

		Assert.False(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, dailyTimeFrame));
		Assert.False(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, weeklyTimeFrame));
		Assert.False(CoinJoinClient.IsRoundEconomic(new FeeRate(25m), coinJoinFeeRateMedians, 0, monthlyTimeFrame));
		Assert.True(CoinJoinClient.IsRoundEconomic(new FeeRate(5m), coinJoinFeeRateMedians, 0, monthlyTimeFrame));
	}
}
