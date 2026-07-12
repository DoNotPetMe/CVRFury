using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace CVRFury.Builder
{
    /// <summary>
    /// Crash-proofs the CCK's pre-build events against UNGUARDED third-party hooks.
    ///
    /// Anyone can AddListener on <c>CCK_BuildUtility.PreAvatarBundleEvent</c>, and one uncaught exception
    /// from any listener aborts the entire upload ("Error occurred during PreBuildEvent: …"). Confirmed in
    /// the wild: Poiyomi/Thry's <c>AbiAutoLock</c> subscribes with NO try/catch and runs the whole
    /// shader-lock parser over every material and animation clip on the avatar — when that throws (it is
    /// content-dependent on the avatar's clips), the upload dies even though nothing is wrong with the
    /// avatar. CVRFury and Thry's own AbiAutoAnchor guard themselves; AbiAutoLock does not.
    ///
    /// This rewraps every foreign runtime listener in a try/catch that LOGS the failure and lets the upload
    /// continue. When the foreign hook works, it works exactly as before. Everything here is reflection into
    /// UnityEvent internals (stable since Unity 4), fully guarded — on any surprise it does nothing.
    /// </summary>
    internal static class HookSandbox
    {
        public static void SandboxForeignListeners(object unityEvent, string eventLabel)
        {
            try
            {
                var callsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.NonPublic | BindingFlags.Instance);
                var calls = callsField?.GetValue(unityEvent);
                var runtimeField = calls?.GetType().GetField("m_RuntimeCalls", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(runtimeField?.GetValue(calls) is IList runtimeCalls)) return;

                var toWrap = new System.Collections.Generic.List<(object call, Delegate del, string owner)>();
                foreach (var call in runtimeCalls)
                {
                    var del = ExtractDelegate(call);
                    var owner = del?.Method?.DeclaringType;
                    if (owner == null) continue;
                    if (owner.Assembly == typeof(HookSandbox).Assembly) continue;      // CVRFury's own
                    var fn = owner.FullName ?? "";
                    if (fn.StartsWith("CVRFury") || fn.StartsWith("ABI.CCK") || fn.StartsWith("CVR.")) continue;
                    toWrap.Add((call, del, fn));
                }

                foreach (var (call, del, owner) in toWrap)
                {
                    runtimeCalls.Remove(call);
                    var captured = del;
                    var name = owner;
                    UnityAction<GameObject> safe = go =>
                    {
                        try { captured.DynamicInvoke(go); }
                        catch (Exception ex)
                        {
                            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                            Debug.LogError($"[CVRFury] Third-party upload hook '{name}' threw " +
                                           $"{inner.GetType().Name}: {inner.Message} — sandboxed, so YOUR UPLOAD " +
                                           "CONTINUES. (Uncaught, this exception aborts the whole CCK upload — " +
                                           "the 'Error occurred during PreBuildEvent' failure.) If the hook is " +
                                           "Thry/Poiyomi AutoLock, lock your materials manually or update Poiyomi.");
                        }
                    };
                    if (Reflect.AddUnityEventListener(unityEvent, safe))
                        Debug.Log($"[CVRFury] Sandboxed third-party {eventLabel} upload hook: {name} " +
                                  "(its crashes can no longer abort uploads).");
                    else
                        runtimeCalls.Add(call); // couldn't re-add safely — restore the original untouched
                }
            }
            catch (Exception e)
            {
                if (CVRFurySettings.VerboseLogging)
                    Debug.Log("[CVRFury] Hook sandbox skipped: " + e.Message);
            }
        }

        // The delegate lives on a field of the InvokableCall (name varies by Unity version) — find it by type.
        private static Delegate ExtractDelegate(object invokableCall)
        {
            if (invokableCall == null) return null;
            for (var t = invokableCall.GetType(); t != null; t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType))
                        if (f.GetValue(invokableCall) is Delegate d) return d;
            return null;
        }
    }
}
