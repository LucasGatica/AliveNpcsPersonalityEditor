# AliveNpcs Personality Editor

An in-game editor for the NPC personality profiles used by [AliveNpcs](https://www.nexusmods.com/stardewvalley/mods/43475).

It is a standalone companion mod: install it alongside AliveNpcs, then press `F10` in a loaded save to edit NPC profiles without editing JSON by hand. Press `F7` to open the farmer character sheet directly.

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0+
- AliveNpcs 1.4.5+

Generic Mod Config Menu is not required. AliveNpcs itself still needs its normal AI-provider configuration.

## Features

- Full-screen Stardew-style menu with **Farmer**, **NPCs**, and **Catalog** tabs.
- Portrait grid for every NPC currently accepted by the AliveNpcs compatibility API.
- Per-NPC checkbox to disable or re-enable all AliveNpcs interactions while preserving base-game dialogue.
- NPC editor for appearance, personality, lore, social tags, gender, manners, social anxiety, optimism, socialization, and romance availability.
- Farmer character sheet editor backed by the same save-specific file used by AliveNpcs.
- Community catalog search, preview, import, upload, and optional owner deletion.
- Configurable NPC editor (`F10`) and farmer sheet (`F7`) hotkeys.
- Immediate AliveNpcs reload after a save; the next AI conversation can use the new profile.
- Localized UI: English, Brazilian Portuguese, Spanish, French, German, and Italian.
- Safe file replacement when saving personality data.

The editor follows the NPC availability returned by the current AliveNpcs API. It does not bypass AliveNpcs compatibility settings or the community AI opt-out dataset.

## Install

1. Install SMAPI and AliveNpcs.
2. Extract `AliveNpcsPersonalityEditor` into `Stardew Valley/Mods`.
3. Load a save and press `F10`.
4. Select a villager, write a profile, and click **Save**.

## Configuration

The first launch creates `config.json` in the mod folder:

```json
{
  "OpenEditorKey": "F10",
  "OpenFarmerTabKey": "F7",
  "OverrideCharacterSheet": true,
  "GalleryServerUrl": "http://localhost:3000",
  "GalleryEnabled": true
}
```

Hotkeys accept any valid SMAPI button name. Set `GalleryEnabled` to `false` when no catalog server is available.

## Data and multiplayer

NPC overrides live as one JSON file per NPC in the editor's `overrides` folder. They are global to that game installation and can be backed up or copied to another installation. Farmer sheets remain save-specific under the AliveNpcs `Data/<saveId>/character_sheet.json` path.

In multiplayer, players who want the same NPC profiles should use the same override files. The editor does not change vanilla dialogue, schedules, portraits, heart events, friendship values, or existing AliveNpcs memories.

## Writing profiles

Describe a character's temperament, values, speech style, habits, boundaries, and how they react to trust or conflict. Keep a recognizable core and let AliveNpcs add the save-specific memories, relationships, mood, gossip, and story context.

Example:

> Sebastian is private and dryly funny, using sarcasm when he feels cornered. He values independence and notices when someone respects his space. Around trusted friends he becomes unexpectedly playful and talks more openly about technology, music, and leaving town one day.

## Support

- [AliveNpcs Discord](https://discord.gg/8vUfXEH852)
- [Buy Me a Coffee](https://buymeacoffee.com/gaticadev)
- [LivePix](https://livepix.gg/gaticadev)
