using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using Nito.AsyncEx;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Announcer;
using WalletWasabi.BitcoinCore;
using WalletWasabi.BitcoinCore.Endpointing;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.BitcoinCore.Monitoring;
using WalletWasabi.BitcoinP2p;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.CoinJoin.Client;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Rpc;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Stores;
using WalletWasabi.Tor;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.Tor.StatusChecker;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Client.Banning;
using WalletWasabi.WabiSabi.Client.RoundStateAwaiters;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using WalletWasabi.Wallets.FilterProcessor;
using WalletWasabi.Models;
using WalletWasabi.Daemon.BuySell;
using WalletWasabi.Daemon.FeeRateProviders;

namespace WalletWasabi.Daemon;

public class Global
{
	/// <remarks>Use this variable as a guard to prevent touching <see cref="StoppingCts"/> that might have already been disposed.</remarks>
	private volatile bool _disposeRequested;

	public Global(string dataDir, string configFilePath, Config config)
	{
		DataDir = dataDir;
		ConfigFilePath = configFilePath;
		Config = config;
		TorSettings = new TorSettings(
			DataDir,
			distributionFolderPath: EnvironmentHelpers.GetFullBaseDirectory(),
			terminateOnExit: Config.TerminateTorOnExit,
			torMode: Config.UseTor,
			socksPort: config.TorSocksPort,
			controlPort: config.TorControlPort,
			torFolder: config.TorFolder,
			bridges: config.TorBridges,
			owningProcessId: Environment.ProcessId,
			log: Config.LogModes.Contains(LogMode.File));

		HostedServices = new HostedServices();

		var networkWorkFolderPath = Path.Combine(DataDir, "BitcoinStore", Network.ToString());
		AllTransactionStore = new AllTransactionStore(networkWorkFolderPath, Network);
		SmartHeaderChain smartHeaderChain = new(maxChainSize: 20_000);
		IndexStore = new IndexStore(Path.Combine(networkWorkFolderPath, "IndexStore"), Network, smartHeaderChain);
		var mempoolService = new MempoolService();
		var blocks = new FileSystemBlockRepository(Path.Combine(networkWorkFolderPath, "Blocks"), Network, config.PersistentConfig.MaxBlockRepositorySize);

		BitcoinStore = new BitcoinStore(IndexStore, AllTransactionStore, mempoolService, smartHeaderChain, blocks);
		HttpClientFactory = BuildHttpClientFactory(() => Config.GetBackendUri());
		CoordinatorHttpClientFactory = BuildHttpClientFactory(() => Config.GetCoordinatorUri());

		TimeSpan requestInterval = Network == Network.RegTest ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
		int maxFiltersToSync = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together

		HostedServices.Register<ExchangeRateService>(() => new ExchangeRateService(TimeSpan.FromSeconds(5), HttpClientFactory, config.ExchangeCurrency), "ExchangeRate Service");

		HostedServices.Register<WasabiSynchronizer>(() => new WasabiSynchronizer(requestInterval, maxFiltersToSync, BitcoinStore, HttpClientFactory), "Wasabi Synchronizer");
		WasabiSynchronizer wasabiSynchronizer = HostedServices.Get<WasabiSynchronizer>();

		HostedServices.Register<UpdateChecker>(() => new UpdateChecker(TimeSpan.FromHours(1), wasabiSynchronizer), "Software Update Checker");
		UpdateChecker updateChecker = HostedServices.Get<UpdateChecker>();

		LegalChecker = new(DataDir, updateChecker);
		UpdateManager = new(DataDir, Config.DownloadNewVersion, HttpClientFactory.NewHttpClient(Mode.DefaultCircuit, maximumRedirects: 10), updateChecker);
		TorStatusChecker = new TorStatusChecker(TimeSpan.FromHours(6), HttpClientFactory.NewHttpClient(Mode.DefaultCircuit), new XmlIssueListParser());

		RoundStateUpdaterCircuit = new PersonCircuit();

		Cache = new MemoryCache(new MemoryCacheOptions
		{
			SizeLimit = 1_000,
			ExpirationScanFrequency = TimeSpan.FromSeconds(30)
		});

		// Register P2P network.
		HostedServices.Register<P2pNetwork>(
			() =>
			{
				var p2p = new P2pNetwork(
						Network,
						Config.GetBitcoinP2pEndPoint(),
						Config.UseTor != TorMode.Disabled ? TorSettings.SocksEndpoint : null,
						Path.Combine(DataDir, "BitcoinP2pNetwork"),
						BitcoinStore);
				if (!Config.BlockOnlyMode)
				{
					p2p.Nodes.NodeConnectionParameters.TemplateBehaviors.Add(BitcoinStore.CreateUntrustedP2pBehavior());
				}

				return p2p;
			},
			friendlyName: "Bitcoin P2P Network");

		FeeRateProvider = new FeeRateProvider(HttpClientFactory, Config.FeeRateEstimationProvider, Network);

		// Block providers.
		SpecificNodeBlockProvider = new SpecificNodeBlockProvider(Network, Config.ServiceConfiguration, HttpClientFactory.TorEndpoint);
		P2PNodesManager = new P2PNodesManager(Network, HostedServices.Get<P2pNetwork>().Nodes, HttpClientFactory.IsTorEnabled);

		IBlockProvider[] trustedFullNodeBlockProviders = BitcoinCoreNode?.RpcClient is null
			? [SpecificNodeBlockProvider]
			: [new RpcBlockProvider(BitcoinCoreNode.RpcClient), SpecificNodeBlockProvider];

		BlockDownloadService = new BlockDownloadService(
			fileSystemBlockRepository: BitcoinStore.BlockRepository,
			trustedFullNodeBlockProviders: trustedFullNodeBlockProviders,
			new P2PBlockProvider(P2PNodesManager));

		HostedServices.Register<UnconfirmedTransactionChainProvider>(() => new UnconfirmedTransactionChainProvider(HttpClientFactory), friendlyName: "Unconfirmed Transaction Chain Provider");
		WalletFactory walletFactory = new(DataDir, config.Network, BitcoinStore, wasabiSynchronizer, config.ServiceConfiguration, FeeRateProvider, BlockDownloadService, HostedServices.Get<UnconfirmedTransactionChainProvider>());

		WalletDirectories walletDirectories = new(Config.Network, DataDir);
		TwoFactorAuthenticationService = new TwoFactorAuthenticationService(walletDirectories, HttpClientFactory.SharedWasabiClient);
		WalletManager = new WalletManager(config.Network, DataDir, walletDirectories, walletFactory, TwoFactorAuthenticationService);
		TransactionBroadcaster = new TransactionBroadcaster(Network, BitcoinStore, HttpClientFactory, WalletManager);
		BuysellClient = new BuySellClient(HttpClientFactory);

		CoinPrison = CoinPrison.CreateOrLoadFromFile(DataDir);
		WalletManager.WalletStateChanged += WalletManager_WalletStateChanged;
	}

