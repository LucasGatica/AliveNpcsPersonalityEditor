using System.Text.Json;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;

namespace AliveNpcsPersonalityEditor;

public sealed class CharacterDataService
{
    private const string CharactersAssetName = "Data/Characters";

    private static readonly Dictionary<string, int[]> BaseSnapshots = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetBaseSnapshot(string npcName, out int[] snapshot)
        => BaseSnapshots.TryGetValue(npcName, out snapshot!);

    internal sealed class ScopedFields
    {
        public string DisplayName = "";
        public int Gender, Age, Manner, SocialAnxiety, Optimism;
        public bool CanSocialize;
        public bool CanBeRomanced;
    }

    private static readonly Dictionary<string, ScopedFields> Originals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, string>> DetectedExternal = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedNpcs = new(StringComparer.OrdinalIgnoreCase);

    public static bool HasDetectedConflicts => DetectedExternal.Count > 0;

    internal static bool TryGetOriginal(string npc, out ScopedFields fields)
        => Originals.TryGetValue(npc, out fields!);

    public static IReadOnlyDictionary<string, Dictionary<string, string>> GetDetectedExternalChanges() => DetectedExternal;

    public static string? GetEffectiveDisplayName(string npc)
    {
        if (DetectedExternal.TryGetValue(npc, out var d) && d.TryGetValue("DisplayName", out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return Originals.TryGetValue(npc, out var o) ? o.DisplayName : null;
    }

    public static int[]? GetEffectiveDefaults(string npc)
    {
        if (!Originals.TryGetValue(npc, out var o))
            return null;
        DetectedExternal.TryGetValue(npc, out var d);

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

    private static ScopedFields Extract(CharacterData d) => new()
    {
        DisplayName = ResolveName(d.DisplayName),
        Gender = (int)d.Gender,
        Age = (int)d.Age,
        Manner = (int)d.Manner,
        SocialAnxiety = (int)d.SocialAnxiety,
        Optimism = (int)d.Optimism,
        CanSocialize = ResolveCanSocialize(d.CanSocialize),
        CanBeRomanced = d.CanBeRomanced
    };

    private static void CaptureOriginals(IDictionary<string, CharacterData> data)
    {
        foreach (var (npc, cd) in data)
            if (!Originals.ContainsKey(npc))
                Originals[npc] = Extract(cd);
    }

    private readonly IMonitor _monitor;
    private readonly Func<Dictionary<string, NpcOverrideEntry>> _getOverrides;
    private readonly Action? _onRestored;

    public CharacterDataService(IMonitor monitor, Func<Dictionary<string, NpcOverrideEntry>> getOverrides, Action? onRestored = null)
    {
        _monitor = monitor;
        _getOverrides = getOverrides;
        _onRestored = onRestored;
    }

    public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo(CharactersAssetName))
            return;

        e.Edit(asset => CaptureOriginals(asset.AsDictionary<string, CharacterData>().Data), AssetEditPriority.Early);

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

                CaptureBaseSnapshot(npcName, characterData);
                ApplyCharacterDataOverride(characterData, entry.CharacterData);
            }
        }, AssetEditPriority.Late);

        e.Edit(asset => DetectExternalChanges(asset.AsDictionary<string, CharacterData>().Data),
            (AssetEditPriority)1_000_000);
    }

    private static void CaptureBaseSnapshot(string npcName, CharacterData d)
    {
        BaseSnapshots[npcName] = new[]
        {
            (int)d.Gender,
            (int)d.Age,
            (int)d.Manner,
            (int)d.SocialAnxiety,
            (int)d.Optimism,
            ResolveCanSocialize(d.CanSocialize) ? 1 : 0,
            d.CanBeRomanced ? 1 : 0,
        };
    }

    private void DetectExternalChanges(IDictionary<string, CharacterData> data)
    {
        var anyRestored = false;
        foreach (var (npc, cd) in data)
        {
            if (!Originals.TryGetValue(npc, out var original))
            {
                Originals[npc] = Extract(cd);
                continue;
            }

            var current = Extract(cd);
            var userEntry = _getOverrides().TryGetValue(npc, out var e) ? e : null;
            var userCd = userEntry?.CharacterData;
            var changed = new Dictionary<string, string>();

            void Check(string name, object orig, object now, bool isPlayerEdit)
            {
                if (Equals(orig, now) || isPlayerEdit)
                    return;
                changed[name] = now?.ToString() ?? "";
            }

            Check("DisplayName", original.DisplayName, current.DisplayName,
                userCd?.DisplayName != null && string.Equals(userCd.DisplayName, current.DisplayName));
            Check("Gender", original.Gender, current.Gender, userCd?.Gender == current.Gender);
            Check("Age", original.Age, current.Age, userCd?.Age == current.Age);
            Check("Manner", original.Manner, current.Manner, userCd?.Manner == current.Manner);
            Check("SocialAnxiety", original.SocialAnxiety, current.SocialAnxiety, userCd?.SocialAnxiety == current.SocialAnxiety);
            Check("Optimism", original.Optimism, current.Optimism, userCd?.Optimism == current.Optimism);

            if (changed.Count > 0)
            {
                DetectedExternal[npc] = changed;
                if (ReportedNpcs.Add(npc))
                    _monitor.Log($"CharacterData conflict: another mod changed {npc} ({string.Join(", ", changed.Keys)}). Restoring original values.", LogLevel.Info);
            }
            else
            {
                DetectedExternal.Remove(npc);
            }

            RestoreScopedFields(cd, original);
            anyRestored = true;
        }

        if (anyRestored && DetectedExternal.Count > 0)
            _onRestored?.Invoke();
    }

    private static void RestoreScopedFields(CharacterData cd, ScopedFields o)
    {
        cd.DisplayName = o.DisplayName;
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
