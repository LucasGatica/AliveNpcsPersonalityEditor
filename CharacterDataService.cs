using System.Text.Json;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Edits Data/Characters in three passes: snapshot the original social fields (Early), apply the player's
/// overrides (Late), then record other mods' changes and put the original social fields back (last).
/// </summary>
/// <remarks>
/// Nothing inside these passes may load Data/Characters again, whether directly, through a token or game state
/// query that reads it, or by invalidating it: SMAPI then logs "Broke loop while loading asset 'Data/Characters'"
/// and loads it with no mod edits at all. So the passes only copy raw values. Display names and CanSocialize are
/// resolved when something reads them, and the diagnostics for AliveNpcs are pushed on the next tick.
/// </remarks>
public sealed class CharacterDataService
{
    private const string CharactersAssetName = "Data/Characters";

    // Each overridden NPC's fields just before the player's override is applied (what the editor's Reset reverts to).
    private static readonly Dictionary<string, ScopedFields> BaseSnapshots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The pre-override fields of an NPC as [gender, age, manner, socialAnxiety, optimism, canSocialize(0/1),
    /// canBeRomanced(0/1)]. CanSocialize is resolved now, outside the asset load.
    /// </summary>
    public static bool TryGetBaseSnapshot(string npcName, out int[] snapshot)
    {
        if (!BaseSnapshots.TryGetValue(npcName, out var s))
        {
            snapshot = null!;
            return false;
        }

        snapshot = new[]
        {
            s.Gender,
            s.Age,
            s.Manner,
            s.SocialAnxiety,
            s.Optimism,
            s.CanSocialize ? 1 : 0,
            s.CanBeRomanced ? 1 : 0,
        };
        return true;
    }

    internal sealed class ScopedFields
    {
        /// <summary>DisplayName as the asset holds it, often a token such as <c>[LocalizedText Strings\NPCNames:Harvey]</c>.</summary>
        public string? RawDisplayName;
        public int Gender, Age, Manner, SocialAnxiety, Optimism;
        /// <summary>CanSocialize as the asset holds it: a game state query, true when unset.</summary>
        public string? RawCanSocialize;
        public bool CanBeRomanced;

        /// <summary>The display name as text in the current language. Never read it inside a Data/Characters edit.</summary>
        public string DisplayName => ResolveName(RawDisplayName);

        /// <summary>Whether the NPC can socialize right now. Never read it inside a Data/Characters edit.</summary>
        public bool CanSocialize => ResolveCanSocialize(RawCanSocialize);
    }

    private static readonly Dictionary<string, ScopedFields> Originals = new(StringComparer.OrdinalIgnoreCase);
    // Other mods' changes (npc -> field -> value) found by the last pass, DisplayName still as the asset holds it.
    private static readonly Dictionary<string, Dictionary<string, string>> DetectedRaw = new(StringComparer.OrdinalIgnoreCase);
    // The same changes with DisplayName resolved, rebuilt from DetectedRaw when read (see ResolveDetected).
    private static readonly Dictionary<string, Dictionary<string, string>> DetectedExternal = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedNpcs = new(StringComparer.OrdinalIgnoreCase);

    private static Func<Dictionary<string, NpcOverrideEntry>>? _overridesSource;
    // DetectedRaw changed since DetectedExternal was built.
    private static bool _detectedStale;
    // A load finished and AliveNpcs hasn't been sent its diagnostics yet.
    private static bool _diagnosticsPending;
    // True from our first to our last pass of a Data/Characters load: nothing may be resolved meanwhile.
    private static bool _editing;

    public static bool HasDetectedConflicts => ResolveDetected().Count > 0;

    internal static bool TryGetOriginal(string npc, out ScopedFields fields)
        => Originals.TryGetValue(npc, out fields!);

    public static IReadOnlyDictionary<string, Dictionary<string, string>> GetDetectedExternalChanges() => ResolveDetected();

    public static string? GetEffectiveDisplayName(string npc)
    {
        if (ResolveDetected().TryGetValue(npc, out var d) && d.TryGetValue("DisplayName", out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return Originals.TryGetValue(npc, out var o) ? o.DisplayName : null;
    }

    public static int[]? GetEffectiveDefaults(string npc)
    {
        if (!Originals.TryGetValue(npc, out var o))
            return null;
        ResolveDetected().TryGetValue(npc, out var d);

        int Pick(string field, int orig) =>
            d != null && d.TryGetValue(field, out var s) && int.TryParse(s, out var v) ? v : orig;

        return new[]
        {
            Pick("Gender", o.Gender),
            Pick("Age", o.Age),
            Pick("Manner", o.Manner),
            Pick("SocialAnxiety", o.SocialAnxiety),
            Pick("Optimism", o.Optimism),
            o.CanSocialize ? 1 : 0,
            o.CanBeRomanced ? 1 : 0
        };
    }

    private static string ResolveName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        try
        {
            return StardewValley.TokenizableStrings.TokenParser.ParseText(raw) ?? raw;
        }
        catch
        {
            return raw;
        }
    }

    private static bool ResolveCanSocialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return false;
        if (Game1.player == null)
            return true;
        try
        {
            return GameStateQuery.CheckConditions(raw, Game1.currentLocation, Game1.player, null, null, null);
        }
        catch
        {
            return true;
        }
    }

    // Raw copies only: no token or query is resolved here, since this runs inside the asset load.
    private static ScopedFields Extract(CharacterData d) => new()
    {
        RawDisplayName = d.DisplayName,
        Gender = (int)d.Gender,
        Age = (int)d.Age,
        Manner = (int)d.Manner,
        SocialAnxiety = (int)d.SocialAnxiety,
        Optimism = (int)d.Optimism,
        RawCanSocialize = d.CanSocialize,
        CanBeRomanced = d.CanBeRomanced
    };

    private static IMonitor? _staticMonitor;

    private static void CaptureOriginals(IDictionary<string, CharacterData> data)
    {
        foreach (var (npc, cd) in data)
            if (!Originals.ContainsKey(npc))
                Originals[npc] = Extract(cd);
    }

    private readonly IMonitor _monitor;
    private readonly Func<Dictionary<string, NpcOverrideEntry>> _getOverrides;
    private Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>? _onDiagnostics;

    public CharacterDataService(IMonitor monitor, Func<Dictionary<string, NpcOverrideEntry>> getOverrides)
    {
        _monitor = monitor;
        _staticMonitor = monitor;
        _getOverrides = getOverrides;
        _overridesSource = getOverrides;
    }

    public void OnDiagnosticsReady(Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> callback)
    {
        _onDiagnostics = callback;
    }

    /// <summary>
    /// Sends AliveNpcs the diagnostics of the last Data/Characters load. Called every tick: building them resolves
    /// display names, which can't be done inside the load itself.
    /// </summary>
    public void FlushPendingDiagnostics()
    {
        if (!_diagnosticsPending || _editing)
            return;

        _diagnosticsPending = false;
        ResolveDetected();
        PushDiagnostics();
    }

    /// <summary>
    /// The detected changes with DisplayName as text: a raw name that differs from the original but reads the same
    /// (another token for the same name), or that is the player's own override, is not a change.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> ResolveDetected()
    {
        if (!_detectedStale || _editing)
            return DetectedExternal;

        _detectedStale = false;
        DetectedExternal.Clear();
        var overrides = _overridesSource?.Invoke();
        foreach (var (npc, raw) in DetectedRaw)
        {
            var changed = new Dictionary<string, string>(raw);
            if (changed.TryGetValue("DisplayName", out var rawName))
            {
                var name = ResolveName(rawName);
                var originalName = Originals.TryGetValue(npc, out var o) ? o.DisplayName : "";
                var userName = overrides != null && overrides.TryGetValue(npc, out var entry) ? entry.CharacterData?.DisplayName : null;
                if (string.Equals(name, originalName) || (userName != null && string.Equals(userName, name)))
                    changed.Remove("DisplayName");
                else
                    changed["DisplayName"] = name;
            }

            if (changed.Count == 0)
                continue;

            DetectedExternal[npc] = changed;
            if (ReportedNpcs.Add(npc))
                _staticMonitor?.Log($"CharacterData conflict: another mod changed {npc} ({string.Join(", ", changed.Keys)}). Restoring original values.", LogLevel.Info);
        }

        return DetectedExternal;
    }

    private void PushDiagnostics()
    {
        if (_onDiagnostics == null)
            return;

        var detectedCopy = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (npc, fields) in DetectedExternal)
            detectedCopy[npc] = new Dictionary<string, string>(fields);

        var originalsCopy = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (npc, fields) in Originals)
        {
            originalsCopy[npc] = new Dictionary<string, string>
            {
                ["DisplayName"] = fields.DisplayName,
                ["Gender"] = fields.Gender.ToString(),
                ["Age"] = fields.Age.ToString(),
                ["Manner"] = fields.Manner.ToString(),
                ["SocialAnxiety"] = fields.SocialAnxiety.ToString(),
                ["Optimism"] = fields.Optimism.ToString(),
            };
        }

