# Changelog

All notable changes to CVRFury are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.1] - 2026-06-14

Conversion fixes from first real-world run (GodWhisper avatar).

### Fixed
- **"Motorcycle pose" / avatar stuck in a pose** — the playable-layer merge was
  bringing in VRChat's Base/Additive/Action/Sitting layers, whose full-body
  animations fight ChilloutVR's own locomotion. Now **only the FX layer is
  merged** (Gesture is opt-in); the rest are skipped and logged.

### Added
- Option **Remove FinalIK / VRIK** (recommended for CVR, which has its own IK) —
  a leftover VRIK can lock the avatar in a pose.
- Option to also merge the Gesture layer (off by default; can clash with CVR
  hand gestures).
- Much louder expression-conversion logging: reports whether the menu and its
  control list resolved and how many controls/entries were processed, so an empty
  in-game menu can be diagnosed from the CVRFury Build Log.

## [0.5.0] - 2026-06-13

The big one: a VRChat → ChilloutVR conversion engine.

### Added
- **VRChat → ChilloutVR Converter** (*Tools ▸ CVRFury ▸ VRChat → ChilloutVR
  Converter*) — a toggle-driven window that turns a VRChat avatar into a
  CVR-ready one. Reads the VRChat avatar purely by reflection (no compile-time
  VRChat SDK dependency; the SDK must be present at convert time so its types
  load — the standard "convert then remove the SDK" workflow). Modular,
  per-step, each independently toggleable:
  - **Avatar basics** (default on) — VRCAvatarDescriptor viewpoint, viseme face
    mesh + lipsync, blink, and eye movement → CVRAvatar.
  - **PhysBones → DynamicBones** (opt-in) — VRCPhysBone/VRCPhysBoneCollider →
    DynamicBone/DynamicBoneCollider, colliders re-linked. Physics values are
    approximate (different models) and may need tuning.
  - **Expressions → AAS** (opt-in) — VRCExpressionsMenu controls (toggle, radial,
    puppet, nested submenus) + VRCExpressionParameters → ChilloutVR Advanced
    Avatar Settings.
  - **Merge playable layers** (opt-in) — FX/Gesture/Action animator controllers
    merged into the CVR animator (reusing the parameter-remapping merger);
    GestureLeft/GestureRight names carry across.
  - **Strip VRChat + broken components** (default on) — removes all `VRC.*`
    components and missing scripts once data has been converted.
- "Enable all automatic" button for one-click aggressive conversion.
- Centralised VRChat/DynamicBone reflection names in `VrcNames.cs` (fail-soft,
  like `CckNames`).

### Notes
- Conversion edits the avatar in place (with undo) and writes generated
  controllers to `Assets/CVRFury Converted/`. Work on a copy.
- This complements (does not replace) external converters; it focuses on a
  built-in, non-destructive-feeling, toggle-driven path inside CVRFury.

## [0.4.0] - 2026-06-13

VRChat-import quality-of-life and CCK integration verification.

### Added
- **Missing-script cleaner** — the headline pain of importing a VRChat avatar into
  CVR is the swarm of broken "The associated script can not be loaded" components
  (VRCAvatarDescriptor, PhysBones, contacts, VRCFury, …) that have no script in a
  CVR project. CVRFury now:
  - strips them automatically during the bake (toggle: *Tools ▸ CVRFury ▸
    Auto-Clean Missing Scripts on Build*, on by default), and
  - offers a one-click, **prefab-aware** *Tools ▸ CVRFury ▸ Clean Missing Scripts
    on Selected* that fixes the prefab **asset** permanently (or just the instance),
    with undo for plain scene objects.
- **CCK integration diagnostic** (*Tools ▸ CVRFury ▸ Diagnose CCK Integration*):
  self-discovers the CCK pre-bundle events and statically validates the entire AAS
  reflection contract (CVRAvatar fields, settings entry/enum, typed setting classes)
  so version mismatches surface instantly without an upload.
- **Self-discovering build hook** — finds the CCK's avatar/prop bundle events by
  reflection instead of a single hard-coded name, resilient to CCK renames.

