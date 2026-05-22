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
            ImmutableArray.Create(WorkflowAnalyzer.DiagnosticIdWF201, WorkflowAnalyzer.DiagnosticIdWF001);

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
    }
}
