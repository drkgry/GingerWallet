using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.AddWallet.Models;
using WalletWasabi.Fluent.Navigation.ViewModels;

namespace WalletWasabi.Fluent.AddWallet.ViewModels.Create;

[NavigationMetaData(NavigationTarget = NavigationTarget.DialogScreen)]
public partial class ConfirmRecoveryWordsViewModel : RoutableViewModel
{
	private readonly List<RecoveryWordViewModel> _words;
	private readonly WalletCreationOptions.AddNewWallet _options;

	[AutoNotify] private bool _isSkipEnabled;
	[AutoNotify] private RecoveryWordViewModel _currentWord;
	[AutoNotify] private List<RecoveryWordViewModel> _availableWords;

	public ConfirmRecoveryWordsViewModel(WalletCreationOptions.AddNewWallet options, List<RecoveryWordViewModel> words)
	{
		Title = Lang.Resources.ConfirmRecoveryWordsViewModel;

		_options = options;
		_availableWords = new List<RecoveryWordViewModel>();
		_words = words.OrderBy(x => x.Index).ToList();
		_currentWord = words.First();
	}

	public ObservableCollectionExtended<RecoveryWordViewModel> ConfirmationWords { get; } = new();

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Uses DisposeWith()")]
	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		ConfirmationWords.Clear();

		var confirmationWordsSourceList = new SourceList<RecoveryWordViewModel>();

		confirmationWordsSourceList
			.DisposeWith(disposables)
			.Connect()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(ConfirmationWords)
			.OnItemAdded(x => x.Reset())
			.Subscribe()
			.DisposeWith(disposables);

		EnableBack = true;

		var nextCommandCanExecute =
			confirmationWordsSourceList
			.Connect()
			.WhenValueChanged(x => x.IsConfirmed)
			.Select(_ => confirmationWordsSourceList.Items.All(x => x.IsConfirmed));

		NextCommand = ReactiveCommand.CreateFromTask(OnNextAsync, nextCommandCanExecute);

		SetSkip();

		confirmationWordsSourceList.AddRange(_words);

		AvailableWords = confirmationWordsSourceList.Items
			.Select(x => new RecoveryWordViewModel(x.Index, x.Word))
			.OrderBy(x => x.Word)
			.ToList();

		var availableWordsSourceList = new SourceList<RecoveryWordViewModel>()
			.DisposeWith(disposables);

		availableWordsSourceList
			.Connect()
			.WhenPropertyChanged(x => x.IsSelected)
			.Subscribe(x => OnWordSelectionChanged(x.Sender))
			.DisposeWith(disposables);

		availableWordsSourceList.AddRange(AvailableWords);

		SetNextWord();

		var enableCancel = UiContext.WalletRepository.HasWallet;
		SetupCancel(enableCancel: false, enableCancelOnEscape: enableCancel, enableCancelOnPressed: false);
	}

	private void SetNextWord()
	{
		if (ConfirmationWords.FirstOrDefault(x => !x.IsConfirmed) is { } nextWord)
		{
			CurrentWord = nextWord;
		}

		EnableAvailableWords(true);
	}

	private void OnWordSelectionChanged(RecoveryWordViewModel selectedWord)
	{
		if (selectedWord.IsSelected)
		{
			CurrentWord.SelectedWord = selectedWord.Word;
		}
		else
		{
			CurrentWord.SelectedWord = null;
		}

		if (CurrentWord.IsConfirmed)
		{
			selectedWord.IsConfirmed = true;
			SetNextWord();
		}
		else if (!selectedWord.IsSelected)
		{
			EnableAvailableWords(true);
		}
		else
		{
			EnableAvailableWords(false);
			selectedWord.IsEnabled = true;
		}
	}

	private void EnableAvailableWords(bool enable)
	{
		foreach (var w in AvailableWords)
		{
			w.IsEnabled = enable;
		}
	}

	private async Task OnNextAsync()
	{
		var dialogCaption = Lang.Resources.ConfirmRecoveryWordsViewModelStoreSafely;
		var password = await UiContext.Navigate().To().CreatePasswordDialog(Lang.Resources.AddPassphrase, dialogCaption, enableEmpty: true).GetResultAsync();

		if (password is { })
		{
			IsBusy = true;

			var options = _options with { Password = password };

			var walletSettings = await UiContext.WalletRepository.NewWalletAsync(options);

			IsBusy = false;

			UiContext.Navigate().To().AddedWalletPage(walletSettings, options);
		}
	}

	private void SetSkip()
	{
#if RELEASE
		IsSkipEnabled = Services.WalletManager.Network != NBitcoin.Network.Main || System.Diagnostics.Debugger.IsAttached;
#else
		IsSkipEnabled = true;
#endif

		if (IsSkipEnabled)
		{
			SkipCommand = ReactiveCommand.CreateFromTask(OnNextAsync);
		}
	}
}
