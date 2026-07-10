using System.Linq;
using UnityEngine;

namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Classifies Udon behaviours by INTENT — what the thing is for, not what class implements it — so the
    /// world converter can turn a raw "37 Udon behaviours" inventory into an actionable migration plan:
    /// which ones auto-convert, which have a one-component CVR recipe, and which genuinely need rework.
    ///
    /// Recognition is program-name pattern matching plus cheap context checks on the GameObject (does it
    /// actually carry a VideoPlayer / Light / AudioSource?). Context agreement raises confidence; the goal is
    /// a trustworthy TODO list, not perfect inference. Patterns cover the prefabs that dominate real VRChat
    /// worlds (USharpVideo / ProTV / iwaSync / VideoTXL, ToggleObject variants, teleporters, doors, light
    /// switches, jukeboxes...) and fall back to "manual" with the generic CVR recipe.
    /// </summary>
    internal static class UdonIntents
    {
        internal sealed class Intent
        {
            public string Kind;      // short label, e.g. "Video player"
            public string CvrPath;   // the concrete CVR recipe for this intent
            public bool Auto;        // true = WorldConverter handles it automatically
            public bool Confident;   // context on the object agreed with the name match
        }

        public static Intent Classify(string programName, GameObject go)
        {
            var n = (programName ?? "").ToLowerInvariant();

            bool Has(params string[] words) => words.Any(w => n.Contains(w));

            if (Has("video", "protv", "usharpvideo", "iwasync", "videotxl", "vidtxl", "ytdl", "mediaplayer"))
                return new Intent
                {
                    Kind = "Video player",
                    CvrPath = "CVRVideoPlayer (added automatically — assign the screen renderer/audio in its inspector)",
                    Auto = true,
                    Confident = go != null && go.GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true) != null
                                || Has("protv", "usharpvideo", "iwasync", "videotxl"),
                };

            if (Has("teleport", "warp", "tphere", "tp_"))
                return new Intent
                {
                    Kind = "Teleporter",
                    CvrPath = "CVRInteractable → Teleport Player action, pointing at the destination Transform",
                    Confident = true,
                };

            if (Has("mirror"))
                return new Intent
                {
                    Kind = "Mirror toggle",
                    CvrPath = "CVRInteractable → Set GameObject Active on the CVRMirror object (mirror itself already converts)",
                    Confident = go != null && go.GetComponentInChildren<Renderer>(true) != null,
                };

            if (Has("door", "gate", "hatch"))
                return new Intent
                {
                    Kind = "Door",
                    CvrPath = "CVRInteractable → Set Animator / Set GameObject Active (reuse the door's existing Animator)",
                    Confident = go != null && go.GetComponentInChildren<Animator>(true) != null,
                };

            if (Has("light", "lamp", "candle", "brightness", "daynight", "sun"))
                return new Intent
                {
                    Kind = "Light control",
                    CvrPath = "CVRInteractable → Set GameObject Active on the Light object(s)",
                    Confident = go != null && go.GetComponentInChildren<Light>(true) != null,
                };

            if (Has("music", "audio", "sound", "radio", "jukebox", "speaker", "bgm"))
                return new Intent
                {
                    Kind = "Audio control",
                    CvrPath = "CVRInteractable → Set GameObject Active on the AudioSource object (or CVRVideoPlayer for streams)",
                    Confident = go != null && go.GetComponentInChildren<AudioSource>(true) != null,
                };

            if (Has("toggle", "switch", "onoff", "on_off", "activate", "enable", "show", "hide"))
                return new Intent
                {
                    Kind = "Object toggle",
                    CvrPath = "CVRInteractable → Set GameObject Active on its target object",
                    Confident = true,
                };

            if (Has("pickup", "grab", "throwable"))
                return new Intent
                {
                    Kind = "Pickup logic",
                    CvrPath = "CVRPickupObject (pickups themselves already convert; extra behaviour needs review)",
                    Confident = true,
                };

            if (Has("chair", "seat", "sit"))
                return new Intent
                {
                    Kind = "Seat logic",
                    CvrPath = "CVRSeat (stations already convert; extra behaviour needs review)",
                    Confident = true,
                };

            if (Has("pen", "marker", "draw", "whiteboard"))
                return new Intent
                {
                    Kind = "Pen / drawing",
                    CvrPath = "no direct CVR equivalent in-world — CVR ships pens as spawnable props instead",
                };

            if (Has("portal"))
                return new Intent
                {
                    Kind = "World portal",
                    CvrPath = "CVRPortalMarker — world IDs differ between platforms, re-link the destination",
                };

            if (Has("postprocess", "post_process", "ppvolume", "skybox", "weather", "fog"))
                return new Intent
                {
                    Kind = "Ambience system",
                    CvrPath = "usually keeps working via the underlying Unity components once the Udon layer is stripped — verify visually",
                };

            return new Intent
            {
                Kind = "Custom logic",
                CvrPath = "manual recreation — CVRInteractable actions cover buttons/toggles/teleports; complex game logic needs CVR's scripting",
            };
        }
    }
}
