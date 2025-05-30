using Microsoft.Extensions.Hosting;
using NBitcoin;
using Nito.AsyncEx;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Filter;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Stores;
using WalletWasabi.Wallets.BlockProvider;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Wallets;

/// <summary>
/// Service that keeps processing block filters.
/// </summary>
/// <seealso href="https://github.com/zkSNACKs/WalletWasabi/issues/10219">TurboSync specification.</seealso>
public class WalletFilterProcessor : BackgroundService
{
	/// <remarks>Guarded by <see cref="Lock"/>.</remarks>
	private FilterModel? _lastProcessedFilter;

	public WalletFilterProcessor(KeyManager keyManager, BitcoinStore bitcoinStore, TransactionProcessor transactionProcessor, BlockDownloadService blockDownloadService)
	{
		KeyManager = keyManager;
		BitcoinStore = bitcoinStore;
		TransactionProcessor = transactionProcessor;
		BlockDownloadService = blockDownloadService;

		FilterIterator = new BlockFilterIterator(BitcoinStore.IndexStore);
	}

	/// <remarks>Guarded by <see cref="Lock"/>.</remarks>
	private PriorityQueue<SyncRequest, Priority> SynchronizationRequests { get; } = new(Priority.Comparer);

	/// <remarks>Guards <see cref="SynchronizationRequests"/> and <see cref="_lastProcessedFilter"/>.</remarks>
	private object Lock { get; } = new();

	private SemaphoreSlim SynchronizationRequestsSemaphore { get; } = new(initialCount: 0);

	private KeyManager KeyManager { get; }
	private BitcoinStore BitcoinStore { get; }
	private TransactionProcessor TransactionProcessor { get; }
	private BlockDownloadService BlockDownloadService { get; }

	public FilterModel? LastProcessedFilter
	{
		get
		{
			lock (Lock)
			{
				return _lastProcessedFilter;
			}
		}
		set
		{
			lock (Lock)
			{
				_lastProcessedFilter = value;
			}
		}
	}

	/// <remarks>Internal only to allow modifications in tests.</remarks>
	internal BlockFilterIterator FilterIterator { get; }

	/// <summary>Make sure we don't process any request while a reorg is happening.</summary>
	private AsyncLock ReorgLock { get; } = new();

	public async Task ProcessAsync(CancellationToken cancellationToken)
	{
		SyncRequest request = new(new TaskCompletionSource());
		Priority priority = new();

		lock (Lock)
		{
			SynchronizationRequests.Enqueue(request, priority);
			SynchronizationRequestsSemaphore.Release(releaseCount: 1);
		}

		await request.Tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	/// <summary>Used for filter synchronization.</summary>
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await SynchronizationRequestsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

				SyncRequest? request;

				lock (Lock)
				{
					if (!SynchronizationRequests.TryPeek(out request, out _))
					{
						continue;
					}
				}

				try
				{
					bool reachedBlockChainTip;
					using (await ReorgLock.LockAsync(cancellationToken).ConfigureAwait(false))
					{
						Height lastHeight = KeyManager.GetBestHeight();

						if (lastHeight == BitcoinStore.SmartHeaderChain.TipHeight)
						{
							lock (Lock)
							{
								if (!request.Tcs.Task.IsCompleted)
								{
									request.Tcs.TrySetResult();
								}
								SynchronizationRequests.Dequeue();
							}
							continue;
						}

						uint currentHeight = (uint)lastHeight.Value + 1;

						FilterModel filter = await FilterIterator.GetAndRemoveAsync(currentHeight, cancellationToken).ConfigureAwait(false);
						var matchFound = await ProcessFilterModelAsync(filter, cancellationToken).ConfigureAwait(false);

						reachedBlockChainTip = currentHeight == BitcoinStore.SmartHeaderChain.TipHeight;
						bool storeToDisk = matchFound || reachedBlockChainTip;
						KeyManager.SetBestHeight(new Height(currentHeight), storeToDisk);
					}

					if (reachedBlockChainTip)
					{
						lock (Lock)
						{
							if (!request.Tcs.Task.IsCompleted)
							{
								request.Tcs.TrySetResult();
							}
							SynchronizationRequests.Dequeue();
						}
					}
					else
					{
						SynchronizationRequestsSemaphore.Release(1);
					}
				}
				catch (Exception ex)
				{
					if (!request.Tcs.TrySetException(ex))
					{
						Logger.LogWarning($"Tried to set exception but status was already {request.Tcs.Task.Status} ({ex}).");
					}

					throw;
				}

				// Clear all caches once not needed.
				if (SynchronizationRequestsSemaphore.CurrentCount == 0)
				{
					await FilterIterator.ClearAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}
		catch (OperationCanceledException)
		{
			Logger.LogDebug("Filter processor's execution was stopped.");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			TerminateService.Instance?.SignalGracefulCrash(ex);
			throw;
		}
		finally
		{
			lock (Lock)
			{
				while (SynchronizationRequests.TryDequeue(out var request, out _))
				{
					request.Tcs.TrySetCanceled(cancellationToken);
				}
			}
		}
	}

