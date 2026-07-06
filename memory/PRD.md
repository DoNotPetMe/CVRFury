# CVRFury — PRD & Progress Memory

## Original problem statement (user, verbatim intent)
Modify the ChilloutVR avatar convertor repo (https://github.com/DoNotPetMe/CVRFury) on a new branch:
1. Fix the **Gesture component** — it stopped uploads with a generic build error in the CCK.
2. **Drop-down menus in advanced avatars for Presets** — equipping one disables the others.
3. **Automate custom crouch/prone animations** triggered by an in-game dropdown, using
   "NotAKidoS SimpleAAS" (https://github.com/NotAKidoS/SimpleAAS). User said they would provide
   the Unity package with the animations + the tool (NEVER UPLOADED — no assets in this job).
4. **Blendshape logic component**: auto-detect blendshapes on a GameObject, set blendshape values
   when specific GameObjects are enabled/disabled (clean slider/clickable UI), multi-condition
   logic, and create toggles out of blendshapes.

User language: English. Repo is a Unity Editor package (C#), NOT a web app — no React/FastAPI/Mongo.
Working branch: `feature/gesture-fix-presets-locomotion-blendshape-logic` (created 2026-07-06).

## Key domain facts (verified against official docs / CCK source mirror)
- CVR gesture values are **−1 … 6**, NOT VRChat's 0–7: OpenHand=−1, Neutral=0, Fist=1 (Float is
  analog 0.01–1.0), ThumbsUp=2, Gun=3, Point=4, Peace=5, RockNRoll=6.
  Discrete Ints: `GestureLeftIdx`/`GestureRightIdx`. Source: docs.chilloutvr.net → Animator Core Parameters.
- CCK upload: `CCK_BuildUtility.BuildAndUploadAvatar` fires `PreAvatarBundleEvent` on the LIVE
  scene object, saves a prefab, builds an asset bundle. CCK's AAS generation clones
  `avatarSettings.baseController` → `animator` and runs `SetupAnimator` per entry (skips entries
  whose machineName already exists as a param). Mirror studied: TayouVR/CVR-CCK (in /tmp/cck).
- Proven AAS wiring pattern (converter path `AasControllerGenerator`): set baseController +
  animator + initialized + overrides (container) + CVRAvatar.overrides + Animator.runtimeAnimatorController.

## Implemented 2026-07-06 (v0.10.0, all on the feature branch)
1. **Gesture fix** (3 root causes):
   - `GestureBuilder.ToCvrGestureIndex` maps the enum (kept in VRChat order for serialization
     compat) to CVR's real −1…6 indices; layer keys on `GestureLeftIdx`/`GestureRightIdx` (Int,
     Equals/NotEqual) instead of float windows. Unit-tested (`Tests/Editor/GestureMappingTests.cs`).
   - `CVRFuryBuilder.FinalizeAnimators` now uses `CckAvatar.AttachGeneratedController` + `Persist()`
     (full AAS wiring — the old code left `avatarSettings.animator`/`overrides` null, the suspected
     source of the generic CCK build error on gesture-only avatars).
   - Gesture layers get the no-humanoid AvatarMask.
2. **Presets** (`CVRFuryPresets` + `PresetsBuilder`, priority 10): AAS Int dropdown; option 0
   "Custom" = empty clip (manual toggles keep control); each preset merges referenced
   CVRFuryToggles' ON actions; union coverage via `ClipBuilder.BuildExclusive` forces everything
   other presets reference to resting → equip one disables the others.
3. **Locomotion Styles** (`CVRFuryLocomotionStyles` + builder): dropdown "Default" + styles; each
   style = crouchClip/proneClip; pose states added INSIDE the locomotion layer (SyncDances/emote
   pattern, found via `ControllerGuard.FindLocomotionLayerIndex`), gated AnyState→(param==i &&
   Crouching/Prone), exits on either mismatch. Works with any clips (user's SimpleAAS pack never
   uploaded, so built generically).
4. **Blendshape Logic** (`CVRFuryBlendshapeLogic` + builder + `BlendshapeLogicEditor`, priority 40):
   rules = conditions (GameObjects on/off, AND) + assignments (blendshape dropdown + 0–100 slider
   via `BlendshapeAssignmentDrawer`). Conditions resolve to the Bool param of the CVRFuryToggle
   driving the object (`BuildContext.FeatureParams`, populated by ToggleBuilder); layer built with
   new `AnimatorUtil.AddMultiConditionBoolLayer`. Inspector auto-detects the mesh and has
   "create toggle from blendshape" (one click adds a configured CVRFuryToggle).
5. **Play Mode Tester** upgrades: Standing/Crouching/Prone stance toolbar + Gesture Left/Right
   dropdowns driving GestureLeftIdx/Idx + Float variants — so all new features are testable in
   Play mode without uploading.
6. Version bumps (package.json + CckNames.CvrFuryVersion → 0.10.0), CHANGELOG entry, README table,
   ROADMAP rows, .meta files generated for all new sources.

## Testing status
- No Unity available in this environment. Validation done: tree-sitter syntax parse of all 83 .cs
  files (0 errors), manual cross-reference audit of every new API usage, CCK source mirror
  verification of the AAS wiring pattern, official CVR docs verification of gesture values.
- NUnit EditMode tests added for the gesture mapping (run in Unity Test Runner).
- FINAL VERIFICATION REQUIRES the user's Unity + CCK project: compile, Test Runner, Play Mode
  Tester, and a CCK upload with a gesture-only avatar.

## Backlog / next (P0 → P2)
- P0: User uploads the crouch/prone animation package + SimpleAAS tool → wire exact clips/preset
  integration if the generic component needs adjustments (e.g. movement blend trees per style).
- P1: User in-Unity verification of the gesture fix (gesture-only avatar upload) — if the CCK
  error persists, get the exact Console stack trace (the generic dialog hides it).
- P1: Optional: momentary-toggle support (`CVRFuryToggle.momentary` field exists but ToggleBuilder
  doesn't use it — pre-existing gap).
- P2: Blendshape Logic: support conditions on AAS GameObject-toggle entries (not just CVRFury
  Toggles); optional slider creation from blendshapes.
- P2: ROADMAP leftovers: SPS target finalisation, armature-link humanoid hardening, CI CCK stubs.

## Conventions for future agents
- All CCK names by reflection via `Editor/Hooks/CckNames.cs` only. Builders auto-discovered
  (component subclass + FeatureBuilder<T> = feature). Clips relative to avatar root via
  ClipBuilder. Humanoid anims go INSIDE the locomotion layer, never separate override layers
  (motorbike pose). Toggle params are Bool (synced-bit budget). `.meta` files required for new
  assets. Don't run npm/yarn on package.json — it's a Unity manifest.
