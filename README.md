# OwnerCheck — sub-building crash guard for Cities: Skylines II

Prevents a crash to desktop that happens after adding or removing sub-buildings (e.g. port and offshore oil rig extensions), and logs exactly which objects caused it.

## The crash

```
NullReferenceException
  at Unity.Entities.BufferLookup`1[T].get_Item (Entity entity)
  at Game.Objects.SubObjectReferencesSystem+UpdateSubObjectReferencesJob.Execute (...)
```
With Burst on (the normal case) the game simply closes; the native crash is in `lib_burst_generated.dll`.

`SubObjectReferencesSystem` updates an owner's `SubObject` list whenever one of its sub-objects is created, updated or deleted. If that owner has already been destroyed, the lookup reads a null archetype and the game dies. Editing port / oil-rig sub-buildings regularly leaves decoration props (mostly `WallLightShortRange01`) pointing at an owner that no longer exists.

## What the mod does

1. **Load scan (read-only):** after a city loads, checks every owned entity and logs any whose owner doesn't exist.
2. **Live watch:** every frame, checks only entities being created / updated / deleted (cheap), in every modification phase.
3. **Crash guard (on by default):** for *objects* with a dead owner, removes the dangling `Owner` component so the vanilla job skips them instead of crashing. Preview objects are discarded by the game anyway; a real object becomes a standalone prop.

Settings (in-game **Options → OwnerCheck**): *Crash guard* and *Verbose log*.
Log: `Logs/OwnerCheck.log` (recreated each launch). The mod stores nothing in saves.

## Building

Requires the official Cities: Skylines II modding toolchain (in-game **Options → Modding → Install**).

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'   # toolchain post-processor targets .NET 6; needed if only a newer runtime is installed
dotnet build -c Release
```
The build is deployed automatically to your local `Mods/OwnerCheck` folder.

## Publishing (maintainer)

```powershell
.\publish.ps1 -Action New       # first upload; writes the ModId into Properties\PublishConfiguration.xml
.\publish.ps1 -Action Version   # new version (bump ModVersion / ChangeLog first)
.\publish.ps1 -Action Update    # listing text / thumbnail only
```

## Notes

- This works around the symptom. The underlying issue (sub-objects outliving their owner) has been reported to Paradox.
- After a game patch, rebuild if the mod fails to load.

## License

MIT — see `LICENSE`.
