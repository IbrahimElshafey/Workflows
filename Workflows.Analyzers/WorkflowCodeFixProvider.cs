using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Workflows.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WorkflowCodeFixProvider)), Shared]
    public class WorkflowCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(WorkflowAnalyzer.DiagnosticIdWF201, WorkflowAnalyzer.DiagnosticIdWF001, WorkflowAnalyzer.DiagnosticIdWFUnsafeState);

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            if (diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWF201)
            {
                var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (declaration == null) return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Seal workflow container class",
                        createChangedDocument: c => SealClassAsync(context.Document, declaration, c),
                        equivalenceKey: nameof(WorkflowCodeFixProvider)),
                    diagnostic);
            }
            else if (diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWF001)
            {
                var forEachStatement = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<ForEachStatementSyntax>().FirstOrDefault();
                if (forEachStatement == null) return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Replace with yield return WaitSubWorkflow",
                        createChangedDocument: c => ReplaceWithWaitSubWorkflowAsync(context.Document, forEachStatement, c),
                        equivalenceKey: "ReplaceWithWaitSubWorkflow"),
                    diagnostic);
            }
            else if (diagnostic.Id == WorkflowAnalyzer.DiagnosticIdWFUnsafeState)
            {
                var variableDeclarator = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                if (variableDeclarator == null) return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Migrate local variable to state POCO",
                        createChangedSolution: c => MigrateLocalToStatePocoAsync(context.Document, variableDeclarator, c),
                        equivalenceKey: "MigrateLocalToStatePoco"),
                    diagnostic);
            }
        }

        private async Task<Document> ReplaceWithWaitSubWorkflowAsync(Document document, ForEachStatementSyntax forEachStatement, CancellationToken cancellationToken)
        {
            var expression = forEachStatement.Expression;
            var argument = SyntaxFactory.Argument(expression);
            var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument));
            var invocation = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("WaitSubWorkflow"), argumentList);
            var yieldStatement = SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, invocation)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

            var newStatement = yieldStatement
                .WithLeadingTrivia(forEachStatement.GetLeadingTrivia())
                .WithTrailingTrivia(forEachStatement.GetTrailingTrivia());

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            var newRoot = root.ReplaceNode(forEachStatement, newStatement);
            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<Document> SealClassAsync(Document document, ClassDeclarationSyntax classDeclaration, CancellationToken cancellationToken)
        {
            var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);
            
            var modifiers = classDeclaration.Modifiers;
            SyntaxTokenList newModifiers;

            if (modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                int partialIndex = modifiers.IndexOf(SyntaxKind.PartialKeyword);
                newModifiers = modifiers.Insert(partialIndex, sealedToken);
            }
            else
            {
                newModifiers = modifiers.Add(sealedToken);
            }

            var newClassDeclaration = classDeclaration.WithModifiers(newModifiers);
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<Solution> MigrateLocalToStatePocoAsync(Document document, VariableDeclaratorSyntax variableDeclarator, CancellationToken cancellationToken)
        {
            var solution = document.Project.Solution;
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return solution;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel == null) return solution;

            var localSymbol = semanticModel.GetDeclaredSymbol(variableDeclarator) as ILocalSymbol;
            if (localSymbol == null) return solution;

            var propName = variableDeclarator.Identifier.ValueText;

            // Get type syntax string
            var typeSymbol = localSymbol.Type;
            var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // Find containing class
            var classDeclaration = variableDeclarator.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration == null) return solution;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
            if (classSymbol == null) return solution;

            // Find the local declaration statement to remove or replace
            var localDeclStatement = variableDeclarator.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
            if (localDeclStatement == null) return solution;

            // Resolve State POCO symbol
            INamedTypeSymbol? stateTypeSymbol = null;
            var baseType = classSymbol.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && baseType.Name == "WorkflowContainer")
                {
                    stateTypeSymbol = baseType.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
                    break;
                }
                baseType = baseType.BaseType;
            }

            // Find all references to the local variable in the syntax tree (excluding its declaration identifier)
            var nodesToReplace = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(node => !(node.Parent is VariableDeclaratorSyntax) && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(node).Symbol, localSymbol))
                .ToList();

            if (stateTypeSymbol == null)
            {
                var stateClassName = classDeclaration.Identifier.ValueText + "State";
                
                // Create property declaration
                var newPropertyDeclaration = SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(typeName),
                    SyntaxFactory.Identifier(propName))
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .AddAccessorListAccessors(
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    )
                    .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

                // Create the state class declaration
                var stateClassDeclaration = SyntaxFactory.ClassDeclaration(stateClassName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .AddMembers(newPropertyDeclaration)
                    .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

                // Create base list WorkflowContainer<StateClassName>
                var baseList = classDeclaration.BaseList;
                BaseListSyntax? newBaseList = null;
                if (baseList != null)
                {
                    var baseTypeSyntax = SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.GenericName(
                            SyntaxFactory.Identifier("WorkflowContainer"),
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                    SyntaxFactory.IdentifierName(stateClassName)
                                )
                            )
                        )
                    );
                    
                    var types = baseList.Types;
                    var newTypes = SyntaxFactory.SeparatedList<BaseTypeSyntax>();
                    bool replaced = false;
                    foreach (var type in types)
                    {
                        var typeStr = type.Type.ToString();
                        if (typeStr == "WorkflowContainer" || typeStr == "Workflows.Definition.WorkflowContainer" || typeStr.StartsWith("WorkflowContainer"))
                        {
                            newTypes = newTypes.Add(baseTypeSyntax);
                            replaced = true;
                        }
                        else
                        {
                            newTypes = newTypes.Add(type);
                        }
                    }
                    if (!replaced)
                    {
                        newTypes = newTypes.Insert(0, baseTypeSyntax);
                    }
                    newBaseList = baseList.WithTypes(newTypes);
                }
                else
                {
                    var baseTypeSyntax = SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.GenericName(
                            SyntaxFactory.Identifier("WorkflowContainer"),
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                    SyntaxFactory.IdentifierName(stateClassName)
                                )
                            )
                        )
                    );
                    newBaseList = SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseTypeSyntax));
                }

                // Track all nodes to update safely
                var nodesToTrack = nodesToReplace.Cast<SyntaxNode>().Concat(new SyntaxNode[] { localDeclStatement, classDeclaration }).ToList();
                var rewrittenRoot = root.TrackNodes(nodesToTrack);

                // 1. Replace variable usages
                foreach (var node in nodesToReplace)
                {
                    var trackedNode = rewrittenRoot.GetCurrentNode(node);
                    if (trackedNode != null)
                    {
                        var replacement = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("state"),
                            SyntaxFactory.IdentifierName(propName))
                            .WithTriviaFrom(trackedNode);
                        rewrittenRoot = rewrittenRoot.ReplaceNode(trackedNode, replacement);
                    }
                }

                // 2. Update class BaseList and insert state class
                var trackedClass = rewrittenRoot.GetCurrentNode(classDeclaration);
                if (trackedClass != null)
                {
                    var updatedClass = trackedClass;
                    if (newBaseList != null)
                    {
                        updatedClass = updatedClass.WithBaseList(newBaseList);
                    }
                    
                    rewrittenRoot = rewrittenRoot.ReplaceNode(trackedClass, updatedClass);
                    
                    var reFoundClass = rewrittenRoot.GetCurrentNode(classDeclaration);
                    if (reFoundClass != null)
                    {
                        rewrittenRoot = rewrittenRoot.InsertNodesAfter(reFoundClass, new[] { stateClassDeclaration });
                    }
                }

                // 3. Remove or replace local declaration
                var trackedDecl = rewrittenRoot.GetCurrentNode(localDeclStatement);
                if (trackedDecl != null)
                {
                    var initializer = variableDeclarator.Initializer;
                    if (initializer != null)
                    {
                        var assignment = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("state"),
                                    SyntaxFactory.IdentifierName(propName)),
                                initializer.Value))
                            .WithTriviaFrom(trackedDecl);
                        rewrittenRoot = rewrittenRoot.ReplaceNode(trackedDecl, assignment);
                    }
                    else
                    {
                        rewrittenRoot = rewrittenRoot.RemoveNode(trackedDecl, SyntaxRemoveOptions.KeepLeadingTrivia | SyntaxRemoveOptions.KeepTrailingTrivia);
                    }
                }

                return solution.WithDocumentSyntaxRoot(document.Id, rewrittenRoot);
            }

            var syntaxRef = stateTypeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null) return solution;

            var stateClassDecl = await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) as ClassDeclarationSyntax;
            if (stateClassDecl == null) return solution;

            var stateDocument = solution.GetDocument(syntaxRef.SyntaxTree);
            if (stateDocument == null) return solution;

            // Create property declaration
            var propertyDeclaration = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(typeName),
                SyntaxFactory.Identifier(propName))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                )
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            var newStateClassDecl = stateClassDecl.AddMembers(propertyDeclaration);

            if (stateDocument.Id == document.Id)
            {
                // Same file! Use TrackNodes to apply both modifications safely to the same tree
                var nodesToTrack = nodesToReplace.Cast<SyntaxNode>().Concat(new SyntaxNode[] { localDeclStatement, stateClassDecl }).ToList();
                var rewrittenRoot = root.TrackNodes(nodesToTrack);

                // 1. Replace variable usages
                foreach (var node in nodesToReplace)
                {
                    var trackedNode = rewrittenRoot.GetCurrentNode(node);
                    if (trackedNode != null)
                    {
                        var replacement = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("state"),
                            SyntaxFactory.IdentifierName(propName))
                            .WithTriviaFrom(trackedNode);
                        rewrittenRoot = rewrittenRoot.ReplaceNode(trackedNode, replacement);
                    }
                }

                // 2. Add property to state class
                var trackedStateClass = rewrittenRoot.GetCurrentNode(stateClassDecl);
                if (trackedStateClass != null)
                {
                    var updatedStateClass = trackedStateClass.AddMembers(propertyDeclaration);
                    rewrittenRoot = rewrittenRoot.ReplaceNode(trackedStateClass, updatedStateClass);
                }

                // 3. Remove or replace local declaration
                var trackedDecl = rewrittenRoot.GetCurrentNode(localDeclStatement);
                if (trackedDecl != null)
                {
                    var initializer = variableDeclarator.Initializer;
                    if (initializer != null)
                    {
                        var assignment = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("state"),
                                    SyntaxFactory.IdentifierName(propName)),
                                initializer.Value))
                            .WithTriviaFrom(trackedDecl);
                        rewrittenRoot = rewrittenRoot.ReplaceNode(trackedDecl, assignment);
                    }
                    else
                    {
                        rewrittenRoot = rewrittenRoot.RemoveNode(trackedDecl, SyntaxRemoveOptions.KeepLeadingTrivia | SyntaxRemoveOptions.KeepTrailingTrivia);
                    }
                }

                return solution.WithDocumentSyntaxRoot(document.Id, rewrittenRoot);
            }
            else
            {
                // Different files!
                // 1. Rewrite usages and replace declaration in workflow file
                var nodesToTrack = nodesToReplace.Cast<SyntaxNode>().Concat(new SyntaxNode[] { localDeclStatement }).ToList();
                var rewrittenWorkflowRoot = root.TrackNodes(nodesToTrack);

                foreach (var node in nodesToReplace)
                {
                    var trackedNode = rewrittenWorkflowRoot.GetCurrentNode(node);
                    if (trackedNode != null)
                    {
                        var replacement = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("state"),
                            SyntaxFactory.IdentifierName(propName))
                            .WithTriviaFrom(trackedNode);
                        rewrittenWorkflowRoot = rewrittenWorkflowRoot.ReplaceNode(trackedNode, replacement);
                    }
                }

                var trackedDecl = rewrittenWorkflowRoot.GetCurrentNode(localDeclStatement);
                if (trackedDecl != null)
                {
                    var initializer = variableDeclarator.Initializer;
                    if (initializer != null)
                    {
                        var newAssignment = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("state"),
                                    SyntaxFactory.IdentifierName(propName)),
                                initializer.Value))
                            .WithTriviaFrom(trackedDecl);
                        rewrittenWorkflowRoot = rewrittenWorkflowRoot.ReplaceNode(trackedDecl, newAssignment);
                    }
                    else
                    {
                        rewrittenWorkflowRoot = rewrittenWorkflowRoot.RemoveNode(trackedDecl, SyntaxRemoveOptions.KeepLeadingTrivia | SyntaxRemoveOptions.KeepTrailingTrivia);
                    }
                }

                // 2. Add property to state POCO file
                var stateRoot = await stateDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (stateRoot == null) return solution;

                var newStateRoot = stateRoot.ReplaceNode(stateClassDecl, newStateClassDecl);

                var newSolution = solution.WithDocumentSyntaxRoot(document.Id, rewrittenWorkflowRoot);
                newSolution = newSolution.WithDocumentSyntaxRoot(stateDocument.Id, newStateRoot);
                return newSolution;
            }
        }
    }
}
