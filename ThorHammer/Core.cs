#if IL2CPP
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.Persistence;
#endif

using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;
using S1API.Items;
using S1API.Shops;
using S1MAPI.Gltf;
using S1MAPI.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(ThorHammer.Core), "Mjolnir", "1.0.0", "hdlmrell", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ThorHammer;

public class Core : MelonMod
{
    private static readonly string[] HardwareShopNames =
        { "Handy Hank's Hardware", "Dan's Hardware" };

    private bool _itemsRegistered;
    private bool _loadHooked;
    private static ItemDefinition _hammerDef;
    private static Sprite _cachedIcon;

    // Config
    private static MelonPreferences_Entry<string> _lightningKeyEntry;
    /// <summary>
    /// The configured key for summoning lightning from the hammer.
    /// </summary>
    public static KeyCode LightningKey { get; private set; } = KeyCode.X;

    private static string IconPath =>
        Path.Combine(MelonEnvironment.UserDataDirectory, "S1API", "Icons", "ThorHammer.png");

    /// <inheritdoc />
    public override void OnInitializeMelon()
    {
#if IL2CPP
        ClassInjector.RegisterTypeInIl2Cpp<HammerEquippable>();
#endif

        var config = MelonPreferences.CreateCategory("Mjolnir", "Mjolnir Settings");
        _lightningKeyEntry = config.CreateEntry("LightningKey", "X", "Lightning Key",
            "Key to summon lightning from the hammer (e.g. X, F, G, T)");

        if (Enum.TryParse<KeyCode>(_lightningKeyEntry.Value, true, out var key))
            LightningKey = key;
        else
            LoggerInstance.Warning($"Invalid lightning key '{_lightningKeyEntry.Value}', defaulting to X");

        LoggerInstance.Msg("Initialized.");
    }

    /// <inheritdoc />
    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (sceneName == "Main" && !_itemsRegistered)
        {
            _itemsRegistered = true;
            RegisterItems();
        }

        if (sceneName == "Main" && !_loadHooked)
        {
#if IL2CPP
            var lm = Singleton<LoadManager>.Instance;
            if (lm != null)
            {
                lm.onLoadComplete.AddListener((UnityEngine.Events.UnityAction)OnGameLoaded);
                _loadHooked = true;
            }
#else
            var lm = LoadManager.Instance;
            if (lm != null)
            {
                lm.onLoadComplete.AddListener(OnGameLoaded);
                _loadHooked = true;
            }
#endif
        }
    }

    /// <inheritdoc />
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (sceneName == "Main")
        {
            _loadHooked = false;
        }
    }

    private void RegisterItems()
    {
        var equippable = ItemCreator.CreateEquippableBuilder()
            .CreateEquippable<HammerEquippable>("ThorHammerEquippable")
            .WithInteraction(canInteract: true, canPickup: true)
            .Build();

        _hammerDef = ItemCreator.CreateBuilder()
            .WithBasicInfo(
                id: "thor_hammer",
                name: "Mjolnir",
                description: "Mjolnir. Whosoever holds this hammer, if they be worthy, shall possess the power of Thor.",
                category: ItemCategory.Tools)
            .WithStackLimit(1)
            .WithPricing(500f, 0.5f)
            .WithLegalStatus(LegalStatus.Legal)
            .WithEquippable(equippable)
            .Build();

        RenderHammerIcon();

        LoggerInstance.Msg("Mjolnir registered.");
    }

    private void RenderHammerIcon()
    {
        if (_cachedIcon != null)
        {
            _hammerDef.Icon = _cachedIcon;
            return;
        }

        string path = IconPath;

        // Try loading from disk cache
        if (File.Exists(path))
        {
            try
            {
                byte[] pngData = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(pngData))
                {
                    _cachedIcon = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _cachedIcon.name = "ThorHammerIcon";
                    _hammerDef.Icon = _cachedIcon;
                    LoggerInstance.Msg("Loaded hammer icon from cache.");
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to load cached icon, re-rendering: {ex.Message}");
            }
        }

        // Render the icon from the GLB model
        GameObject stageRoot = null;
        Camera cam = null;
        RenderTexture rt = null;
        try
        {
            stageRoot = new GameObject("TH_IconStage");
            stageRoot.transform.position = new Vector3(0f, 5000f, 0f);

            // Load hammer model
            byte[] glbData = EmbeddedResourceLoader.LoadBytes(
                "ThorHammer.Resources.ThorHammer.glb",
                Assembly.GetExecutingAssembly());
            if (glbData == null)
            {
                LoggerInstance.Warning("Cannot render icon: GLB resource not found");
                return;
            }

            var hammer = GltfLoader.LoadGlb(glbData);
            if (hammer == null)
            {
                LoggerInstance.Warning("Cannot render icon: GLB load failed");
                return;
            }

            hammer.transform.SetParent(stageRoot.transform, false);
            hammer.transform.localPosition = Vector3.zero;
            hammer.transform.localRotation = Quaternion.Euler(0f, 135f, -30f);
            hammer.transform.localScale = Vector3.one * 0.5f;

            // Directional light
            var lightGo = new GameObject("TH_IconLight");
            lightGo.transform.SetParent(stageRoot.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Fill light
            var fillGo = new GameObject("TH_IconFill");
            fillGo.transform.SetParent(stageRoot.transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.8f, 0.85f, 0.95f);
            fill.intensity = 0.5f;
            fillGo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

            // Camera — isometric product shot
            var camGo = new GameObject("TH_IconCamera");
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.8f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = ~0;
            cam.enabled = false;

            camGo.transform.position = stageRoot.transform.position + new Vector3(-1.5f, 1.5f, 1.5f);
            camGo.transform.LookAt(stageRoot.transform.position);

            // Render to texture
            rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            // Read pixels
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // Save to disk cache
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to save icon to disk: {ex.Message}");
            }

            // Create sprite
            _cachedIcon = Sprite.Create(tex,
                new Rect(0, 0, 256, 256),
                new Vector2(0.5f, 0.5f), 100f);
            _cachedIcon.name = "ThorHammerIcon";
            _hammerDef.Icon = _cachedIcon;

            LoggerInstance.Msg("Rendered hammer icon.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"RenderHammerIcon failed: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            if (stageRoot != null) UnityEngine.Object.Destroy(stageRoot);
        }
    }

    private void OnGameLoaded()
    {
        if (_hammerDef == null) return;

        int added = ShopManager.AddToShops(_hammerDef, HardwareShopNames);
        LoggerInstance.Msg($"Mjolnir added to {added} hardware store(s).");
    }
}
