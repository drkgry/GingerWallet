using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.Common.ViewModels;
using WalletWasabi.Fluent.HomeScreen.BuySell.Models;
using WalletWasabi.Fluent.SearchBar.Models;

namespace WalletWasabi.Fluent.HomeScreen.BuySell.ViewModels;

public abstract class OfferViewModel : ViewModelBase
{
	protected OfferViewModel(OfferModel offer, Func<OfferViewModel, Task> acceptOffer)
	{
		Offer = offer;
		AcceptCommand = ReactiveCommand.CreateFromTask(async () => await acceptOffer(this));
	}

	public ICommand AcceptCommand { get; }
	public OfferModel Offer { get; }
	public string Amount { get; protected set; } = "";
	public string Fee { get; protected set; } = "";
	public string FeeToolTip { get; protected set; } = "";
	public bool IsNoKycVisible { get; protected set; }
	public string MethodName => Offer.MethodName;
	public ComposedKey Key => new(Offer);
}
