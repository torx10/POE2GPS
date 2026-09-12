using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Static Atlas reference data — the offline classification + content layer that pairs with the live
/// node read in <see cref="Poe2Atlas"/>. Two embedded tables (both adopted from an upstream Atlas plugin, generated from GGG .dat dumps for PoE2 0.5.x):
///
/// <para><b>atlas_maps.json</b> — keyed by the internal WorldArea MapId (e.g. <c>MapBurialBog</c>,
/// exactly the code <see cref="Poe2Atlas.ResolveTags"/> reads): display name + <c>type</c>
/// (normal/unique) + <c>group</c> + cross-cutting <c>tags</c> (<c>lineage</c>, <c>arbiter</c>, …). This
/// gives us classification the code-prefix <see cref="Poe2Atlas.Classify"/> can't derive.</para>
///
/// <para><b>atlas_content.json</b> — the checked-in numeric content snapshot keyed by the live byte
/// <c>ContentId</c>. It supplies display name, icon basename, and effect description when the current
/// id is known. The live EndgameMapContent.dat table base and VisualIdentity row are not exposed by this
/// repository, so this reader deliberately does not guess either pointer chain.</para>
///
/// <para>Loaded once; read-only. Unmapped ids remain available as raw byte ids in the live node snapshot.
/// The <c>translates</c> blocks are imported but unused for now (English-only).</para>
/// </summary>
public sealed class AtlasMapData
{
    /// <summary>One map archetype's offline metadata. <see cref="Tags"/> is never null.
    /// <see cref="Translates"/> is the shipped-with-JSON per-language name table (10 languages
    /// including English) — null when the source entry didn't carry a translates block.</summary>
    public readonly record struct MapMeta(string Name, string Type, string Group, IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string>? Translates = null)
    {
        public bool HasTag(string tag) =>
            Tags != null && Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

        /// <summary>Reach — v0.26 (Long #38): returns the localized display name for the requested
        /// language key (e.g. "french", "german", "traditional chinese"). Falls back to <see cref="Name"/>
        /// when the entry has no translates block, when the language key is unknown, or when the language
        /// entry is empty.</summary>
        public string LocalizedName(string? language)
        {
            if (Translates is null || string.IsNullOrEmpty(language)) return Name;
            return Translates.TryGetValue(language, out var s) && !string.IsNullOrEmpty(s) ? s : Name;
        }
    }

    /// <summary>Offline metadata for one numeric EndgameMapContent id. The id is the byte stored in
    /// the live node's ContentIds vector; no pointer from the game process is retained.</summary>
    public readonly record struct ContentMeta(byte Id, string Name, string Icon, string Description);

    private readonly Dictionary<string, MapMeta> _maps = new(StringComparer.OrdinalIgnoreCase);    // MapId → meta
    private readonly Dictionary<string, string> _contentDesc = new(StringComparer.OrdinalIgnoreCase); // content name → effect text
    private readonly Dictionary<string, string> _contentIcon = new(StringComparer.OrdinalIgnoreCase); // content name → icon basename
    private readonly Dictionary<byte, ContentMeta> _contents = new(); // live ContentId → offline metadata

    private static readonly string[] NoTags = Array.Empty<string>();

    /// <summary>The shared data, loaded once from the embedded tables.</summary>
    public static AtlasMapData Shared { get; } = LoadEmbedded();

    /// <summary>Number of mapped archetypes (0 ⇒ the table failed to load).</summary>
    public int MapCount => _maps.Count;
    /// <summary>Number of numeric content ids in the checked-in snapshot.</summary>
    public int ContentCount => _contents.Count;

    /// <summary>Offline metadata for a live byte ContentId. Returns false when the snapshot has no row.</summary>
    public bool TryGetContent(byte id, out ContentMeta meta) => _contents.TryGetValue(id, out meta);

    /// <summary>Offline metadata for an internal MapId, or null when unmapped.</summary>
    public MapMeta? Get(string? mapId)
        => !string.IsNullOrEmpty(mapId) && _maps.TryGetValue(mapId, out var m) ? m : null;

