# Changelog

All notable changes to CVRFury are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.21.0] - 2026-07-13 — Nipple bump generator

### Added
- **🍒 Nipple bump (poke through clothing)** (Avatar features). Adds a bump to a clothing mesh so the nipple
  reads through it — procedurally, since Unity can't sculpt: place a marker (pink gizmo sphere) on the shirt
  where each nipple is, and every vertex within the radius is pushed OUTWARD along its normal with a smooth
  cosine falloff (full at the marker, zero at the edge), baked as a "CVRFuryNipplePoke" blendshape on a NEW
  mesh asset (the original is untouched). Markers map to vertices via the current skinned pose (BakeMesh), so
  where you place them on the shirt is where the bump appears; size and poke-amount are sliders; it previews
  at full strength instantly and regenerates non-destructively from the original each time (existing
  blendshapes preserved). Drive the baked blendshape with a toggle or slider for an in-game on/off. Pairs
  with Tools ▸ CVRFury ▸ Placement to hide the body while lining up the markers.

## [0.20.3] - 2026-07-13

### Added
- **Placement view** (Tools ▸ CVRFury ▸ Placement) — for positioning things that sit on the body but are
  hidden by opaque clothing (nipple bumps, piercings, SPS sockets, touch zones): "Hide Selected" makes the
  selected clothing renderers invisible in the editor so you can see the body underneath and place precisely,
  and "Show All Again" restores exactly what it hid. Toggles renderer visibility only (not activeSelf), so
  nothing about the avatar's toggles or the shipped result is affected — it's purely an authoring aid.

## [0.20.2] - 2026-07-13 — Poiyomi unlock: material toggles that actually drive

### Added
- **🔓 Unlock Poiyomi** (Menu Wizard section). On avatars whose clothing is combined into shared meshes and
  hidden by Poiyomi DISSOLVE / hue / emission or blendshapes (instead of separate GameObjects), the toggles
  animate material properties — and a LOCKED Poiyomi shader ignores animation on any property not tagged
  "Animated," so the converted toggle silently does nothing in CVR even though the property works when set by
  hand. VRChat hides this because VRCFury/Poiyomi mark animated properties before locking; a converted CVR
  avatar has no such pass. One click unlocks every locked Poiyomi material on the avatar (via Thry's optimizer
  by reflection) so all their properties become animatable and material-driven toggles work. Reports what it
  unlocked and flags any it couldn't (unlock those manually via the material's "Unlock Shader" button).

## [0.20.1] - 2026-07-13 — The real root cause: every toggle ships its own built layer

A source-read of the real CCK (via the verification workflow) corrected the 0.20.0 approach BEFORE it
reached anyone. `gameObjectTargets` is `#if UNITY_EDITOR`-only — there is NO runtime native toggle; the CCK
BAKES targets into an `m_IsActive` clip inside a generated animator layer at build time. And the saga's true
root cause: the CCK skips regenerating any entry whose parameter already exists in the controller, so an
entry that has the parameter but no built LAYER (gameObjectTargets-only, or the 0.20.0 native path) never
gets a layer and the object is dead — while material/blendshape toggles worked precisely because CVRFury
built real layers for them.

### Fixed
- **Every menu toggle now ships as a real clip layer that CVRFury builds and attaches itself** — object
  toggles included. Object toggles get an ON clip from the FX graph plus a guaranteed-correct OFF clip
  (`m_IsActive` inverted), so `BuildAndAttach` builds a proper masked layer for each; because CVRFury's
  controller (base + animator) already declares every parameter, the CCK preserves those layers instead of
  regenerating over them. This is the exact path that always made skin/piercings work — now every clothing
  toggle takes it too. Reverted the 0.20.0 native-GameObject-toggle routing.

## [0.20.0] - 2026-07-13 — Clothing toggles go native (no clips, no animator, no breakage)

The verifier report on a real avatar proved the ON clips resolved and only the synthesized OFF clips were
broken. Conclusion: clothing toggles should never have used clips at all.

### Changed
- **Object toggles now convert to CVR-NATIVE GameObject toggles.** Any menu toggle whose clip only switches
  GameObjects on/off (i.e. all clothing/accessories) becomes a native CVR GameObject toggle: the game engine
  drives the objects directly from the setting — **no animation clip, no animator layer, no synthesized off,
  nothing that can be broken by a clip/parameter/regeneration bug.** Targets (with correct per-object
  on-state, including objects the toggle HIDES) are read from the ON clip's `m_IsActive` curves, which the
  verifier confirmed resolve. Material/blendshape/transform toggles keep the clip path (both states explicit
  via the fixed synthesizer); shared-parameter outfits stay Int dropdowns.
- `AddGameObjectToggle` carries per-target on-state; the Verifier recognizes native toggles as valid without
  a layer (checking only that their target objects resolve) instead of false-flagging them.

## [0.19.2] - 2026-07-13 — Verifier-driven fixes: synthesized OFF clips + material reading

The Menu Verifier immediately earned its keep: run on a heavily-VRCFury commercial avatar it mapped every
failure precisely, revealing two real bugs behind the mass of dead clothing toggles.

### Fixed
- **Synthesized OFF clips were coming out EMPTY.** Off-clip synthesis used `AnimationUtility.GetFloatValue`,
  which cannot read `material.*` shader properties (GPU uniforms, not serialized fields) and silently skipped
  those bindings — so a synthesized OFF that couldn't read the property produced an empty clip, and with
  WriteDefaults off that toggle is dead. Synthesis now NEVER skips a binding: actives/enabled invert,
  readable properties take their live scene value via the new `SceneBindingReader` (which reads material
  floats/color-channels, blendshapes, enabled flags, active states, and material-slot object refs that
  Unity's API can't), and the last-resort fallback flips the ON value. Every property the ON clip writes now
  has a matching OFF.
- **The verifier's own "changes NOTHING" false alarms.** It read scene state with the same limited API, so
  material-driven toggles (wetness/glitter) were wrongly flagged. It now uses `SceneBindingReader` too, and
  treats an unreadable property as possibly-visible rather than dead. `SceneBindingReader` is shared by both.

## [0.19.1] - 2026-07-13 — 🔬 The Menu Verifier: no more upload-and-pray

### Added
- **Menu Verifier** — verifies EVERY menu entry in the editor, deterministically, before any upload. It
  checks the complete causal chain per entry: parameter exists in the attached controller → a layer actually
  LISTENS to it → the states have clips → every clip binding path RESOLVES to a real object on THIS avatar →
  the clip produces a VISIBLE change vs the current scene state. Any break = a dead toggle in-game, and the
  report names the exact broken link ("NONE of the clip's 14 paths exist on this avatar (e.g.
  'Armature/Hips/Skirt') — wrong hierarchy"). Runs automatically after every wizard Apply, plus a
  standalone "🔬 Verify menu" button. A menu that verifies green here works in ChilloutVR — and when
  something is red, the fix is named instead of guessed.

## [0.19.0] - 2026-07-13 — Bulletproof menu conversion

The Menu Wizard rebuilt around one principle: CVRFury builds every animator layer itself, both states are
always explicit, and nothing depends on CCK generation or native handling. Root causes found by adversarial
code verification of a real avatar's failure (clothing dead, materials fine):

### Fixed
- **One-Int outfit systems** (many menu controls sharing one parameter with different values — how most
  commercial avatars wire clothing): previously each control became a Bool entry with the SAME machine name,
  so they collapsed to a single mistyped entry and the whole clothing menu died. Now they fold into **one
  real Int dropdown**: per-option clips extracted via the Equals conditions for each value, a synthesized
  "Off" option when none covers zero, and a proper multi-state Int layer (one state per option, Equals
  transitions, humanoid-masked) built by the new `AnimatorUtil.AddIntDropdownLayer`.
