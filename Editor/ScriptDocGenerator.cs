// Assets/Editor/ScriptDocGenerator.cs
// Generates Doxygen-ish docs (JSON + Markdown) for scripts in Assets/Scripts.
//
// HOW TO USE
// 1) Put this file in Assets/Editor/
// 2) In Unity: Tools -> Docs -> Generate Script Docs
//
// OPTIONAL (Recommended): Enable Roslyn parsing
// - Add Microsoft.CodeAnalysis + Microsoft.CodeAnalysis.CSharp DLLs (and dependencies) to Assets/Editor/Roslyn/
// - Unity will then use the rich parser automatically.
//
// Output (now unified under ProjectExports):
// ProjectExports/ScriptDocs/script_docs.json
// ProjectExports/ScriptDocs/README.md
// ProjectExports/ScriptDocs/Classes/*.md

#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Roslyn (optional)
#if SCRIPT_DOCS_ROSLYN
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
#endif

public static class ScriptDocGenerator
{
    private const string DefaultScriptsRoot = "Assets/Scripts";

    // Unified output location (same root as ProjectLayoutExporter)
    private static string OutputRoot =>
        Path.Combine(ProjectLayoutExporter.GetExportDirectory(), "ScriptDocs");

    private static string OutputJson =>
        Path.Combine(OutputRoot, "script_docs.json");

    private static string OutputReadme =>
        Path.Combine(OutputRoot, "README.md");

    private static string OutputClassesDir =>
        Path.Combine(OutputRoot, "Classes");

    [MenuItem("Tools/Docs/Generate Script Docs")]
    public static void Generate()
    {
        try
        {
            var scriptsRoot = DefaultScriptsRoot;
            if (!AssetDatabase.IsValidFolder(scriptsRoot))
            {
                EditorUtility.DisplayDialog("Script Docs",
                    $"Folder not found: {scriptsRoot}\nEdit DefaultScriptsRoot in ScriptDocGenerator if needed.",
                    "OK");
                return;
            }

            Directory.CreateDirectory(OutputRoot);
            Directory.CreateDirectory(OutputClassesDir);

            var csFiles = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
                                   .Where(p => !p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                                   .ToList();

            var doc = new ProjectDoc
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                scriptsRoot = scriptsRoot,
                fileCount = csFiles.Count,
                files = new List<FileDoc>()
            };

#if SCRIPT_DOCS_ROSLYN
            bool usedRoslyn = TryGenerateWithRoslyn(csFiles, doc);
            doc.parser = usedRoslyn ? "roslyn" : "fallback";
#else
            doc.parser = "fallback";
            GenerateWithFallback(csFiles, doc);
#endif

            // Write JSON
            var json = JsonUtility.ToJson(doc, true);
            File.WriteAllText(OutputJson, json, Encoding.UTF8);

            // Write Markdown pages
            WriteMarkdown(doc);

            // Output is outside Assets/, so Refresh does not import these files as assets.
            // Keeping Refresh is harmless (in case user later moves output under Assets/).
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Script Docs",
                $"Done.\n\nJSON: {OutputJson}\nMarkdown: {OutputRoot}\n\nParser: {doc.parser}",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Script Docs", $"Failed:\n{ex.Message}", "OK");
        }
    }

#if SCRIPT_DOCS_ROSLYN
    private static bool TryGenerateWithRoslyn(List<string> csFiles, ProjectDoc doc)
    {
        try
        {
            foreach (var path in csFiles)
            {
                var text = File.ReadAllText(path);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = tree.GetCompilationUnitRoot();

                var fileDoc = new FileDoc
                {
                    assetPath = ToAssetPath(path),
                    fullPath = path,
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(path).ToString("o"),
                    namespaces = new List<NamespaceDoc>(),
                    types = new List<TypeDoc>()
                };

                // Collect namespace blocks + types
                foreach (var member in root.Members)
                {
                    if (member is NamespaceDeclarationSyntax ns)
                        ParseNamespace(ns, fileDoc);
                    else if (member is FileScopedNamespaceDeclarationSyntax fns)
                        ParseFileScopedNamespace(fns, fileDoc);
                    else
                        ParseTopLevel(member, fileDoc, "");
                }

                doc.files.Add(fileDoc);
            }

            // Post-process: build a map for link/indexing
            BuildTypeIndex(doc);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Roslyn parse failed; falling back. Reason: {e.Message}");
            doc.files.Clear();
            GenerateWithFallback(csFiles, doc);
            return false;
        }
    }

