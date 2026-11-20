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

        // ── WF010: write to an ignored state member ──────────────────────────────────

        /// <summary>
        /// A member marked [JsonIgnore] is not part of the persisted snapshot, so anything a workflow
        /// method writes into it is lost the moment the state is rehydrated (which happens after every
        /// yield). This catches the write at the point the author can see it.
        /// </summary>
        public static void AnalyzeIgnoredStateWrite(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor)
        {
            var assignment = (AssignmentExpressionSyntax)context.Node;
            var methodSymbol = context.SemanticModel.GetEnclosingSymbol(assignment.SpanStart) as IMethodSymbol;
            if(!WorkflowAnalyzer.IsWorkflowMethod(methodSymbol))
                return;

            if(methodSymbol?.ContainingType == null || !WorkflowAnalyzer.InheritsFromWorkflowContainer(methodSymbol.ContainingType))
                return;

            var symbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
            var member = symbol as ISymbol;
            if(member is not IPropertySymbol && member is not IFieldSymbol)
                return;

            if(member!.IsStatic || !HasIgnoreAttribute(member))
                return;

            var diagnostic = Diagnostic.Create(
                descriptor,
                assignment.Left.GetLocation(),
                member.Name,
                methodSymbol!.Name);
            context.ReportDiagnostic(diagnostic);
        }

        private static bool HasIgnoreAttribute(ISymbol member) => member.GetAttributes().Any(attribute =>
        {
            var name = attribute.AttributeClass?.Name;
            if(name != "JsonIgnoreAttribute")
                return false;

            var ns = attribute.AttributeClass?.ContainingNamespace?.ToDisplayString();
            return ns == "Newtonsoft.Json" || ns == "System.Text.Json.Serialization";
        });

        // ── WF011: interface/abstract typed state member ─────────────────────────────

        /// <summary>
        /// The snapshot serializer writes concrete types. A member whose declared type is an interface
        /// (or an abstract class) has no way back unless the author supplied polymorphic metadata, so
        /// deserialization either throws or silently yields an empty value.
        /// </summary>
        public static void AnalyzeStateShape(SymbolAnalysisContext context, DiagnosticDescriptor descriptor)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if(type.TypeKind != TypeKind.Class)
                return;

            var isContainer = WorkflowAnalyzer.InheritsFromWorkflowContainer(type);
            if(!isContainer)
                return;

            ReportUnreconstructableMembers(context, type, descriptor);

            // The state DTO supplied by the start method is serialized exactly like the container.
            var stateType = WorkflowAnalyzer.GetWorkflowStateType(type);
            if(stateType is INamedTypeSymbol namedState)
            {
                ReportUnreconstructableMembers(context, namedState, descriptor);
            }
        }

        /// <summary>
        /// Same check for a state type handed to <c>.WithState&lt;T&gt;()</c>.
        /// </summary>
        public static void AnalyzeWithStateShape(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            IMethodSymbol methodSymbol,
            DiagnosticDescriptor descriptor)
        {
            if(methodSymbol.Name != "WithState")
                return;

            ITypeSymbol? stateType = methodSymbol.TypeArguments.Length == 1
                ? methodSymbol.TypeArguments[0]
                : context.SemanticModel.GetTypeInfo(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression!).Type;

            if(stateType is not INamedTypeSymbol namedState || namedState.TypeKind != TypeKind.Class)
                return;

            var stateArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
            var location = stateArgument?.GetLocation() ?? invocation.GetLocation();
            foreach(var member in UnreconstructableMembers(namedState))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, member.Member.Name, member.Type.ToDisplayString()));
            }
        }

        private static void ReportUnreconstructableMembers(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            DiagnosticDescriptor descriptor)
        {
            foreach(var member in UnreconstructableMembers(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    member.Member.Locations.FirstOrDefault(),
                    member.Member.Name,
                    member.Type.ToDisplayString()));
            }
        }

        private static IEnumerable<(ISymbol Member, ITypeSymbol Type)> UnreconstructableMembers(INamedTypeSymbol type)
        {
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for(var current = type; current != null && current.SpecialType == SpecialType.None; current = current.BaseType)
            {
                foreach(var member in current.GetMembers())
                {
                    if(!seen.Add(member) || member.IsImplicitlyDeclared)
                        continue;

                    ITypeSymbol? memberType = member switch
                    {
                        IPropertySymbol { IsStatic: false, IsIndexer: false, SetMethod: not null } p => p.Type,
                        IFieldSymbol { IsStatic: false, IsConst: false } f => f.Type,
                        _ => null
                    };

                    if(memberType == null)
                        continue;

                    if(member.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonConverterAttribute"
                                                        || a.AttributeClass?.Name == "JsonConverter"))
                        continue;

                    if(IsUnreconstructableAbstraction(memberType))
                    {
                        yield return (member, memberType);
                    }
                }
            }
        }
        private static bool IsUnreconstructableAbstraction(ITypeSymbol type)
        {
            if(type.TypeKind != TypeKind.Interface && !(type.TypeKind == TypeKind.Class && type.IsAbstract))
                return false;

            // The serializer restores these from their concrete counterparts.
            var fullName = type is INamedTypeSymbol named && named.IsGenericType
                ? named.ConstructedFrom.ToDisplayString()
                : type.ToDisplayString();

            if(_reconstructableAbstractions.Contains(fullName))
                return false;

            // An explicit converter on the abstraction itself gives the serializer a way back.
            if(type.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonConverterAttribute"
                                              || a.AttributeClass?.Name == "JsonConverter"))
                return false;

            return true;
        }

        private static readonly HashSet<string> _reconstructableAbstractions = new()
        {
            "System.Collections.Generic.IEnumerable<T>",
            "System.Collections.Generic.ICollection<T>",
            "System.Collections.Generic.IList<T>",
            "System.Collections.Generic.IReadOnlyCollection<T>",
            "System.Collections.Generic.IReadOnlyList<T>",
            "System.Collections.Generic.ISet<T>",
            "System.Collections.Generic.IDictionary<TKey, TValue>",
            "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
            "System.IAsyncEnumerable<T>",
            "System.IFormattable",
        };

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
