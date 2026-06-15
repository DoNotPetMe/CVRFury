# Changelog

All notable changes to CVRFury are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.12] - 2026-06-15

### Fixed
- **"Link CCK Parameters" is now non-destructive.** It previously cleared the AAS list before rebuilding,
  which wiped on/off clips you'd assigned by hand. It now keeps every existing entry (and its clips) and
  only adds parameters that aren't already present, so re-running is safe.

### Added
- **`Tools ▸ CVRFury ▸ Link Toggle Animations from Folder`** — a clip scanner. Point it at a folder, tell
  it the word ON clips end with (e.g. `toggled`) and OFF clips end with (e.g. `default`); it pairs clips
  by base name, matches each base to an existing toggle entry (by menu name or machine-name leaf,
  normalised so spacing/case/punctuation don't matter), and assigns the on/off clips onto that entry.
  Strictly non-destructive — only fills clip fields, never clears entries or edits the controller. Reports
  toggles with no matching clip and clip pairs with no matching toggle. Adds `CckAvatar.SetToggleClips`.

## [0.9.11] - 2026-06-15

### Fixed
- Compile error in `AasParameterLinker` (`CS0104: 'Object' is an ambiguous reference`): qualified the
  submenu-cycle guard's `UnityEngine.Object` uses now that the file imports `System`.

## [0.9.10] - 2026-06-15

**"Link CCK Parameters" now auto-assigns GameObject targets.** The red "parameter not present in the
animator's parameters" warnings appear because the Base Controller (CVR's stock `AvatarAnimator`) doesn't
contain the toggle parameters — in CVR those are generated from the AAS entries. For avatars whose
clothing items are separate GameObjects, the linker now wires each toggle as a **CVR-native GameObject
toggle**: it matches the toggle's parameter (e.g. `Toggle/Witch Outfit/Corset`) to a GameObject in the
hierarchy (a "Corset" object), preferring one whose ancestor path contains the parameter's category
segment when names collide, and writes it into the entry's `gameObjectTargets` (onState = visible-when-on).

After running, one click of the CCK's **Create Controller** generates a parameter per entry (clearing the
red warnings) and the toggles directly show/hide their objects — no animation clips or custom controller
needed. Toggles it can't confidently match (presets, idle/animation toggles) are listed in the summary so
they can be assigned by hand. Added `CckAvatar.AddGameObjectToggle`.

## [0.9.9] - 2026-06-15

**New: one-click "Link CCK Parameters from VRChat Menu" — for when you've already made your own
controller.** Adds `Tools ▸ CVRFury ▸ Link CCK Parameters from VRChat Menu (keep my controller)`. It
walks the avatar's VRChat Expressions Menu (following submenus), and for every control creates a
matching ChilloutVR Advanced Avatar Setting with its **Machine Name set to the exact VRChat parameter
name** and the default value carried over (Toggle for bool params, Slider for radial/float). It then
marks the AAS container initialized and refreshes the inspector.

It deliberately does **not** create, merge, generate or attach any animator controller — it leaves a
hand-made/attached controller untouched, and simply wires the menu parameters to it. Because it walks
the menu rather than the raw parameter list, VRCFury-internal parameters (which never appear in the
menu) are skipped automatically. Re-running clears and rebuilds the AAS list so it can't duplicate.

## [0.9.8] - 2026-06-14

**The stuck "measure" pose: drop VRChat body-pose layers that move humanoid *bones*, not just muscles.**
0.9.7 brought the clothing/species back (the `Blend` fix worked — the avatar now renders correctly), but
the arms-out, fingers-splayed pose remained. Reading the controller: the avatar ships **GoGoLoco + a
calibration system** (`Calibration`, `LocalSolver` layers, plus `CalibrationCheck` in the hierarchy),
stacked at weight-1 Override *above* the gesture layers. CVR can't complete VRChat's contact-driven
calibration, so that layer holds the avatar in its measurement pose forever — overriding everything
below. 0.9.6's "drop humanoid-pose layers" only detected **muscle** curves; these layers pose the rig
through **Transform curves on humanoid bones** (and IK), which slipped through — so only 2 layers dropped
and the pose stayed.