- **The native-toggle path is gone from Apply.** m_IsActive-only clips (= all clothing) used to become
  "native" entries with no clips and no layer — and the parameter pre-created for them even suppressed the
  CCK's own generation. Double-dead. Every toggle now takes the proven clip path.
- **Missing OFF clips are synthesized, always.** With WriteDefaults off, an empty Off state writes nothing —
  one-way or dead toggles (all direct-blend-tree extractions, and any transition graph without an explicit
  off state). Synthesis rule: object actives get the INVERSE of the ON value, every other property is
  captured from the current scene state, material swaps restore the current material. Saved as real assets
  under `CVRFury Generated/Wizard`.

## [0.18.2] - 2026-07-13

### Fixed
- **Menu Wizard now builds and attaches the animator — the red ❗ actually clears.** The red mark on AAS
  entries means "this parameter doesn't exist in the attached animator yet"; on main that was always cleared
  by Step 2's controller build, which the wizard flow skipped — so every wizard/linker entry stayed red and
  nothing would have animated. Apply now finishes with the exact Step 2 machinery (`BuildAndAttach`): a
  locomotion-carrying controller (stock CVR base), a parameter for every entry, humanoid-masked toggle
  layers from the on/off clips, and 1D blend-tree layers for sliders — attached and stamped into the AAS.
  Since it walks the entire settings list, it also repairs entries created earlier by Link Parameters.

## [0.18.1] - 2026-07-13

### Fixed
- **Red-❗ menu entries from VRCFury-style parameter names.** VRCFury names parameters with menu paths and
  sync markers ("Nsfw/Toy/(s-f)Scale") — slashes and parentheses the CCK rejects as invalid machine names,
  flagging every such entry red. All AAS entry creation now sanitizes machine names at the single choke
  point (letters/digits/underscore, leading '#' preserved), the Full Controller bake renames merged animator
  parameters through the SAME sanitizer so menu entries and animator layers stay wired to each other, and
  the replace-by-name dedupe compares sanitized names — so re-running the wizard/converter automatically
  replaces the old red entries instead of stacking beside them.

## [0.18.0] - 2026-07-12 — Rework: find-anything search + the Menu Wizard

### Added
- **🔍 Find-anything search.** A search box at the top of the window: type a word ("slider", "hue",
  "clothing", "wizard"…) and only matching sections render — pulled from EVERY category, forced open. No
  more digging through tabs to find the one thing you want. Quick-nav chips (Convert / Features / Emotes /
  Collapse all) when the search is empty.
- **🪄 Menu Wizard — the replacement for clip-name scanning.** The old Step 2 guesses which animation belongs
  to which toggle from FILE NAMES in a folder — heuristic, tedious, sometimes flat wrong. The wizard reads
  the FX ANIMATOR GRAPH instead: VRChat stores exactly which parameter plays exactly which clip (toggle
  states behind If/IfNot/Equals/Greater transitions, radial 1D blend trees, VRCFury direct-blend-tree
  children), so extraction is deterministic — including the Bool- and Int-conditioned toggles the old finder
  missed entirely. Every preview row shows its PROVENANCE (which layer, which states, which tree), clips are
  editable before applying, and toggles whose clip only flips GameObject actives become **CVR-native object
  toggles with no clips at all** — nothing to regenerate, nothing to mismatch. Parameters keep VRChat's
  synced/local and default values. The folder scanner stays as a fallback for avatars with no FX controller.

## [0.17.0] - 2026-07-12 — The Prefab Converter

### Added
- **🧰 Prefab Converter (VRCFury → CVRFury).** Import a VRCFury-based .unitypackage (toys, clothing, avatar
  additions), put the prefab on your avatar as its instructions say, and convert: every VRCFury feature is
  read BY REFLECTION (no compile dependency; field names verified against VRCFury's published source,
  including the modern one-feature-per-component `content` storage, the legacy `config.features` list, and
  GuidWrapper asset references with guid:fileID fallback) and recreated as CVRFury components:
  - **Toggles** → CVRFury Toggle (object on/off with TurnOn/TurnOff/Toggle semantics, blendshapes incl.
    all-renderers expansion, material swaps with slot + resolved material, scale, shader float/color
    properties, hold-to-press → momentary, transitions → transition seconds, submenu paths, global params);
    slider-style toggles → CVRFury Slider.
  - **Full Controllers** (the standard prefab wiring) → CVRFury Full Controller: animator controllers merged
    at bake, every VRC expression parameter exposed as a CVR menu control with a best-fit type
    (Bool→Toggle, Float→Slider, Int→Dropdown), and `toggleParam` props get a real menu toggle.
  - **Armature Links** → CVRFury Armature Link (Reparent), resolving humanoid-bone / object / path link
    targets, bone suffix and offsets settings carried.
  - Delete-during-upload markers → CVRFury Object State (delete). SPS/haptic components are pointed at the
    SPS step. VRChat-only features (security pins, MMD, HeadChop, parameter compression…) and build-hygiene
    features are each listed with a reason — nothing is dropped silently.
  - **Scan** gives the full read-only plan first; converting can remove the VRCFury components afterwards
    (default on). Step 5 Strip now warns instead of losing data when unconverted VRCFury features remain.

## [0.16.7] - 2026-07-12

### Added
- **Dead-swap lint.** The two silent ways a material swap "does nothing" in-game are now caught at
  pre-flight and in the bake log, with plain-English remedies:
  - a slot index that doesn't exist on the target renderer (slot = the 0-based position in THAT renderer's
    Materials list, not a unique ID across swaps — a one-material hair mesh is slot 0), which animates
    nothing at all;
  - several independent toggles/sliders animating the SAME renderer+slot — each control is its own animator
    layer writing constantly, the topmost always wins, and the rest appear dead. The lint names the fighting
    controls and points at the fix: exclusive variants belong in ONE Modes dropdown.

## [0.16.6] - 2026-07-12