	public const string ThemeBackgroundBrushResourceKey = "ThemeBackgroundBrush";
	public const string ApplicationAccentForegroundBrushResourceKey = "ApplicationAccentForegroundBrush";

	/// <summary>Lock that makes sure the application initialization and dispose methods do not run concurrently.</summary>
	private AsyncLock InitializationAsyncLock { get; } = new();

	/// <summary>Cancellation token to cancel <see cref="InitializeNoWalletAsync(TerminateService)"/> processing.</summary>
	private CancellationTokenSource StoppingCts { get; } = new();

	public string DataDir { get; }
	public TorSettings TorSettings { get; }
	public BitcoinStore BitcoinStore { get; }

	/// <summary>HTTP client factory for sending HTTP requests.</summary>
	public WasabiHttpClientFactory HttpClientFactory { get; }

	public WasabiHttpClientFactory CoordinatorHttpClientFactory { get; }

	public LegalChecker LegalChecker { get; private set; }
	public string ConfigFilePath { get; }
	public Config Config { get; }
	public WalletManager WalletManager { get; }
	public TransactionBroadcaster TransactionBroadcaster { get; set; }
	public CoinJoinProcessor? CoinJoinProcessor { get; set; }
	private SpecificNodeBlockProvider SpecificNodeBlockProvider { get; }
	private BlockDownloadService BlockDownloadService { get; }
	private P2PNodesManager P2PNodesManager { get; }
	private TorProcessManager? TorManager { get; set; }
	public CoreNode? BitcoinCoreNode { get; private set; }
	public TorStatusChecker TorStatusChecker { get; set; }
	public UpdateManager UpdateManager { get; }
	public HostedServices HostedServices { get; }

