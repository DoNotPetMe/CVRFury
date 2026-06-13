# Changelog

All notable changes to CVRFury are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