### Fixed
- **Stuck calibration / IK / emote pose.** The merge now also drops a VRChat layer when its clips animate
  Transform curves on the avatar's actual **humanoid bones** (computed from the rig), not only muscle
  curves. This removes GoGoLoco's calibration "measure" pose, IK solvers and emote-pose layers — which
  CVR neither needs nor can drive — while keeping prop toggles (e.g. the daggers move their own objects,
  not bones) and CVR's own seeded locomotion. The build log now lists each dropped layer by name, and the
  pose diagnostic checks every state (not just the default) so a stuck non-default pose is reported.

## [0.9.7] - 2026-06-14

**The real root cause of dead toggles + invisible clothing + the human/furry blend stuck mid-morph: a
missing Direct Blend Tree weight parameter.** Reading the generated controller directly: the avatar's
entire toggle / blendshape / material / AAP system is driven by one always-on **Direct Blend Tree**
(VRCFury / d4rk style — layers `DBT_Toggles`, `DBT_Smoothing`, `DBT_Logic`). Every one of its **559**
children is weighted by a constant parameter named **`Blend`** — but `Blend` was **never declared as a
parameter** (VRCFury injects it at VRChat build time). Unity evaluates a missing `directBlendParameter`
as **0**, so the whole tree multiplied to zero: clothing shrink-blendshapes never reached their "shown"
value (enabled-but-invisible garments), the `Species` blend sat at its default (stuck between human and
furry — and being a body-shape blend, it also distorted the silhouette), and every AAP/toggle did
nothing. This was invisible in the build log because the conversion itself "succeeds" — the controller
is structurally valid, it just computes nothing.

### Fixed
- **Toggles / clothing / species now actually drive: missing blend-tree parameters are recreated.**
  `AnimatorUtil.EnsureBlendTreeParametersExist` scans every blend tree after the merge and declares any
  referenced parameter that doesn't exist — a Direct-Blend constant weight (e.g. `Blend`) defaults to
  **1** ("apply"), a 1D/2D blend position defaults to 0 (neutral). Run on both the merged and the
  generated/attached controller. The build log reports exactly what was restored.

## [0.9.6] - 2026-06-14

**The motorcycle pose, found by the v0.9.4 diagnostic and fixed at the source.** With the gesture
conditions fixed in 0.9.5, the build log's `AAS diagnostic — pose` line finally named the cause: 59
merged layers animate the humanoid rig at their default state, including `Locomotion/Emotes`, `LeftHand`
(`HandLeftOpen`), `RightHand` (`HandRightOpen`) and `Face Reset` — all at weight 1, Override. These are
VRChat playable-layer body-pose layers baked into the FX controller. ChilloutVR drives locomotion, hand
gestures, emotes and visemes/blink **natively** (from the seeded `AvatarAnimator`), so running these on
top overrides CVR's whole body and freezes the avatar in the motorcycle pose — which is why even CVR's
own *Create Controller* couldn't fix it: the conflict was in the merged data.

### Fixed
- **Motorcycle pose: humanoid-pose layers are no longer merged.** The merge now drops VRChat layers that
  animate humanoid muscles, root motion or IK goals — the hand-gesture, locomotion, emote and face
  layers CVR already provides. The build log reports how many were dropped.
- **VRCFury AAP toggles are preserved.** AAP clips drive a float *parameter* through a curve whose
  binding type is `Animator` — the same type as a muscle curve — so the new `HumanoidCurves` detector
  matches property names against Unity's humanoid muscle/root/IK list instead of binding type alone.
  Clothing/object/blendshape toggles (including AAP) are kept; only true body-pose layers are dropped.
  The pose diagnostic uses the same precise check, so AAP toggles are no longer mislabelled `[muscles]`.

### Known
- One `#`-local toggle (`#Nsfw/Toy/(s-b)Sps`) still reports a single "not compatible with condition
  type" transition at upload; CCK ignores that transition. It is local (zero synced bits) and does not
  affect other toggles — to be addressed separately.

## [0.9.5] - 2026-06-14

**Root cause of the motorcycle pose + the `GestureLeft/GestureRight ... not compatible with condition
type` spam, found in the generated controller.** Inspecting the actual `… AAS.controller` asset GodWhisper
produced: `GestureLeft`/`GestureRight` are **Float** — and have to be, because CVR drives them as floats
and they feed the hand-pose blend trees (`m_BlendParameter`). But GodWhisper's gesture-driven weapon
system (merged from the VRChat FX layer) gates **103 Equals + 40 NotEqual** transitions on them. Unity
only allows Equals/NotEqual on **Int** parameters, so every one of those transitions is invalid: the
weapon/gesture layer can never leave its posed ("Holding"/"Draw") state, which both spams the validator
and **freezes the arms in the "motorcycle pose."** A single Mecanim parameter cannot be both Float (for
the blend trees) and Int (for the conditions), so retyping is not an option.