### Fixed
- **Material swaps no longer fail the CCK's content validation.** Root cause (matched against CVR's own
  validation docs): a swap-target material was never ON a renderer, so its import settings were never
  checked — but the swap clip pulls it into the build, where two ERROR-level validators reject the upload:
  "Requires Streaming Mipmaps" (every used texture must have them) and "Missing or Broken Shaders". The bake
  now runs a SwapMaterialGuard over every MaterialSwap target (toggles, slider states, modes): it
  auto-enables streaming mipmaps on their textures (mirroring the CCK's own autofix) and reports broken/
  missing shaders by material name. Pre-flight gains a "Swap materials" line so the problem is visible
  before an upload is spent. Clip generation also skips null and scene-instance materials (they can't ship
  in a bundle) instead of writing keyframes that poison the build.

## [0.16.5] - 2026-07-12

### Fixed
- **Touch reactions no longer trip the CCK's content validation.** The trigger references its AAS setting by
  NAME, but the non-destructive model created that setting only at bake-time — so in-scene the reference was
  dangling (the red ❗ in the trigger inspector), and the CCK's upload validator can reject the asset for it
  ("Build asset failed content validation"). Creating a reaction now pre-registers the AAS entry immediately
  and persists it, so the reference is valid from the moment it exists. AAS entry creation is now
  replace-by-machine-name (no duplicate entries when the bake recreates it — duplicates are another validator
  trip-wire), and re-adding a reaction for the same zone reuses its toggle instead of stacking a second one.

## [0.16.4] - 2026-07-12

### Changed
- **Bypass now unhooks CVRFury from the CCK build events entirely** (after a recompile), instead of
  attaching a listener that returns early. This cleanly separates two failure modes of the CCK's legacy
  pre-build bridge: a listener whose body crashes vs. a bridge that crashes when ANY runtime listener is
  attached — the latter fails even for a listener that does nothing.

## [0.16.3] - 2026-07-11

### Fixed
- **Uploads can no longer be aborted by unguarded third-party hooks (the "Index was outside the bounds of
  the array" PreBuildEvent failure).** Root cause, verified in source: Poiyomi/Thry's `AbiAutoLock`
  subscribes to the CCK's pre-build event with NO try/catch and runs the full shader-lock parser over every
  material and animation clip on the avatar — content-dependent, which is why the failure appeared whenever
  clip-generating features (gesture blendshapes, touch reactions) were added, and vanished when they were
  removed. One uncaught throw from any listener kills the whole upload. CVRFury now SANDBOXES every foreign
  listener on those events: the hook runs normally, but if it throws, the exception is logged (naming the
  hook) and the upload continues. Thry's own AutoAnchor and CVRFury were already self-guarded; nothing
  changes for well-behaved hooks.

## [0.16.2] - 2026-07-11

### Added
- **Upload diagnostics.** The bake now brackets itself in the Console ("Bake starting…" / "Bake finished —
  handing back to the CCK"), so when the CCK's pre-build event fails, the position of the error relative to
  those lines proves whether the thrower is CVRFury's bake or ANOTHER subscriber on the same event (shader
  tools like Poiyomi/Thry auto-lock hook the identical CCK event). New **Tools ▸ CVRFury ▸ Bypass CVRFury At
  Upload (diagnostic)** kill-switch skips the bake entirely for one upload — if the upload still fails with
  bypass on, the failure is definitively not CVRFury's.

## [0.16.1] - 2026-07-11

### Fixed
- **Touch-permission wiring adapts to the installed CCK.** The trigger's core fields (setting name/value,
  area size) wire fine, but the "others can trigger" fields are named differently across CCK versions and
  produced console warnings. They're now found by name shape (any bool field matching allow-other /
  others-to-trigger / network patterns, quietly skipped when absent), and when nothing matches, ONE console
  line prints the trigger component's actual field layout — paste it and the exact names get pinned. The
  trigger itself was always created and functional; only the permission checkbox could fall back to the
  CCK's default.

## [0.16.0] - 2026-07-11 — Touch reactions: multiple blendshapes + custom zones

### Added
- **Multiple blendshapes per touch.** One touch now fires any number of blendshapes together, each with its
  own strength (add/remove rows in the window). Works in both styles: Instant applies them all at once;
  Build-up ramps every shape to its own target value over the build time in one generated clip.
- **Custom touch zones — put the trigger exactly where you mean.** The zone menu gains "Custom (place a
  box)": click Place and a `CVRFury Touch Zone` box appears (parented to the head so it follows), drawn as a
  **magenta gizmo in the Scene view** — move it with the normal transform tools and set its Size on the
  component; what the gizmo covers is *exactly* what will trigger. Name it ("Nose") and create: the previewed
  box itself becomes the CCK trigger (same object, same size — zero drift between preview and reality), and
  the authoring component is consumed. Solves the "touching anywhere on my head fires my nose boop" problem:
  a nose-sized box on the nose triggers only on the nose.
- A zone that's placed but never turned into a reaction is stripped at upload with a friendly note (it's
  editor-only intent, like every CVRFury component).

## [0.15.0] - 2026-07-11 — Clothing setup & Reactions

### Added
- **🧥 Clothing setup (manual).** Drag your clothing items into a list and configure each one in place, then
  create everything in one click. Per item: a menu toggle (label/default editable), a **Blendshape Link** so
  the clothing mesh FOLLOWS the body's shape keys — bust/hips/weight sliders deform the outfit instead of the
  body clipping through it — and **clipping-fix body shapes** (pick from the body's blendshape list + value)
  that apply only while the item is worn, wired into the item's own toggle state.
- **🫦 Touch reactions.** Touch a body part (head/chest/hips/thighs/neck — a CCK trigger on the actual bone)
  → a face blendshape reaction. Two styles: **Instant**, or **Build-up** — a generated animator ramp layer
  grows the blendshape from 0→100 over N seconds of continuous touch and eases back on release (authored as
  a real controller asset, merged at upload through the Full Controller pipeline). Optional extra outputs:
  a **sound** played positionally at the touched spot and a **heart-particle burst** while touched.
  "Others can trigger it" is a checkbox — off means only your own hands fire it; degrades to a menu button
  when the CCK trigger type is missing.
- **🫁 Breathing.** A generated always-on looping layer cycles a chest/breath blendshape forever (cycle
  length + intensity configurable) — subtle idle life that no toggle or slider can produce.

## [0.12.1] - 2026-07-11

### Added
- **👁 Reveal invisible clothing** (Avatar features). Fixes items whose GameObject is ON but the mesh doesn't
  render (selection outline shows, surface doesn't): creators often toggle clothing via an animated MATERIAL
  property — a Poiyomi dissolve/alpha float in the FX animator — instead of the GameObject, so with no
  animator running in the editor the material sits at its baked "hidden" default. The ground truth lives in
  the animation clips: **Diagnose** scans every animator reachable on the avatar (live Animator, VRChat
  playable layers, CVR AAS animator) for `material.*` curves on that renderer's path and lists each animated
  property with its current value vs the clip values; **Make visible** bakes the *shown* state into the
  material — since the mesh is hidden right now, the clip value farthest from the current one is the visible
  one, no per-shader knowledge needed. Handles both float properties and color channels, warns about the
  mundane causes first (disabled renderer, inactive parents, zero scale), and auto-unlocks locked Poiyomi
  materials via Thry's optimizer when present (locked shaders silently ignore edits otherwise — the reason
  "messing with the alpha" does nothing). The report also names the property + both values so the item's
  CVRFury toggle can get a matching Material Property action and keep toggling in CVR.

## [0.12.0] - 2026-07-10 — World Converter Layer 3: Udon toggles rebuild themselves

### Added
- **Udon public-variable extraction.** An Udon behaviour's *program* can't run outside VRChat, but its
  public variables — which objects a button toggles, where a teleporter points, which Light a switch drives —
  are plain serialized data. CVRFury now reads them by reflection (tolerant across SDK versions: every
  variable-table API shape is tried, none assumed) and surfaces the scene references. The Scan's migration
  plan now prints `targets: …` under every toggle-style behaviour, so the plan reads "this button toggles
  THAT" instead of "a toggle exists".
- **Toggle-style Udon buttons rebuild as ready-made CVRInteractables.** At Convert, every behaviour classified
  as an object/mirror/light/audio toggle gets a `CVRInteractable` on its object with an on-use
  Set-GameObject-Active operation wired to the SAME targets read from its variables. The action graph is
  built by SHAPE (lists found by element type, enums by fuzzy member match, targets by field type), so it
  tolerates CCK field-name drift; when a shape can't be found, the component is still placed and the report
  prints both the intended targets (a 30-second inspector job) and the actual CCK member layout — one pasted
  report is enough to tighten the wiring for that CCK version. Runs before the strip so the Udon data is read
  while it still exists, and it's a window toggle (default ON).

## [0.11.0] - 2026-07-10 — World Converter Layer 2

### Added
- **Udon migration plan.** The Scan no longer stops at "37 Udon behaviours found": every behaviour is
  classified by INTENT (video player, teleporter, object toggle, door, light control, audio, mirror toggle,
  pen, portal, ambience, custom logic) using program-name patterns for the prefabs that dominate real worlds
  (USharpVideo / ProTV / iwaSync / VideoTXL, ToggleObject variants, teleporters…) plus context checks on the
  object (does it actually carry a VideoPlayer / Light / AudioSource — agreement is marked, guesses flagged).
  The result is a three-bucket plan — auto-converts · one-component CVR recipe · needs rework — with the
  concrete CVR path printed per item, and the post-convert log turns the same data into your rebuild list.
- **More of the world converts itself.** New automatic conversions: VRCObjectSync → CVRObjectSync,
  VRChat SDK video players (Unity + AVPro) → CVRVideoPlayer, VRCPortalMarker → CVRPortalMarker (destinations
  need re-linking — world IDs differ), and recognised Udon-based video players get a CVRVideoPlayer placed on
  the same object so only the screen/audio assignment remains.
- **World Pre-flight.** The world-side counterpart of the avatar pre-flight: CVRWorld + spawn presence,
  a raycast under every spawn (players falling forever), spawns below the respawn height (instant respawn
  loop), leftover VRChat/Udon components, missing scripts, scene-wide shader errors, a lighting sanity check
  (no lightmaps AND no active light = dark world), and unsaved-scene detection — all before an upload is wasted.
- **One-click "Convert & Verify" for worlds.** Duplicates the scene asset, opens the COPY, converts it there,
  strips the VRChat/Udon layer, and pre-flights the result — the original scene is untouched by construction,
  same safety model as the avatar converter. The report can be copied or saved as a Markdown migration file
  next to the scene, and the window now matches the main CVRFury look.

### Changed
- Streamlined the main window to the core avatar workflow.

## [0.9.89] - 2026-06-25

### Added
- **World Converter (Beta)** — Tools ▸ CVRFury ▸ World Converter. The start of VRChat-world → ChilloutVR
  conversion, on the avatar converter's proven architecture (reflection-based, tolerant member lookup,
  convert-what-converts + report-the-rest). v1 converts the structural layer: scene descriptor → CVRWorld
  (spawns, reference camera, respawn height), mirrors → CVRMirror, pickups → CVRPickupObject, chairs →
  CVRSeat, plus an optional VRChat/Udon strip. Every Udon behaviour is inventoried by program name — the
  foundation the Udon → CVR interactables/scripting layer builds on next. The scan also explains known
  harmless Worlds-SDK console spam (EasyEventEditor "drawer type map").

