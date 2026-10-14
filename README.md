# OwnerCheck — dead-owner crash guard for Cities: Skylines II

Prevents a crash to desktop that happens after adding or removing sub-buildings (e.g. port and offshore oil rig extensions), and logs exactly which objects caused it.

## The crash

```
NullReferenceException
  at Unity.Entities.BufferLookup`1[T].get_Item (Entity entity)
  at Game.Objects.SubObjectReferencesSystem+UpdateSubObjectReferencesJob.Execute (...)
```
(With Burst on — the normal case — the game just closes; the native crash is in `lib_burst_generated.dll`.)

`SubObjectReferencesSystem` updates an owner's `SubObject` list whenever one of its sub-objects is created, updated or deleted. If that owner has already been destroyed, the lookup reads a null archetype and the game dies. Editing port / oil-rig sub-buildings regularly leaves decoration props (mostly `WallLightShortRange01`) pointing at an owner that no longer exists.

## What the mod does

1. **Load scan (read-only):** after a city loads, checks every owned entity and logs any whose owner doesn't exist.
2. **Live watch:** every frame, checks only entities that are being created / updated / deleted (cheap), in every modification phase, and logs any with a dead owner immediately.
3. **Guard (on by default):** for *objects* with a dead owner, removes the dangling `Owner` component so the vanilla job skips them instead of crashing. Preview objects are discarded by the game anyway; a real object becomes a standalone prop.

Disable the guard (log only): create an empty file `ModsData/OwnerCheck/guard_off.txt` in the game's user data folder.

Log: `Logs/OwnerCheck.log` (recreated each launch).

## Install (local mod)

Copy `OwnerCheck.dll` to:
```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\OwnerCheck\
```
Check `Logs/Modding.log` for `Loaded OwnerCheck`. Uninstall by deleting the folder — the mod stores nothing in saves.

## Build

Requires a modern C# compiler (Roslyn). No Unity or modding toolchain needed:
```powershell
.\build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II" -Csc "path\to\Microsoft.Net.Compilers.Toolset\tasks\net472\csc.exe"
```
Add `-Install` to copy the result into your local Mods folder.
The script compiles `src/OwnerCheck.cs` against the game's `Cities2_Data/Managed` assemblies into `bin/OwnerCheck.dll`.

## Notes

- This works around the symptom. The underlying issue (sub-objects outliving their owner) is in the game and has been reported to Paradox.
- After a game patch, rebuild against the new game assemblies if the mod fails to load.

## License

MIT — see `LICENSE`.
