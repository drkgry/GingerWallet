using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tor.Http;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.DoSPrevention;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Backend.Rounds.CoinJoinStorage;
using WalletWasabi.WabiSabi.Backend.Statistics;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi;
using WalletWasabi.Tests.TestCommon;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Integration;

public class WabiSabiApiApplicationFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup : class
{
	// There is a deadlock in the current version of the asp.net testing framework
	// https://www.strathweb.com/2021/05/the-curious-case-of-asp-net-core-integration-test-deadlock/
	protected override IHost CreateHost(IHostBuilder builder)
	{
		var host = builder.Build();
		Task.Run(() => host.StartAsync()).GetAwaiter().GetResult();
		return host;
	}

	protected override void ConfigureClient(HttpClient client)
	{
		client.Timeout = TimeSpan.FromMinutes(10);
	}

	protected override IHostBuilder CreateHostBuilder()
	{
		var builder = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(x => x.UseStartup<TStartup>().UseTestServer());
		return builder;
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// will be called after the `ConfigureServices` from the Startup
		builder.ConfigureTestServices(services =>
		{
			services.AddHostedService<BackgroundServiceStarter<Arena>>();
			services.AddSingleton<Arena>();
			services.AddSingleton(_ => Network.RegTest);
			services.AddScoped<IRPCClient>(_ => BitcoinFactory.GetMockMinimalRpc());
			services.AddScoped<Prison>(_ => WabiSabiTestFactory.CreatePrison());
			services.AddScoped(services => CreateConfig(10));
			services.AddScoped<RoundParameterFactory>();
			services.AddScoped(typeof(TimeSpan), _ => TimeSpan.FromSeconds(2));
			services.AddScoped<ICoinJoinIdStore>(s => new CoinJoinIdStore());
			services.AddScoped(s => new CoinJoinScriptStore());
			services.AddSingleton<CoinJoinFeeRateStatStore>();
			services.AddHttpClient();
			services.AddSingleton<CoinJoinMempoolManager>();
			services.AddSingleton(s => new MempoolMirror(TimeSpan.Zero, null!, null!));
		});
		builder.ConfigureLogging(o => o.SetMinimumLevel(LogLevel.Warning));
	}

	public static WabiSabiConfig CreateConfig(int maxIputCount)
	{
		WabiSabiConfig config = WabiSabiBackendFactory.Instance.CreateWabiSabiConfig();
		config.MaxInputCountByRound = maxIputCount;
		config.StandardInputRegistrationTimeout = TimeSpan.FromSeconds(40);
		config.BlameInputRegistrationTimeout = TimeSpan.FromSeconds(40);
		config.ConnectionConfirmationTimeout = TimeSpan.FromSeconds(40);
		config.OutputRegistrationTimeout = TimeSpan.FromSeconds(40);
		config.TransactionSigningTimeout = TimeSpan.FromSeconds(40);
		config.MaxSuggestedAmountBase = Money.Satoshis(ProtocolConstants.MaxAmountCredentialValue);
		config.CreateNewRoundBeforeInputRegEnd = TimeSpan.Zero;
		return config;
	}

	public Task<ArenaClient> CreateArenaClientAsync(HttpClient httpClient) =>
		CreateArenaClientAsync(CreateWabiSabiHttpApiClient(httpClient));

	public Task<ArenaClient> CreateArenaClientAsync(IHttpClient httpClient) =>
		CreateArenaClientAsync(new WabiSabiHttpApiClient(httpClient));

	public async Task<ArenaClient> CreateArenaClientAsync(WabiSabiHttpApiClient wabiSabiHttpApiClient)
	{
		var rounds = (await wabiSabiHttpApiClient.GetStatusAsync(RoundStateRequest.Empty, CancellationToken.None)).RoundStates;
		var round = rounds.First(x => x.CoinjoinState is ConstructionState);
		var arenaClient = new ArenaClient(
			round.CreateAmountCredentialClient(TestRandom.Wasabi(1)),
			round.CreateVsizeCredentialClient(TestRandom.Wasabi(2)),
			round.CoinjoinState.Parameters.CoordinationIdentifier,
			wabiSabiHttpApiClient);
		return arenaClient;
	}

	public WabiSabiHttpApiClient CreateWabiSabiHttpApiClient(HttpClient httpClient) =>
		new(new ClearnetHttpClient(httpClient));
}