## [0.9.87 – 0.9.88] - 2026-06-25 — "Big leagues" pass

### Fixed
- **Sliders snap-to-max / dead hue sliders (root cause).** The AAS slider entries carried no min/max clips,
  so whenever the CCK regenerated the animator the slider lost its blend and snapped (or did nothing) —
  "sometimes works" was regeneration luck. Entries now carry the clips, so sliders blend gradually after ANY
  regeneration. Also: the bake now stamps the generated-animator slot + live Animator and persists the AAS
  data, killing the stale-animator nondeterminism entirely.
- **Hue/emission sliders defaulted to 0.5–2** — out of range for 0–1 shader properties. Now 0–1. Material
  sliders also validate the property actually exists, with an exact remedy message for locked Poiyomi
  materials ("mark the property Animated, re-lock").
- **Gesture component aborting the upload.** Our controller's GestureLeft/GestureRight could collide with
  (or carry the wrong type into) the CCK's regeneration → the generic build abort. Parameter declarations are
  now deduplicated, type-corrected, and the bake runs the same condition-type repair as the converter.
- **Material-property toggles now restore the original value when toggled off** (e.g. Poiyomi glitter).

### Added
- **Blendshape Logic component** — plain-English conditional blendshapes: "WHEN coat is ON and bra is ON →
  set squish blendshape to 100; otherwise back to normal." Rules watch the same menu toggles that drive those
  objects, so the logic follows the toggles in game. Blendshape fields everywhere are now pick-from-the-mesh
  dropdowns (no more typing shapekey names).
- **Crouch/Prone style dropdown** — build an in-game dropdown of custom crouch/prone animations from packs
  like CCK BaseAnimatorPatch (clips or blend-tree assets), gated on the real pose so "Default" keeps CVR's
  stock locomotion. No SimpleAAS needed.
- **Outfit presets dropdown** — list what's ON per preset; picking one un-equips the rest (built on Modes).
- **Convert & Verify now converts a COPY** ("<name> (CVR)") and keeps the original untouched and disabled
  beside it — the tool can no longer damage your source avatar.

## [0.9.81 – 0.9.84] - 2026-06-19 — "Ultracode" pass

Big audit-driven improvement pass (bugs, automation, clarity, features).

### Added
- **One-click "Convert & Verify"** at the top of Convert — runs the full VRChat→CVR pipeline with recommended
  options (no playable-layer merge, so no GoGo Loco motorbike) and pre-flights the result.
- **Pre-flight one-click fixes** — failures now show contextual Fix buttons (reset locomotion, clean missing scripts).
- **Blendshape sliders** — new slider kind: pick a mesh, choose a blendshape from a dropdown, set min/max.
- **Empty-state guidance** and **avatar auto-root** (dropping a child snaps to the avatar root).
- Confirmation dialogs before the destructive Strip and Reset-locomotion actions; pre-flight failures show red.

### Fixed
- **Native toggles synced as the wrong type** — CVRFury toggles built a Float animator param while declaring
  the AAS entry Bool, wasting the synced-bit budget and mis-reporting it. Now Bool-driven end to end.
- **Synced-bit estimate** unified to one conservative source (Float costs 64) so over-budget avatars don't pass.
- **AAS layer generation** resolves the typed setting robustly (no reliance on a `setting` property that may
  not exist), so per-entry layers actually build.
- **CVR locomotion lookup** validates real locomotion + prefers the exact `AvatarAnimator` file (no wrong-controller motorbike).
- **Idempotency** — re-running SPS orifice toggles, PhysBone conversion, and emote/dance audio no longer stacks
  duplicate components/assets.
- Upload hook warns when the avatar event specifically isn't hooked; ControllerMerger null-log guard.

## [0.9.80] - 2026-06-19

### Changed
- **Content no longer stretches across wide monitors.** The window keeps its controls in a readable ~600px
  column (left-aligned) instead of spreading edge-to-edge, so fields, the clip-match review list, and toggle
  tags stay legible at any window width.

## [0.9.79] - 2026-06-19

### Changed
- **Themed info boxes.** The grey HelpBoxes are now tinted to match the window (brand-purple for info, with a
  coloured accent bar); warnings and errors keep their amber/red so they still stand out.

## [0.9.78] - 2026-06-19

### Changed
- **Window redesigned: grouped + branded.** The ~13 flat foldouts are now organised under three coloured
  category headers — **Convert** (the numbered VRChat→CVR steps + PhysBones/Magica/Strip), **Emotes, Dances &
  Poses** (emote toggles, emote wheel, Sync Dances), and **Avatar features** (sliders, SPS/DPS) — with
  Pre-flight at the top and Credits at the bottom. Added a brand banner, accent colours, and a darker backdrop
  instead of the flat gray.

## [0.9.77] - 2026-06-19

### Added
- **Pre-flight check.** New section at the top of the window: one button reports whether the avatar is
  upload-ready — CVRAvatar present, **locomotion is CVR's (flags GoGo Loco / VRChat locomotion)**, no missing
  scripts, all shaders compile, and synced bits under 3200 — each with a ✓/✗ so problems are caught before
  CVR's cryptic upload abort.

### Changed
- **Conversion won't build on VRChat locomotion.** When the merged base is GoGo Loco / VRChat locomotion (or
  has no CVR locomotion blend tree), AAS generation now falls back to CVR's stock AvatarAnimator, so converted
  avatars get working CVR locomotion instead of the motorbike pose.

## [0.9.76] - 2026-06-19

### Removed
- **VRCFury → CVR toggle conversion** (added in 0.9.75) — pulled for now; it was too janky to ship. May
  return later in a more robust form.

## [0.9.75] - 2026-06-19

