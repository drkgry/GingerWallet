using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WalletWasabi.Fluent.Generators;

public static class AnalyzerExtensions
{
	private static readonly string RoutableViewModelPath = "WalletWasabi.Fluent.Navigation.ViewModels.RoutableViewModel";

	public static bool IsSubTypeOf(this SyntaxNode node, SemanticModel model, string baseType)
	{
		if (node is not ClassDeclarationSyntax cls)
		{
			return false;
		}

		var currentType = model.GetDeclaredSymbol(cls);
		while (currentType != null)
		{
			if (currentType.ToDisplayString() == baseType)
			{
				return true;
			}
			currentType = currentType.BaseType;
		}

		return false;
	}

	public static bool IsAbstractClass(this ClassDeclarationSyntax cls, SemanticModel model)
	{
		var typeInfo = model.GetDeclaredSymbol(cls)
					   ?? throw new InvalidOperationException($"Unable to get Declared Symbol: {cls.Identifier}");

		return typeInfo.IsAbstract;
	}

	public static bool IsRoutableViewModel(this SyntaxNode node, SemanticModel model)
	{
		return node.IsSubTypeOf(model, RoutableViewModelPath);
	}

	public static (string? TypeName, IEnumerable<string> Namespaces) GetDialogResultType(this SyntaxNode node, SemanticModel model)
	{
		if (node is not ClassDeclarationSyntax cls)
		{
			return (null, []);
		}

		var currentType = model.GetDeclaredSymbol(cls);
		while (currentType != null)
		{
			if (currentType.ConstructedFrom?.ToDisplayString() == "WalletWasabi.Fluent.Common.ViewModels.DialogBase.DialogViewModelBase<TResult>")
			{
				var typeArgument = currentType.TypeArguments.FirstOrDefault();
				if (typeArgument is { })
				{
					return (typeArgument.ToDisplayString(), typeArgument.GetNamespaces());
				}
			}
			currentType = currentType.BaseType;
		}

		return (null, []);
	}

	public static List<string> GetNamespaces(this ITypeSymbol? typeSymbol)
	{
		return GetNamespaceSymbols(typeSymbol)
			.Where(x => !x.IsGlobalNamespace)
			.Select(x => x.ToDisplayString())
			.ToList();
	}

	private static IEnumerable<INamespaceSymbol> GetNamespaceSymbols(this ITypeSymbol? typeSymbol)
	{
		if (typeSymbol is null)
		{
			yield break;
		}

		yield return typeSymbol.ContainingNamespace;

		if (typeSymbol is INamedTypeSymbol namedType)
		{
			foreach (var typeArg in namedType.TypeArguments)
			{
				yield return typeArg.ContainingNamespace;
			}
		}
	}

	public static string SimplifyType(this ITypeSymbol typeSymbol, List<string> namespaces)
	{
		if (typeSymbol is IArrayTypeSymbol arrayType)
		{
			var dimensions = new string(',', arrayType.Rank - 1);

			return $"{arrayType.ElementType.SimplifyType(namespaces)}[{dimensions}]";
		}

		if (typeSymbol is not INamedTypeSymbol type)
		{
			return "";
		}

		if (type.NullableAnnotation == NullableAnnotation.Annotated && type.Name == "Nullable")
		{
			return type.TypeArguments.First().SimplifyType(namespaces) + "?";
		}

		if (!type.ContainingNamespace.IsGlobalNamespace)
		{
			namespaces.Add(type.ContainingNamespace.ToDisplayString());
		}

		var typeName =
			type.SpecialType switch
			{
				SpecialType.System_Object => "object",
				SpecialType.System_Void => "void",
				SpecialType.System_Boolean => "bool",
				SpecialType.System_Char => "char",
				SpecialType.System_Byte => "byte",
				SpecialType.System_Int16 => "short",
				SpecialType.System_Int32 => "int",
				SpecialType.System_Int64 => "long",
				SpecialType.System_Decimal => "decimal",
				SpecialType.System_Single => "float",
				SpecialType.System_Double => "double",
				SpecialType.System_String => "string",
				_ => type.Name
			};

		if (type.ContainingType is { } containingType)
		{
			typeName = containingType.SimplifyType(namespaces) + "." + typeName;
		}

		if (type.IsTupleType)
		{
			typeName = "(";

			var elements =
				from element in type.TupleElements
				let elementType = element.Type.SimplifyType(namespaces)
				let elementName = element.Name
				select $"{elementType} {elementName}";

			typeName += string.Join(", ", elements);

			typeName += ")";
		}
		else if (type.IsGenericType)
		{
			typeName += "<";

			var typeArguments =
				from argument in type.TypeArguments
				let argumentType = argument.SimplifyType(namespaces)
				select argumentType;

			typeName += string.Join(", ", typeArguments);

			typeName += ">";
		}

		if (type.NullableAnnotation == NullableAnnotation.Annotated)
		{
			typeName += "?";
		}

		return typeName;
	}

	public static string? GetExplicitDefaultValueString(this IParameterSymbol parameter)
	{
		if (!parameter.HasExplicitDefaultValue)
		{
			return null;
		}

		return parameter.ExplicitDefaultValue switch
		{
			string s => $"\"{s}\"",
			bool b when b => "true",
			bool b when !b => "false",
			null => "null",
			_ => parameter.ExplicitDefaultValue.ToString()
		};
	}
}