    private static void ParseFileScopedNamespace(FileScopedNamespaceDeclarationSyntax fns, FileDoc fileDoc)
    {
        var nsName = fns.Name.ToString();
        foreach (var m in fns.Members)
            ParseTopLevel(m, fileDoc, nsName);
    }

    private static void ParseNamespace(NamespaceDeclarationSyntax ns, FileDoc fileDoc)
    {
        var nsName = ns.Name.ToString();
        foreach (var m in ns.Members)
            ParseTopLevel(m, fileDoc, nsName);
    }

    private static void ParseTopLevel(MemberDeclarationSyntax member, FileDoc fileDoc, string nsName)
    {
        if (member is BaseTypeDeclarationSyntax typeDecl)
        {
            var typeDoc = ParseType(typeDecl, nsName, fileDoc.assetPath);
            fileDoc.types.Add(typeDoc);
        }
        else if (member is DelegateDeclarationSyntax del)
        {
            // optional: handle delegates
        }
    }

    private static TypeDoc ParseType(BaseTypeDeclarationSyntax decl, string nsName, string assetPath)
    {
        var typeKind = decl.Kind().ToString(); // ClassDeclaration, StructDeclaration, InterfaceDeclaration, EnumDeclaration
        var name = decl.Identifier.Text;
        var fullName = string.IsNullOrEmpty(nsName) ? name : $"{nsName}.{name}";

        var typeDoc = new TypeDoc
        {
            name = name,
            fullName = fullName,
            @namespace = nsName,
            kind = typeKind.Replace("Declaration", "").ToLowerInvariant(),
            file = assetPath,
            modifiers = decl.Modifiers.Select(m => m.Text).ToList(),
            attributes = GetAttributes(decl.AttributeLists),
            summary = ExtractXmlSummary(decl),
            bases = new List<string>(),
            members = new List<MemberDoc>(),
            nestedTypes = new List<TypeDoc>()
        };

        // Base list (classes/interfaces)
        if (decl is TypeDeclarationSyntax td && td.BaseList != null)
        {
            foreach (var bt in td.BaseList.Types)
                typeDoc.bases.Add(bt.Type.ToString());
        }

        // Members
        foreach (var m in decl.Members)
        {
            if (m is BaseTypeDeclarationSyntax nested)
            {
                typeDoc.nestedTypes.Add(ParseType(nested, nsName, assetPath));
                continue;
            }

            var md = ParseMember(m);
            if (md != null) typeDoc.members.Add(md);
        }

        return typeDoc;
    }