	public Network Network => Config.Network;

	public IMemoryCache Cache { get; private set; }
	public CoinPrison CoinPrison { get; }
	public JsonRpcServer? RpcServer { get; private set; }

	public Uri? OnionServiceUri { get; private set; }

	private PersonCircuit RoundStateUpdaterCircuit { get; }
	private AllTransactionStore AllTransactionStore { get; }
	private IndexStore IndexStore { get; }

	public TwoFactorAuthenticationService TwoFactorAuthenticationService { get; }

	private BuySellClient BuysellClient { get; }
	public FeeRateProvider FeeRateProvider { get; }

	private WasabiHttpClientFactory BuildHttpClientFactory(Func<Uri> backendUriGetter) =>
		new(
			Config.UseTor != TorMode.Disabled ? TorSettings.SocksEndpoint : null,
			backendUriGetter,
			torControlAvailable: Config.UseTor == TorMode.Enabled);

	public async Task InitializeNoWalletAsync(bool initializeSleepInhibitor, TerminateService terminateService, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StoppingCts.Token);
		CancellationToken cancel = linkedCts.Token;

		// StoppingCts may be disposed at this point, so do not forward the cancellation token here.
		using (await InitializationAsyncLock.LockAsync(cancellationToken))
		{
			Logger.LogTrace("Initialization started.");

			if (_disposeRequested)
			{
				return;
			}

			try
			{
				var bitcoinStoreInitTask = BitcoinStore.InitializeAsync(cancel);

				await LegalChecker.InitializeAsync().ConfigureAwait(false);

				cancel.ThrowIfCancellationRequested();

				await StartTorProcessManagerAsync(cancel).ConfigureAwait(false);

				try
				{
					await bitcoinStoreInitTask.ConfigureAwait(false);

					// Make sure that the height of the wallets will not be better than the current height of the filters.
					WalletManager.SetMaxBestHeight(BitcoinStore.SmartHeaderChain.TipHeight);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// If our internal data structures in the Bitcoin Store gets corrupted, then it's better to rescan all the wallets.
					WalletManager.SetMaxBestHeight(SmartHeader.GetStartingHeader(Network, IndexType.SegwitTaproot).Height);
					throw;
				}

				await StartLocalBitcoinNodeAsync(cancel).ConfigureAwait(false);

				var rpcFeeProvider = BitcoinCoreNode is null ? null : new RpcFeeRateProvider(BitcoinCoreNode.RpcClient);
				FeeRateProvider.Initialize(rpcFeeProvider);

				await BlockDownloadService.StartAsync(cancel).ConfigureAwait(false);

				RegisterCoinJoinComponents();

				if (initializeSleepInhibitor)
				{
					await CreateSleepInhibitorAsync().ConfigureAwait(false);
				}

				HostedServices.Register<AnnouncementManager>(() => new AnnouncementManager(DataDir, TimeSpan.FromMinutes(.5), (DisplayLanguage)Config.Language, Config.PersistentConfig.ExtraNostrPubKey, HttpClientFactory), nameof(AnnouncementManager));

				HostedServices.Register<BuySellManager>(() => new BuySellManager(BuysellClient, WalletManager, TimeSpan.FromSeconds(10)), nameof(BuySellManager));

				await HostedServices.StartAllAsync(cancel).ConfigureAwait(false);

				Logger.LogInfo("Start synchronizing filters...");

				TransactionBroadcaster.Initialize(HostedServices.Get<P2pNetwork>().Nodes, BitcoinCoreNode?.RpcClient);
				CoinJoinProcessor = new CoinJoinProcessor(Network, HostedServices.Get<WasabiSynchronizer>(), WalletManager, BitcoinCoreNode?.RpcClient);

				await StartRpcServerAsync(terminateService, cancel).ConfigureAwait(false);

				WalletManager.Initialize();
			}
			finally
			{
				Logger.LogTrace("Initialization finished.");
			}
		}
	}

	private async Task CreateSleepInhibitorAsync()
	{
		SleepInhibitor? sleepInhibitor = await SleepInhibitor.CreateAsync(HostedServices.Get<CoinJoinManager>()).ConfigureAwait(false);

		if (sleepInhibitor is not null)
		{
			HostedServices.Register<SleepInhibitor>(() => sleepInhibitor, "Sleep Inhibitor");
		}
		else
		{
			Logger.LogInfo("Sleep Inhibitor is not available on this platform.");
		}
	}

	private async Task StartRpcServerAsync(TerminateService terminateService, CancellationToken cancel)
	{
		// HttpListener doesn't support onion services as prefix and for that reason we have no alternative
		// other than using
		var prefixes = OnionServiceUri is { }
			? Config.JsonRpcServerPrefixes.Append($"http://+:37129/").ToArray()
			: Config.JsonRpcServerPrefixes;

		var jsonRpcServerConfig = new JsonRpcServerConfiguration(Config.JsonRpcServerEnabled, Config.JsonRpcUser, Config.JsonRpcPassword, prefixes);
		if (jsonRpcServerConfig.IsEnabled)
		{
			var wasabiJsonRpcService = new Rpc.WasabiJsonRpcService(global: this);
			RpcServer = new JsonRpcServer(wasabiJsonRpcService, jsonRpcServerConfig, terminateService);
			try
			{
				await RpcServer.StartAsync(cancel).ConfigureAwait(false);
			}
			catch (HttpListenerException e)
			{
				Logger.LogWarning($"Failed to start {nameof(JsonRpcServer)} with error: {e.Message}.");
				RpcServer = null;
			}
		}
	}

	private async Task StartTorProcessManagerAsync(CancellationToken cancellationToken)
	{
		if (Config.UseTor != TorMode.Disabled)
		{
			TorManager = new TorProcessManager(TorSettings);
			await TorManager.StartAsync(attempts: 3, cancellationToken).ConfigureAwait(false);
			Logger.LogInfo($"{nameof(TorProcessManager)} is initialized.");

			var (_, torControlClient) = await TorManager.WaitForNextAttemptAsync(cancellationToken).ConfigureAwait(false);
			if (Config is { JsonRpcServerEnabled: true, RpcOnionEnabled: true } && torControlClient is { } nonNullTorControlClient)
			{
				var anonymousAccessAllowed = string.IsNullOrEmpty(Config.JsonRpcUser) || string.IsNullOrEmpty(Config.JsonRpcPassword);
				if (!anonymousAccessAllowed)
				{
					var onionServiceId = await nonNullTorControlClient.CreateOnionServiceAsync(TorSettings.RpcVirtualPort, TorSettings.RpcOnionPort, cancellationToken).ConfigureAwait(false);
					OnionServiceUri = new Uri($"http://{onionServiceId}.onion");
					Logger.LogInfo($"RPC server listening on {OnionServiceUri}");
				}
				else
				{
					Logger.LogInfo("Anonymous access RPC server cannot be exposed as onion service.");
				}
			}

			// Do not monitor Tor when Tor is an already running service.
			if (TorSettings.TorMode == TorMode.Enabled)
			{
				HostedServices.Register<TorMonitor>(() => new TorMonitor(period: TimeSpan.FromMinutes(1), torProcessManager: TorManager, httpClientFactory: HttpClientFactory), nameof(TorMonitor));
			}

			HostedServices.Register<TorStatusChecker>(() => TorStatusChecker, "Tor Network Checker");
		}
	}

	private async Task StartLocalBitcoinNodeAsync(CancellationToken cancel)
	{
		try
		{
			if (Config.StartLocalBitcoinCoreOnStartup)
			{
				CoreNodeParams coreNodeParams = new(
					Network,
					BitcoinStore.MempoolService,
					Config.LocalBitcoinCoreDataDir,
					tryRestart: false,
					tryDeleteDataDir: false,
					EndPointStrategy.Default(Network, EndPointType.P2p),
					EndPointStrategy.Default(Network, EndPointType.Rpc),
					txIndex: null,
					prune: null,
					disableWallet: 1,
					mempoolReplacement: null,
					userAgent: $"/WasabiClient:{Constants.ClientVersion}/",
					fallbackFee: null, // ToDo: Maybe we should have it, not only for tests?
					Cache);

				CoreNode coreNode = await CoreNode.CreateAsync(coreNodeParams, cancel).ConfigureAwait(false);

				RegisterLocalNodeDependentComponents(coreNode);
				BitcoinCoreNode = coreNode;
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
		}
	}

	private void RegisterLocalNodeDependentComponents(CoreNode coreNode)
	{
		HostedServices.Register<BlockNotifier>(() => new BlockNotifier(TimeSpan.FromSeconds(7), coreNode.RpcClient, coreNode.P2pNode), "Block Notifier");
		HostedServices.Register<RpcMonitor>(() => new RpcMonitor(TimeSpan.FromSeconds(7), coreNode.RpcClient), "RPC Monitor");
		if (!Config.BlockOnlyMode)
		{
			HostedServices.Register<MempoolMirror>(
				() => new MempoolMirror(TimeSpan.FromSeconds(21), coreNode.RpcClient, coreNode.P2pNode),
				"Full Node Mempool Mirror");
		}
	}

	private void RegisterCoinJoinComponents()
	{
		string[] allowedCoordinationIdentifiers = Config.CoordinatorIdentifier.Length > 0 ? [Config.CoordinatorIdentifier, Config.MainNetCoordinatorUri] : [Config.MainNetCoordinatorUri];
		Tor.Http.IHttpClient roundStateUpdaterHttpClient = CoordinatorHttpClientFactory.NewHttpClient(Mode.SingleCircuitPerLifetime, RoundStateUpdaterCircuit);
		HostedServices.Register<RoundStateUpdater>(() => new RoundStateUpdater(TimeSpan.FromSeconds(0.1), allowedCoordinationIdentifiers, new WabiSabiHttpApiClient(roundStateUpdaterHttpClient)), "Round info updater");

		var coinJoinConfiguration = new CoinJoinConfiguration(Config.CoordinatorIdentifier, Config.MaxCoordinationFeeRate, Config.MaxCoinjoinMiningFeeRate, Config.AbsoluteMinInputCount, AllowSoloCoinjoining: false);
		HostedServices.Register<CoinJoinManager>(() => new CoinJoinManager(WalletManager, HostedServices.Get<RoundStateUpdater>(), CoordinatorHttpClientFactory, HostedServices.Get<WasabiSynchronizer>(), coinJoinConfiguration, CoinPrison), "CoinJoin Manager");
	}

	private void WalletManager_WalletStateChanged(object? sender, WalletState e)
	{
		// Load banned coins in wallet.
		// This event function can be deleted later when SmartCoin.IsBanned is removed.
		if (e is not WalletState.Started)
		{
			return;
		}

		var wallet = sender as Wallet ?? throw new InvalidOperationException($"The sender for {nameof(WalletManager.WalletStateChanged)} was not a Wallet.");
		CoinPrison.UpdateWallet(wallet);
	}

	public async Task DisposeAsync()
	{
		// Dispose method may be called just once.
		if (!_disposeRequested)
		{
			_disposeRequested = true;
			StoppingCts.Cancel();
		}
		else
		{
			return;
		}

		using (await InitializationAsyncLock.LockAsync())
		{
			Logger.LogWarning("Process is exiting.", nameof(Global));

			try
			{
				try
				{
					using var dequeueCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
					await WalletManager.RemoveAndStopAllAsync(dequeueCts.Token).ConfigureAwait(false);
					WalletManager.WalletStateChanged -= WalletManager_WalletStateChanged;
					Logger.LogInfo($"{nameof(WalletManager)} is stopped.", nameof(Global));
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error during {nameof(WalletManager.RemoveAndStopAllAsync)}: {ex}");
				}

				UpdateManager.Dispose();
				Logger.LogInfo($"{nameof(UpdateManager)} is stopped.");

				CoinPrison.Dispose();

				if (RpcServer is { } rpcServer)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
					await rpcServer.StopAsync(cts.Token).ConfigureAwait(false);
					Logger.LogInfo($"{nameof(RpcServer)} is stopped.", nameof(Global));
				}

				if (BlockDownloadService is { } blockDownloadService)
				{
					blockDownloadService.Dispose();
					Logger.LogInfo($"{nameof(BlockDownloadService)} is disposed.");
				}

				if (SpecificNodeBlockProvider is { } specificNodeBlockProvider)
				{
					await specificNodeBlockProvider.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(SpecificNodeBlockProvider)} is disposed.");
				}

				if (CoinJoinProcessor is { } coinJoinProcessor)
				{
					coinJoinProcessor.Dispose();
					Logger.LogInfo($"{nameof(CoinJoinProcessor)} is disposed.");
				}

				if (LegalChecker is { } legalChecker)
				{
					legalChecker.Dispose();
					Logger.LogInfo($"Disposed {nameof(LegalChecker)}.");
				}

				if (HostedServices is { } backgroundServices)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
					await backgroundServices.StopAllAsync(cts.Token).ConfigureAwait(false);
					backgroundServices.Dispose();
					Logger.LogInfo("Stopped background services.");
				}

				RoundStateUpdaterCircuit.Dispose();
				Logger.LogInfo($"Disposed {nameof(RoundStateUpdaterCircuit)}.");

				if (HttpClientFactory is { } httpClientFactory)
				{
					await httpClientFactory.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(HttpClientFactory)} is disposed.");
				}

				await CoordinatorHttpClientFactory.DisposeAsync().ConfigureAwait(false);
				Logger.LogInfo($"{nameof(CoordinatorHttpClientFactory)} is disposed.");

				if (BitcoinCoreNode is { } bitcoinCoreNode)
				{
					await bitcoinCoreNode.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(BitcoinCoreNode)} is disposed.");

					if (Config.StopLocalBitcoinCoreOnShutdown)
					{
						await bitcoinCoreNode.TryStopAsync().ConfigureAwait(false);
						Logger.LogInfo($"{nameof(BitcoinCoreNode)} is stopped.");
					}
				}

				if (TorStatusChecker is { } torStatusChecker)
				{
					torStatusChecker.Dispose();
					Logger.LogInfo($"{nameof(TorStatusChecker)} is stopped.");
				}

				if (TorManager is { } torManager)
				{
					using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
					var (_, torControlClient) = await TorManager.WaitForNextAttemptAsync(cts.Token).ConfigureAwait(false);
					if (OnionServiceUri is { } nonNullOnionServiceUri && torControlClient is { } nonNullTorControlClient)
					{
						try
						{
							var isDestroyedSuccessfully = await nonNullTorControlClient
								.DestroyOnionServiceAsync(nonNullOnionServiceUri.Host, cts.Token).ConfigureAwait(false);
							if (!isDestroyedSuccessfully)
							{
								Logger.LogInfo($"Onion service '{nonNullOnionServiceUri.Host}' failed to be destroyed.");
							}
						}
						catch (OperationCanceledException)
						{
							Logger.LogInfo($"'{nonNullOnionServiceUri.Host}' failed to be stopped in allotted time.");
						}
					}

					await torManager.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(TorManager)} is stopped.");
				}

				if (Cache is { } cache)
				{
					cache.Dispose();
					Logger.LogInfo($"{nameof(Cache)} is disposed.");
				}

				try
				{
					await IndexStore.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(IndexStore)} is disposed.");

					await AllTransactionStore.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(AllTransactionStore)} is disposed.");
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error during the disposal of {nameof(IndexStore)} and {nameof(AllTransactionStore)}: {ex}");
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
			finally
			{
				StoppingCts.Dispose();
				Logger.LogTrace("Dispose finished.");
			}
		}
	}
}
