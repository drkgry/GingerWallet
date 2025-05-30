using System.Threading.Tasks;
using WalletWasabi.Fluent.Authorization.Models;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Lang;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Authorization.ViewModels;

[NavigationMetaData(NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class HardwareWalletAuthDialogViewModel : AuthorizationDialogBase
{
	private readonly HardwareWalletModel _wallet;
	private readonly TransactionAuthorizationInfo _transactionAuthorizationInfo;

	public HardwareWalletAuthDialogViewModel(HardwareWalletModel wallet, TransactionAuthorizationInfo transactionAuthorizationInfo)
	{
		Title = Resources.AuthorizeWithHardwareWallet;

		_wallet = wallet;
		_transactionAuthorizationInfo = transactionAuthorizationInfo;
		WalletType = wallet.Settings.WalletType;

		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

		EnableBack = false;

		AuthorizationFailedMessage = Resources.AuthorizationFailedWithDeviceCheck.SafeInject(Environment.NewLine);
	}

	public WalletType WalletType { get; }

	protected override Task<bool> AuthorizeAsync()
	{
		return _wallet.AuthorizeTransactionAsync(_transactionAuthorizationInfo);
	}
}
