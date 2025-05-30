using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NBitcoin;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Helpers;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Fluent.Controls;

public class CurrencyEntryBox : TextBox
{
	public static readonly StyledProperty<string> CurrencyCodeProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, string>(nameof(CurrencyCode));

	public static readonly StyledProperty<bool> IsFiatProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsFiat));

	public static readonly StyledProperty<bool> IsApproximateProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsApproximate));

	public static readonly StyledProperty<decimal> ConversionRateProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, decimal>(nameof(ConversionRate));

	public static readonly StyledProperty<bool> IsRightSideProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsRightSide));

	public static readonly StyledProperty<int> MaxDecimalsProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, int>(nameof(MaxDecimals), 8);

	public static readonly StyledProperty<Money> BalanceBtcProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, Money>(nameof(BalanceBtc));

	public static readonly StyledProperty<decimal> BalanceFiatProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, decimal>(nameof(BalanceFiat));

	public static readonly StyledProperty<bool> ValidatePasteBalanceProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(ValidatePasteBalance));

	private static readonly string[] InvalidCharacters = new string[1] { "\u007f" };

	private static readonly Regex InvalidCharacterRegex = new(
		$"[^0-9{Regex.Escape(Lang.Resources.Culture.NumberFormat.CurrencyDecimalSeparator)}]",
		RegexOptions.Compiled
	);

	public CurrencyEntryBox()
	{
		SetCurrentValue(TextProperty, string.Empty);

		PseudoClasses.Set(":noexchangerate", true);
		PseudoClasses.Set(":isrightside", false);

		this.GetObservable(IsRightSideProperty)
			.Subscribe(x => PseudoClasses.Set(":isrightside", x));
	}

	public decimal ConversionRate
	{
		get => GetValue(ConversionRateProperty);
		set => SetValue(ConversionRateProperty, value);
	}

	public string CurrencyCode
	{
		get => GetValue(CurrencyCodeProperty);
		set => SetValue(CurrencyCodeProperty, value);
	}

	public bool IsFiat
	{
		get => GetValue(IsFiatProperty);
		set => SetValue(IsFiatProperty, value);
	}

	public bool IsApproximate
	{
		get => GetValue(IsApproximateProperty);
		set => SetValue(IsApproximateProperty, value);
	}

	public bool IsRightSide
	{
		get => GetValue(IsRightSideProperty);
		set => SetValue(IsRightSideProperty, value);
	}

	public int MaxDecimals
	{
		get => GetValue(MaxDecimalsProperty);
		set => SetValue(MaxDecimalsProperty, value);
	}

	public Money BalanceBtc
	{
		get => GetValue(BalanceBtcProperty);
		set => SetValue(BalanceBtcProperty, value);
	}

	public decimal BalanceFiat
	{
		get => GetValue(BalanceFiatProperty);
		set => SetValue(BalanceFiatProperty, value);
	}

	public bool ValidatePasteBalance
	{
		get => GetValue(ValidatePasteBalanceProperty);
		set => SetValue(ValidatePasteBalanceProperty, value);
	}

	private decimal FiatToBitcoin(decimal fiatValue)
	{
		if (ConversionRate == 0m)
		{
			return 0m;
		}

		return fiatValue / ConversionRate;
	}

	protected override void OnGotFocus(GotFocusEventArgs e)
	{
		base.OnGotFocus(e);

		SetCurrentValue(CaretIndexProperty, Text?.Length ?? 0);

		Dispatcher.UIThread.Post(SelectAll);
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		var input = e.Text ??= "";

		// Only allow valid chars
		if (string.IsNullOrWhiteSpace(input) || InvalidCharacterRegex.IsMatch(input))
		{
			e.Handled = true;
			base.OnTextInput(e);
			return;
		}

		if (IsReplacingWithImplicitDecimal(input))
		{
			var result = ReplaceCurrentTextWithLeadingZero(e);
			base.OnTextInput(result);
			return;
		}

		if (IsInsertingImplicitDecimal(input))
		{
			var result = InsertLeadingZeroForDecimal(e);
			base.OnTextInput(result);
			return;
		}

		var preComposedText = PreComposeText(input);

		var isValid = ValidateEntryText(preComposedText);

		preComposedText = preComposedText.TotalTrim();

		var parsed = decimal.TryParse(preComposedText, NumberStyles.Number, Lang.Resources.Culture.NumberFormat, out var fiatValue);

		e.Handled = !(isValid && parsed);

		if (IsFiat && !e.Handled)
		{
			e.Handled = FiatToBitcoin(fiatValue) >= Constants.MaximumNumberOfBitcoins;
		}

		base.OnTextInput(e);
	}

	private bool IsReplacingWithImplicitDecimal(string input)
	{
		return input.StartsWith(Lang.Resources.Culture.NumberFormat.NumberDecimalSeparator) && SelectedText == Text;
	}

	private bool IsInsertingImplicitDecimal(string input)
	{
		return input.StartsWith(Lang.Resources.Culture.NumberFormat.NumberDecimalSeparator) && CaretIndex == 0 && Text is not null && !Text.Contains(Lang.Resources.Culture.NumberFormat.NumberDecimalSeparator);
	}

	private TextInputEventArgs ReplaceCurrentTextWithLeadingZero(TextInputEventArgs e)
	{
		var finalText = "0" + e.Text;
		SetCurrentValue(TextProperty, "");
		SetCurrentValue(CaretIndexProperty, finalText.Length);
		ClearSelection();
		return new TextInputEventArgs { Text = finalText };
	}

	private TextInputEventArgs InsertLeadingZeroForDecimal(TextInputEventArgs e)
	{
		var prependText = "0" + e.Text;
		SetCurrentValue(TextProperty, Text?.Insert(0, prependText));
		SetCurrentValue(CaretIndexProperty, CaretIndex + prependText.Length);
		return new TextInputEventArgs { Text = "" };
	}

	private bool ValidateEntryText(string preComposedText)
	{
		var decimalSeparator = Lang.Resources.Culture.NumberFormat.NumberDecimalSeparator;

		if (preComposedText.Count(c => c == decimalSeparator[0]) > 1)
		{
			return false;
		}

		int decimalIndex = preComposedText.IndexOf(decimalSeparator, StringComparison.Ordinal);
		var wholeCount = 0;
		var fractionCount = 0;

		if (decimalIndex != -1)
		{
			wholeCount = preComposedText.Substring(0, decimalIndex).Length;
			fractionCount = preComposedText.Substring(decimalIndex + 1).Length;
		}
		else
		{
			wholeCount = preComposedText.Length;
		}

		// Passthrough the decimal place char or the group separator.
		if (preComposedText == decimalSeparator)
		{
			return false;
		}

		if (IsFiat)
		{
			// Fiat input restriction is to only allow 2 decimal places max
			// and also 18 whole number places.
			if (wholeCount > 18 || fractionCount > MaxDecimals)
			{
				return false;
			}
		}
		else
		{
			// Bitcoin input restriction is to only allow 8 decimal places max
			// and also 8 whole number places.
			if (wholeCount > 8 || fractionCount > MaxDecimals)
			{
				return false;
			}
		}

		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		DoPasteCheck(e);
	}

	private void DoPasteCheck(KeyEventArgs e)
	{
		var keymap = Application.Current?.PlatformSettings?.HotkeyConfiguration;

		bool Match(IEnumerable<KeyGesture> gestures) => gestures.Any(g => g.Matches(e));

		if (keymap is { } && Match(keymap.Paste))
		{
			ModifiedPasteAsync();
		}
		else
		{
			base.OnKeyDown(e);
		}
	}

	public async void ModifiedPasteAsync()
	{
		var text = await ApplicationHelper.GetTextAsync();

		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		text = text.Replace("\r", "").Replace("\n", "").Trim();

		// Ignore paste if there are invalid characters
		if (!CurrencyInput.RegexValidNumber().IsMatch(text))
		{
			return;
		}

		text = TextHelpers.ClearNumberFormat(text);

		if (!TryParsePastedValue(text, out text))
		{
			return;
		}

		if (ValidateEntryText(text))
		{
			OnTextInput(new TextInputEventArgs { Text = text });

			Dispatcher.UIThread.Post(() =>
			{
				ClearSelection();
				CaretIndex = Text?.Length ?? 0;
			});
		}
	}

	private bool TryParsePastedValue(string text, [NotNullWhen(true)] out string? result)
	{
		if (!IsFiat)
		{
			if (CurrencyInput.TryCorrectBitcoinAmount(text, out var corrected))
			{
				text = corrected;
			}

			var money = ValidatePasteBalance
				? ClipboardObserver.ParseToMoney(text, BalanceBtc)
				: ClipboardObserver.ParseToMoney(text);
			if (money is not null)
			{
				result = money.ToString(Lang.Resources.Culture.NumberFormat, trimExcessZero: true);
				return true;
			}
		}
		else
		{
			if (CurrencyInput.TryCorrectAmount(text, out var corrected))
			{
				text = corrected;
			}

			var fiat = ValidatePasteBalance
				? ClipboardObserver.ParseToFiat(text, BalanceFiat)
				: ClipboardObserver.ParseToFiat(text);
			if (fiat is not null)
			{
				result = fiat.Value.ToString(Lang.Resources.Culture.NumberFormat);
				return true;
			}
		}

		result = null;
		return false;
	}

	// Pre-composes the TextInputEventArgs to see the potential Text that is to
	// be committed to the TextPresenter in this control.

	private string RemoveInvalidCharacters(string text)
	{
		for (var i = 0; i < InvalidCharacters.Length; i++)
		{
			text = text.Replace(InvalidCharacters[i], string.Empty);
		}

		return text;
	}

	// An event in Avalonia's TextBox with this function should be implemented there for brevity.
	private string PreComposeText(string input)
	{
		input = RemoveInvalidCharacters(input);
		var preComposedText = Text ?? "";
		var caretIndex = CaretIndex;
		var selectionStart = SelectionStart;
		var selectionEnd = SelectionEnd;

		if (!string.IsNullOrEmpty(input) && (MaxLength == 0 ||
											 input.Length + preComposedText.Length -
											 Math.Abs(selectionStart - selectionEnd) <= MaxLength))
		{
			if (selectionStart != selectionEnd)
			{
				var start = Math.Min(selectionStart, selectionEnd);
				var end = Math.Max(selectionStart, selectionEnd);
				preComposedText = $"{preComposedText[..start]}{preComposedText[end..]}";
				caretIndex = start;
			}

			return $"{preComposedText[..caretIndex]}{input}{preComposedText[caretIndex..]}";
		}

		return "";
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == IsReadOnlyProperty)
		{
			PseudoClasses.Set(":readonly", change.GetNewValue<bool>());
		}
		else if (change.Property == ConversionRateProperty)
		{
			PseudoClasses.Set(":noexchangerate", change.GetNewValue<decimal>() == 0m);
		}
		else if (change.Property == IsFiatProperty)
		{
			PseudoClasses.Set(":isfiat", change.GetNewValue<bool>());
		}
		else if (change.Property == WatermarkProperty)
		{
			PseudoClasses.Set(":approxwatermark", change.GetNewValue<string>().StartsWith('≈'));
		}
	}
}
