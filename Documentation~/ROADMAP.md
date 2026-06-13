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
- 🟡 Prop / spawnable pipeline via `PrePropBundleEvent` (structural features only)
- 🟡 **Validate** that the pre-bundle event operates on a build copy across CCK
  versions; if not, move the bake onto an explicit clone (see ARCHITECTURE.md)
- ⬜ EditMode tests for clip/animator/merge utilities (need CCK stubs in CI)

## Features vs. VRCFury
| VRCFury feature | CVRFury status | Notes |
|---|---|---|
| Toggle | ✅ | object/blendshape/material/scale/property actions; synced AAS toggle |
| Full Controller | ✅ | controller merge with parameter remap + menu exposure |
| Armature Link | 🟡 | reparent + merge-bones modes; needs humanoid edge-case hardening |
| Blendshape Link | 🟡 | bakes values; "keep live" animator-driven mirroring planned |
| Object State / Apply During Upload | ✅ | activate / deactivate / delete |
| Modes / multi-state (dropdown) | ✅ | exclusive states, union coverage, AAS `GameObjectDropdown` |
| Puppet / radial slider | ✅ | 1D blend-tree layer + synced AAS slider |
| Gestures | ⬜ | needs CVR gesture/animator-parameter mapping |
| Visemes / Blink / Eye helper | 🟡 | `CVRFuryAvatarSettings` sets the CVRAvatar fields (verify member names) |
| SPS (penetration system) | ⬜ | large; CVR has no DPS/SPS equivalent — would be net-new |
| Direct Tree / OSC / Advanced | ⬜ | |
| Remove/strip components, fix bad bindings | ⬜ | "automatic fixes" suite |
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
