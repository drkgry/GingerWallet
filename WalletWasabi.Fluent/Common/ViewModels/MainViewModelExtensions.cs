using WalletWasabi.Fluent.AddWallet.ViewModels;
using WalletWasabi.Fluent.HelpAndSupport.ViewModels;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.OpenDirectory.ViewModels;
using WalletWasabi.Fluent.Settings.ViewModels;
using WalletWasabi.Fluent.TransactionBroadcasting.ViewModels;

namespace WalletWasabi.Fluent.Common.ViewModels;

public static class MainViewModelExtensions
{
	public static void RegisterAllViewModels(this MainViewModel mainViewModel, UiContext uiContext)
	{
		PrivacyModeViewModel.Register(mainViewModel.PrivacyMode);
		AddWalletPageViewModel.RegisterLazy(() => new AddWalletPageViewModel(uiContext));
		SettingsPageViewModel.Register(mainViewModel.SettingsPage);

		GeneralSettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 0;
			return mainViewModel.SettingsPage;
		});

		AppearanceSettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 1;
			return mainViewModel.SettingsPage;
		});

		BitcoinTabSettingsViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 2;
			return mainViewModel.SettingsPage;
		});

		AdvancedSettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 3;
			return mainViewModel.SettingsPage;
		});

		SecuritySettingsTabViewModel.RegisterLazy(() =>
		{
			mainViewModel.SettingsPage.SelectedTab = 4;
			return mainViewModel.SettingsPage;
		});

		AboutViewModel.RegisterLazy(() => new AboutViewModel(uiContext));
		BroadcasterViewModel.RegisterLazy(() => new BroadcasterViewModel(uiContext));
		LegalDocumentsViewModel.RegisterLazy(() => new LegalDocumentsViewModel(uiContext));
		UserSupportViewModel.RegisterLazy(() => new UserSupportViewModel(uiContext));
		BugReportLinkViewModel.RegisterLazy(() => new BugReportLinkViewModel(uiContext));
		DocsLinkViewModel.RegisterLazy(() => new DocsLinkViewModel(uiContext));
		OpenDataFolderViewModel.RegisterLazy(() => new OpenDataFolderViewModel(uiContext));
		OpenWalletsFolderViewModel.RegisterLazy(() => new OpenWalletsFolderViewModel(uiContext));
		OpenLogsViewModel.RegisterLazy(() => new OpenLogsViewModel(uiContext));
		OpenTorLogsViewModel.RegisterLazy(() => new OpenTorLogsViewModel(uiContext));
		OpenConfigFileViewModel.RegisterLazy(() => new OpenConfigFileViewModel(uiContext));
	}
}