### Fixed
- **Gesture-locked motorcycle pose + "not compatible with condition type" errors.** The harmoniser now
  keeps `GestureLeft`/`GestureRight` (and any Float blend parameter that is also Equals/NotEqual-gated)
  as Float and **rewrites those conditions into Float-compatible threshold windows**:
  - `Equals N` → `Greater(N-0.5)` AND `Less(N+0.5)` (in place — a transition's conditions are AND-ed).
  - `NotEqual N` → `Less(N-0.5)` OR `Greater(N+0.5)`; since one transition is AND-only, the transition is
    duplicated (one copy bounded below the value, one above), preserving all other conditions and timing.
  This is applied to both the merged CVR controller and the generated AAS controller, and is idempotent.
  The build log now reports how many transitions were rewritten and how many extra transitions were
  added to express NotEqual.

## [0.9.4] - 2026-06-14

**A build-log X-ray of the generated controller, to pin down the two field-only symptoms.**
After 0.9.3 the conversion log reports success, yet in-game the avatar still holds the "motorcycle
pose" and the menu toggles still do nothing — and this persists even after clicking the CCK's own
**Create Controller + Attach**. Neither symptom is visible from CVRFury's own bookkeeping, so this
release adds a diagnostic that inspects the controller CVRFury actually attaches and reports the two
mechanical causes directly into the build log, so the next build tells us which one it is.

### Added
- **AAS controller diagnostic (`ControllerDiagnostics`).** After generating/attaching the AAS
  controller, CVRFury now logs:
  - **Pose suspects** — layers that animate humanoid muscles / Transforms at their *default* state.
    At weight > 0 these override CVR locomotion and produce the motorcycle pose.
  - **Dead toggles** — AAS `machineName` parameters that are not *read* by any transition condition
    or blend-tree parameter in the generated controller (and any that aren't even declared). Nothing
    responds when CVR drives these, which is the "toggle does nothing" symptom.
  Output is bounded so it stays readable when pasted back from the Unity console.

## [0.9.3] - 2026-06-14

**The actual root cause: the CCK inspector was silently wiping every entry CVRFury added.**
The CCK source (`CCK_CVRAvatarEditorAdvSettings.InitializeSettingsListIfNeeded`) shows that when the
CVRAvatar inspector first draws, it checks `avatarSettings.initialized`. If that flag is false it calls
`CreateAvatarSettings`, which **replaces `avatarSettings` with a brand-new empty container** (empty
settings list, Base Controller reset to the default `AvatarAnimator`). CVRFury never set that flag, so
the instant the avatar was selected, all 127 converted entries were destroyed — which is precisely why
the list showed empty, the Base Controller read "AvatarAnimator", and toggles did nothing. The log's
readback saw 127 entries only because it ran *during* conversion, before the inspector redrew.

### Fixed
- **All converted AAS entries were wiped the moment the avatar was selected.** CVRFury now sets
  `avatarSettings.initialized = true` when it creates/populates the container (and again after
  generation), so the CCK inspector keeps the entries instead of replacing the container.
- **AAS generation now mirrors the CCK's own `CreateAASController`.** Entries whose machine name is
  already a parameter in the base (merged) controller are left to that controller's existing layer
  instead of being regenerated — matching CCK exactly and avoiding the `AddParameter` "already exists"
  throw on local (`#`) entries and redundant conflicting layers. Only genuinely-new parameters get a
  freshly generated layer. The log now reports reused vs. built vs. failed.

## [0.9.2] - 2026-06-14

**The generated controller is now actually the one the avatar runs — toggles drive in-game.**
0.9.0/0.9.1 generated a working AAS controller, but the orchestrator then *overwrote* the avatar's
override controller with the raw merged FX controller right afterwards. So the generated controller
was orphaned (every toggle dead) and the merged controller — with its incompatible VRChat gesture
blend trees — was the thing uploaded and validated, producing the `GestureLeft/GestureRight ... is
not float type` errors. Clicking **Create Controller + Attach** by hand worked precisely because it
re-did the wiring CVRFury was clobbering. This release makes CVRFury do that wiring itself and keep it.

### Fixed
- **Toggles did nothing in-game / generated controller was orphaned.** `AasControllerGenerator` now
  generates onto a copy of the **merged** CVR controller (the same controller the Base Controller
  points at — locomotion plus every merged FX toggle layer) instead of a bare locomotion base, and
  **attaches the generated controller to the avatar's `overrides`** (the "Attach Created Override to
  Avatar" step). The orchestrator no longer overwrites that wiring with the raw merged controller; it
  only falls back to wiring the merged controller when AAS generation didn't run.
- **`GestureLeft`/`GestureRight` "is not float type" blend-tree error (regression from 0.9.1).** The
  0.9.1 harmoniser retyped every `Equals`/`NotEqual`-gated parameter to **Int**, but the gesture
  parameters are *also* used as **Float blend-tree parameters** (hand-pose blends), which Unity then
  rejected. The harmoniser now leaves any parameter used as a blend-tree input as **Float** and only
  retypes Equals/NotEqual params that are never used in a blend tree.
- **AAS list showed "List is Empty" and the CCK inspector threw `ArgumentOutOfRangeException` at
  `AAS_SettingsList.cs:143`.** CVRFury appends entries by reflection (out of band of the inspector's
  cached `SerializedObject`/ReorderableList), leaving the open inspector stale. The conversion now
  explicitly persists the component (`SetDirty` + prefab-modification record + mark-scene-dirty) and
  deselects/reselects the avatar so the inspector rebuilds against the populated list.

## [0.9.1] - 2026-06-14

### Fixed
- **`GestureLeft`/`GestureRight` "parameter not compatible with condition type" error spam.**
  VRChat declares the gesture parameters as **Int** and gates on `Equals`/`NotEqual`; ChilloutVR's
  base declares them **Float**, so after the merge those conditions were invalid and the console
  filled with errors (and the gesture layers broke). A new pass harmonises any parameter used with
  an `Equals`/`NotEqual` condition to **Int**, on both the merged controller and the generated AAS
  controller, so they validate cleanly.

## [0.9.0] - 2026-06-14

**Toggles are now generated and attached automatically — the real fix.** The CCK
source revealed that ChilloutVR doesn't run the Base Controller directly: it
generates a fresh AnimatorController by calling each AAS entry's `SetupAnimator()`
on top of the base, then attaches it (the "Create Controller" + "Attach Created
Override to Avatar" buttons). CVRFury never triggered this, so the avatar had no
working controller and every toggle was dead.

Worse, `SetupAnimator()` calls `AddParameter()` for each entry, and we had set the
Base Controller to the **merged VRChat FX controller, which already contained those
parameters** — so even clicking the buttons manually threw "parameter already
exists" and generation aborted. That's why nothing worked.

### Added
- **Automatic AAS controller generation** (`AasControllerGenerator`). After the menu
  entries are built it: seeds a fresh controller from CVR's **clean locomotion**
  animator, calls each entry's `SetupAnimator()` via reflection (using the clips
  attached in 0.8.0) to build that toggle/slider's layer + parameter + states, then
  attaches the generated controller (and an override) to the avatar — exactly what
  the inspector buttons do, but hands-free. Each entry is wrapped in try/catch so one
  bad entry can't abort the whole generation, and the log reports built vs. skipped.

### Fixed
- Generation no longer aborts on "parameter already exists": the generated controller
  is built on a **clean** base, not the parameter-laden merged FX controller, so
  `AddParameter` succeeds for every entry. The merged FX controller is now used only
  as a source for toggle clips, not as the avatar's runtime controller.

## [0.8.1] - 2026-06-14

### Fixed
- **Locomotion / default-pose warning.** The default-avatar-animator search only matched the
  `ABI.CCK` install folder; newer kits ship under `CVR.CCK`, so it never found
  `…/CCK/Animations/AvatarAnimator.controller` and the merged controller had no locomotion. Now
  matches any `.CCK` install (with a name-based fallback), so the avatar keeps CVR's locomotion
  instead of holding a default pose.

## [0.8.0] - 2026-06-14

**Toggles now actually toggle.** Converted toggles appeared in the menu but did
nothing in-game — a bug present since the first conversion, unrelated to the
synced-bit work. Root cause: ChilloutVR builds the working toggle/slider animator
layer **at upload time from the AAS entry's own animation clips / GameObject
targets**, not from the merged base controller's parameter-driven layers. CVRFury
created the AAS entries with neither, so CVR generated empty layers.

### Fixed
- **Each converted toggle now carries its animation clip on the AAS entry**
  (`useAnimationClip` + `animationClip` + `offAnimationClip`), and each radial
  carries its min/max clips, so ChilloutVR's AAS generator builds real, working
  layers. CVRFury locates the clip in the merged FX controller per parameter:
  - a per-toggle 1D blend tree → its real off/on clips;
  - a Direct Blend Tree weight → the child clip as "on", with a synthesised zeroed
    "off" (correct for blendshape / material-float / object toggles);
  - a simple two-state transition → the destination/source clips.
- The conversion log now reports how many toggles got a clip attached (will work)
  vs. how many are driven indirectly (e.g. AAP parameter chains) and may need a
  manual clip on the CVRAvatar toggle.

### Notes
- Toggles whose visual comes through an indirect AAP parameter chain can't be
  captured as a single clip automatically; they're counted in the log so you know
  which (if any) to finish by hand.

## [0.7.3] - 2026-06-14

0.7.2's diagnostic pinpointed it: GodWhisper's toggles live in a **Direct Blend
Tree**, and 52 were rejected only because their clips drive material floats / AAP
(Animated Animator Parameter) curves rather than blendshapes — the sample binding
`(AAP-f)Bodysuit` gave it away. They're plain float curves, and in a direct blend
tree a weight of 0 means a child contributes 0, so the correct "off" value for any
float binding is exactly 0.

### Changed
- **Direct-blend toggle compression now handles material-float and AAP toggles.** The
  "safe clip" check no longer restricts to blendshape/active bindings; it accepts any
  float curve (zeroing it for the Off pose, which matches a blend weight of 0) and
  only rejects genuine ambiguities: **object-reference (material swap)** curves and
  **Transform** (scale/position/rotation) curves.
- **Exclusivity guard.** A direct-blend child is only lifted into an override layer if
  its bindings aren't also animated by a sibling child in the same tree — otherwise the
  additive sum would be lost. Shared-binding toggles are reported and left as floats.

### Notes
- AAP-driven toggles keep working only if ChilloutVR plays parameter-driving clips in
  ordinary states; this gets the avatar under the synced-bit cap regardless. Verify
  toggles in-world and report any that don't visually change.

## [0.7.2] - 2026-06-14

0.7.1 compressed nothing on GodWhisper (`compressed 0`) because its float toggles
aren't Direct-Blend-Tree-with-blendshape-clips — they're a different shape. This
release broadens the compressor and makes the diagnostic pinpoint the pattern.

### Added
- **Per-toggle 1D blend-tree compression.** The common "one toggle = one 1D blend
  tree (param, two clips at 0 and 1)" layout is now compressed: each becomes a
  Bool-driven On/Off layer using the tree's **real** off and on clips, and the
  original float-driven blend-tree state is neutralised. Because the off clip
  already exists, this works for **any** property the toggle animates — materials,
  shader floats, scale, blendshapes — not just blendshape/active toggles.
- **Detailed compressor diagnostic.** The log now reports the compressible toggles
  it found by shape (condition / 1D-blend / direct-blend), and for anything left as
  a float, *why* (radial/complex blend, 2D blend, material/scale direct clip, motion
  param) with a sample binding name — so the remaining synced-bit cost is fully
  attributable.

### Notes
- Direct-blend-tree weights whose clips drive materials/scale still can't be
  auto-compressed (no real off pose to fall back on) and are reported, not touched.

## [0.7.1] - 2026-06-14

The diagnostic from 0.7.0 nailed it: on GodWhisper all 104 synced floats were
**Direct Blend Tree weights** (a modern avatar puts every toggle into one big DBT
as a 0/1 float weight), and the real CVR float cost is ~32 bits, not 64
(104 × 32 + 14 bools ≈ the observed 3344). None were transition conditions, so the
0.7.0 retyper found nothing to do.

### Added
- **Blend-tree toggle compression.** The sync-bit optimiser now lifts each binary
  **menu toggle** that is implemented as a Direct Blend Tree weight out of the tree
  into its own Bool-driven On/Off layer, then retypes the parameter to Bool — taking
  it from ~32 synced bits to ~1. A generated Off clip zeroes the toggle's blendshape
  / object-active bindings (matching a blend weight of 0). Toggles whose clip drives
  scale / position / a material value are detected and **left as floats** (their Off
  pose is ambiguous), so nothing silently breaks.
- Real radial puppets (1D/2D blend parameters) and state speed/time params are still
  left intact.

### Fixed
- Corrected the synced-bit estimate to CVR's actual ~32 bits per float (was 64), so
  the log's estimate now tracks the CCK's counter.

### Result
- A typical toggle-heavy avatar drops from "100+ synced floats" to a handful of real
  radials plus cheap bools — clearing the 3200-bit cap with large headroom.

## [0.7.0] - 2026-06-14

Automatic synced-bit compression — the CVR-native equivalent of VRCFury's
parameter compressor. After 0.6.1 GodWhisper sat at 3344/3200; the remaining
cost was synced **Float** parameters, because many VRChat "toggles" are backed by
a float (for smooth blends) and ChilloutVR charges synced bits by the *animator
parameter's type* (a float ≈ 64 bits, a bool ≈ 1).

### Added
- **Sync-bit optimiser** (runs automatically at the end of a conversion). It
  analyses the merged animator controller and **retypes float parameters that are
  only ever used as on/off transition conditions (thresholds within 0..1) down to
  Bool**, rewriting their conditions (`Greater`→`If`, `Less`→`IfNot`). That turns a
  64-bit synced float into a ~1-bit synced bool with no behavioural change — a
  native, zero-latency form of parameter compression suited to CVR's cost model
  (unlike VRChat, where VRCFury time-multiplexes a flat-cost budget).
- **Synced-parameter breakdown in the log**: reports how many synced parameters are
  Bool / Int / Float, splits the floats into *blend-tree/continuous*,
  *on/off-convertible*, and *other*, and estimates the synced bits before and after
  optimisation — so it's obvious whether any remaining overage is genuine radials
  (which need a structural compressor or to be made local) rather than toggles.

### Notes
- Genuinely-continuous floats (radial puppets, blend-tree drivers) are left intact;
  collapsing those requires more than a retype and would change behaviour.

## [0.6.1] - 2026-06-14

Finishes the synced-bit job. 0.6.0 took GodWhisper from 6848 → 3416, still just
over the 3200 cap. The remaining ~2800 bits were VRChat parameters flagged
network-synced but **not exposed by any menu control** — driven in VRChat by
contacts/OSC/parameter-drivers that don't convert, so in CVR they were syncing
(64 bits per float) for nothing.

### Changed
- **A parameter stays synced only if it is VRChat-synced *and* reachable from a
  menu control.** CVRFury now pre-walks the expression menu before merging and
  localises (`#`) every other parameter — including synced-but-unused ones. This
  removes the dead-weight synced floats that pushed GodWhisper over the limit,
  with no behavioural loss (CVR had nothing driving those parameters anyway).
- Merge log now reports how many menu parameters were kept sync-eligible.

## [0.6.0] - 2026-06-14

**The actual fix for "over the Synced Bit Limit."** The 0.5.3 readback proved
the AAS was being written correctly (toggles as Bool) yet the CCK still reported
`6848/3200`. That number is `107 × 64` — ChilloutVR was syncing ~107 **Float**
parameters in the *base animator controller*, not the AAS entries.

### Root cause
ChilloutVR network-syncs **every animator-controller parameter by default**, and
only treats a parameter as local if its name starts with **`#`** (e.g. the
documented `#MotionScale`). The converter was merging VRChat's entire FX
controller, which is full of local smoothing/driver/remap floats that VRChat
never synced — and CVR dutifully synced all of them. The AAS `usedType` controls
the menu encoding, but the synced-bit cost comes from the controller parameters.

### Fixed
- **Non-synced parameters are now localised with `#` during the merge.** CVRFury
  reads VRChat's `VRCExpressionParameters` to learn which parameters are actually
  network-synced; everything else (non-synced expression params **and** all
  FX-internal locals) is renamed to `#name` in the merged controller, with every
  reference (transition conditions, blend trees, state speed/time/mirror params)
  remapped automatically. CVR core/locomotion params (`GestureLeft`, `Grounded`,
  …) and genuinely-synced params keep their names. Result: only the handful of
  real synced parameters count toward the 3200-bit budget.
- **AAS menu entries drive the final (possibly `#`-localised) parameter name**, so
  toggles keep working after localisation — local toggles work for you, cost zero
  synced bits, just aren't visible to others.
- **"Make all parameters local" now genuinely works** — it forces the `#` prefix
  on everything, guaranteeing a 0-bit avatar that always builds.

### Changed
- The sync map is now read **before** merging (so the merge can localise), and the
  diagnostic readback reports synced-vs-`#`-local counts and notes that CVR's
  counter sums the controller's non-`#` parameters.

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