### Added
- **VRCFury → CVR toggle conversion (experimental).** With VRCFury imported (so its components load instead
  of showing as missing scripts), a new section detects VRCFury features and converts the **Toggles** into
  CVRFury menu toggles (menu path + object on/off, default-on, saved) — read by reflection, version-tolerant.
  Note: VRCFury toggles aren't in the VRChat menu at rest (VRCFury injects them only at build), which is why
  the normal expression-menu conversion never caught them. Armature/Blendshape Link and Full Controller
  aren't converted yet.

## [0.9.74] - 2026-06-19

### Fixed
- **Sync Dances no longer motorbikes.** Dances were built as a separate full-body override layer at weight 1,
  which fights CVR's locomotion and forces the default pose even when "Off". They're now added as states
  **inside the base locomotion layer** (driven by the Dances int param, returning to Standard Locomotion at
  0) — exactly how CVR handles emotes — so the avatar stands when no dance is selected. Re-running cleans up
  the old override layer/states first, and the menu dropdown is refreshed in place.

## [0.9.73] - 2026-06-19

### Added
- **"Reset to CVR native locomotion" button (Step 2).** Force-replaces the avatar's controller with a copy of
  CVR's stock AvatarAnimator, so an avatar that shipped with VRChat locomotion (GoGo Loco) — baked into the
  controller during conversion's "Merge playable layers" — finally stands in CVR. It's a clean base without
  toggles; re-run "Link clips & build" (empty Controller) to rebuild them on it.

## [0.9.72] - 2026-06-19

### Added
- **Detects VRChat locomotion (GoGo Loco) as the motorbike cause.** When the base layer is a VRChat
  locomotion system, "Fix motorbike pose" now says so explicitly and tells you to remove it and rebuild on
  CVR's native locomotion (Step 2 with an empty Controller field). VRChat locomotion is driven by parameters
  CVR doesn't provide, so it can't work in CVR.

## [0.9.70-0.9.71] - 2026-06-19

### Added
- Play Mode Tester diagnostics: live animator-layer weights/masks, the base layer's currently-playing clip,
  whether a real CVR locomotion blend tree exists, and the controller asset path; simulate-standing also
  zeroes Emote and sets IsLocal.

## [0.9.69] - 2026-06-19

### Added
- **Live layer diagnostics in the Play Mode Tester.** A "Animator layers (motorbike diagnostics)" foldout
  lists every animator layer with its live weight and whether it's masked — so the layer posing the body
  (an unmasked, weight-1 non-locomotion layer, often a merged VRChat Action/FX layer) is easy to spot.

## [0.9.68] - 2026-06-19

### Fixed
- **Dance/emote audio error ("There is no 'AudioSource' attached…").** Caused by the `GetComponent<T>() ?? AddComponent<T>()`
  pattern, which bypasses Unity's special null handling so the AudioSource came back missing. Replaced with a
  proper `== null` check (also fixed the same pattern in the Magica Cloth path).

## [0.9.67] - 2026-06-19

### Fixed
- **Motorbike from emote/dance layers (real controller cause).** Full-body override layers now run
  WriteDefaults-OFF so their empty "Off" state passes through to locomotion instead of writing the bind pose.
  Emote layers no longer "match the controller" (which could force WD-on); "Fix motorbike pose" now also forces
  WD-off on all CVRFury emote/dance layers and prints the controller's layers (weight + mask) to pinpoint any
  other full-body layer causing it.

### Added
- **Auto-matched dance audio.** Building the dance menu now finds an AudioClip sharing each dance's name (in
  the dances folder, else project-wide) and plays it with the dance via an AudioSource the dance activates.

## [0.9.66] - 2026-06-19

### Fixed
- **Dance menu was a slider, not a dropdown.** The dance layer used a Float parameter; it's now an **Int** with
  exact `Equals` conditions so it matches the synced AAS dropdown (and shows as a dropdown, not a 0–1 slider).
- **Play Mode Tester motorbike pose.** The tester now simulates a grounded, standing avatar (drives CVR's
  locomotion params: Grounded on, movement zero) so it shows the in-game idle instead of the no-input
  motorbike pose. Toggle "Stand still (simulate grounded)" to disable.
- Dance layer keeps WriteDefaults off so the empty "Off" option passes through to locomotion (no motorbike).

## [0.9.65] - 2026-06-19

### Added
- **Sync Dances → CVR dance menu.** Finds a Sync-Dances-style pack's dance clips already in the project
  (auto-located by folder name, or point it at the folder) and builds a CVR-native **synced dropdown** (Off +
  one option per dance) driving an exclusive full-body layer. Uses only the dance clips — none of the VRChat
  controller/menu — and CVR syncs the dropdown so everyone sees the same dance. Written into the base
  controller too, so it survives upload regeneration.

## [0.9.64] - 2026-06-19

### Added
- **Emote wheel slot editor (CVR's emote menu, not the toggle menu).** New section detects the avatar's real
  emote slots — the animator states driven by the `Emote` parameter — and lists each with its current clip,
  so you can confirm it's targeting the emote wheel. Assign a replacement animation per slot (and an optional
  audio clip, played via an AudioSource the emote activates). Changes are written into the base controller as
  well, so they survive CVR's upload regeneration.

## [0.9.63] - 2026-06-19

