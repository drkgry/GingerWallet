using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using WalletWasabi.Fluent.Generators.Abstractions;

namespace WalletWasabi.Fluent.Generators.Generators;

internal class FluentNavigationGenerator: GeneratorStep
{
	public List<ConstructorDeclarationSyntax> Constructors { get; } = new();

	public override void OnInitialize(Compilation compilation, GeneratorStep[] steps)
	{
		foreach (var syntaxTree in compilation.SyntaxTrees)
		{
			var semanticModel = compilation.GetSemanticModel(syntaxTree);

			var constructors = syntaxTree.GetRoot()
				.DescendantNodes()
				.OfType<ClassDeclarationSyntax>()
				.Where(cls => cls.IsRoutableViewModel(semanticModel) && !cls.IsAbstractClass(semanticModel))
				.Where(cls => !HasAppLifetimeAttribute(cls, semanticModel)) // Exclude [AppLifetime] classes
				.Where(cls => HasValidNavigationMetaData(cls, semanticModel)) // Exclude invalid [NavigationMetaData]
				.SelectMany(cls => cls.Members.OfType<ConstructorDeclarationSyntax>());

			Constructors.AddRange(constructors);
		}
	}

	private static bool HasValidNavigationMetaData(ClassDeclarationSyntax cls, SemanticModel semanticModel)
	{
		var symbol = semanticModel.GetDeclaredSymbol(cls);
		if (symbol == null)
		{
			return false;
		}

		var navigationMetaData = symbol.GetAttributes().FirstOrDefault(attr =>
			attr.AttributeClass?.ToDisplayString() == "WalletWasabi.Fluent.NavigationMetaDataAttribute" || // Fully qualified name
			attr.AttributeClass?.Name == "NavigationMetaDataAttribute"); // Short name as fallback

		if (navigationMetaData is null)
		{
			return false;
		}

		var navigationTargetArg = navigationMetaData.NamedArguments.FirstOrDefault(arg => arg.Key == "NavigationTarget");

		if (navigationTargetArg.Equals(default(KeyValuePair<string, TypedConstant>)) ||
		    navigationTargetArg.Value.Value is int navTargetValue && navTargetValue == 0)
		{
			return false;
		}

		return true;
	}

	private static bool HasAppLifetimeAttribute(ClassDeclarationSyntax cls, SemanticModel semanticModel)
	{
		var symbol = semanticModel.GetDeclaredSymbol(cls);
		if (symbol == null)
		{
			return false;
		}

		return symbol.GetAttributes().Any(attr =>
			attr.AttributeClass?.ToDisplayString() == "AppLifetime" ||
			attr.AttributeClass?.ToDisplayString().EndsWith(".AppLifetimeAttribute") == true);
	}

	public override void Execute()
	{
		var namespaces = new List<string>();
		var methods = new List<string>();

		foreach (var constructor in Constructors)
		{
			var semanticModel = GetSemanticModel(constructor.SyntaxTree);

			if (constructor.Parent is not ClassDeclarationSyntax cls)
			{
				continue;
			}

			if (!cls.IsRoutableViewModel(semanticModel))
			{
				continue;
			}

			if (cls.IsAbstractClass(semanticModel))
			{
				continue;
			}

			var viewModelTypeInfo = semanticModel.GetDeclaredSymbol(cls);

			if (viewModelTypeInfo == null)
			{
				continue;
			}

			var className = cls.Identifier.ValueText;

			var constructorNamespaces = constructor.ParameterList.Parameters
				.Where(p => p.Type is not null)
				.Select(p => semanticModel.GetTypeInfo(p.Type!))
				.Where(t => t.Type is not null)
				.SelectMany(t => t.Type.GetNamespaces())
				.ToArray();

			var methodParams = constructor.ParameterList;

			var navigationMetadata = viewModelTypeInfo
				.GetAttributes()
				.FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == NavigationMetaDataGenerator.NavigationMetaDataAttributeDisplayString);

			var defaultNavigationTarget = "DialogScreen";

			if (navigationMetadata != null)
			{
				var navigationArgument = navigationMetadata.NamedArguments
					.FirstOrDefault(x => x.Key == "NavigationTarget");

				if (navigationArgument.Value.Type is INamedTypeSymbol navigationTargetEnum)
				{
					var enumValue = navigationTargetEnum
						.GetMembers()
						.OfType<IFieldSymbol>()
						.FirstOrDefault(x => x.ConstantValue?.Equals(navigationArgument.Value.Value) == true);

					if (enumValue != null)
					{
						defaultNavigationTarget = enumValue.Name;
					}
				}
			}

			var additionalMethodParams =
				$"NavigationTarget navigationTarget = NavigationTarget.{defaultNavigationTarget}, NavigationMode navigationMode = NavigationMode.Normal";

			methodParams = methodParams.AddParameters(SyntaxFactory.ParseParameterList(additionalMethodParams).Parameters.ToArray());

			var constructorArgs =
				SyntaxFactory.ArgumentList(
					SyntaxFactory.SeparatedList(
					constructor.ParameterList
						.Parameters
						.Select(x => x.Identifier.ValueText)
						.Select(x => SyntaxFactory.ParseExpression(x))
						.Select(SyntaxFactory.Argument),
					constructor.ParameterList
						.Parameters
						.Skip(1)
						.Select(x => SyntaxFactory.Token(SyntaxKind.CommaToken))));

			namespaces.Add(viewModelTypeInfo.ContainingNamespace.ToDisplayString());
			namespaces.AddRange(constructorNamespaces);

			var methodName = className.Replace("ViewModel", "");

			var (dialogReturnType, dialogReturnTypeNamespace) = cls.GetDialogResultType(semanticModel);

			foreach (var ns in dialogReturnTypeNamespace)
			{
				namespaces.Add(ns);
			}

			if (dialogReturnType is { })
			{
				var dialogString =
					$$"""
						public FluentDialog<{{dialogReturnType}}> {{methodName}}{{methodParams}}
						{
						    var dialog = new {{className}}{{constructorArgs.ToFullString()}};
							var target = UiContext.Navigate(navigationTarget);

							return new FluentDialog<{{dialogReturnType}}>(target.NavigateDialogAsync(dialog, navigationMode));
						}

					""";
				methods.Add(dialogString);
			}
			else
			{
				var methodString =
				$$"""
					public void {{methodName}}{{methodParams}}
					{
						UiContext.Navigate(navigationTarget).To(new {{className}}{{constructorArgs.ToFullString()}}, navigationMode);
					}

				""";
				methods.Add(methodString);
			}
		}

		var usings = namespaces
			.Distinct()
			.OrderBy(x => x)
			.Select(n => $"using {n};")
			.ToArray();

		var usingsString = string.Join("\r\n", usings);

		var methodsString = string.Join("\r\n", methods);

		var sourceText =
			$$"""
			// <auto-generated />
			#nullable enable

			{{usingsString}}
			using WalletWasabi.Fluent.Navigation.Models;
			using WalletWasabi.Fluent.Navigation.Interfaces;

			namespace WalletWasabi.Fluent.Navigation.ViewModels;

			public partial class FluentNavigate
			{
			{{methodsString}}
			}

			""";

		AddSource("FluentNavigate.g.cs", sourceText);
	}
}
