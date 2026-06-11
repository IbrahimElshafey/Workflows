using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Workflows.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WorkflowCodeFixProvider)), Shared]
    public class WorkflowCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            WorkflowAnalyzer.DiagnosticIdWF201,
            WorkflowAnalyzer.DiagnosticIdWF001,
            WorkflowAnalyzer.DiagnosticIdWFUnsafeState);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if(root == null)
                return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            if(diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWF201)
            {
                var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault();
                if(declaration == null)
                    return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Seal workflow container class",
                        createChangedDocument: c => SealClassAsync(context.Document, declaration, c),
                        equivalenceKey: nameof(WorkflowCodeFixProvider)),
                    diagnostic);
            } else if(diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWF001)
            {
                var forEachStatement = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                    .OfType<ForEachStatementSyntax>()
                    .FirstOrDefault();
                if(forEachStatement == null)
                    return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Replace with yield return WaitSubWorkflow",
                        createChangedDocument: c => ReplaceWithWaitSubWorkflowAsync(
                            context.Document,
                            forEachStatement,
                            c),
                        equivalenceKey: "ReplaceWithWaitSubWorkflow"),
                    diagnostic);
            } else if(diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWFUnsafeState)
            {
                var variableDeclarator = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                    .OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault();
                if(variableDeclarator == null)
                    return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Migrate local variable to state POCO",
                        createChangedSolution: c => MigrateLocalToStatePocoAsync(
                            context.Document,
                            variableDeclarator,
                            c),
                        equivalenceKey: "MigrateLocalToStatePoco"),
                    diagnostic);
            }
        }

        private async Task<Document> ReplaceWithWaitSubWorkflowAsync(
            Document document,
            ForEachStatementSyntax forEachStatement,
            CancellationToken cancellationToken)
        {
            var expression = forEachStatement.Expression;
            var argument = SyntaxFactory.Argument(expression);
            var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument));
            var invocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName("WaitSubWorkflow"),
                argumentList);
            var yieldStatement = SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, invocation)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

            var newStatement = yieldStatement
                .WithLeadingTrivia(forEachStatement.GetLeadingTrivia())
                .WithTrailingTrivia(forEachStatement.GetTrailingTrivia());

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if(root == null)
                return document;

            var newRoot = root.ReplaceNode(forEachStatement, newStatement);
            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<Document> SealClassAsync(
            Document document,
            ClassDeclarationSyntax classDeclaration,
            CancellationToken cancellationToken)
        {
            var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space);

            var modifiers = classDeclaration.Modifiers;
            SyntaxTokenList newModifiers;

            if(modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                int partialIndex = modifiers.IndexOf(SyntaxKind.PartialKeyword);
                newModifiers = modifiers.Insert(partialIndex, sealedToken);
            } else
            {
                newModifiers = modifiers.Add(sealedToken);
            }

            var newClassDeclaration = classDeclaration.WithModifiers(newModifiers);
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if(root == null)
                return document;

            var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }


        /// <summary>
        /// Roslyn code fix: migrates a local variable declaration inside a workflow method into a property on a "State
        /// POCO" class, either pre-existing or newly generated.  Branches: A) Method already has a state parameter as
        /// its first argument → strip the local declaration → rewrite all reads/writes of the variable to
        /// state.PropName → inject the property into the state class (same file or other document)  B) No state
        /// parameter yet → create a brand-new public StateXxx class → add it as the first parameter of the workflow
        /// method → rewrite references inside the method body → append the new class to the end of the current file
        /// </summary>
        private async Task<Solution> MigrateLocalToStatePocoAsync(
                Document document,
                VariableDeclaratorSyntax localDeclarator,
                CancellationToken ct)
            {
                SemanticModel semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                SyntaxNode root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);

                // ── Resolve the local variable ────────────────────────────────────────
                var localDeclaration = localDeclarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
                if(localDeclaration is null)
                    return document.Project.Solution;

                string localVarName = localDeclarator.Identifier.ValueText;
                TypeSyntax localVarType = ((VariableDeclarationSyntax)localDeclarator.Parent!).Type;

                // Resolve the type to its fully-qualified form for use in new code.
                ITypeSymbol? typeSymbol = semanticModel.GetTypeInfo(localVarType, ct).Type;
                TypeSyntax resolvedType = typeSymbol is not null
                    ? ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                    : localVarType.WithoutTrivia();

                // ── Walk up to the containing method ─────────────────────────────────
                var methodDecl = localDeclaration.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if(methodDecl is null)
                    return document.Project.Solution;

                // ── Decide which branch to take ───────────────────────────────────────
                bool hasStateParam = TryGetStateParameter(
                    methodDecl,
                    semanticModel,
                    ct,
                    out ParameterSyntax? stateParam,
                    out ITypeSymbol? stateTypeSymbol,
                    out string? stateParamName);

                if(hasStateParam)
                {
                    return await Branch_ExistingState(
                        document,
                        root,
                        semanticModel,
                        methodDecl,
                        localDeclaration,
                        localDeclarator,
                        localVarName,
                        resolvedType,
                        stateParam!,
                        stateTypeSymbol!,
                        stateParamName!,
                        ct)
                        .ConfigureAwait(false);
                } else
                {
                    return await Branch_NewState(
                        document,
                        root,
                        semanticModel,
                        methodDecl,
                        localDeclaration,
                        localDeclarator,
                        localVarName,
                        resolvedType,
                        ct)
                        .ConfigureAwait(false);
                }
            }

            // =========================================================================
            // Branch A – state parameter already exists
            // =========================================================================

            private async Task<Solution> Branch_ExistingState(
                Document document,
                SyntaxNode root,
                SemanticModel semanticModel,
                MethodDeclarationSyntax methodDecl,
                LocalDeclarationStatementSyntax localDeclaration,
                VariableDeclaratorSyntax localDeclarator,
                string localVarName,
                TypeSyntax resolvedType,
                ParameterSyntax stateParam,
                ITypeSymbol stateTypeSymbol,
                string stateParamName,
                CancellationToken ct)
            {
                string propName = ToPascalCase(localVarName);
                ExpressionSyntax? initializer = localDeclarator.Initializer?.Value;

                // ── 1. Rewrite the method body in the current document ────────────────
                //       a) Remove the local declaration statement
                //       b) Replace all references to `localVarName` with `stateParamName.PropName`
            //       c) If there was an initializer, prepend  state.Prop = value;  before first use

            var memberAccess = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(stateParamName),
                IdentifierName(propName));

                SyntaxNode newRoot = root.TrackNodes(methodDecl, localDeclaration);

                // Remove the local declaration
                var trackedLocalDecl = newRoot.GetCurrentNode(localDeclaration)!;
                newRoot = newRoot.RemoveNode(trackedLocalDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;

                // If the local had an initializer, inject  state.Prop = initializer;
                // right before the first reference inside the (now-modified) method body.
                if(initializer is not null)
                {
                    var trackedMethod = newRoot.GetCurrentNode(methodDecl)!;
                    var assignmentStmt = BuildAssignmentStatement(memberAccess, initializer);

                    // Find the first statement in the body and insert before it,
                    // or prepend to the block if there are no statements yet.
                    var body = trackedMethod.DescendantNodes()
                        .OfType<BlockSyntax>()
                        .FirstOrDefault(b => b.Parent == trackedMethod);

                    if(body is not null && body.Statements.Count > 0)
                    {
                        var firstStatement = body.Statements[0];
                        newRoot = newRoot.InsertNodesBefore(firstStatement, new[] { assignmentStmt });
                    } else if(body is not null)
                    {
                        var newBody = body.AddStatements(assignmentStmt);
                        newRoot = newRoot.ReplaceNode(body, newBody);
                    }
                }

                // Rewrite all remaining identifiers that refer to the old local variable
                newRoot = RewriteLocalReferences(newRoot, semanticModel, localVarName, memberAccess, ct);

                Solution solution = document.Project.Solution;
                solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);

                // ── 2. Inject the property into the state class ───────────────────────
                solution = await InjectPropertyIntoStateClass(solution, stateTypeSymbol, propName, resolvedType, ct)
                    .ConfigureAwait(false);

                return solution;
            }

            // =========================================================================
            // Branch B – no state parameter; create one from scratch
            // =========================================================================

            private async Task<Solution> Branch_NewState(
                Document document,
                SyntaxNode root,
                SemanticModel semanticModel,
                MethodDeclarationSyntax methodDecl,
                LocalDeclarationStatementSyntax localDeclaration,
                VariableDeclaratorSyntax localDeclarator,
                string localVarName,
                TypeSyntax resolvedType,
                CancellationToken ct)
            {
                string methodName = methodDecl.Identifier.ValueText;
                string stateClassName = BuildStateClassName(methodName);
                string stateParamName = "state";
                string propName = ToPascalCase(localVarName);
                ExpressionSyntax? initializer = localDeclarator.Initializer?.Value;

                var memberAccess = MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(stateParamName),
                    IdentifierName(propName));

                // ── 1. Rewrite the existing document ─────────────────────────────────

                SyntaxNode newRoot = root.TrackNodes(methodDecl, localDeclaration);

                // a) Remove local declaration
                var trackedLocalDecl = newRoot.GetCurrentNode(localDeclaration)!;
                newRoot = newRoot.RemoveNode(trackedLocalDecl, SyntaxRemoveOptions.KeepLeadingTrivia)!;

                // b) Add `state` as the first parameter
                var trackedMethod = newRoot.GetCurrentNode(methodDecl)!
                as MethodDeclarationSyntax ??
                    (MethodDeclarationSyntax)newRoot.GetCurrentNode(methodDecl)!;

                var stateParameter = Parameter(Identifier(stateParamName))
                    .WithType(ParseTypeName(stateClassName).WithTrailingTrivia(Space));

                var newParams = trackedMethod.ParameterList.Parameters.Count == 0
                    ? trackedMethod.ParameterList.AddParameters(stateParameter)
                    : trackedMethod.ParameterList
                        .WithParameters(
                            trackedMethod.ParameterList.Parameters
                                    .Insert(
                                        0,
                                        stateParameter.WithTrailingTrivia(
                                                    TriviaList(
                                                        SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " ")))));

                var methodWithParam = trackedMethod.WithParameterList(newParams);
                newRoot = newRoot.ReplaceNode(trackedMethod, methodWithParam);

                // c) Inject initializer assignment if present
                if(initializer is not null)
                {
                    var bodyMethod = newRoot.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .First(m => m.Identifier.ValueText == methodName);

                    var body = bodyMethod.Body;
                    if(body is not null)
                    {
                        var assignmentStmt = BuildAssignmentStatement(memberAccess, initializer);
                        var newBody = body.Statements.Count > 0
                            ? body.WithStatements(body.Statements.Insert(0, assignmentStmt))
                            : body.AddStatements(assignmentStmt);
                        newRoot = newRoot.ReplaceNode(body, newBody);
                    }
                }

                // d) Rewrite all remaining references to the old local
                newRoot = RewriteLocalReferences(newRoot, semanticModel, localVarName, memberAccess, ct);

                // e) Append the new state class to the compilation unit
                var stateClass = BuildStateClass(stateClassName, propName, resolvedType);
                var compilationUnit = (CompilationUnitSyntax)newRoot;

                // Place the class inside the same namespace if one exists, otherwise at top level.
                var namespaceDecl = compilationUnit.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

                if(namespaceDecl is not null)
                {
                    var newNs = namespaceDecl.AddMembers(stateClass);
                    newRoot = compilationUnit.ReplaceNode(namespaceDecl, newNs);
                } else
                {
                    newRoot = compilationUnit.AddMembers(stateClass);
                }

                Solution solution = document.Project.Solution;
                solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);

                return solution;
            }

            // =========================================================================
            // Helpers – state parameter detection
            // =========================================================================

            /// <summary>
            /// Returns true when the method's first parameter is a user-defined reference type whose name ends with
            /// "State" (or any POCO-like heuristic you prefer).
            /// </summary>
            private static bool TryGetStateParameter(
                MethodDeclarationSyntax method,
                SemanticModel semanticModel,
                CancellationToken ct,
                out ParameterSyntax? parameter,
                out ITypeSymbol? typeSymbol,
                out string? paramName)
            {
                parameter = null;
                typeSymbol = null;
                paramName = null;

                var first = method.ParameterList.Parameters.FirstOrDefault();
                if(first is null)
                    return false;
                if(first.Type is null)
                    return false;

                var symbol = semanticModel.GetTypeInfo(first.Type, ct).Type;
                if(symbol is null)
                    return false;

                // Heuristic: class whose name ends with "State"
                bool looksLikeState =
                symbol.TypeKind == TypeKind.Class && symbol.Name.EndsWith("State", System.StringComparison.Ordinal);

                if(!looksLikeState)
                    return false;

                parameter = first;
                typeSymbol = symbol;
                paramName = first.Identifier.ValueText;
                return true;
            }

            // =========================================================================
            // Helpers – reference rewriting
            // =========================================================================

            /// <summary>
            /// Walks the (already-modified) syntax tree and replaces every simple IdentifierNameSyntax that still
            /// spells <paramref name="localVarName"/> with the <paramref name="replacement"/> member-access expression.
            ///  We operate on the new syntax tree after the declaration was removed, so the semantic model from the
            /// original compilation is used only to avoid renaming things that happen to share the identifier but are
            /// unrelated (e.g., a parameter or field of the same name declared elsewhere).
            /// </summary>
            private static SyntaxNode RewriteLocalReferences(
                SyntaxNode root,
                SemanticModel semanticModel,
                string localVarName,
                MemberAccessExpressionSyntax replacement,
                CancellationToken ct)
            {
                // Collect candidate nodes from the ORIGINAL tree's positions via the
                // semantic model, then map them to the new tree via annotation tracking
                // or simply do a textual match restricted to the right parent scope.

                var toReplace = root.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(
                        id => id.Identifier.ValueText == localVarName &&
                            // Exclude left-hand side of member access: state.Foo – we don't
                            // want to accidentally double-rewrite something we already fixed.
                            (id.Parent is not MemberAccessExpressionSyntax mae ||
                            mae.Expression != id))
                    .ToList();

                if(toReplace.Count == 0)
                    return root;

                return root.ReplaceNodes(
                    toReplace,
                    (original, _) => replacement
                    .WithLeadingTrivia(original.GetLeadingTrivia())
                        .WithTrailingTrivia(original.GetTrailingTrivia()));
            }

            // =========================================================================
            // Helpers – property injection into an existing state class
            // =========================================================================

            private static async Task<Solution> InjectPropertyIntoStateClass(
                Solution solution,
                ITypeSymbol stateTypeSymbol,
                string propName,
                TypeSyntax propType,
                CancellationToken ct)
            {
                // The class may live in any document of the solution.
                foreach(var location in stateTypeSymbol.Locations)
                {
                    if(!location.IsInSource)
                        continue;

                    var docId = solution.GetDocumentId(location.SourceTree);
                    if(docId is null)
                        continue;

                    var doc = solution.GetDocument(docId)!;
                    var docRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    if(docRoot is null)
                        continue;

                    var classNode = docRoot
                    .DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.ValueText == stateTypeSymbol.Name);

                    if(classNode is null)
                        continue;

                    // Avoid duplicate injection
                    bool alreadyExists = classNode.Members
                        .OfType<PropertyDeclarationSyntax>()
                        .Any(p => p.Identifier.ValueText == propName);

                    if(alreadyExists)
                        break;

                    var newProp = BuildAutoProperty(propType, propName);
                    var newClass = classNode.AddMembers(newProp);
                    var newDocRoot = docRoot.ReplaceNode(classNode, newClass);

                    solution = solution.WithDocumentSyntaxRoot(docId, newDocRoot);
                    break;
                }

                return solution;
            }

            // =========================================================================
            // Helpers – syntax construction
            // =========================================================================

            /// <summary>
            /// Builds:  public TYPE PropName { get; set; }
            /// </summary>
            private static PropertyDeclarationSyntax BuildAutoProperty(TypeSyntax type, string name)
            {
                return PropertyDeclaration(type.WithTrailingTrivia(Space), Identifier(name))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(Space))
                    .AddAccessorListAccessors(
                        AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                        AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .WithLeadingTrivia(ElasticCarriageReturnLineFeed, Whitespace("    "))
                    .WithTrailingTrivia(ElasticCarriageReturnLineFeed);
            }

            /// <summary>
            /// Builds:  state.Prop = value;
            /// </summary>
            private static ExpressionStatementSyntax BuildAssignmentStatement(
                MemberAccessExpressionSyntax lhs,
                ExpressionSyntax rhs)
            { return ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, lhs, rhs)); }

            /// <summary>
            /// Builds a complete state POCO class with one initial property: <code> public class WorkflowNameState {
            /// public TYPE PropName { get; set; } }</code>
            /// </summary>
            private static ClassDeclarationSyntax BuildStateClass(
                string className,
                string firstPropName,
                TypeSyntax firstPropType)
            {
                var property = BuildAutoProperty(firstPropType, firstPropName);

                return ClassDeclaration(className)
                    .AddModifiers(
                        Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(Space),
                        Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(Space))
                    .AddMembers(property)
                    .WithLeadingTrivia(ElasticCarriageReturnLineFeed, ElasticCarriageReturnLineFeed)
                    .WithTrailingTrivia(ElasticCarriageReturnLineFeed)
                    .NormalizeWhitespace();
            }

            // =========================================================================
            // Helpers – naming conventions
            // =========================================================================

            /// <summary>
            /// Derives the state class name from the workflow method name. Examples: ProcessOrder       →
            /// ProcessOrderState RunPaymentWorkflow → RunPaymentWorkflowState  (avoids double "Workflow") ExecuteAsync 
            ///      → ExecuteState
            /// </summary>
            private static string BuildStateClassName(string methodName)
            {
                // Strip common async suffixes so we get cleaner names.
                string stripped = methodName.EndsWith("Async", System.StringComparison.Ordinal)
                    ? methodName.Substring(0, methodName.Length - "Async".Length)
                    : methodName;

                return stripped.Length == 0 ? "WorkflowState" : stripped + "State";
            }

            /// <summary>
            /// camelCase → PascalCase; already-Pascal inputs are unchanged.
            /// </summary>
            private static string ToPascalCase(string name)
            {
                if(string.IsNullOrEmpty(name))
                    return name;
                return char.ToUpperInvariant(name[0]) + name.Substring(1);
            }
    }
}

