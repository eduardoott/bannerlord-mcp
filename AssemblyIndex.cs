using System.Reflection.Metadata;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Output;
using ICSharpCode.Decompiler.TypeSystem;

namespace BannerlordMcp;

/// <summary>
/// Carrega (via ICSharpCode.Decompiler) todas as assemblies gerenciadas do Bannerlord — o bin
/// raiz + todos os Modules/*/bin — e responde consultas de API: buscar tipo, listar membros,
/// achar membro e decompilar. As DLLs nunca entram no contexto do LLM; só as respostas filtradas.
/// </summary>
public sealed class AssemblyIndex
{
    sealed record TypeEntry(ITypeDefinition Type, CSharpDecompiler Decompiler, string Assembly);

    readonly List<TypeEntry> _types = new();
    readonly Dictionary<string, TypeEntry> _byFullName = new(StringComparer.OrdinalIgnoreCase);

    public int AssemblyCount { get; }
    public int TypeCount => _types.Count;
    public IReadOnlyList<string> Assemblies { get; }

    // Assinatura legível em C# (modificadores + tipo de retorno + lista de parâmetros nomeados).
    static readonly CSharpAmbience Ambience = new() { ConversionFlags = ConversionFlags.StandardConversionFlags };

    public AssemblyIndex(string gameDir)
    {
        var dirs = DiscoverBinDirs(gameDir);
        if (dirs.Count == 0)
            throw new DirectoryNotFoundException(
                $"Nenhum bin do Bannerlord encontrado sob '{gameDir}'. Ajuste a variável de ambiente BANNERLORD_DIR.");

        var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string>();
        int skipped = 0;

        foreach (var dll in dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.dll")))
        {
            var name = Path.GetFileName(dll);
            if (!seen.Add(name)) continue; // mesmo nome em vários módulos: fica o primeiro

            try
            {
                var resolver = new UniversalAssemblyResolver(dll, throwOnError: false, targetFramework: null);
                foreach (var d in dirs) resolver.AddSearchDirectory(d);

                var decompiler = new CSharpDecompiler(dll, resolver, settings);
                foreach (var t in decompiler.TypeSystem.MainModule.TypeDefinitions)
                {
                    var entry = new TypeEntry(t, decompiler, name);
                    _types.Add(entry);
                    _byFullName[t.FullName] = entry;
                }
                loaded.Add(name);
            }
            catch
            {
                skipped++; // DLL nativa/não-gerenciada ou ilegível — ignora
            }
        }

        AssemblyCount = loaded.Count;
        Assemblies = loaded;
        Console.Error.WriteLine(
            $"[bl-mcp] Indexado: {loaded.Count} assemblies, {_types.Count} tipos (puladas {skipped} não-gerenciadas) em {dirs.Count} pastas.");
    }

    static List<string> DiscoverBinDirs(string gameDir)
    {
        var result = new List<string>();
        void Add(string p) { if (Directory.Exists(p)) result.Add(p); }

        Add(Path.Combine(gameDir, "bin", "Win64_Shipping_Client"));
        var modules = Path.Combine(gameDir, "Modules");
        if (Directory.Exists(modules))
            foreach (var m in Directory.EnumerateDirectories(modules))
                Add(Path.Combine(m, "bin", "Win64_Shipping_Client"));
        return result;
    }

    // ---- consultas ----------------------------------------------------------

    public string Info()
        => $"Bannerlord API index: {AssemblyCount} assemblies, {TypeCount} tipos.\n"
         + string.Join("\n", Assemblies.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).Select(a => "  " + a));

