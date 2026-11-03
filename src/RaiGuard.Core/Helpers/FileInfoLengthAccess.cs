using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RaiGuard.Core.Helpers;

public static class FileInfoLengthAccess
{
	public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
	{
		while (expression is ParenthesizedExpressionSyntax parentheses)
			expression = parentheses.Expression;
		return expression;
	}

	public static MemberAccessExpressionSyntax? FromCreation(ObjectCreationExpressionSyntax creation)
	{
		SyntaxNode node = creation;
		while (node.Parent is ParenthesizedExpressionSyntax parentheses)
			node = parentheses;
		return node.Parent is MemberAccessExpressionSyntax access && access.Expression == node
			&& access.Name.Identifier.ValueText == "Length" ? access : null;
	}

	public static ObjectCreationExpressionSyntax? InlineCreation(MemberAccessExpressionSyntax access)
		=> access.Name.Identifier.ValueText == "Length"
			? Unwrap(access.Expression) as ObjectCreationExpressionSyntax : null;
}
