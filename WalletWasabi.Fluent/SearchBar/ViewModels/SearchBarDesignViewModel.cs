using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using ReactiveUI;
using WalletWasabi.Fluent.SearchBar.Interfaces;
using WalletWasabi.Fluent.SearchBar.Models;
using WalletWasabi.Fluent.SearchBar.ViewModels.SearchItems;

namespace WalletWasabi.Fluent.SearchBar.ViewModels;

[Localizable(false)]
public class SearchBarDesignViewModel : ReactiveObject
{
	private readonly IEnumerable<ISearchItem> _items;

	public SearchBarDesignViewModel()
	{
		static Task PreventExecution() => Task.Run(() => { });

		var actionable = new IActionableItem[]
		{
			new ActionableItem("Test 1: Short", "Description short", PreventExecution, "Settings")
			{
				Icon = "settings_bitcoin_regular"
			},
			new ActionableItem("Test 2: Loooooooooooong", "Description long", PreventExecution, "Settings")
			{
				Icon = "settings_bitcoin_regular"
			},
			new ActionableItem(
				"Test 3: Short again",
				"Description very very loooooooooooong and difficult to read",
				PreventExecution,
				"Settings")
			{
				Icon = "settings_bitcoin_regular"
			},
			new ActionableItem("Test 3", "Another", PreventExecution, "Settings")
			{
				Icon = "settings_bitcoin_regular"
			},
			new ActionableItem(
				"Test 4: Help topics",
				"Description very very loooooooooooong and difficult to read",
				PreventExecution,
				"Help")
			{
				Icon = "settings_bitcoin_regular"
			},
			new ActionableItem("Test 3", "Another", PreventExecution, "Help")
			{
				Icon = "settings_bitcoin_regular"
			}
		};

		_items = actionable.ToList();
	}

	public ReadOnlyObservableCollection<SearchItemGroup> Groups => new(
		new ObservableCollection<SearchItemGroup>(
			_items
				.GroupBy(r => r.Category)
				.Select(
					grouping =>
					{
						var sourceCache = new SourceCache<ISearchItem, ComposedKey>(r => r.Key);
						sourceCache.PopulateFrom(grouping.ToObservable());
						return new SearchItemGroup(grouping.Key, sourceCache.Connect());
					})));

	public string SearchText => "";
}