    public string FindTypes(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return "(query vazia)";

        var hits = _types
            .Where(e => e.Type.FullName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => Rank(e.Type.Name, e.Type.FullName, query))
            .ThenBy(e => e.Type.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToList();

        if (hits.Count == 0) return $"Nenhum tipo casa com '{query}'.";

        return string.Join("\n", hits.Select(e =>
        {
            var bases = string.Join(", ", e.Type.DirectBaseTypes.Select(b => b.Name));
            var kind = e.Type.Kind.ToString().ToLowerInvariant();
            var baseStr = string.IsNullOrEmpty(bases) ? "" : $" : {bases}";
            return $"{e.Type.FullName}  ({kind}{baseStr})  [{e.Assembly}]";
        }));
    }

    public string TypeMembers(string fullName)
    {
        if (!TryGetType(fullName, out var entry, out var note)) return note;
        var t = entry.Type;

        var sb = new System.Text.StringBuilder();
        var tps = t.TypeParameters.Count > 0 ? $"<{string.Join(", ", t.TypeParameters.Select(p => p.Name))}>" : "";
        var bases = string.Join(", ", t.DirectBaseTypes.Select(b => b.Name));
        sb.AppendLine($"// {t.FullName}{tps}{(string.IsNullOrEmpty(bases) ? "" : " : " + bases)}  [{entry.Assembly}]");

        Section(sb, "Methods", t.Methods.Where(m => !m.IsAccessor).Select(SafeSig));
        Section(sb, "Properties", t.Properties.Select(SafeSig));
        Section(sb, "Fields", t.Fields.Select(SafeSig));
        Section(sb, "Events", t.Events.Select(SafeSig));
        Section(sb, "Nested types", t.NestedTypes.Select(n => n.Name));

        return sb.ToString().TrimEnd();
    }

    public string FindMembers(string name, int limit)
    {
        if (string.IsNullOrWhiteSpace(name)) return "(nome vazio)";

        var hits = _types
            .SelectMany(e => e.Type.Members
                .Where(m => m is IMethod meth ? !meth.IsAccessor : true)
                .Where(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(m => (Entry: e, Member: m)))
            .OrderBy(x => x.Member.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Entry.Type.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToList();

        if (hits.Count == 0) return $"Nenhum membro casa com '{name}'.";

        return string.Join("\n", hits.Select(x =>
            $"{SafeSig(x.Member)}\n    in {x.Entry.Type.FullName}  [{x.Entry.Assembly}]"));
    }

    public string Decompile(string fullName, string? method)
    {
        if (!TryGetType(fullName, out var entry, out var note)) return note;

        try
        {
            if (string.IsNullOrWhiteSpace(method))
                return entry.Decompiler.DecompileAsString(entry.Type.MetadataToken);

            var handles = entry.Type.Members
                .Where(m => m.Name.Equals(method, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.MetadataToken)
                .ToList();

            if (handles.Count == 0)
                return $"Tipo '{entry.Type.FullName}' não tem membro chamado '{method}'. Use bl_type_members para listar.";

            return entry.Decompiler.DecompileAsString(handles);
        }
        catch (Exception ex)
        {
            return $"Falha ao decompilar '{fullName}': {ex.Message}";
        }
    }

    // ---- helpers ------------------------------------------------------------

    bool TryGetType(string fullName, out TypeEntry entry, out string note)
    {
        note = "";
        if (_byFullName.TryGetValue(fullName, out var e)) { entry = e; return true; }

        var matches = _types
            .Where(x => x.Type.FullName.Contains(fullName, StringComparison.OrdinalIgnoreCase))
            .Take(20).ToList();

        entry = null!;
        if (matches.Count == 0) { note = $"Tipo '{fullName}' não encontrado. Use bl_find_type para buscar."; return false; }

        note = $"'{fullName}' não é um nome exato. Você quis dizer:\n"
             + string.Join("\n", matches.Select(m => "  " + m.Type.FullName));
        return false;
    }

    static string SafeSig(IEntity e)
    {
        try { return Ambience.ConvertSymbol(e); }
        catch { return e.Name; }
    }

    static void Section(System.Text.StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"// {title} ({list.Count})");
        foreach (var s in list) sb.AppendLine("  " + s);
    }

    static int Rank(string name, string fullName, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (fullName.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }
}
