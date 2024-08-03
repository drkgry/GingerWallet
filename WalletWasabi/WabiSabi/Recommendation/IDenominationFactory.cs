using NBitcoin;
using System.Collections.Generic;

namespace WalletWasabi.WabiSabi.Recommendation;

public interface IDenominationFactory
{
	public List<Money> StandardDenominations { get; }

	public List<Money> CreatePreferedDenominations(List<Money> inputEffectiveValues, FeeRate miningFee);
}