    public bool TryGet(string? mapId, out MapMeta meta)
    {
        if (!string.IsNullOrEmpty(mapId)) return _maps.TryGetValue(mapId, out meta);
        meta = default; return false;
    }

    /// <summary>Effect description for a content display name (e.g. "Breach"), or null when unknown.</summary>
    public string? ContentDesc(string? name)
        => !string.IsNullOrEmpty(name) && _contentDesc.TryGetValue(name, out var d) ? d : null;

    /// <summary>Icon asset basename (e.g. "AtlasIconContentBreach") for a content display name, or null.</summary>
    public string? ContentIcon(string? name)
        => !string.IsNullOrEmpty(name) && _contentIcon.TryGetValue(name, out var i) ? i : null;

    private static AtlasMapData LoadEmbedded()
    {
        var data = new AtlasMapData();
        try
        {
            var asm = Assembly.GetExecutingAssembly();

            // ── atlas_maps.json: MapId → { name, type, group, tags[] } ──────────────────────────────
            using (var s = OpenResource(asm, "atlas_maps"))
                if (s != null)
                {
                    var doc = JsonDocument.Parse(s);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var v = prop.Value;
                        IReadOnlyList<string> tags = NoTags;
                        if (v.TryGetProperty("tags", out var ta) && ta.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var t in ta.EnumerateArray())
                                if (t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } ts) list.Add(ts);
                            if (list.Count > 0) tags = list;
                        }
                        // Reach — v0.26 (Long #38): pull the shipped translates block if present.
                        // Shape is flat { language → name } — atlas_maps.json carries these for 10 langs.
                        // Absent block → Translates stays null and LocalizedName falls back to the top-level name.
                        Dictionary<string, string>? translates = null;
                        if (v.TryGetProperty("translates", out var trObj) && trObj.ValueKind == JsonValueKind.Object)
                        {
                            translates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var tp in trObj.EnumerateObject())
                                if (tp.Value.ValueKind == JsonValueKind.String && tp.Value.GetString() is { Length: > 0 } locStr)
                                    translates[tp.Name] = locStr;
                            if (translates.Count == 0) translates = null;
                        }
                        data._maps[prop.Name] = new MapMeta(
                            Name: Str(v, "name"),
                            Type: Str(v, "type"),
                            Group: Str(v, "group"),
                            Tags: tags,
                            Translates: translates);
                    }
                }

            // ── atlas_content.json: numeric id → { name, icon, desc } snapshot ────────────────────────
            using (var s = OpenResource(asm, "atlas_content"))
                if (s != null)
                {
                    var doc = JsonDocument.Parse(s);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!byte.TryParse(prop.Name, out var id)) continue;
                        var v = prop.Value;
                        var name = Str(v, "name");
                        if (name.Length == 0) continue;
                        var icon = Str(v, "icon");
                        var desc = Str(v, "desc");
                        data._contents[id] = new ContentMeta(id, name, icon, desc);
                        if (icon.Length > 0) data._contentIcon[name] = icon;
                        if (desc.Length > 0) data._contentDesc[name] = desc;
                    }
                }

            data.SeedSpecialBadges();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AtlasMapData load failed: {ex.Message}");
        }
        return data;
    }

    // Special map-state content with a VisualIdentity icon but no EndgameMapContent row, so it's absent
    // from atlas_content.json. Ported from prior art in an upstream Atlas plugin (SeedSpecialBadges). The id drifts
    // ±1 per patch but the NAME is stable, so seeding the name→icon/desc here is patch-durable.
    private void SeedSpecialBadges()
    {
        const string grandMirror = "Grand Mirror";
        _contentIcon[grandMirror] = "AtlasIconContentGigaMirror";
        _contentDesc[grandMirror] = "Contains a reflection of the Map Boss. When the bosses are " +
            "defeated Delirium fog spreads to nearby Maps.";
    }

    private static Stream? OpenResource(Assembly asm, string contains)
    {
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains(contains));
        return name == null ? null : asm.GetManifestResourceStream(name);
    }

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
