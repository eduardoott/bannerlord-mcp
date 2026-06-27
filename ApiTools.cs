using System.ComponentModel;
using ModelContextProtocol.Server;

namespace BannerlordMcp;

/// <summary>Singleton preguiçoso do índice — construído na primeira tool call (carregar as DLLs leva alguns segundos).</summary>
public static class Bannerlord
{
    public static string GameDir { get; set; } = "";
    static readonly Lazy<AssemblyIndex> _index = new(() => new AssemblyIndex(GameDir));
    public static AssemblyIndex Index => _index.Value;
}

[McpServerToolType]
public static class ApiTools
{
    [McpServerTool(Name = "bl_index_info")]
    [Description("Show what was indexed: assembly count, type count and the list of indexed assemblies. Useful to confirm the Bannerlord path is correct.")]
    public static string IndexInfo() => Bannerlord.Index.Info();

    [McpServerTool(Name = "bl_find_type")]
    [Description("Search Bannerlord/TaleWorlds types by name (case-insensitive substring on the full type name). Returns each match with its kind, base types and assembly. Start here when you don't know the exact type name.")]
    public static string FindType(
        [Description("Substring to match against the full type name, e.g. 'Hero', 'CampaignSystem.Hero', 'MissionBehavior'.")] string query,
        [Description("Max results to return (default 50).")] int limit = 50)
        => Bannerlord.Index.FindTypes(query, limit);

    [McpServerTool(Name = "bl_type_members")]
    [Description("List the members (methods, properties, fields, events, nested types) of a type, with C# signatures. Run bl_find_type first to get the exact full name.")]
    public static string TypeMembers(
        [Description("Exact full type name, e.g. 'TaleWorlds.CampaignSystem.Hero'.")] string fullName)
        => Bannerlord.Index.TypeMembers(fullName);

    [McpServerTool(Name = "bl_find_member")]
    [Description("Find which type declares a given method/property/field/event, across all indexed assemblies. Answers 'where is GetSkillValue defined?'.")]
    public static string FindMember(
        [Description("Member name to search (case-insensitive substring), e.g. 'GetSkillValue'.")] string name,
        [Description("Max results to return (default 50).")] int limit = 50)
        => Bannerlord.Index.FindMembers(name, limit);

    [McpServerTool(Name = "bl_decompile")]
    [Description("Decompile a type (or a single method) back to C# source, so you can see what the game actually does — essential before writing a Harmony patch.")]
    public static string Decompile(
        [Description("Exact full type name, e.g. 'TaleWorlds.CampaignSystem.CampaignBehaviors.CampaignMapConversation'.")] string fullName,
        [Description("Optional method name. If omitted, the whole type is decompiled. Overloads are all returned.")] string? method = null)
        => Bannerlord.Index.Decompile(fullName, method);
}