    private static MemberDoc ParseMember(MemberDeclarationSyntax m)
    {
        switch (m)
        {
            case FieldDeclarationSyntax f:
                return new MemberDoc
                {
                    kind = "field",
                    name = f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "(multiple)",
                    signature = f.ToString().Split('\n').FirstOrDefault()?.Trim() ?? "",
                    modifiers = f.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(f.AttributeLists),
                    summary = ExtractXmlSummary(f)
                };

            case PropertyDeclarationSyntax p:
                return new MemberDoc
                {
                    kind = "property",
                    name = p.Identifier.Text,
                    signature = $"{p.Type} {p.Identifier}{(p.AccessorList != null ? " { ... }" : "")}",
                    modifiers = p.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(p.AttributeLists),
                    summary = ExtractXmlSummary(p)
                };

            case MethodDeclarationSyntax md:
                return new MemberDoc
                {
                    kind = "method",
                    name = md.Identifier.Text,
                    signature = md.ToString().Split('\n').FirstOrDefault()?.Trim() ?? "",
                    modifiers = md.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(md.AttributeLists),
                    summary = ExtractXmlSummary(md),
                    parameters = md.ParameterList.Parameters.Select(p =>
                        $"{p.Type} {p.Identifier.Text}{(p.Default != null ? " = " + p.Default.Value : "")}"
                    ).ToList()
                };

            case ConstructorDeclarationSyntax c:
                return new MemberDoc
                {
                    kind = "ctor",
                    name = c.Identifier.Text,
                    signature = c.ToString().Split('\n').FirstOrDefault()?.Trim() ?? "",
                    modifiers = c.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(c.AttributeLists),
                    summary = ExtractXmlSummary(c),
                    parameters = c.ParameterList.Parameters.Select(p =>
                        $"{p.Type} {p.Identifier.Text}{(p.Default != null ? " = " + p.Default.Value : "")}"
                    ).ToList()
                };

            case EventDeclarationSyntax e:
                return new MemberDoc
                {
                    kind = "event",
                    name = e.Identifier.Text,
                    signature = e.ToString().Split('\n').FirstOrDefault()?.Trim() ?? "",
                    modifiers = e.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(e.AttributeLists),
                    summary = ExtractXmlSummary(e)
                };

            case EventFieldDeclarationSyntax ef:
                return new MemberDoc
                {
                    kind = "event",
                    name = ef.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "(multiple)",
                    signature = ef.ToString().Split('\n').FirstOrDefault()?.Trim() ?? "",
                    modifiers = ef.Modifiers.Select(x => x.Text).ToList(),
                    attributes = GetAttributes(ef.AttributeLists),
                    summary = ExtractXmlSummary(ef)
                };

            default:
                return null;
        }
    }

    private static List<string> GetAttributes(SyntaxList<AttributeListSyntax> lists)
    {
        var result = new List<string>();
        foreach (var l in lists)
            foreach (var a in l.Attributes)
                result.Add(a.Name.ToString());
        return result;
    }

    private static string ExtractXmlSummary(SyntaxNode node)
    {
        // Pull `/// <summary>...</summary>` if present
        var triv = node.GetLeadingTrivia();
        var sb = new StringBuilder();
        foreach (var t in triv)
        {
            if (!t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                continue;

            var s = t.ToFullString();
            sb.AppendLine(s);
        }

        var raw = sb.ToString().Trim();
        if (string.IsNullOrEmpty(raw))
            return "";

        // Minimal cleanup: remove leading ///
        var lines = raw.Split('\n')
                       .Select(l => l.Trim())
                       .Where(l => l.StartsWith("///"))
                       .Select(l => l.Substring(3).Trim());
        var joined = string.Join("\n", lines);

        // Quick extract between <summary> tags if present
        var start = joined.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
        var end = joined.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            var inner = joined.Substring(start + "<summary>".Length, end - (start + "<summary>".Length));
            return inner.Trim();
        }

        return joined.Trim();
    }

    private static void BuildTypeIndex(ProjectDoc doc)
    {
        doc.typeIndex = new List<TypeIndexEntry>();
        foreach (var f in doc.files)
        {
            foreach (var t in f.types)
            {
                doc.typeIndex.Add(new TypeIndexEntry
                {
                    fullName = t.fullName,
                    kind = t.kind,
                    file = t.file
                });

                // nested
                AddNested(doc.typeIndex, t);
            }
        }

        doc.typeIndex = doc.typeIndex.OrderBy(x => x.fullName).ToList();
    }

