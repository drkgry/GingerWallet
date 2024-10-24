using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Lang;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.CoinJoinProfiles;

internal class PrivateCoinJoinProfileViewModel : CoinJoinProfileViewModelBase
{
	// https://github.com/zkSNACKs/WalletWasabi/pull/10468#issuecomment-1506284198
	public const int MinAnonScore = 27;

	public const int MaxAnonScore = 76;

	public PrivateCoinJoinProfileViewModel(int anonScoreTarget)
	{
		AnonScoreTarget = anonScoreTarget;
	}

	public PrivateCoinJoinProfileViewModel()
	{
		AnonScoreTarget = GetRandom(MinAnonScore, MaxAnonScore);
	}

	public override string Title => Resources.PrivateCoinJoinProfileTitle;

	public override string Description => Resources.PrivateCoinJoinProfileDescription;

	public override int AnonScoreTarget { get; }

	public override int SafeMiningFeeRate => 30;

	public override bool RedCoinIsolation { get; } = true;

	public override CoinjoinSkipFactors SkipFactors { get; } = CoinjoinSkipFactors.PrivacyMaximizing;

	public override int FeeRateMedianTimeFrameHours => 0;

	private static int GetRandom(int minInclusive, int maxExclusive)
	{
		return SecureRandom.Instance.GetInt(minInclusive, maxExclusive);
	}

	public override bool Equals(object? obj)
	{
		if (ReferenceEquals(this, obj))
		{
			return true;
		}

		if (obj is not CoinJoinProfileViewModelBase profile)
		{
			return false;
		}

		return profile.AnonScoreTarget < MaxAnonScore
			&& profile.AnonScoreTarget >= MinAnonScore
			&& profile.SafeMiningFeeRate == SafeMiningFeeRate
			&& profile.FeeRateMedianTimeFrameHours == FeeRateMedianTimeFrameHours
			&& profile.RedCoinIsolation == RedCoinIsolation
			&& profile.SkipFactors == SkipFactors;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(AnonScoreTarget, SafeMiningFeeRate, FeeRateMedianTimeFrameHours, RedCoinIsolation, SkipFactors);
	}
}