        _onDiagnostics(detectedCopy, originalsCopy);
    }

    public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo(CharactersAssetName))
            return;

        // Early: capture vanilla data BEFORE any mod applies overrides (including our own Late pass).
        // Registered as a separate handler so it fires on the very first asset load.
        e.Edit(asset =>
        {
            _editing = true;
            CaptureOriginals(asset.AsDictionary<string, CharacterData>().Data);
        }, AssetEditPriority.Early);

        // Late: apply user overrides.
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, CharacterData>().Data;
            var overrides = _getOverrides();

            if (overrides == null || overrides.Count == 0)
                return;

            foreach (var (npcName, entry) in overrides)
            {
                if (entry.CharacterData == null || !entry.CharacterData.HasAnyField)
                    continue;

                if (!data.TryGetValue(npcName, out var characterData))
                    continue;

                BaseSnapshots[npcName] = Extract(characterData);
                ApplyCharacterDataOverride(characterData, entry.CharacterData);
            }
        }, AssetEditPriority.Late);

        e.Edit(asset =>
        {
            try
            {
                DetectExternalChanges(asset.AsDictionary<string, CharacterData>().Data);
            }
            finally
            {
                _editing = false;
            }
        }, (AssetEditPriority)1_000_000);
    }

    private void DetectExternalChanges(IDictionary<string, CharacterData> data)
    {
        var overrides = _getOverrides();
        foreach (var (npc, cd) in data)
        {
            if (!Originals.TryGetValue(npc, out var original))
            {
                Originals[npc] = Extract(cd);
                continue;
            }

            var current = Extract(cd);
            var userCd = overrides != null && overrides.TryGetValue(npc, out var e) ? e.CharacterData : null;
            var changed = new Dictionary<string, string>();

            void Check(string name, object orig, object now, bool isPlayerEdit)
            {
                if (Equals(orig, now) || isPlayerEdit)
                    return;
                changed[name] = now?.ToString() ?? "";
            }

            // DisplayName is compared as the asset holds it; whether the names really read differently (and whether
            // it's the player's own override) is settled when read, in ResolveDetected.
            if (!string.Equals(original.RawDisplayName, current.RawDisplayName, StringComparison.Ordinal))
                changed["DisplayName"] = current.RawDisplayName ?? "";
            Check("Gender", original.Gender, current.Gender, userCd?.Gender == current.Gender);
            Check("Age", original.Age, current.Age, userCd?.Age == current.Age);
            Check("Manner", original.Manner, current.Manner, userCd?.Manner == current.Manner);
            Check("SocialAnxiety", original.SocialAnxiety, current.SocialAnxiety, userCd?.SocialAnxiety == current.SocialAnxiety);
            Check("Optimism", original.Optimism, current.Optimism, userCd?.Optimism == current.Optimism);

            if (changed.Count > 0)
                DetectedRaw[npc] = changed;
            else
                DetectedRaw.Remove(npc);

            RestoreScopedFields(cd, original);
        }

        // The restored values are already in this asset, so there is nothing to reload: invalidating Data/Characters
        // here (as this pass used to) reloaded it from inside its own load.
        _detectedStale = true;
        _diagnosticsPending = true;
    }

    private static void RestoreScopedFields(CharacterData cd, ScopedFields o)
    {
        // The original as the asset held it, so a localizable name keeps following the game's language.
        cd.DisplayName = o.RawDisplayName!;
        cd.Gender = (Gender)o.Gender;
        cd.Age = (NpcAge)o.Age;
        cd.Manner = (NpcManner)o.Manner;
        cd.SocialAnxiety = (NpcSocialAnxiety)o.SocialAnxiety;
        cd.Optimism = (NpcOptimism)o.Optimism;
    }

    private static void ApplyCharacterDataOverride(CharacterData data, CharacterDataOverride overrides)
    {
        if (overrides.DisplayName != null)
            data.DisplayName = overrides.DisplayName;

        if (overrides.Gender.HasValue)
            data.Gender = (Gender)overrides.Gender.Value;

        if (overrides.Age.HasValue)
            data.Age = (NpcAge)overrides.Age.Value;

        if (overrides.Manner.HasValue)
            data.Manner = (NpcManner)overrides.Manner.Value;

        if (overrides.SocialAnxiety.HasValue)
            data.SocialAnxiety = (NpcSocialAnxiety)overrides.SocialAnxiety.Value;

        if (overrides.Optimism.HasValue)
            data.Optimism = (NpcOptimism)overrides.Optimism.Value;

        if (overrides.BirthSeason != null)
            data.BirthSeason = (Season)Enum.Parse(typeof(Season), overrides.BirthSeason, true);

        if (overrides.BirthDay.HasValue)
            data.BirthDay = overrides.BirthDay.Value;

        if (overrides.CanSocialize.HasValue)
            data.CanSocialize = overrides.CanSocialize.Value ? "TRUE" : "FALSE";

        if (overrides.CanReceiveGifts.HasValue)
            data.CanReceiveGifts = overrides.CanReceiveGifts.Value;

        if (overrides.CanBeRomanced.HasValue)
            data.CanBeRomanced = overrides.CanBeRomanced.Value;
    }
}