### Verified
- Confirmed against a live CCK install: `ABI.CCK.Components.CVRAvatar` and
  `ABI.CCK.Scripts.Editor.CCK_BuildUtility.Pre{Avatar,Prop}BundleEvent` resolve and
  are hooked correctly.

## [0.3.0] - 2026-06-13

More features, hardening, and the first tests.

### Added
- **Gesture** — play an animation while a hand holds a specific gesture (fist,
  open hand, point, victory, …), reading ChilloutVR's `GestureLeft`/`GestureRight`
  parameters. Builds an animator layer keyed on the gesture value.
- **Parameters** — declare animator parameters (Float/Int/Bool), optionally
  exposed in the in-game menu as a toggle or slider. Lets prefabs declare the
  synced parameters a companion Full Controller expects.
- **Automatic Fixes** pass — prunes Advanced Avatar Settings entries that have no
  parameter name and warns about duplicate synced parameter names (which would
  collide in-game). Never renames, to avoid breaking the animator↔menu link.
- **EditMode test suite** — `ParamNameAllocator` (uniqueness/sanitisation) and
  `HierarchyUtil.GetPath` are covered by NUnit tests in a dedicated test assembly.

### Changed
- Extracted synced-parameter name allocation into a pure, unit-tested
  `ParamNameAllocator`. `InternalsVisibleTo` exposes internals to the test assembly.

## [0.2.0] - 2026-06-13

Feature expansion toward VRCFury parity.

### Added
- **Modes** — exclusive multi-state control baked as a synced AAS dropdown plus an
  animator layer with mutually-exclusive states (outfit/hair/weapon variants).
  Clips are built with union binding coverage so switching modes fully resets.
- **Slider** — continuous radial/puppet control baked as a synced AAS slider plus
  a 1D blend-tree animator layer interpolating a 0% and 100% state.
- **Avatar Settings** helper — sets the CVRAvatar viewpoint / voice position, face
  mesh, and the viseme / blink / eye-movement toggles from a prefab.
- **Blendshape Link "keep live"** — now actually implemented: animated source
  blendshape curves (from toggles/modes/sliders) are mirrored onto linked meshes,
  not just copied once statically.
- **Prop / spawnable pipeline** — hooks `CCK_BuildUtility.PrePropBundleEvent`;
  applies structural features (Object State) and strips CVRFury components.
- **Synced-parameter budget check** — warns when an avatar accumulates an unusually
  large number of synced Advanced Avatar Settings.

### Reflection layer
- Added AAS dropdown option types/fields and CVRAvatar viseme/blink/eye/viewpoint
  field names to `CckNames.cs`.

## [0.1.0] - 2026-06-13

Initial public foundation.

### Added
- **Non-destructive build pipeline** that hooks the ChilloutVR CCK avatar build
  via reflection (`CCK_BuildUtility.PreAvatarBundleEvent`). Source scenes are
  never modified; all baking happens on a throwaway clone the CCK uploads.
- **Reflection layer** (`Cck.*`) that wraps `ABI.CCK.Components.CVRAvatar` and
  the Advanced Avatar Settings (AAS) data model, isolating all version-sensitive
  member names in one place.
- **Feature framework** mirroring VRCFury: each feature is a `CVRFuryComponent`
  you drop on the avatar; a matching `FeatureBuilder` bakes it at build time.
- Implemented features:
  - **Toggle** — menu toggle that animates objects / blendshapes / materials,
    registered as an AAS GameObject Toggle so it shows in the in-game menu.
  - **Object State** — force objects on/off (and apply default states) at build.
  - **Full Controller** — merge an external Animator Controller (and its
    parameters / AAS entries / menus) into the avatar.
  - **Armature Link** — attach prop armatures to matching avatar bones
    (VRCFury-style), with bone-name matching and merge/keep modes.
  - **Blendshape Link** — drive blendshapes on linked meshes from a source mesh.
- Editor inspectors for every feature, plus a build-log window.

### Known limitations
- CCK member names are resolved by reflection and centralised in
  `Editor/Hooks/CckNames.cs`; if a future CCK release renames members, update
  that file. CVRFury logs a clear diagnostic and skips a feature rather than
  breaking your upload.
- Not yet at full VRCFury parity. See `Documentation~/ROADMAP.md`.
