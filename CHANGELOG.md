# Changelog

All notable changes to CVRFury are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.4] - 2026-06-14

Diagnostics to pin down why the synced-bit overflow can persist after the 0.5.3
fix. When the bit count comes back *identical* (e.g. still 6848/3200), the new
encoding isn't reaching the entries — almost always because the package didn't
recompile in the Unity project.

### Added
- **Version stamp in the conversion log** — the first line now reads
  `CVRFury vX.Y.Z — converting …` so you can confirm at a glance which build ran.
  If it doesn't say the latest version, update/reimport the package (Package
  Manager ▸ CVRFury ▸ Update, or reimport the git package) and re-run.
- **AAS encoding readback** — after building Advanced Avatar Settings, CVRFury
  reads each entry back and reports `N Bool, N Int, N Float — est. ~X synced bits`.
  Toggles should be **Bool**; if they show as **Float**, the `usedType` fix is not
  active (stale build) and the log says so explicitly. `6848` bits ≈ 107 Float
  toggles × 64 bits — the readback makes that visible without an upload.

### Notes
- This release changes no conversion behaviour beyond logging; if the readback
  shows all-Float toggles on the latest version, that's a genuine bug to report
  (paste the readback line).

## [0.5.3] - 2026-06-14

The real fix for "toggles do nothing" **and** the synced-bit overflow: CVRFury
was writing the Advanced Avatar Settings data model with the wrong member names.
A live CCK type dump pinned the actual model, and the AAS writer was rebuilt
against it.

### Fixed
- **Wrong AAS member names → toggles never worked and every parameter was a Float.**
  The previous writer set a single non-existent `setting` field, an `isLocal`
  field that doesn't exist, and (the killer) mis-set the parameter's `usedType` to
  the menu's `SettingsType` value. CVRFury now writes the real model:
  - the typed settings object is attached to its correct **per-type field**
    (`toggleSettings` / `sliderSettings` / `dropDownSettings`) on the entry,
  - the entry's `type` is set to the correct `SettingsType` member
    (`Toggle` / `Slider` / `Dropdown`), resolved straight from the field's enum, and
  - each setting's **`usedType`** is set explicitly — **`Bool` for toggles**,
    `Int` for dropdowns, `Float` for sliders.
- **"Over the Synced Bit Limit (6848/3200)."** Root cause was the `usedType` bug
  above: toggles defaulted to **Float** (≈32 bits each) instead of **Bool**
  (≈1 bit). Encoding toggles as Bool and dropdowns as Int cuts a typical avatar's
  synced-bit usage by an order of magnitude, so the CCK can build the controller.
- **`baseController` written to the wrong object.** `CVRAvatar` has no
  `baseController`; the animator lives on `avatarSettings.baseController`. The
  setter now targets the AAS container only (and ensures it exists first).

### Notes
- The "Make all parameters local" option is retained, but the real bit savings
  now come from correct per-parameter `usedType` encoding (CVR's AAS has no
  per-entry local flag — bit cost is driven entirely by the parameter type).
- Verify against your CCK with *Tools ▸ CVRFury ▸ Diagnose CCK Integration*: the
  data-model contract now checks the per-type setting fields and each setting's
  `usedType`.

## [0.5.2] - 2026-06-14

Synced-bit budget + pose fixes from the GodWhisper conversion (the CCK was
refusing to build the AAS controller: "over the Synced Bit Limit").

### Fixed
- **"Over the Synced Bit Limit" → toggles do nothing.** The converter was syncing
  every parameter. Now it:
  - honours VRChat's per-parameter **networkSynced** flag (non-synced params become
    **local** in CVR, costing zero synced bits),
  - **de-duplicates** a parameter referenced by multiple menu controls (one AAS
    entry per parameter), and
  - reports synced vs local counts + a clear warning about the 3200-bit cap.
- **"Motorcycle pose."** The merged controller was being set as the Base Controller
  with no locomotion. It is now **seeded from ChilloutVR's default avatar animator**
  (locomotion/idle preserved) before FX layers are added on top.

### Added
- Converter option **"Make all parameters local"** — forces every converted
  parameter local so the CCK can always build the controller (others won't see your
  toggles; use it to verify the avatar works, then selectively re-enable sync).

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