	/// <summary>
	/// Return the keys to test against the filter depending on the height of the filter and the type of synchronization.
	/// </summary>
	/// <param name="filterHeight">Height of the filter that needs to be tested.</param>
	/// <returns>Keys to test against this filter.</returns>
	private List<byte[]> GetScriptPubKeysToTest(Height filterHeight)
	{
		bool ScriptAlreadySpent(KeyManager.ScriptPubKeySpendingInfo spendingInfo) =>
			spendingInfo.LatestSpendingHeight is { } spendingHeight && spendingHeight < filterHeight;

		bool ScriptNotSpentAtTheMoment(KeyManager.ScriptPubKeySpendingInfo spendingInfo) =>
			!ScriptAlreadySpent(spendingInfo);

		var scriptsSpendingInfo = KeyManager.UnsafeGetSynchronizationInfos();
		var scriptPubKeyAccordingSyncType = scriptsSpendingInfo;

		return scriptPubKeyAccordingSyncType.Select(x => x.CompressedScriptPubKey).ToList();
	}

	private async Task<bool> ProcessFilterModelAsync(FilterModel filter, CancellationToken cancel)
	{
		var height = new Height(filter.Header.Height);
		var toTestKeys = GetScriptPubKeysToTest(height);

		var matchFound = false;
		if (toTestKeys.Any())
		{
			var compressedScriptPubKeys = toTestKeys;
			matchFound = FilterChecker.HasMatch(filter.Filter, filter.FilterKey, compressedScriptPubKeys);

			if (matchFound)
			{
				// Wait until downloaded.
				Block currentBlock = await KeepTryingToGetBlockAsync(filter.Header.BlockHash, new Priority(filter.Header.Height), cancel)
					.ConfigureAwait(false);

				var txsToProcess = new List<SmartTransaction>();
				for (int i = 0; i < currentBlock.Transactions.Count; i++)
				{
					Transaction tx = currentBlock.Transactions[i];
					txsToProcess.Add(new SmartTransaction(tx, height, currentBlock.GetHash(), i, firstSeen: currentBlock.Header.BlockTime, labels: BitcoinStore.MempoolService.TryGetLabel(tx.GetHash())));
				}

				TransactionProcessor.Process(txsToProcess);
			}
		}

		LastProcessedFilter = filter;
		return matchFound;
	}

	private async void ReorgedAsync(object? sender, FilterModel invalidFilter)
	{
		try
		{
			uint256 invalidBlockHash = invalidFilter.Header.BlockHash;
			uint newBestHeight = invalidFilter.Header.Height - 1;

			using (await ReorgLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
			{
				KeyManager.SetMaxBestHeight(new Height(newBestHeight));
				TransactionProcessor.UndoBlock((int)invalidFilter.Header.Height);
				BitcoinStore.TransactionStore.ReleaseToMempoolFromBlock(invalidBlockHash);
				await FilterIterator.RemoveNewerThanAsync(newBestHeight, CancellationToken.None).ConfigureAwait(false);
				await BlockDownloadService.RemoveBlocksAsync(invalidFilter.Header.Height).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex);
		}
	}

	/// <summary>
	/// Attempt to get the bitcoin block from a full node as a primary source of data, or use P2P as a fallback.
	/// </summary>
	private async Task<Block> KeepTryingToGetBlockAsync(uint256 blockHash, Priority priority, CancellationToken cancellationToken)
	{
		ISourceRequest[] sourceRequests = [TrustedFullNodeSourceRequest.Instance, P2pSourceRequest.Automatic];
		while (true)
		{
			foreach (ISourceRequest sourceRequest in sourceRequests)
			{
				BlockDownloadService.IResult result = await BlockDownloadService.TryGetBlockAsync(sourceRequest, blockHash, priority, cancellationToken)
					.ConfigureAwait(false);

				switch (result)
				{
					case BlockDownloadService.SuccessResult successFullNodeResult:
						return successFullNodeResult.Block;

					case BlockDownloadService.CanceledResult:
						throw new OperationCanceledException();
				}
			}
		}
	}

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		BitcoinStore.IndexStore.Reorged += ReorgedAsync;
		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		BitcoinStore.IndexStore.Reorged -= ReorgedAsync;
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	public override void Dispose()
	{
		SynchronizationRequestsSemaphore.Dispose();
		base.Dispose();
	}

	public record SyncRequest(TaskCompletionSource Tcs);
}
