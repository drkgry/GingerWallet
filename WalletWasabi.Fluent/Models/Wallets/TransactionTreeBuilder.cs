using DynamicData;
using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Lang;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

public class TransactionTreeBuilder
{
	private readonly Wallet _wallet;

	public TransactionTreeBuilder(Wallet wallet)
	{
		_wallet = wallet;
	}

	public IEnumerable<TransactionModel> Build(List<TransactionSummary> summaries)
	{
		TransactionModel? coinJoinGroup = default;

		var result = new List<TransactionModel>();

		for (var i = 0; i < summaries.Count; i++)
		{
			var item = summaries[i];

			if (!item.IsCoinjoin())
			{
				AddCoinjoinOrGroup(result, ref coinJoinGroup);
				result.Add(CreateRegular(i, item));
			}
			else
			{
				coinJoinGroup ??= CreateCoinjoinGroup(i, item);
				coinJoinGroup.Add(CreateCoinjoinTransaction(i, item));
			}
		}
		AddCoinjoinOrGroup(result, ref coinJoinGroup);

		// This second iteration is necessary to transform the flat list of speed-ups into actual groups.
		// Here are the steps:
		// 1. Identify which transactions are CPFP (parents) and their children.
		// 2. Create a speed-up group with parent and children.
		// 3. Remove the previously added items from the history (they should no longer be there, but in the group).
		// 4. Add the group.
		foreach (var summary in summaries)
		{
			if (summary.Transaction.IsCPFPd)
			{
				// Group creation.
				var childrenTxs = summary.Transaction.ChildrenPayForThisTx;

				if (!TryFindHistoryItem(summary.GetHash(), result, out var parent))
				{
					continue; // If the parent transaction is not found, continue with the next summary.
				}

				var groupItems = new List<TransactionModel> { parent };
				foreach (var childTx in childrenTxs)
				{
					if (TryFindHistoryItem(childTx.GetHash(), result, out var child))
					{
						groupItems.Add(child);
					}
				}

				// If there is only one item in the group, it's not a group.
				// This can happen, for example, when CPFP occurs between user-owned wallets.
				if (groupItems.Count <= 1)
				{
					continue;
				}

				var speedUpGroup = CreateSpeedUpGroup(summary, parent, groupItems);
				result.Add(speedUpGroup);

				// Remove the items.
				result.RemoveMany(groupItems);
			}
		}

		return result;
	}

	private void AddCoinjoinOrGroup(List<TransactionModel> list, ref TransactionModel? group)
	{
		if (group is not null)
		{
			if (group.Children.Count == 1)
			{
				list.Add(group.Children[0]);
			}
			else
			{
				UpdateCoinjoinGroup(group);
				list.Add(group);
			}
			group = default;
		}
	}

	private bool TryFindHistoryItem(uint256 txid, IEnumerable<TransactionModel> history, [NotNullWhen(true)] out TransactionModel? found)
	{
		found = history.SingleOrDefault(x => x.Id == txid);
		return found is not null;
	}

	private TransactionModel CreateRegular(int index, TransactionSummary transactionSummary)
	{
		var itemType = GetItemType(transactionSummary);
		var date = transactionSummary.FirstSeen.ToLocalTime();
		var confirmations = transactionSummary.GetConfirmations();
		var status = GetItemStatus(transactionSummary);

		return new TransactionModel
		{
			Id = transactionSummary.GetHash(),
			Amount = transactionSummary.Amount,
			HasBeenSpedUp = transactionSummary.IsSpeedup,
			OrderIndex = index,
			Labels = transactionSummary.Labels,
			Date = date,
			DateString = date.ToUserFacingFriendlyString(),
			DateToolTipString = date.ToUserFacingString(),
			CanCancelTransaction = transactionSummary.Transaction.IsCancellable(_wallet.KeyManager),
			CanSpeedUpTransaction = transactionSummary.Transaction.IsSpeedupable(_wallet.KeyManager),
			Type = itemType,
			Status = status,
			Confirmations = confirmations,
			BlockHeight = transactionSummary.Height.Type == HeightType.Chain ? transactionSummary.Height.Value : 0,
			BlockHash = transactionSummary.BlockHash,
			Fee = transactionSummary.GetFee(),
			FeeRate = transactionSummary.FeeRate(),
			ConfirmedTooltip = GetConfirmationToolTip(status, confirmations, transactionSummary.Transaction),
		};
	}

