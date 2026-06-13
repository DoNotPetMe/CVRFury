# CVRFury

**Non-destructive avatar tooling for ChilloutVR.** CVRFury brings the workflow
that [VRCFury](https://vrcfury.com) made popular on VRChat to the **ChilloutVR
Content Creation Kit (CCK)**: drop feature components onto your avatar, and they
are baked into the avatar's animators and **Advanced Avatar Settings (AAS)**
automatically at upload time — your source scene is never modified.

> CVRFury is an independent project, inspired by VRCFury's design but containing
> no VRCFury code. It is not affiliated with Alpha Blend Interactive (ChilloutVR)
> or with VRChat / VRCFury. See [`LICENSE`](LICENSE).

---

## Why it exists

ChilloutVR's CCK is powerful but its avatar setup is **destructive**: you wire
toggles directly into one big animator and the CVRAvatar component, and prefabs
shipped by clothing/asset creators can't "just work" on an arbitrary avatar.
VRChat solved this with non-destructive tools (VRCFury, Modular Avatar) built on
the SDK's preprocess callback. CVRFury does the same for ChilloutVR.

The catch: **ChilloutVR's CCK exposes no formal preprocess interface.** CVRFury
hooks the build the same proven way lilToon and NDMF-for-CVR do — by subscribing
via reflection to the CCK's `CCK_BuildUtility.PreAvatarBundleEvent`, which fires
on the avatar GameObject right before it is bundled for upload. See
[`Documentation~/ARCHITECTURE.md`](Documentation~/ARCHITECTURE.md).

---

## Features

| Component | What it does |
|-----------|--------------|
| **CVRFury Toggle** | Menu toggle that animates objects, blendshapes, materials, scale, or shader properties. Registered as a synced AAS GameObject Toggle so it appears in the in-game Advanced Settings menu. |
| **CVRFury Modes** | Exclusive multi-state control (outfit/hair/weapon variants) baked as a synced AAS dropdown — pick one, the rest turn off. |
| **CVRFury Slider** | Continuous radial/puppet control baked as a synced AAS slider with a 1D blend-tree (blendshape size, fades, scale). |
| **CVRFury Full Controller** | Merge a prebuilt Animator Controller (and its parameters / menu entries) into the avatar. The backbone for shippable prefabs. |
| **CVRFury Armature Link** | Attach a prop's armature to the avatar skeleton by bone name — reparent the prop skeleton or merge its skinned meshes onto the avatar's bones. |
| **CVRFury Blendshape Link** | Copy blendshape values from a source mesh (e.g. the body) onto clothing/accessory meshes — statically and live (mirrors animated curves). |
| **CVRFury Avatar Settings** | Set the CVRAvatar viewpoint / voice position, face mesh, and viseme / blink / eye-movement toggles from a prefab. |
| **CVRFury Object State** | Force objects active/inactive or delete them at build (e.g. strip editor-only helpers). |

More features (SPS-style, gestures/visemes helpers, inventory, etc.) are on the
[roadmap](Documentation~/ROADMAP.md).

---

## Requirements

- Unity **2021.3 LTS** (the version the ChilloutVR CCK targets).
- The **ChilloutVR CCK** imported into your project. CVRFury detects it at
  runtime via reflection — there is **no compile-time dependency**, so CVRFury
  installs cleanly even before the CCK is present.

---

## Installation

### Unity Package Manager (git URL)
1. `Window ▸ Package Manager ▸ + ▸ Add package from git URL…`
2. Paste: `https://github.com/donotpetme/cvrfury.git`

### Manual
Clone/copy this repository into your project's `Packages/` folder, or into
`Assets/CVRFury/`.

---

## Quick start

1. Add your avatar to the scene and set up its `CVRAvatar` component as usual.
2. Select the object you want to toggle (say, a hat). Add a **CVRFury Toggle**
   component (`Add Component ▸ CVRFury ▸ CVRFury Toggle`).
3. Set **Menu Path** to `Clothing/Hat`, add an **Object Toggle** action pointing
   at the hat, and leave **Default On** unchecked.
4. (Optional) Preview without uploading: select the avatar root and run
   **Tools ▸ CVRFury ▸ Test Bake Selected Avatar (clone)**.
5. Upload through the CCK as normal. CVRFury bakes the toggle into a generated
   animator + a synced AAS entry, and strips its own components from the upload.

---

## How non-destruction works

CVRFury never edits your source assets:

- It runs on the GameObject the CCK hands it at build time.
- Generated animator controllers and clips are **clones** written to a temporary
  folder (`Assets/_CVRFury/Generated`, cleared on each build).
- All `CVRFuryComponent`s are stripped from the uploaded copy.

If anything goes wrong, CVRFury logs a clear message (see **Tools ▸ CVRFury ▸
Show Last Build Log**) and skips the offending feature rather than breaking your
upload.

---

## Project layout

```
Runtime/    Feature components you put on your avatar (no runtime logic; editor-only intent)
Editor/
  Hooks/    CCK reflection layer + build-pipeline hook  (the integration core)
  Builder/  Bake orchestrator + per-feature builders
  Util/     Animator / clip / asset helpers
  Inspector/Editor UI, menu, build-log window
Documentation~/  Architecture + roadmap
Samples~/        Demo content
```

See [`CHANGELOG.md`](CHANGELOG.md) for version history and known limitations.

## Contributing

Adding a feature is two files: a `CVRFuryComponent` subclass in `Runtime/` and a
matching `FeatureBuilder<T>` in `Editor/Builder/Features/`. The builder registry
discovers it automatically. See `Documentation~/ARCHITECTURE.md`.
