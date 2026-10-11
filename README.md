# Video TV for Anime Shop Simulator

A MelonLoader mod that adds wall TVs that play your own videos. Put videos in `UserData\VideoTV` and buy the
**Video TV** in the shop; every subfolder (`UserData\VideoTV\<Name>`) becomes its own channel with its own TV.
Made by **Fabianens**.

- Each TV plays its channel's videos in file name order; all TVs of a channel show the same picture
  (one video player and one screen texture per channel).
- Look at a TV for on-screen buttons: pause / play, previous, next. Mute works from anywhere.
- Video stops when you walk away and the player is released after 20 seconds; it continues where it was when you come back.
- Sold only on the [Fabianens Mod Market](https://github.com/universesofcyberpunk/anime-shop-simulator-mod-market) MODS screen, under its own **TV** filter.

## Requirements

- MelonLoader 0.7.x
- [Fabianens Mod Market](https://github.com/universesofcyberpunk/anime-shop-simulator-mod-market) 1.4.0 or newer (the TV is bought on its MODS screen)
- Recommended: Fabianens ModSettings (F10 menu) for volume, keys and the other settings

## Install

Needs MelonLoader 0.7.x. Download the ready mod from Nexus Mods (or the [Releases](../../releases) page) and extract it
into the game folder, so that `Mods\VideoTV.dll` ends up in the game's `Mods` folder.

Supported formats are what Unity's VideoPlayer plays on Windows; H.264 `.mp4` and VP8 `.webm` are the safe choices.
Settings (volume, keys, play distance, screen resolution, TV size and shape, price) are in `UserData\MelonPreferences.cfg`
under `[VideoTV]`, or in the F10 menu with ModSettings.

## How the video works

The game is IL2CPP and its managed code has most `UnityEngine.Video.VideoPlayer` methods stripped, but the engine
(`UnityPlayer.dll`) still has all of them. The mod resolves the VideoPlayer internal calls directly with
`il2cpp_resolve_icall` and calls them through function pointers, so a full video player works without shipping
anything extra. The TV itself is cloned from one of the game's posters (16:9 screen and a black frame).
No save methods and no FishNet SyncVar callbacks are patched.

## Build from source

1. Install the [.NET 6 SDK or newer](https://dotnet.microsoft.com/download) and run the game once with MelonLoader 0.7.x
   (that generates `MelonLoader\Il2CppAssemblies`, which the project references).
2. Build:

```bat
dotnet build -c Release
:: game not in the default Steam folder?
dotnet build -c Release -p:GameDir="D:\Games\Anime Shop Simulator"
```

The DLL is copied to the game's `Mods` folder (close the game first). The game's own DLLs are not in this repository.

| File | What it is |
|---|---|
| `VideoTV.cs` | the whole mod: channels, TV item, video player through engine icalls, controls |

## Licence

MIT, see `LICENSE`. If you reuse the code, credit **Fabianens**.