	private TransactionModel CreateCoinjoinGroup(int index, TransactionSummary transactionSummary)
	{
		var date = transactionSummary.FirstSeen.ToLocalTime();
		var confirmations = transactionSummary.GetConfirmations();
		var status = GetItemStatus(transactionSummary);

		return new TransactionModel
		{
			Amount = Money.Zero,
			Labels = transactionSummary.Labels,
			Confirmations = confirmations,
			ConfirmedTooltip = GetConfirmationToolTip(status, confirmations, transactionSummary.Transaction),
			Id = transactionSummary.GetHash(),
			Date = date,
			DateString = date.ToUserFacingFriendlyString(),
			DateToolTipString = date.ToUserFacingString(),
			OrderIndex = index,
			Type = TransactionType.CoinjoinGroup,
			Status = status,
		};
	}

	private TransactionModel CreateSpeedUpGroup(TransactionSummary transactionSummary, TransactionModel parent, IEnumerable<TransactionModel> children)
	{
		var isConfirmed = children.All(x => x.IsConfirmed);

		var result = new TransactionModel
		{
			Id = transactionSummary.GetHash(),
			Amount = parent.Amount,
			OrderIndex = parent.OrderIndex,
			Date = parent.Date.ToLocalTime(),
			DateString = parent.DateString,
			DateToolTipString = parent.DateToolTipString,
			Confirmations = parent.Confirmations,
			BlockHeight = parent.BlockHeight,
			BlockHash = parent.BlockHash,
			ConfirmedTooltip = parent.ConfirmedTooltip,
			Labels = parent.Labels,
			CanCancelTransaction = transactionSummary.Transaction.IsCancellable(_wallet.KeyManager),
			CanSpeedUpTransaction = transactionSummary.Transaction.IsSpeedupable(_wallet.KeyManager),

			Type = GetItemType(transactionSummary),
			Status =
				isConfirmed
				? TransactionStatus.Confirmed
				: TransactionStatus.Pending,
			HasBeenSpedUp = true,
		};

		var dates = children.Select(tx => tx.Date).ToImmutableArray();
		var firstDate = dates.Min().ToLocalTime();
		var lastDate = dates.Max().ToLocalTime();
		if (firstDate.Day == lastDate.Day)
		{
			foreach (var child in children)
			{
				child.DateString = child.Date.ToLocalTime().ToOnlyTimeString();
			}
		}

		foreach (var child in children)
		{
			result.Add(child);
			child.IsChild = true;
		}

		return result;
	}

	private void UpdateCoinjoinGroup(TransactionModel coinjoinGroup)
	{
		foreach (var child in coinjoinGroup.Children)
		{
			child.IsChild = true;
		}

		var isConfirmed = coinjoinGroup.Children.All(x => x.IsConfirmed);
		coinjoinGroup.Status =
			isConfirmed
			? TransactionStatus.Confirmed
			: TransactionStatus.Pending;

		coinjoinGroup.ConfirmedTooltip = coinjoinGroup.Children.MinBy(x => x.Confirmations)?.ConfirmedTooltip ?? "";
		coinjoinGroup.Date = coinjoinGroup.Children.Select(tx => tx.Date).Max().ToLocalTime();

		var amount = coinjoinGroup.Children.Sum(x => x.Amount);
		coinjoinGroup.Amount = amount;

		var fee = coinjoinGroup.Children.Sum(x => x.Fee ?? Money.Zero);
		coinjoinGroup.Fee = fee;

		var dates = coinjoinGroup.Children.Select(tx => tx.Date).ToImmutableArray();
		var firstDate = dates.Min().ToLocalTime();
		var lastDate = dates.Max().ToLocalTime();

		coinjoinGroup.DateString = lastDate.ToUserFacingFriendlyString();

		if (firstDate.Day == lastDate.Day)
		{
			coinjoinGroup.DateToolTipString = $"{firstDate.ToUserFacingString(withTime: false)}";

			foreach (var child in coinjoinGroup.Children)
			{
				child.DateString = child.Date.ToLocalTime().ToOnlyTimeString();
			}
		}
		else
		{
			coinjoinGroup.DateToolTipString = $"{firstDate.ToUserFacingString(withTime: true)} - {lastDate.ToUserFacingString(withTime: true)}";
		}
	}

