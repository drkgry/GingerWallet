using NBitcoin;
using System.Collections.Generic;

namespace WalletWasabi.WabiSabi.Recommendation;

public interface IDenominationFactory
{
	public List<Money> StandardDenominations { get; }

	public List<Money> CreatePreferedDenominations(IEnumerable<Money> inputEffectiveValues, FeeRate miningFee);
}
