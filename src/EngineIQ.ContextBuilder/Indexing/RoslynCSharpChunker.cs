using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EngineIQ.ContextBuilder.Indexing;

/// <summary>
/// Chunks a C# file per member (method/constructor/property with body ≥ 5 lines) using Roslyn.
/// Tiny types (records/DTOs/enums/interfaces) become one chunk per type. Returns <c>null</c> on
/// parse failure so the caller can fall back to <see cref="SlidingWindowChunker"/>.
/// </summary>
public sealed class RoslynCSharpChunker
{
    private const int MinMemberBodyLines = 5;

    public IReadOnlyList<RawChunk>? Chunk(string filePath, string content)
    {
        SyntaxTree tree;
        try
        {
            tree = CSharpSyntaxTree.ParseText(content);
        }
        catch
        {
            return null;
        }

        var root = tree.GetRoot();
        if (root.ContainsDiagnostics && root.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            // Still try — many "errors" are incomplete snippets; only bail if we get zero types.
        }

        var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();
        if (typeDeclarations.Count == 0)
            return null;

        var chunks = new List<RawChunk>();
        foreach (var type in typeDeclarations)
        {
            var ns = GetNamespace(type);
            var typeName = type.Identifier.Text;
            var header = $"// {ns}.{typeName}";
            var typeDeclLine = GetFirstLine(type);

            if (type is EnumDeclarationSyntax or InterfaceDeclarationSyntax)
            {
                chunks.Add(ToTypeChunk(type, header, typeDeclLine, typeName));
                continue;
            }

            if (type is not TypeDeclarationSyntax typeDecl)
            {
                chunks.Add(ToTypeChunk(type, header, typeDeclLine, typeName));
                continue;
            }

            var members = typeDecl.Members.Where(IsChunkableMember).ToList();
            // Methods/constructors always chunk; properties only when body ≥ MinMemberBodyLines.
            var substantial = members
                .Where(m => m is not PropertyDeclarationSyntax || CountNonEmptyLines(m) >= MinMemberBodyLines)
                .ToList();

            // Records / DTOs / types whose members are all tiny → one chunk for the type.
            if (substantial.Count == 0)
            {
                chunks.Add(ToTypeChunk(type, header, typeDeclLine, typeName));
                continue;
            }

            foreach (var member in substantial)
            {
                var body = $"{header}\n{typeDeclLine}\n{member.ToFullString().Trim()}";
                var span = member.GetLocation().GetLineSpan();
                chunks.Add(new RawChunk(
                    body,
                    span.StartLinePosition.Line + 1,
                    span.EndLinePosition.Line + 1,
                    BuildSymbol(ns, typeName, GetMemberName(member)),
                    GetMemberKind(member)));
            }
        }

        return chunks.Count > 0 ? chunks : null;
    }

    /// <summary>Backward-compatible overload used by older call sites/tests.</summary>
    public IReadOnlyList<RawChunk>? Chunk(string content) => Chunk(string.Empty, content);

    private static RawChunk ToTypeChunk(BaseTypeDeclarationSyntax type, string header, string typeDeclLine, string typeName)
    {
        var ns = GetNamespace(type);
        var span = type.GetLocation().GetLineSpan();
        return new RawChunk(
            $"{header}\n{type.ToFullString().Trim()}",
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            BuildSymbol(ns, typeName, null),
            "type");
    }

    private static bool IsChunkableMember(MemberDeclarationSyntax member) =>
        member is MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax
            or OperatorDeclarationSyntax or DestructorDeclarationSyntax;

    private static string GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        OperatorDeclarationSyntax o => o.OperatorToken.Text,
        DestructorDeclarationSyntax d => d.Identifier.Text,
        _ => "member"
    };

    private static string GetMemberKind(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax => "method",
        ConstructorDeclarationSyntax => "constructor",
        PropertyDeclarationSyntax => "property",
        OperatorDeclarationSyntax => "operator",
        DestructorDeclarationSyntax => "destructor",
        _ => "member"
    };

    private static int CountNonEmptyLines(SyntaxNode node) =>
        node.ToFullString().Replace("\r\n", "\n").Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));

    private static string GetFirstLine(SyntaxNode node)
    {
        var text = node.ToFullString().TrimStart();
        var nl = text.IndexOf('\n');
        return nl < 0 ? text.Trim() : text[..nl].Trim();
    }

    private static string GetNamespace(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            if (current is FileScopedNamespaceDeclarationSyntax fs)
                return fs.Name.ToString();
        }

        return "global";
    }

    private static string BuildSymbol(string ns, string typeName, string? member) =>
        string.IsNullOrEmpty(member) ? $"{ns}.{typeName}" : $"{ns}.{typeName}.{member}";
}