	private TransactionModel CreateCoinjoinTransaction(int index, TransactionSummary transactionSummary)
	{
		var date = transactionSummary.FirstSeen.ToLocalTime();
		var confirmations = transactionSummary.GetConfirmations();
		var status = GetItemStatus(transactionSummary);

		return new TransactionModel
		{
			Id = transactionSummary.GetHash(),
			Amount = transactionSummary.Amount,
			OrderIndex = index,
			Date = date,
			DateString = date.ToUserFacingFriendlyString(),
			DateToolTipString = date.ToUserFacingString(),
			Labels = transactionSummary.Labels,
			Type = TransactionType.Coinjoin,
			Status = status,
			Confirmations = confirmations,
			BlockHeight = transactionSummary.Height.Type == HeightType.Chain ? transactionSummary.Height.Value : 0,
			BlockHash = transactionSummary.BlockHash,
			ConfirmedTooltip = GetConfirmationToolTip(status, confirmations, transactionSummary.Transaction),
			Fee = transactionSummary.GetFee(),
			FeeRate = transactionSummary.FeeRate(),
			ShowWarning = transactionSummary.ClientInputRatio > 0.5
		};
	}

	private TransactionType GetItemType(TransactionSummary transactionSummary)
	{
		var transaction = transactionSummary.Transaction.Transaction;
		int inputCount = transactionSummary.InputCoinCount;
		int outputCount = transactionSummary.OutputCoinCount;

		// 0: Zero, 1: Some, 2: All coins are ours.
		int inputType = inputCount == 0 ? 0 : (inputCount < transaction.Inputs.Count ? 1 : 2);
		int outputType = outputCount == 0 ? 0 : (outputCount < transaction.Outputs.Count ? 1 : 2);

		if (!transactionSummary.IsCPFP && !transactionSummary.IsCancellation && inputType == 2 && outputType == 2)
		{
			return TransactionType.SelfTransferTransaction;
		}

		if (!transactionSummary.IsCPFP && inputType == 0 && outputType > 0)
		{
			return TransactionType.IncomingTransaction;
		}

		if (!transactionSummary.IsCPFP && !transactionSummary.IsCancellation && inputType == 2 && outputType <= 1)
		{
			return TransactionType.OutgoingTransaction;
		}

		if (transactionSummary.IsCancellation)
		{
			return TransactionType.Cancellation;
		}

		if (transactionSummary.IsCPFP)
		{
			return TransactionType.CPFP;
		}

		return TransactionType.Unknown;
	}

	private static TransactionStatus GetItemStatus(TransactionSummary transactionSummary)
	{
		return transactionSummary.IsConfirmed() ? TransactionStatus.Confirmed : TransactionStatus.Pending;
	}

	private string GetConfirmationToolTip(TransactionStatus status, int confirmations, SmartTransaction smartTransaction)
	{
		if (status == TransactionStatus.Confirmed)
		{
			return TextHelpers.GetConfirmationText(confirmations);
		}

		var friendlyString = TransactionFeeHelper.TryEstimateConfirmationTime(_wallet.FeeProvider, _wallet.Network, smartTransaction, _wallet.UnconfirmedTransactionChainProvider, out var estimate)
			? TextHelpers.TimeSpanToFriendlyString(estimate.Value)
			: "";

		return (status, friendlyString != "") switch
		{
			(TransactionStatus.Pending, true) => Resources.PendingConfirmingIn.SafeInject(friendlyString),
			(TransactionStatus.Pending, false) => Resources.Pending,
			_ => Resources.Unknown
		};
	}
}
