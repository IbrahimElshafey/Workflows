using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Workflows.Analyzers.Rules
{
    public static class SerializationRules
    {
        public static void AnalyzeAwaitForeach(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor)
        {
            var forEachStatement = (ForEachStatementSyntax)context.Node;
            if(forEachStatement.AwaitKeyword == default)
                return;

            var containingMethod = forEachStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethod == null)
                return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            var expr = forEachStatement.Expression;
            var typeInfo = context.SemanticModel.GetTypeInfo(expr);
            var type = typeInfo.Type as INamedTypeSymbol;

            if(type != null &&
                (type.ConstructedFrom?.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>" ||
                    type.Name == "IAsyncEnumerable"))
            {
                var typeArg = type.TypeArguments.FirstOrDefault();
                if(typeArg != null && (WorkflowAnalyzer.InheritsFromWait(typeArg) || typeArg.Name == "Wait"))
                {
                    var diagnostic = Diagnostic.Create(descriptor, expr.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        public static void AnalyzeVariableDeclaration(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor)
        {
            var variableDecl = (VariableDeclarationSyntax)context.Node;
            var containingMethod = variableDecl.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethod == null)
                return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            foreach(var variable in variableDecl.Variables)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                if(symbol == null)
                    continue;

                var type = symbol.Type;
                if(type == null)
                    continue;

                if(IsUnserializableType(type))
                {
                    var diagnostic = Diagnostic.Create(
                        descriptor,
                        variable.GetLocation(),
                        symbol.Name,
                        type.ToDisplayString());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static bool IsUnserializableType(ITypeSymbol type, HashSet<ITypeSymbol>? visited = null)
        {
            if(type == null)
                return false;

            visited ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            if(!visited.Add(type))
                return false;

            // 1. Pointers
            if(type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer)
                return true;

            // 2. Delegates (Action, Func, EventHandler, Expression<T> …)
            if(type.TypeKind == TypeKind.Delegate)
                return true;

            // 3. IDisposable and other blocked interfaces
            if(ImplementsAnyBlockedInterface(type))
                return true;

            // 4. Leaf primitives — safe, skip deep walk
            if(IsPrimitiveLike(type))
                return false;

            // 5. Full deep structural walk
            return HasUnserializableMembersDeep(type, visited);
        }

        // ── Full deep structural walk ─────────────────────────────────────────────────
        private static bool ImplementsAnyBlockedInterface(ITypeSymbol type) => type.AllInterfaces
            .Any(i => _blockedInterfaces.Contains(i.ToDisplayString()));

        private static bool IsPrimitiveLike(ITypeSymbol type)
        {
            // Built-in keyword types (int, bool, string…)
            if(type.SpecialType != SpecialType.None)
                return true;

            var fullName = type.ToDisplayString();
            if(_primitiveFullNames.Contains(fullName))
                return true;

            // Nullable<T> where T is primitive
            if(type is INamedTypeSymbol { IsGenericType: true } nt &&
                nt.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            {
                return IsPrimitiveLike(nt.TypeArguments[0]);
            }

            // Enums are always serializable
            if(type.TypeKind == TypeKind.Enum)
                return true;

            return false;
        }

        private static readonly HashSet<string> _primitiveFullNames = new()
        {
            "System.Boolean",
            "System.Byte",
            "System.SByte",
            "System.Int16",
            "System.UInt16",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Single",
            "System.Double",
            "System.Decimal",
            "System.Char",
            "System.String",
            "System.Object",
            "System.DateTime",
            "System.DateTimeOffset",
            "System.TimeSpan",
            "System.Guid",
            "System.Uri",
            "System.Version",
        };
        private static readonly HashSet<string> _blockedInterfaces = new()
        {
            "System.IDisposable",
            "System.Runtime.InteropServices.ICustomMarshaler",
            "System.Runtime.InteropServices.SafeHandle",
        };

        private static bool HasUnserializableMembersDeep(ITypeSymbol type, HashSet<ITypeSymbol> visited)
        {
            // Walk the inheritance chain of this type
            var current = type;
            while(current != null && current.SpecialType == SpecialType.None)
            {
                foreach(var member in current.GetMembers())
                {
                    ITypeSymbol? memberType = member switch
                    {
                        IFieldSymbol { IsStatic: false } f => f.Type,
                        IPropertySymbol { IsStatic: false, IsIndexer: false } p => p.Type,
                        _ => null
                    };

                    if(memberType == null)
                        continue;

                    // Recurse fully into the member's type (its members, its members' members …)
                    if(IsUnserializableType(memberType, visited))
                        return true;
                }

                current = current.BaseType;
            }

            // Also check generic type arguments at this level
            //   e.g. Wrapper<Holder<SqlConnection>>  →  catches the SqlConnection inside Holder
            if(type is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                if(namedType.TypeArguments.Any(arg => IsUnserializableType(arg, visited)))
                    return true;
            }

            return false;
        }

        public static void AnalyzeYieldStatement(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor)
        {
            var yieldStatement = (YieldStatementSyntax)context.Node;
            var containingMethod = yieldStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethod == null)
                return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            var parent = yieldStatement.Parent;
            while(parent != null && parent != containingMethod)
            {
                if(parent is UsingStatementSyntax)
                {
                    var diagnostic = Diagnostic.Create(descriptor, yieldStatement.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }

                if(parent is BlockSyntax block)
                {
                    foreach(var stmt in block.Statements)
                    {
                        if(stmt.SpanStart >= yieldStatement.SpanStart)
                            break;
                        if(stmt is LocalDeclarationStatementSyntax localDecl && localDecl.UsingKeyword != default)
                        {
                            var diagnostic = Diagnostic.Create(descriptor, yieldStatement.GetLocation());
                            context.ReportDiagnostic(diagnostic);
                            return;
                        }
                    }
                }
                parent = parent.Parent;
            }
        }

        public static void AnalyzeMemberAccessExpression(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            var containingMethod = memberAccess.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethod == null)
                return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
            if(symbol != null)
            {
                var displayString = symbol.ToDisplayString();
                if(displayString.StartsWith("System.Threading.AsyncLocal") && displayString.EndsWith(".Value"))
                {
                    var diagnostic = Diagnostic.Create(descriptor, memberAccess.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }
                if(displayString.Contains("HttpContext"))
                {
                    var diagnostic = Diagnostic.Create(descriptor, memberAccess.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }
            }
        }

        public static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor)
        {
            var identifier = (IdentifierNameSyntax)context.Node;
            if(identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
            {
                return;
            }

            var containingMethod = identifier.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethod == null)
                return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
            if(symbol != null)
            {
                var displayString = symbol.ToDisplayString();
                if(displayString.Contains("System.Threading.AsyncLocal") && displayString.Contains(".Value"))
                {
                    var diagnostic = Diagnostic.Create(descriptor, identifier.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                } else if(displayString.Contains("HttpContext"))
                {
                    var diagnostic = Diagnostic.Create(descriptor, identifier.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        public static void AnalyzeWithStateInvocation(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            DiagnosticDescriptor wf005)
        {
            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if(methodSymbol == null || methodSymbol.Name != "WithState")
                return;

            var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if(argument == null)
                return;

            var typeInfo = context.SemanticModel.GetTypeInfo(argument.Expression);
            var type = typeInfo.Type;

            // FIX: Ignore whether the variable came from a sync or async method.
            // Only flag it if it's clearly an unserializable type (like a Delegate or IDisposable)
            if(type != null)
            {
                if(type.TypeKind == TypeKind.Delegate || WorkflowAnalyzer.ImplementsDisposable(type))
                {
                    var diagnostic = Diagnostic.Create(
                        wf005,
                        argument.GetLocation(),
                        argument.Expression.ToString(),
                        type.Name);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        public static void AnalyzeWithStateAnonymousType(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            DiagnosticDescriptor descriptor)
        {
            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if(methodSymbol == null || methodSymbol.Name != "WithState")
                return;

            var containingMethodNode = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(containingMethodNode == null)
                return;

            var containingMethodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethodNode);
            if(containingMethodSymbol == null)
                return;

            var containingType = containingMethodSymbol.ContainingType;
            if(!WorkflowAnalyzer.InheritsFromWorkflowContainer(containingType))
                return;

            foreach(var arg in invocation.ArgumentList.Arguments)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(arg.Expression);
                var type = typeInfo.Type;
                if(type != null && type.IsAnonymousType)
                {
                    var diagnostic = Diagnostic.Create(descriptor, arg.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