    private static void AddNested(List<TypeIndexEntry> idx, TypeDoc parent)
    {
        foreach (var n in parent.nestedTypes)
        {
            idx.Add(new TypeIndexEntry
            {
                fullName = n.fullName,
                kind = n.kind,
                file = n.file
            });
            AddNested(idx, n);
        }
    }
#endif

    // ---------------- Fallback parser (no Roslyn) ----------------
    // This is intentionally "good enough": file-level summary and simple class/method detection.
    private static void GenerateWithFallback(List<string> csFiles, ProjectDoc doc)
    {
        foreach (var path in csFiles)
        {
            var text = File.ReadAllText(path);

            var fileDoc = new FileDoc
            {
                assetPath = ToAssetPath(path),
                fullPath = path,
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(path).ToString("o"),
                namespaces = new List<NamespaceDoc>(),
                types = new List<TypeDoc>()
            };

            // naive namespace + class scan
            var ns = RegexLiteFindNamespace(text);
            var typeNames = RegexLiteFindTypes(text);

            foreach (var tn in typeNames)
            {
                var full = string.IsNullOrEmpty(ns) ? tn : $"{ns}.{tn}";
                fileDoc.types.Add(new TypeDoc
                {
                    name = tn,
                    fullName = full,
                    @namespace = ns,
                    kind = "unknown",
                    file = fileDoc.assetPath,
                    modifiers = new List<string>(),
                    attributes = new List<string>(),
                    summary = "",
                    bases = new List<string>(),
                    members = new List<MemberDoc>(),
                    nestedTypes = new List<TypeDoc>()
                });
            }

            doc.files.Add(fileDoc);
        }

        doc.typeIndex = doc.files
            .SelectMany(f => f.types.Select(t => new TypeIndexEntry { fullName = t.fullName, kind = t.kind, file = t.file }))
            .OrderBy(x => x.fullName)
            .ToList();
    }

