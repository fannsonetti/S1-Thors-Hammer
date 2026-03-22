#if IL2CPP
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Weather;
#else
using ScheduleOne.Effects;
using ScheduleOne.NPCs;
using ScheduleOne.Weather;
#endif

using MelonLoader;
using System.Collections;
using UnityEngine;

namespace ThorHammer;

/// <summary>
/// Lightning effect helpers. Kept in a non-injected type so the IEnumerator
/// coroutine does not crash Il2CppInterop during type registration.
/// </summary>
internal static class LightningHelper
{
    private static VFXEffectHandler _lightningVFX;
    private static bool _searched;

    /// <summary>Clears the electrify effect from an NPC after a delay.</summary>
    internal static IEnumerator ClearElectrifyCoroutine(NPC npc)
    {
        yield return new WaitForSeconds(4f);
        if (npc != null && npc.Avatar != null)
            Electrifying.ClearFromAvatar(npc.Avatar);
    }

    /// <summary>Strikes the game's real lightning VFX at the given position.</summary>
    internal static void StrikeLightning(Vector3 position)
    {
        EnsureVFX();
        if (_lightningVFX == null) return;
        _lightningVFX.SetPosition(position);
        _lightningVFX.Activate();
        _lightningVFX.DelayDeactivate(2f);
    }

    /// <summary>
    /// Lazily finds and clones the Lightning VFXEffectHandler from the game's
    /// ThunderController. The clone lives in a standalone active hierarchy
    /// (DontDestroyOnLoad) so it renders even when there is no active storm.
    /// </summary>
    private static void EnsureVFX()
    {
        if (_searched) return;
        _searched = true;

        // Search all ThunderControllers including inactive/dormant weather volumes
        var all = Resources.FindObjectsOfTypeAll<ThunderController>();
        ThunderController tc = all.Length > 0 ? all[0] : null;

        if (tc == null)
        {
            Melon<Core>.Logger.Warning("No ThunderController found — lightning VFX unavailable");
            return;
        }

        // Find the original Lightning VFXEffectHandler.
        // _lightningEffect is null when Awake() hasn't run (no storm active),
        // so also search the serialized visualEffects list.
        VFXEffectHandler original = null;
#if IL2CPP
        original = tc._lightningEffect;
        if (original == null && tc.visualEffects != null)
        {
            foreach (var vfx in tc.visualEffects)
            {
                if (vfx != null && vfx.Id == "Lightning")
                {
                    original = vfx;
                    break;
                }
            }
        }
#else
        var field = typeof(ThunderController).GetField("_lightningEffect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        original = field?.GetValue(tc) as VFXEffectHandler;
        if (original == null)
        {
            var listField = typeof(WeatherEffectController).GetField("visualEffects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (listField?.GetValue(tc) is System.Collections.IList list)
            {
                foreach (var item in list)
                {
                    if (item is VFXEffectHandler vfx && vfx.Id == "Lightning")
                    {
                        original = vfx;
                        break;
                    }
                }
            }
        }
#endif

        if (original == null)
        {
            Melon<Core>.Logger.Warning("No lightning VFX found on ThunderController");
            return;
        }

        // Clone into a standalone active hierarchy so Activate() actually renders.
        // The original sits under ThunderController which is inactive when there's no storm.
        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        clone.name = "ThorLightningVFX";
        clone.SetActive(true);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        _lightningVFX = clone.GetComponent<VFXEffectHandler>();
        if (_lightningVFX != null)
        {
            _lightningVFX.Deactivate();
            Melon<Core>.Logger.Msg("Cloned game lightning VFX for ThorHammer.");
        }
        else
        {
            Melon<Core>.Logger.Warning("Cloned lightning VFX has no VFXEffectHandler component");
            UnityEngine.Object.Destroy(clone);
        }
    }
}
