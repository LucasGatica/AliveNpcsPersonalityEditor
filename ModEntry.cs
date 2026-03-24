using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

public sealed class ModEntry : Mod
{
    private PersonalityStore _store = null!;
    private EditorConfig _config = null!;
    private IAliveNpcsApi? _api;

    public override void Entry(IModHelper helper)
    {
        _config = helper.ReadConfig<EditorConfig>();
        _store = new PersonalityStore(helper.DirectoryPath, Monitor);
        _store.Load();

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    public override object GetApi() => new PersonalityEditorApi(this);

    public void OpenEditorMenu()
    {
        if (_api == null || !Context.IsWorldReady) return;
        Game1.activeClickableMenu = new PersonalityEditorMenu(_store, _api, Monitor, Helper.Translation);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        _api = Helper.ModRegistry.GetApi<IAliveNpcsApi>("Lucas.AliveNpcs");
        if (_api is null)
            Monitor.Log("AliveNpcs not found — personality editor cannot function.", LogLevel.Error);
        else
            Monitor.Log($"Personality Editor ready. Press {_config.OpenEditorKey} to open.", LogLevel.Info);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (_api == null) return;
        if (!Context.IsWorldReady) return;
        if (e.Button != _config.OpenEditorKey) return;
        if (Game1.activeClickableMenu != null) return;

        Game1.activeClickableMenu = new PersonalityEditorMenu(_store, _api, Monitor, Helper.Translation);
    }
}