    private static string RegexLiteFindNamespace(string text)
    {
        // Very lightweight: finds "namespace X.Y"
        var marker = "namespace ";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return "";
        idx += marker.Length;
        var end = idx;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '.' || text[end] == '_'))
            end++;
        return text.Substring(idx, end - idx).Trim();
    }

    private static List<string> RegexLiteFindTypes(string text)
    {
        // naive: looks for "class X", "struct X", "interface X", "enum X"
        var results = new List<string>();
        var keywords = new[] { "class ", "struct ", "interface ", "enum " };

        foreach (var kw in keywords)
        {
            var start = 0;
            while (true)
            {
                var idx = text.IndexOf(kw, start, StringComparison.Ordinal);
                if (idx < 0) break;
                idx += kw.Length;
                var end = idx;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    end++;
                var name = text.Substring(idx, end - idx).Trim();
                if (!string.IsNullOrEmpty(name) && !results.Contains(name))
                    results.Add(name);
                start = end;
            }
        }

        return results;
    }

    // ---------------- Markdown output ----------------
    private static void WriteMarkdown(ProjectDoc doc)
    {
        // README / index
        var sb = new StringBuilder();
        sb.AppendLine("# Script Documentation");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{doc.generatedAtUtc}`");
        sb.AppendLine($"Unity: `{doc.unityVersion}`");
        sb.AppendLine($"Parser: `{doc.parser}`");
        sb.AppendLine($"Files scanned: `{doc.fileCount}`");
        sb.AppendLine();
        sb.AppendLine("## Types");
        sb.AppendLine();

        foreach (var t in doc.typeIndex ?? new List<TypeIndexEntry>())
        {
            var fileName = SanitizeFileName(t.fullName) + ".md";
            // Note: output is outside Assets/, so links to assets are best left as plain text.
            sb.AppendLine($"- **{t.fullName}** ({t.kind}) — `{t.file}` → Docs: `Classes/{fileName}`");
        }

        File.WriteAllText(OutputReadme, sb.ToString(), Encoding.UTF8);

        // Per-type pages
        foreach (var f in doc.files)
        {
            foreach (var t in f.types)
                WriteTypePage(t);
        }
    }

    private static void WriteTypePage(TypeDoc t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {t.fullName}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: `{t.kind}`");
        sb.AppendLine($"- File: `{t.file}`");
        if (t.modifiers != null && t.modifiers.Count > 0)
            sb.AppendLine($"- Modifiers: `{string.Join(" ", t.modifiers)}`");
        if (t.bases != null && t.bases.Count > 0)
            sb.AppendLine($"- Inherits/Implements: `{string.Join(", ", t.bases)}`");
        if (t.attributes != null && t.attributes.Count > 0)
            sb.AppendLine($"- Attributes: `{string.Join(", ", t.attributes)}`");

        if (!string.IsNullOrWhiteSpace(t.summary))
        {
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(t.summary);
        }

        sb.AppendLine();
        sb.AppendLine("## Members");
        sb.AppendLine();

        if (t.members == null || t.members.Count == 0)
        {
            sb.AppendLine("_No members captured (or fallback parser mode)._");
        }
        else
        {
            foreach (var m in t.members)
            {
                sb.AppendLine($"### {m.kind}: {m.name}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(m.signature))
                    sb.AppendLine($"`{m.signature}`");
                if (m.modifiers != null && m.modifiers.Count > 0)
                    sb.AppendLine($"\n- Modifiers: `{string.Join(" ", m.modifiers)}`");
                if (m.attributes != null && m.attributes.Count > 0)
                    sb.AppendLine($"- Attributes: `{string.Join(", ", m.attributes)}`");
                if (m.parameters != null && m.parameters.Count > 0)
                    sb.AppendLine($"- Params: `{string.Join(", ", m.parameters)}`");
                if (!string.IsNullOrWhiteSpace(m.summary))
                {
                    sb.AppendLine();
                    sb.AppendLine(m.summary);
                }
                sb.AppendLine();
            }
        }

        if (t.nestedTypes != null && t.nestedTypes.Count > 0)
        {
            sb.AppendLine("## Nested Types");
            sb.AppendLine();
            foreach (var n in t.nestedTypes)
            {
                sb.AppendLine($"- **{n.fullName}** ({n.kind})");
            }
        }

        var outPath = Path.Combine(OutputClassesDir, SanitizeFileName(t.fullName) + ".md");
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
    }

    private static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace('.', '_');
    }

    private static string ToAssetPath(string fullPath)
    {
        fullPath = fullPath.Replace('\\', '/');
        var idx = fullPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return fullPath.Substring(idx);
        return fullPath;
    }

    // ---------------- Data model (Unity JsonUtility friendly) ----------------
    [Serializable]
    private class ProjectDoc
    {
        public string generatedAtUtc;
        public string unityVersion;
        public string parser;
        public string scriptsRoot;
        public int fileCount;

        public List<FileDoc> files;
        public List<TypeIndexEntry> typeIndex;
    }

    [Serializable]
    private class FileDoc
    {
        public string assetPath;
        public string fullPath;
        public string lastWriteTimeUtc;

        public List<NamespaceDoc> namespaces; // reserved / not used heavily
        public List<TypeDoc> types;
    }

    [Serializable]
    private class NamespaceDoc
    {
        public string name;
    }

    [Serializable]
    private class TypeIndexEntry
    {
        public string fullName;
        public string kind;
        public string file;
    }

    [Serializable]
    private class TypeDoc
    {
        public string name;
        public string fullName;
        public string @namespace;
        public string kind;
        public string file;

        public List<string> modifiers;
        public List<string> attributes;
        public string summary;

        public List<string> bases;
        public List<MemberDoc> members;
        public List<TypeDoc> nestedTypes;
    }

    [Serializable]
    private class MemberDoc
    {
        public string kind;
        public string name;
        public string signature;
        public string summary;

        public List<string> modifiers;
        public List<string> attributes;
        public List<string> parameters;
    }
}
#endif
