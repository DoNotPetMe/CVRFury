# CVRFury Architecture

CVRFury mirrors VRCFury's mental model — *components describe intent, a build
pipeline bakes them non-destructively* — adapted to ChilloutVR's CCK and its
quirks.

## The build hook (the hard part)

VRChat's SDK gives tools a formal entry point: `IVRCSDKPreprocessAvatarCallback`.
**ChilloutVR's CCK has no such interface.** The community-proven workaround
(used by lilToon's `ChilloutVRModule` and the NDMF-for-CVR projects) is to
subscribe by **reflection** to two static `UnityEvent<GameObject>` fields on
`ABI.CCK.Scripts.Editor.CCK_BuildUtility`:

- `PreAvatarBundleEvent` — fires on an avatar GameObject just before bundling.
- `PrePropBundleEvent` — same, for props/spawnables.

We subscribe to the avatar event in `CckBuildHook` (`[InitializeOnLoad]`) and run
`CVRFuryBuilder.Run(go)`.

### Why reflection rather than an assembly reference?

The CCK ships its scripts in Unity's default `Assembly-CSharp` with **no
`.asmdef`**. An asmdef-based, distributable package therefore *cannot* reference
CCK types at compile time. Reflection is the only robust option — and it has a
bonus: CVRFury installs and compiles even when the CCK isn't present yet.

All CCK type/member name strings live in **one** file, `Editor/Hooks/CckNames.cs`.
If a CCK update renames something, that's the only place to edit. `Reflect.cs`
fails soft (logs a clear warning, returns null) so a rename downgrades a feature
to "skipped" instead of a broken upload.

## Non-destructive guarantees

1. The bake runs on the GameObject the CCK is about to bundle.
2. Generated controllers/clips are **clones** saved to a temp folder
   (`Assets/_CVRFury/Generated`), wiped at the start of each build. The avatar's
   build instance is repointed at the clone, so the original animator asset is
   never touched.
3. Every `CVRFuryComponent` is removed from the build instance before upload.

> **Assumption to validate per CCK version:** `PreAvatarBundleEvent` is expected
> to fire on the CCK's build instance (or for the change to be acceptable on the
> live object, as lilToon assumes by restoring afterward). If a future CCK fires
> it on the live scene object *without* a build copy, the bake should be moved
> onto an explicit clone here. This is called out in the roadmap.

## Feature framework

```
CVRFuryComponent (Runtime)         IFeatureBuilder (Editor)
  ├─ CVRFuryToggle          ←→       ToggleBuilder : FeatureBuilder<CVRFuryToggle>
  ├─ CVRFuryFullController  ←→       FullControllerBuilder
  ├─ CVRFuryArmatureLink    ←→       ArmatureLinkBuilder
  ├─ CVRFuryBlendshapeLink  ←→       BlendshapeLinkBuilder
  └─ CVRFuryObjectState     ←→       ObjectStateBuilder
```

`FeatureBuilderRegistry` discovers builders by reflection over the editor
assembly, so **adding a feature = add a component + a builder**. No central list.

`BuildContext` threads shared state through a bake:
- the working `AnimatorController` (a clone, created lazily),
- the temp `AssetSaver`,
- a unique synced-parameter name allocator,
- a `BuildLog`.

Components carry a `BuildPriority`; structural features (Armature Link = −20,
Object State = −10) run before animator features so later builders see the final
hierarchy.

## ChilloutVR AAS specifics

The CCK's Advanced Avatar Settings are a list of `CVRAdvancedSettingsEntry`
objects on `CVRAvatar.avatarSettings`. Each entry has a display `name`, a synced
`machineName` (which is also the animator parameter name), a `type`
(`GameObjectToggle`, `Slider`, `GameObjectDropdown`, joystick/input/material
variants…), and a typed `setting` payload. `CckAvatar` wraps creating and adding
these entries.

CVRFury's toggle therefore does two things at once: it builds its own animator
**layer** (so it can animate anything, not just a GameObject's active state), and
it registers a matching synced **AAS entry** with the same `machineName` so the
control appears in the in-game radial/menu and syncs to other players.
