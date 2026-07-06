# CVRFury Roadmap

CVRFury 0.1 is a working **foundation**: the non-destructive CCK build hook, the
feature/builder framework, and five real features. It is **not yet at full
VRCFury feature parity** — that is a large, ongoing effort. This document is
honest about what exists and what's next.

## Status legend
✅ implemented · 🟡 partial · ⬜ planned

## Core pipeline
- ✅ Reflection hook into `CCK_BuildUtility.PreAvatarBundleEvent`
- ✅ Non-destructive bake (clone controllers, temp assets, strip components)
- ✅ Auto-discovered feature/builder registry
- ✅ Test-bake (clone) menu command + build-log window
- ✅ Synced-parameter budget warning
- ✅ Automatic-fixes pass (prune broken AAS entries, warn on duplicate params)
- 🟡 EditMode tests — pure utilities (`ParamNameAllocator`, `HierarchyUtil`) covered;
  clip/animator/merge tests still need CCK stubs in CI
- 🟡 Prop / spawnable pipeline via `PrePropBundleEvent` (structural features only)
- 🟡 **Validate** that the pre-bundle event operates on a build copy across CCK
  versions; if not, move the bake onto an explicit clone (see ARCHITECTURE.md)

## Features vs. VRCFury
| VRCFury feature | CVRFury status | Notes |
|---|---|---|
| Toggle | ✅ | object/blendshape/material/scale/property actions; synced AAS toggle |
| Full Controller | ✅ | controller merge with parameter remap + menu exposure |
| Armature Link | 🟡 | reparent + merge-bones modes; needs humanoid edge-case hardening |
| Blendshape Link | 🟡 | bakes values; "keep live" animator-driven mirroring planned |
| Object State / Apply During Upload | ✅ | activate / deactivate / delete |
| Modes / multi-state (dropdown) | ✅ | exclusive states, union coverage, AAS `GameObjectDropdown` |
| Presets dropdown (equip one, rest turn off) | ✅ | `CVRFuryPresets` — built from existing Toggles; "Custom" option keeps manual control |
| Custom crouch/prone styles dropdown | ✅ | `CVRFuryLocomotionStyles` — in-locomotion-layer states gated on `Crouching`/`Prone` |
| Blendshape logic (object-state → blendshape rules) | ✅ | `CVRFuryBlendshapeLogic` — auto-detect, multi-condition AND, create-toggle-from-blendshape |
| Puppet / radial slider | ✅ | 1D blend-tree layer + synced AAS slider |
| Gestures | ✅ | `CVRFuryGesture` keys a layer on `GestureLeftIdx`/`GestureRightIdx` (CVR −1…6 mapping, unit-tested) |
| Visemes / Blink / Eye helper | 🟡 | `CVRFuryAvatarSettings` sets the CVRAvatar fields (verify member names) |
| Parameters declaration | ✅ | `CVRFuryParameters` declares params + optional menu exposure |
| Exclusive toggle tags | ⬜ | CVR has no animator parameter drivers; use **Modes** for true exclusivity |
| Automatic fixes (dedupe / prune) | 🟡 | broken-entry prune + duplicate-param warning done; more to come |
| SPS (penetration system) | 🟡 | detection (VRCFury SPS / VRChat Contacts-TPS / Raliv DPS) + plug/socket location picker in the window; CVR has no native deformation, so only the contact layer (CVRPointer + CVRAdvancedAvatarSettingsTrigger) will convert — target being finalised |
| Direct Tree / OSC / Advanced | ⬜ | |
| Remove/strip missing (broken) scripts | ✅ | auto at build + prefab-aware manual tool — key for VRChat imports |
| VRChat → CVR converter (descriptor/menu/params/physbones/layers) | ✅ | toggle-driven window; reflection-based; physics values approximate |
| Typed component mapping fidelity | 🟡 | converter exists; PhysBone physics + puppet→joystick mapping need refinement |
| World-constraint, Boundingbox fix, Toggle-folder | ⬜ | |

## CVR-specific quirks still to wire up
- DynamicBones vs. PhysBones: prop physics handling on Armature Link.
- Synced-bit budget reporting (the CCK shows used bits) — surface in inspector.
- CVR menu nesting: confirm how submenu paths map to AAS grouping per version.
- Material/Texture property animation naming differences vs. VRChat.

## Contributing priorities
1. Test against a real CCK install and pin exact member names in `CckNames.cs`.
2. Harden `ControllerMerger` against sub-state-machine + any-state edge cases.
3. Add the Dropdown/Modes feature (high value, maps cleanly to AAS).