### Added
- **"Bare name = ON" clip matching.** Leave the ON suffix blank and CVRFury treats a clip named exactly after
  the toggle as the ON animation, paired with its `name + off` counterpart (e.g. "Tail to side" on / "Tail to
  side off" off). Covers creators who name the on clip after the toggle and only suffix the off clip.

## [0.9.62] - 2026-06-19

### Fixed
- **Compile error in the Sliders panel** — the "Whole-avatar size" button still referenced the old single
  `target` field after the multi-target refactor. It now seeds the target list correctly.

## [0.9.61] - 2026-06-19

### Added
- **Scan multiple clip folders.** Step 2 (clip linking) and the bulk renamer now take a primary Animations
  Folder plus any number of extra folders, so clips split across separate folders are all covered. Nested
  subfolders were already scanned recursively (clarified in the UI) — this is for *non-nested* sibling folders.

## [0.9.60] - 2026-06-19

### Changed
- README repeats trimmed (non-destruction stated once; condensed the dedicated section).

## [0.9.59] - 2026-06-19

### Changed
- **README / package description rewritten** to be shorter and more human: leads with what the tool does,
  what it does automatically, and why it's easy (one window, automatic at upload, non-destructive).

## [0.9.58] - 2026-06-19

### Added
- **Release workflow for clean download names.** GitHub's branch-ZIP is auto-named "CVRFury-<branch>.zip",
  which can't be renamed. A new GitHub Actions workflow builds properly-named **CVRFury.zip** and
  **CVRFury.unitypackage** — attached to a GitHub Release when you push a `v*` tag, or downloadable as a
  workflow artifact via "Run workflow". Distribute those instead of the branch ZIP.

## [0.9.57] - 2026-06-19

### Added
- **One slider can drive multiple targets equally.** Each Sliders row now takes a list of targets (or meshes
  for hue/emission) — add left + right and a single slider scales both together (e.g. one "Boobs" size
  slider). Added an optional Menu name per row so you can call it whatever you like instead of auto-naming
  from the first target.

## [0.9.56] - 2026-06-19

### Fixed
- **Sliders panel min/max fields were unusably tiny.** The number boxes had their labels packed inside a
  fixed width, leaving no room to type. Labels are now drawn separately so min/max (and the Length axis) are
  proper, editable fields.

## [0.9.55] - 2026-06-19

### Added
- **Play Mode Tester (Tools ▸ CVRFury ▸ Play Mode Tester).** A CVR version of VRChat's Gesture Manager: in
  Unity Play mode it lists your avatar's Advanced Avatar Settings (labelled from the AAS entries) and lets you
  flip toggles, drag sliders, pick dropdowns and fire triggers live — the animations play exactly as in-game,
  with no upload. Hides ChilloutVR's core locomotion/gesture parameters, and has a "Reset all to defaults".

## [0.9.54] - 2026-06-19

### Added
- **Sliders panel (size, length, hue, emission).** The resize section is now a general "Sliders" panel: each
  row picks a type — **Size** (uniform), **Length** (one axis), **Hue** or **Emission** (a material float
  property, e.g. Poiyomi's _MainHueShift / _EmissionStrength) — with its own target and min/max. One place to
  add any in-game menu slider, and room to grow.
- **Last-resort clip-ending renamer.** When a pack's clip names are too inconsistent to match, a new Step 2
  tool scans the folder, guesses on/off from words at the end of each name (show/hide, enable/disable…), and
  renames clips to end with your chosen markers (e.g. 1/0). Clearly flagged as a last resort — it edits asset
  files, can mis-guess, and is confirmed before running.

## [0.9.53] - 2026-06-19

### Added
- **Size / Length sliders for any part.** The old whole-avatar size slider is now a multi-part panel: add any
  bone/object (or the whole avatar) to a list, tick **Size** (uniform) and/or **Length** (one axis, X/Y/Z)
  per part with their own min/max shown beside them, then "Create sliders". Each becomes an in-game Advanced
  Settings slider that resizes that part at runtime in CVR — e.g. chest size, privates length. Scale actions
  gained a per-axis option to make single-axis "length" possible.

### Changed
- **DPS orifice toggles are named after their socket** (e.g. "Footjob Socket", "Thighjob Socket") instead of
  nine identical "DPS Orifice" entries, so you can tell them apart in the menu.

## [0.9.52] - 2026-06-19

### Fixed
- **Emotes now survive CVR's upload regeneration (the "sometimes it uploads different" bug).** At upload CVR
  rebuilds the AAS animator from the base controller + entries, which was wiping/rebuilding the emote layers
  inconsistently (often as a motorbike pose). Emote parameters + layers are now written into the **base
  controller** as well (when it's a safe per-avatar controller), so regeneration copies the correct layer and
  skips rebuilding it — the upload matches the editor every time. Re-run "Add emote toggles" to apply; it
  also repairs existing emote layers. (If no per-avatar base is found, it says so and points you to Step 2.)

## [0.9.51] - 2026-06-19

### Added
- **"Remove emote toggles" button.** One click strips every CVRFury emote (menu entries + animator layers +
  parameters), so a posing/motorbike problem caused by emotes is instantly reversible — and it isolates the
  cause: if the avatar still poses wrong with all emotes removed, the issue is the base controller, not the
  emotes.

## [0.9.50] - 2026-06-19

### Fixed
- **Emotes looked wrong when idle but snapped right when moving.** That's a WriteDefaults mismatch: the emote
  layers were hardcoded WriteDefaults-on while CVR's locomotion may run WriteDefaults-off, so the default pose
  bled through at rest. Emote layers now detect and **match the controller's existing WriteDefaults**
  convention, and re-running "Add emote toggles" repairs already-built emote layers in place.

## [0.9.49] - 2026-06-19

### Changed
- **Strip step now also removes inert VRCFury SPS/haptic components.** After converting to DPS, the VRChat-
  only SPS components (VF.Model.VRCFuryHaptic*/Sps*) do nothing in CVR and become missing scripts if VRCFury
  isn't installed, so the "Strip VRChat + broken components" step now drops the component — leaving the mesh,
  its DPS lights, and the shader intact. (It does not delete anything from a separate VRChat copy.)

## [0.9.48] - 2026-06-19

### Changed
- **Step 3 now makes the Poiyomi DPS-vs-SPS choice explicit.** It always enables the DPS / light-based
  deform path (never SPS, which is contact-driven and inert in CVR), says so in the result, and warns if the
  material also has SPS turned on so you're not fooled into expecting SPS to deform. Added a one-line note in
  the panel recommending a DPS/light shader like Poiyomi.

## [0.9.47] - 2026-06-19

### Added
- **Pre-upload shader-compile check.** A shader on the avatar that fails to compile (e.g. an old
  XSToon/DPS orifice shader on current Unity) makes ChilloutVR abort with a cryptic "Build asset failed
  content validation" error. The bake now scans the avatar's materials and reports the failing shader and
  its compile error in plain language — with the stopgap that turning a deform option back off lets it
  upload meanwhile.

## [0.9.46] - 2026-06-19

### Changed
- **Synced-bit budget check now reports real numbers and fails clearly.** Instead of a vague "that's a lot"
  warning at 64 settings, the bake now estimates the actual synced-bit cost and compares it to ChilloutVR's
  ~3200-bit cap: an explicit error when over (the cause of the CCK's cryptic "Build asset failed content
  validation" upload abort), a heads-up at 75%, and a quiet info otherwise — with concrete advice (delete
  unused menu params, prefix Machine Names with '#' to make them local, keep on/off controls as Bool).

## [0.9.45] - 2026-06-19

### Added
- **"Remove CVRFury-baked orifices" button.** One click deletes every orifice (and its menu toggle) that
  CVRFury baked, so a bulk "Add to every socket" that picked up wrong spots is fully reversible — then re-bake
  or place them by hand. Only removes CVRFury-made rigs (matched by marker-light name), never the avatar's own
  DPS. Clarified that "Add to this spot" is repeatable for as many manual orifices as you want.

## [0.9.43] - 2026-06-19

### Fixed
- **SPS/DPS detection accuracy.** Plug detection now recognises common penetrator names (shaft, penis, cock,
  phallus, knot, …) that were previously missed — so a "Shaft" no longer reports 0 plugs. Contact/PhysBone
  helper objects ("Orifice Detector", "Hole Detector", senders/receivers/colliders) are no longer matched as
  sockets, so "Add to every socket found" won't bake onto them. The detect report now shows the detection
  source in [brackets] for each entry and recommends "Add to this spot" when results look noisy.

## [0.9.42] - 2026-06-19

### Added
- **Avatar size slider.** New "Avatar size slider" section adds a native in-game menu slider that scales the
  whole avatar between a min and max factor (default 0.5×–2×, loading at normal size). Works in CVR with no
  contacts or special shaders.
- **DPS orifices are now opt-in toggles.** Every baked orifice starts disabled with its own menu toggle (OFF
  by default), so nothing deforms until the wearer turns it on. Uncheck the option in Step 2 to skip it.
- **Distinct DPS scene icon.** Baked orifices get a magenta ring icon in the Scene/Hierarchy (no runtime
  component added) so you can find them instead of hunting plain Unity light icons.
- **Menu Path label picker + Step 3 plug auto-fill.** The Menu Path field has a ▾ dropdown to fill the label
  from the object name or a label already used on the avatar; Step 3 can auto-fill the plug mesh from detection.

### Changed
- **Menu Path clarified.** CVR's Advanced Settings is a single flat list, so "Menu Path" is just the display
  label (the builder only used the last path segment). Fixed the misleading "slashes create submenus" tooltip;
  the field now reads "Menu Label". Multiple parts (male + female) bake in one pass.

### Fixed
- **Motorbike pose with custom emotes.** Emote layers now build with WriteDefaults on, so the Off state hands
  the body back to the locomotion layer instead of freezing it. The "Fix motorbike pose" check now requires a
  real locomotion blend tree (not just the parameter names), so it no longer reports a broken/regenerated
  controller as healthy.

## [0.9.37] - 2026-06-16

### Changed
- **SPS/DPS buttons now use only the clean inline status box** — they no longer also dump into the shared
  Log box at the bottom, so each action's result shows in one place right under the buttons.
- **Step 3 result is explicit about the shader.** On success it names the shader and the exact property it
  toggled ("set _EnablePenetration = 1"); when it can't, it prints the exact shader name to send back for
  wiring. Fixed stale "Next: switch the plug's shader" text — it now points at the Step 3 button.

## [0.9.36] - 2026-06-16

### Added
- **Step 3 is now a button too — auto-enable plug deformation.** Pick the plug mesh and CVRFury reads the
  material's *actual* shader properties, switches on whichever penetration/DPS deform-enable toggle really
  exists, and reports exactly what it changed. No hardcoded shader names: it adapts to whatever shader the
  material uses, flags locked Poiyomi materials (unlock → run → re-lock), and tells you when a shader simply
  has no such toggle. The whole SPS→DPS flow is now three buttons.

## [0.9.35] - 2026-06-16

### Changed
- **SPS/DPS tab made intuitive.** Three plainly-labelled steps (Find the parts → Add the orifice lights →
  Switch the plug's shader) with short one-line descriptions instead of walls of text. Each button now shows
  an **inline result right in the panel** ("Done — added lights to 2 sockets. Next: Step 3…") so you can tell
  it worked and what to do next without hunting in the Log box. The clone-from-template path is tucked behind
  a single toggle for the rare case where you already have a working DPS avatar.

## [0.9.34] - 2026-06-16

### Changed
- **SPS/DPS section rewritten as a clear ordered walkthrough.** It now spells out the SPS-only path: (1)
  detect, (2) bake DPS orifice lights at your socket(s) — no template needed, (3) enable light-based
  deformation on the plug's material (Poiyomi "Penetration Deformation" or the Raliv DPS shader), (4) test
  in CVR. The "clone a working orifice" option is now clearly marked as an alternative for avatars that
  already have a DPS rig, not the starting point. Removed the leftover plug-tip picker from the old
  contact-conversion idea.

## [0.9.33] - 2026-06-16

### Changed
- **Credits footer corrected.** Now reads "Made by **DoNotPetMe** / **--Stardust--**" with "--Stardust-- in
  CVR" underneath. The bounce is now click-triggered: names sit still until you click one, then that name
  bounces with a decaying amplitude and settles on its own (no more constant bouncing).

## [0.9.32] - 2026-06-16

### Added
- **♥ Credits footer** at the bottom of the CVRFury window.

## [0.9.31] - 2026-06-16

### Added
- **SPS → DPS auto-bake (experimental).** Generates Raliv-DPS orifice marker lights from scratch — no
  template needed — using the canonical DPS light encoding (point lights, Range 0.5, Intensity 0, plus a
  forward "normal" light for orientation). Bake at the picked socket, or auto-bake at every detected socket
  that doesn't already have a DPS rig. Because DPS deformation is light + shader driven, these render and
  deform in CVR. The exact light codes are surfaced in the log and easy to calibrate: if deformation
  doesn't trigger, report a known-good orifice's Range/Intensity and the constants will be tuned.

## [0.9.30] - 2026-06-16

### Added
- **SPS/DPS: clone a working DPS orifice onto a socket (gets real deformation working in CVR).** DPS bends
  the mesh through marker point-lights read by the penetrator's shader, and those render in CVR — which is
  why DPS works there and SPS (which finds sockets via VRChat Contacts) doesn't. The new "Clone DPS orifice
  → socket target" button copies a known-working DPS orifice rig (lights and all) onto a chosen socket
  location, so an SPS-only avatar gets working deformation without hand-encoding Raliv's exact light codes.
  Confirmed CVRFury's strip step never removes lights or shaders, so existing DPS plugs/orifices carry over
  untouched. SPS→DPS auto-bake is the next step.

## [0.9.29] - 2026-06-16

### Added
- **SPS / DPS section (experimental) — detection + plug/socket location picker.** New window section that
  detects VRChat penetration markers (VRCFury SPS components, VRChat Contacts / TPS collision tags, Raliv
  DPS marker lights, and name fallbacks) and reports candidate plugs/sockets, plus VRCFury-style object
  fields to pick the plug (penetrator) and socket (orifice) transforms by hand. Detection is non-destructive.
  ChilloutVR has no native SPS deformation, so only the contact layer (plug → `CVRPointer`, socket →
  `CVRAdvancedAvatarSettingsTrigger`) will convert; the conversion action lands once the CVR target is locked.

## [0.9.28] - 2026-06-16

### Fixed
- **Smart-match review no longer breaks CVR-native object toggles.** Toggles that switch GameObjects
  directly (CVR's native object toggles — the kind that make the avatar look exactly like the Unity scene)
  work without any animation clip. The fuzzy matcher was assigning them a guessed clip, and applying a clip
  flips a toggle into animation-clip mode, which overrode and broke the native toggle ("nothing toggles in
  game"). The review now detects these, marks them **● object toggle (no clip needed)**, and never
  auto-assigns a clip to them (you can still drop one in by hand if you really want). They're counted under
  the "Found" filter.

## [0.9.27] - 2026-06-16

### Added
- **Filter buttons in the smart-match review** — a toolbar of All / Found / Guessed / None / Changed (each
  with a live count) to instantly narrow the list, on top of the search box. "Changed" shows only the rows
  you've edited by hand. A message appears when nothing matches the current filter/search.

## [0.9.26] - 2026-06-16

### Added
- **Smart-match review for clip toggles (optional).** Tick "Review & fix clip matches before building" in
  Step 2, then "Preview / refresh matches" to see what clip the tool pairs to every toggle/slider before
  anything is built. Exact matches are marked ✔; toggles with no exact match get a **fuzzy best-guess**
  clip (containment + edit-distance) marked ?, and ✘ means nothing was found. Each row has editable ON/OFF
  (or slider Min/Max) clip fields with the ⊙ object picker and drag-and-drop, so you can confirm or correct
  a guess by hand. A search box filters the list. Your picks are **persisted per-avatar**, and "Apply
  matches & build controller" uses exactly what's shown.

## [0.9.25] - 2026-06-16

### Fixed
- **Motorbike pose from editing the avatar after building the controller (e.g. assigning visemes).** When
  the CVRAvatar inspector is changed after Step 2, the CCK can regenerate `avatarSettings.animator` and
  drop CVRFury's locomotion — an animator with no CVR movement loads as the seated/"motorbike" pose. A new
  guard now runs **at upload** (and via a button) that detects an AAS animator with no CVR locomotion
  (MovementX / MovementY / Grounded) and re-points it at a controller that has it — the base controller it
  was extending, or the generated AAS controller on disk. Order of operations no longer matters: even if
  something resets the animator, the uploaded avatar still moves.

### Added
- **"Fix motorbike pose (re-assert locomotion)" button** in Step 2, so you can repair and verify in-editor
  without uploading. Reports whether the controller was already fine or had to be re-asserted.

## [0.9.24] - 2026-06-16

### Fixed
- **Emotes / Poses no longer cause the motorbike pose when pointed at a GoGoLoco-style folder.** Those
  folders mix real poses/dances with **locomotion/system clips** (idle, AFK, stand, walk/run, jump,
  crouch, prone, fly, swim, tracking/calibration, T-pose). Added as always-present override layers, the
  system clips pose the body at rest — the motorbike look. The Emotes step now **skips clips whose names
  look like locomotion/system clips** and only builds layers for genuine poses, listing what it skipped.
  Rename a skipped clip (drop the locomotion word) and re-run if it was actually a pose you wanted.
- Emote AAS entries now carry their clip (on = pose) like the clothing-toggle path, so the CCK sees a
  proper animation toggle rather than an empty one.

## [0.9.23] - 2026-06-16

### Added
- **Custom / multiple ON-OFF suffix words in Step 2.** The "clip name ends with" fields now accept several
  comma-separated alternatives (e.g. ON: `toggled, on, enabled` / OFF: `default, off, disabled`), so clips
  a creator named differently from the rest still pair up. Longer words are matched first so `toggled on`
  wins over `on`.

### Notes
- Next: a searchable, per-avatar-persisted manual override editor in Step 2 — see every toggle's matched
  ON/OFF clip and reassign or swap them by hand (also covers setting defaults for clip-only toggles).

## [0.9.22] - 2026-06-16

### Changed
- **Toggle defaults now follow the Unity scene** where possible. When the parameter linker finds the
  GameObject a toggle controls, the toggle's Default Value is taken from that object's current active
  state in the scene (so clothing/accessories you have enabled in Unity load enabled in CVR), instead of
  always using the VRChat parameter default. Toggles whose target object isn't found (e.g. clip-only
  toggles on packed meshes) still fall back to the VRChat default — use the upcoming manual override to
  fix any of those by hand.

## [0.9.21] - 2026-06-16

### Added
- **Auto viseme mapping in Step 0 (Avatar basics).** CVRFury now copies VRChat's 15 viseme blendshapes
  straight onto the CVRAvatar, so lip sync is fully set up without touching the CCK's "Auto Select
  Visemes" button. This matters because clicking that button *after* the controller is built can make the
  CCK regenerate the AAS animator and drop CVRFury's toggle/slider layers — the cause of "I added visemes
  and the toggles stopped working." Do visemes via Step 0 and build the controller (Step 2) last.

## [0.9.20] - 2026-06-16

### Fixed
- **Sliders / radials now actually animate** (hue-shift sliders, blendshape sliders, and the common
  VRChat habit of putting a hair/clothing toggle on a radial instead of a real toggle). Previously the
  build step created the synced Float *parameter* for a slider but never built an animator layer or linked
  its clips, so moving the slider did nothing. Now:
  - The clip-pairing step links a slider's matched clips as **min (value 0) = the "default"/off clip** and
    **max (value 1) = the "toggled"/on clip**, mirroring how toggles get on/off clips.
  - The controller build adds a **1D blend-tree layer** (min→max driven by the synced Float) for each
    slider, with the same no-humanoid mask the toggle layers use, so the slider drives its property
    without ever posing the body. Body-posing slider clips are skipped like body-posing toggle clips.

## [0.9.19] - 2026-06-16

### Changed
- **CVR (CCK) movement is now always forced as the foundation of the built controller.** The generated
  AAS controller is always based on a controller that carries CVR locomotion (MovementX / MovementY /
  Grounded). If the optional Controller field is empty, CVR's stock AvatarAnimator is used (as before). If
  a controller is supplied, it is used as the base only when it already contains CVR locomotion; otherwise
  the tool falls back to the stock AvatarAnimator so the avatar can still walk/run/jump — and says so in
  the log ("Movement: …"). The build aborts with a clear message rather than producing a motionless avatar
  if no CVR locomotion can be found at all.

## [0.9.18] - 2026-06-16

### Fixed
- **Motorbike pose, structurally.** Every CVRFury clip-toggle layer in the built controller now carries an
  AvatarMask with all humanoid body parts (muscles + IK goals) disabled. Per-clip detection of body-posing
  clips could miss cases (a clip posing the rig through bone transforms or muscle channels the detector
  didn't match), and any such clip on an always-weighted toggle layer posed the whole avatar — the
  recurring motorbike pose. With the mask, a clothing/accessory toggle layer physically cannot move the
  skeleton; it can only toggle GameObjects/blendshapes. The existing skip guard is kept as a first line of
  defense. **Note:** this only affects newly built controllers — delete the old `CVRFury Generated`
  controller and re-run Step 2 ("Build & attach"), then re-upload.

## [0.9.17] - 2026-06-15

### Fixed
- **"Show Last Build Log" is no longer empty after using the CVRFury window.** Every workflow step
  (parameters, clips, build & attach, PhysBones, Magica, emotes, strip) now mirrors its output into the
  persistent build log surfaced by **Tools ▸ CVRFury ▸ Show Last Build Log**, not just the window's own
  log area. Previously the menu only showed logs produced by the upload-time CCK hook, which stays empty
  when the conversion is done ahead of time in the window — so the log always read as empty.

## [0.9.16] - 2026-06-15

### Added
- **Emotes / Poses section** in the CVRFury window: point it at a folder of full-body animation clips
  (sit, dance, GoGoLoco-style poses) and it adds a menu **toggle per clip**. Each toggle plays its clip
  while ON and returns to normal CVR movement when OFF — the Override layer's Off state is empty, so it
  only poses the body while toggled on and never breaks locomotion. Built onto the existing AAS
  controller (run step 2 first); intentionally allows humanoid-posing clips (unlike clothing toggles).
  Toggle one at a time.

## [0.9.15] - 2026-06-15

### Added
- **PhysBones — tunable & more accurate.** Step 3 now exposes Damping, Elasticity ×, Stiffness ×,
  Radius × and Gravity × sliders, and maps VRChat `pull` → DynamicBone Elasticity (with `spring`
  fallback) for a closer feel. Plus **"Remove the VRChat PhysBones after converting."**
- **Magica Cloth 2 wiring (step 4).** When Magica is installed, converts each VRCPhysBone to a
  MagicaCloth component on its root, with a **cloth-type option** (BoneCloth / BoneSpring), assigns the
  root bone, and an option to remove the originals. (Magica's solver is configured at its Build step, so
  open each and press Build / enter Play, then tune.)
- **Avatar basics (step 0)** and **Strip VRChat + broken components (step 5)** are now in the unified
  window, so the whole workflow lives in one place.

### Removed
- The standalone **"VRChat → ChilloutVR Converter"** window — its workflow now lives entirely in the
  single `Tools ▸ CVRFury ▸ CVRFury` window. The menu is down to that one window plus utilities
  (Clean Missing Scripts, Diagnose, Show Last Build Log, Verbose Logging).

## [0.9.14] - 2026-06-15

### Added
- **One unified window: `Tools ▸ CVRFury ▸ CVRFury`.** A guided, numbered workflow replacing the
  scattered menu items — pick the avatar once, then run each step, with a shared log: **1) Parameters**
  (link from the VRChat menu), **2) Toggle clips + build/attach controller**, **3) PhysBones →
  DynamicBones**, **4) Magica Cloth** (detected-if-installed; conversion options coming next). The
  separate "Link CCK Parameters" and "Link Toggle Animations" menu items are folded into this window.

### Fixed
- **Float/slider "motorbike pose" guard.** When building toggle layers from clips, a clip that animates
  the humanoid rig (muscles or actual bones) is no longer turned into a layer — that's what posed the
  whole body when a control hit its extreme. Such toggles get their parameter (so the red ❗ still clears)
  but no body-posing layer, and the build reports how many were skipped.

### Changed
- `AasParameterLinker` and the toggle-clip linker are now reusable library logic behind the unified
  window (single source of truth) instead of separate windows.

## [0.9.13] - 2026-06-15

### Added
- **Toggle Clip Linker can now build & attach the controller** (the part that clears the red "parameter
  not present" warnings). New options in `Tools ▸ CVRFury ▸ Link Toggle Animations from Folder`: an
  optional **Controller** slot and a **Build & attach a controller** checkbox. When enabled it copies the
  given controller (or CVR's stock `AvatarAnimator` if left empty, so locomotion is preserved) — never
  modifying the original — adds a parameter for every AAS entry (Bool/Float/Int by type) plus a clean
  clip-driven two-state layer for each toggle that has on/off clips, then attaches the copy as the avatar's
  AAS controller (base + override + animator). Because the controller now contains every machine-name
  parameter, the CCK's red warnings clear and toggles with clips animate — no manual Create Controller
  needed. Adds `AnimatorUtil.AddBoolToggleLayer` and `CckAvatar.AttachGeneratedController`.

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
