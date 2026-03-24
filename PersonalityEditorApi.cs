using StardewModdingAPI;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

public class PersonalityEditorApi
{
    private readonly ModEntry _mod;

    public PersonalityEditorApi(ModEntry mod)
    {
        _mod = mod;
    }

    public void OpenMenu()
    {
        _mod.OpenEditorMenu();
    }
}
