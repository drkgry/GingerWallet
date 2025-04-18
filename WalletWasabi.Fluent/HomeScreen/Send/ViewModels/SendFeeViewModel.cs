using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Fluent.Common.ViewModels.DialogBase;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.HomeScreen.Send.Models;
using WalletWasabi.Lang;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.HomeScreen.Send.ViewModels;

[NavigationMetaData(
	Caption = "",
	IconName = "wallet_action_send",
	NavBarPosition = NavBarPosition.None,
	Searchable = false,
	NavigationTarget = NavigationTarget.DialogScreen)]
public partial class SendFeeViewModel : DialogViewModelBase<FeeRate>
{
	private readonly Wallet _wallet;
	private readonly TransactionInfo _transactionInfo;
	private readonly bool _isSilent;

	public SendFeeViewModel(Wallet wallet, TransactionInfo transactionInfo, bool isSilent)
	{
		Title = Resources.WalletSend;
		_isSilent = isSilent;
		IsBusy = isSilent;
		_wallet = wallet;
		_transactionInfo = transactionInfo;

		FeeChart = new FeeChartViewModel();

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: false, escapeGoesBack: true);
		EnableBack = true;

		NextCommand = ReactiveCommand.Create(OnNext);

		AdvancedOptionsCommand = ReactiveCommand.CreateFromTask(ShowAdvancedOptionsAsync);
	}

	public FeeChartViewModel FeeChart { get; }

	public ICommand AdvancedOptionsCommand { get; }

	private void OnNext()
	{
		var blockTarget = FeeChart.CurrentConfirmationTarget;
		_transactionInfo.ConfirmationTimeSpan = TransactionFeeHelper.CalculateConfirmationTime(blockTarget);
		Services.UiConfig.FeeTarget = (int)blockTarget;
		Close(DialogResultKind.Normal, new FeeRate(FeeChart.GetSatoshiPerByte(blockTarget)));
	}

	private async Task ShowAdvancedOptionsAsync()
	{
		var result = await ShowCustomFeeRateDialogAsync();
		if (result is { } feeRate && feeRate != FeeRate.Zero)
		{
			Close(DialogResultKind.Normal, feeRate);
		}
	}

	private async Task<FeeRate?> ShowCustomFeeRateDialogAsync()
	{
		var result = await NavigateDialogAsync(new CustomFeeRateDialogViewModel(_transactionInfo), NavigationTarget.CompactDialogScreen);
		return result.Result;
	}

	private async Task FeeEstimationsAreNotAvailableAsync()
	{
		await ShowErrorAsync(
			Resources.TransactionFee,
			Resources.TransactionFeeEstimationsUnavailable,
			"",
			NavigationTarget.CompactDialogScreen);

		var customFeeRate = await ShowCustomFeeRateDialogAsync();

		if (customFeeRate is { })
		{
			Close(DialogResultKind.Normal, customFeeRate);
		}
		else
		{
			Close();
		}
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		IsBusy = true;

		base.OnNavigatedTo(isInHistory, disposables);

		var feeProvider = _wallet.FeeProvider;

		Observable
			.FromEventPattern(feeProvider, nameof(feeProvider.AllFeeEstimateChanged))
			.Select(_ =>
			{
				TransactionFeeHelper.TryGetFeeEstimates(_wallet, out var estimates);
				return estimates;
			})
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(estimations => FeeChart.UpdateFeeEstimates(estimations.WildEstimations, _transactionInfo.MaximumPossibleFeeRate))
			.DisposeWith(disposables);

		RxApp.MainThreadScheduler.Schedule(async () =>
		{
			AllFeeEstimate feeEstimates;
			using var cancelTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));

			try
			{
				feeEstimates = await TransactionFeeHelper.GetFeeEstimatesWhenReadyAsync(_wallet, cancelTokenSource.Token);
			}
			catch (Exception ex)
			{
				Logger.LogInfo(ex);
				await FeeEstimationsAreNotAvailableAsync();
				return;
			}

			FeeChart.UpdateFeeEstimates(feeEstimates.WildEstimations, _transactionInfo.MaximumPossibleFeeRate);

			if (_transactionInfo.FeeRate != FeeRate.Zero)
			{
				FeeChart.InitCurrentConfirmationTarget(_transactionInfo.FeeRate);
			}

			if (_isSilent)
			{
				_transactionInfo.ConfirmationTimeSpan = TransactionFeeHelper.CalculateConfirmationTime(FeeChart.CurrentConfirmationTarget);

				OnNext();
			}
			else
			{
				IsBusy = false;
			}
		});
	}
}
