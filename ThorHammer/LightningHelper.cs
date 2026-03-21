#if IL2CPP
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.NPCs;
#else
using ScheduleOne.Effects;
using ScheduleOne.NPCs;
#endif

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThorHammer;

/// <summary>
/// Lightning effect helpers. Kept in a non-injected type so the IEnumerator
/// coroutine does not crash Il2CppInterop during type registration.
/// </summary>
internal static class LightningHelper
{
    internal static IEnumerator BurstCoroutine(Vector3 target, NPC npc, Vector3 origin,
        int boltCount, float interval)
    {
        for (int i = 0; i < boltCount; i++)
        {
            CreateBolt(origin, target);
            if (i < boltCount - 1)
                yield return new WaitForSeconds(interval);
        }

        yield return new WaitForSeconds(4f);
        if (npc != null && npc.Avatar != null)
            Electrifying.ClearFromAvatar(npc.Avatar);
    }

    internal static void CreateBolt(Vector3 start, Vector3 end)
    {
        var boltGo = new GameObject("ThorLightningBolt");
        var lr = boltGo.AddComponent<LineRenderer>();

        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        var mat = new Material(shader);
        mat.color = new Color(0.6f, 0.75f, 1f, 1f);
        lr.material = mat;

        lr.startWidth = 0.2f;
        lr.endWidth = 0.06f;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;

        int segments = 14;
        lr.positionCount = segments + 1;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 pos = Vector3.Lerp(start, end, t);
            if (i > 0 && i < segments)
            {
                pos.x += UnityEngine.Random.Range(-1.5f, 1.5f);
                pos.y += UnityEngine.Random.Range(-0.5f, 0.5f);
                pos.z += UnityEngine.Random.Range(-1.5f, 1.5f);
            }
            lr.SetPosition(i, pos);
        }

        UnityEngine.Object.Destroy(boltGo, 0.15f);
    }
}
