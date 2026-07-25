using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

public sealed class ModEntry : Mod
{
    private PersonalityStore _store = null!;
    private PresetStore _presetStore = null!;
    private FarmerStore? _farmerStore;
    private EditorConfig _config = null!;
    private IAliveNpcsApi? _api;
    private GalleryService? _galleryService;
    private CharacterDataService? _characterDataService;
    private bool _cdConflictHudShown;

    public override void Entry(IModHelper helper)
    {
        _config = helper.ReadConfig<EditorConfig>();
        _store = new PersonalityStore(helper.DirectoryPath, Monitor);
        _store.Load();
        _presetStore = new PresetStore(helper.DirectoryPath, Monitor);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    public override object GetApi() => new PersonalityEditorApi(this);

    public void OpenEditorMenu() => OpenMenuAtTab(1);

    public void OpenFarmerTab() => OpenMenuAtTab(0);

    private void OpenMenuAtTab(int tab)
    {
        if (_api == null || !Context.IsWorldReady) return;
        _farmerStore?.Load();
        Game1.activeClickableMenu = new PersonalityEditorMenu(
            _store, _presetStore, _farmerStore!, _api, _config, _galleryService, Monitor, Helper.Translation,
            onServerUrlSaved: url =>
            {
                _config.GalleryServerUrl = url;
                Helper.WriteConfig(_config);
            },
            initialTab: tab);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        _api = Helper.ModRegistry.GetApi<IAliveNpcsApi>("Lucas.AliveNpcs");
        if (_api is null)
        {
            Monitor.Log("AliveNpcs not found — personality editor cannot function.", LogLevel.Error);
            return;
        }

        var alivenpcsDir = ResolveAliveNpcsDirectory();
        _farmerStore = new FarmerStore(alivenpcsDir ?? Helper.DirectoryPath, Monitor);
        Monitor.Log($"FarmerStore backing dir: {(alivenpcsDir ?? Helper.DirectoryPath)}", LogLevel.Debug);

        try
        {
            if (!_api.RegisterCustomPersonalityDirectory(_store.OverridesDirectory))
                Monitor.Log("AliveNpcs did not accept the overrides directory path. The directory must be named 'overrides'.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            Monitor.Log($"AliveNpcs API error during directory registration ({ex.GetType().Name}): {ex.Message}", LogLevel.Trace);
        }

        if (_config.GalleryEnabled && !string.IsNullOrWhiteSpace(_config.GalleryServerUrl))
        {
            _galleryService = new GalleryService(_config.GalleryServerUrl);
            Monitor.Log($"Gallery service initialized — server: {_config.GalleryServerUrl}", LogLevel.Info);
        }

        _characterDataService = new CharacterDataService(Monitor, () => _store.Overrides, OnCharacterDataRestored);
        Helper.Events.Content.AssetRequested += _characterDataService.OnAssetRequested;
        Helper.Events.GameLoop.SaveLoaded += OnSaveLoadedReportCdConflicts;

        // Push CD diagnostics (detected external changes + originals) to AliveNpcs for the prompt builder.
        _characterDataService.OnDiagnosticsReady((detected, originals) =>
        {
            try { _api?.SetCharacterDataDiagnostics(detected, originals); }
            catch (Exception ex) { Monitor.Log($"Could not push CD diagnostics: {ex.Message}", LogLevel.Trace); }
        });

        // Visual layer: swap the dialogue portrait name to the overridden name (with "Originally X" tooltip).
        PortraitNamePatches.Initialise(Monitor, Helper.Translation, () => _store.Overrides, () => _config.IncludeCharacterDataInPrompt);
        PortraitNamePatches.Apply(new HarmonyLib.Harmony(ModManifest.UniqueID + ".PortraitName"));

        RegisterGmcmConfig();
        PushCharacterDataPromptSetting();

        Monitor.Log($"Personality Editor ready. Press {_config.OpenFarmerTabKey} for Farmer tab, {_config.OpenEditorKey} for NPCs/Catalog.", LogLevel.Info);
    }

    private void PushCharacterDataPromptSetting()
    {
        try { _api?.SetCharacterDataPromptEnabled(_config.IncludeCharacterDataInPrompt); }
        catch (Exception ex) { Monitor.Log($"Could not push CharacterData prompt setting: {ex.Message}", LogLevel.Trace); }
    }

    private void OnCharacterDataRestored()
    {
        Helper.GameContent.InvalidateCache("Data/Characters");
    }

    private void OnSaveLoadedReportCdConflicts(object? sender, SaveLoadedEventArgs e)
    {
        if (_cdConflictHudShown || !CharacterDataService.HasDetectedConflicts)
            return;

        _cdConflictHudShown = true;
        EditorNotify.Info(Helper.Translation.Get("cd.restored_notification"));
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        _store.Load();
        _farmerStore?.Load();
    }

    private string? ResolveAliveNpcsDirectory()
    {
        try
        {
            var editorDir = Helper.DirectoryPath;
            var modsRoot = System.IO.Path.GetDirectoryName(editorDir);
            if (!string.IsNullOrEmpty(modsRoot))
            {
                var candidates = new[] { "AliveNpcs", "AliveNpcsRevamp", "AliveNpcsRevamp-experimental", "Lucas.AliveNpcs" };
                foreach (var cand in candidates)
                {
                    var full = System.IO.Path.Combine(modsRoot, cand);
                    if (System.IO.Directory.Exists(full))
                        return full;
                }
            }
            var info = Helper.ModRegistry.Get("Lucas.AliveNpcs");
            return info is null ? null : editorDir;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not resolve AliveNpcs directory ({ex.GetType().Name}): {ex.Message}", LogLevel.Trace);
            return null;
        }
    }

    private void RegisterGmcmConfig()
    {
        try
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null) return;

            var modManifest = ModManifest;

            gmcm.Register(modManifest, reset: () =>
            {
                _config = Helper.ReadConfig<EditorConfig>();
            }, save: () =>
            {
                Helper.WriteConfig(_config);
                if (_config.GalleryEnabled && !string.IsNullOrWhiteSpace(_config.GalleryServerUrl))
                    _galleryService = new GalleryService(_config.GalleryServerUrl);
                else
                    _galleryService = null;
                PushCharacterDataPromptSetting();
            });

            gmcm.AddSectionTitle(modManifest, () => Helper.Translation.Get("config.chardata.section"));
            gmcm.AddParagraph(modManifest, () => Helper.Translation.Get("config.chardata.disclaimer"));
            gmcm.AddBoolOption(modManifest,
                () => _config.IncludeCharacterDataInPrompt,
                v => _config.IncludeCharacterDataInPrompt = v,
                () => Helper.Translation.Get("config.chardata.toggle"),
                () => Helper.Translation.Get("config.chardata.toggle.tooltip"));

            gmcm.AddSectionTitle(modManifest, () => Helper.Translation.Get("gallery.config.enabled"));
            gmcm.AddParagraph(modManifest, () => Helper.Translation.Get("gallery.config.description"));
            gmcm.AddBoolOption(modManifest,
                () => _config.GalleryEnabled,
                v => _config.GalleryEnabled = v,
                () => Helper.Translation.Get("gallery.config.enabled"),
                () => Helper.Translation.Get("gallery.config.enabled.tooltip"));

            gmcm.AddTextOption(modManifest,
                () => _config.GalleryServerUrl,
                v => _config.GalleryServerUrl = v,
                () => Helper.Translation.Get("gallery.config.server_url"),
                () => Helper.Translation.Get("gallery.config.server_url.tooltip"));
        }
        catch (Exception ex)
        {
            Monitor.Log($"GMCM registration failed: {ex.Message}", LogLevel.Trace);
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (_api == null) return;
        if (!Context.IsWorldReady) return;
        if (Game1.activeClickableMenu != null) return;

        if (e.Button == _config.OpenFarmerTabKey && _config.OverrideCharacterSheet)
        {
            OpenMenuAtTab(0);
            Helper.Input.Suppress(e.Button);
            return;
        }

        if (e.Button == _config.OpenEditorKey)
        {
            OpenMenuAtTab(1);
        }
    }
}
