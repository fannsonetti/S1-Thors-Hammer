#if IL2CPP
using Il2CppScheduleOne;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.FX;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Noise;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vision;
using Il2CppScheduleOne.Weather;
#else
using ScheduleOne;
using ScheduleOne.Audio;
using ScheduleOne.Combat;
using ScheduleOne.DevUtilities;
using ScheduleOne.Effects;
using ScheduleOne.Equipping;
using ScheduleOne.FX;
using ScheduleOne.ItemFramework;
using ScheduleOne.Noise;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Vision;
using ScheduleOne.Weather;
#endif

using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using S1MAPI.Gltf;
using S1MAPI.Utils;
using System.Reflection;

namespace ThorHammer;

public class HammerEquippable : Equippable_Viewmodel
{
#if IL2CPP
    public HammerEquippable(IntPtr ptr) : base(ptr) { }
#endif

    // ── Melee stats ──
    private const float Range = 1.5f;
    private const float HitRadius = 0.3f;
    private const float SwingCooldown = 0.25f;
    private const float SwingDuration = 0.15f;
    private const float SwingAngle = 70f;
    private const float HitTime = 0.06f;

    // ── Wind-up (right-click hold) ──
    private const float WindUpMaxSpinSpeed = 5400f;
    private const float AimFOVReduction = 15f;
    private const float AimZoomDuration = 0.2f;

    // ── Lightning zap ──
    private static float LightningRange => Core.MaxThrowRange * 0.7f;
    private const float LightningAimRadius = 0.5f;

    // ── Flight ──
    private const float FlightGracePeriod = 0.5f;

    // ── Throw ──
    private const float ThrowHitRadius = 0.3f;
    private const float ReturnSpeed = 45f;
    private const float ReturnCatchDistance = 0.5f;
    private const float SpinSpeed = 1440f;

    // ── State ──
    private enum HammerState { Idle, WindingUp, FlyingOut, FlyingBack, Flying }
    private HammerState _state = HammerState.Idle;

    // Melee swing
    private float _cooldownRemaining;
    private bool _isSwinging;
    private float _swingElapsed;
    private bool _hitChecked;

    // Model
    private GameObject _hammerModel;
    private Quaternion _modelBaseRotation;

    // Wind-up / aim
    private float _windUpElapsed;
    private bool _fovOverridden;
    private bool _playerCharged;

    // Throw projectile
    private GameObject _projectile;
    private Vector3 _throwDirection;
    private float _throwDistance;

    // Flight
    private float _savedGravityMultiplier = 1f;
    private float _flightStartTime;

    // Audio
    private AudioClip _thunderClip;
    private bool _thunderClipSearched;

    /// <inheritdoc />
    public override void Equip(ItemInstance item)
    {
        gameObject.SetActive(true);

        localPosition = new Vector3(0.35f, -0.3f, 0.5f);
        localEulerAngles = new Vector3(0f, 0f, 0f);
        localScale = Vector3.one;

        LoadHammerModel();

#if IL2CPP
        // Inline base.Equip() — IL2CPP wrappers use il2cpp_object_get_virtual_method
        // which dispatches back to this override, causing infinite recursion.

        // Equippable.Equip:
        itemInstance = item;
        PlayerSingleton<PlayerInventory>.Instance.SetEquippable(this);
        PlayerSingleton<PlayerInventory>.Instance.EquippedSlotChanged();

        // Equippable_Viewmodel.Equip (continued):
        transform.localPosition = localPosition;
        transform.localEulerAngles = localEulerAngles;
        transform.localScale = localScale;
        LayerUtility.SetLayerRecursively(gameObject, LayerMask.NameToLayer("Viewmodel"));
        var renderers = gameObject.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            if (mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                mr.enabled = false;
            else
                mr.shadowCastingMode = ShadowCastingMode.Off;
        }
#else
        base.Equip(item);
#endif
    }

    /// <inheritdoc />
    public override void Unequip()
    {
        if (_state == HammerState.Flying)
            StopFlight();
        if (_state == HammerState.WindingUp)
            CancelWindUp();
        CleanupThrow();
        ClearPlayerCharged();

#if IL2CPP
        // Inline base.Unequip() — same virtual dispatch recursion issue.
        // Equippable.Unequip:
        PlayerSingleton<PlayerInventory>.Instance.SetEquippable(null);
        PlayerSingleton<PlayerInventory>.Instance.EquippedSlotChanged();
        UnityEngine.Object.Destroy(gameObject);
#else
        base.Unequip();
#endif
    }

    private void LoadHammerModel()
    {
        var glbData = EmbeddedResourceLoader.LoadBytes(
            "ThorHammer.Resources.ThorHammer.glb",
            Assembly.GetExecutingAssembly());

        if (glbData == null)
        {
            Melon<Core>.Logger.Error("Failed to load ThorHammer.glb from embedded resources");
            return;
        }

        _hammerModel = GltfLoader.LoadGlb(glbData);
        if (_hammerModel == null)
        {
            Melon<Core>.Logger.Error("GltfLoader.LoadGlb returned null");
            return;
        }

        _hammerModel.transform.SetParent(transform, false);
        _hammerModel.transform.localPosition = new Vector3(0.02f, -0.15f, 0.05f);
        _hammerModel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        _hammerModel.transform.localScale = Vector3.one * 0.08f;
        _modelBaseRotation = _hammerModel.transform.localRotation;
    }

#if IL2CPP
    public
#else
    protected
#endif
    override void Update()
    {
#if !IL2CPP
        base.Update(); // IL2CPP: skipped — base is empty and virtual dispatch would recurse
#endif

        if (_cooldownRemaining > 0f)
            _cooldownRemaining -= Time.deltaTime;

        switch (_state)
        {
            case HammerState.Idle:
                UpdateSwingAnimation();
                UpdateIdle();
                break;
            case HammerState.WindingUp:
                UpdateWindUp();
                break;
            case HammerState.FlyingOut:
                UpdateFlyingOut();
                break;
            case HammerState.FlyingBack:
                UpdateFlyingBack();
                break;
            case HammerState.Flying:
                UpdateFlying();
                break;
        }
    }

    // ════════════════════════════════════════════
    //  IDLE (melee, start wind-up, lightning zap)
    // ════════════════════════════════════════════

    private void UpdateIdle()
    {
        bool canInteract = !GameInput.IsTyping &&
            PlayerSingleton<PlayerCamera>.Instance.activeUIElementCount == 0;

        // Right-click → start wind-up spin
        if (canInteract && GameInput.GetButtonDown(GameInput.ButtonCode.SecondaryClick))
        {
            StartWindUp();
            return;
        }

        // Lightning zap (configurable key, default X)
        if (canInteract && Input.GetKeyDown(Core.LightningKey))
        {
            if (Core.StaminaEnabled &&
                PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve < Core.LightningStaminaCost)
                return;
            if (Core.StaminaEnabled)
                PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.LightningStaminaCost);
            TryLightningZap();
        }

        // Left-click melee
        if (canInteract && !_isSwinging && _cooldownRemaining <= 0f &&
            GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick))
        {
            if (Core.StaminaEnabled &&
                PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve < Core.SwingStaminaCost)
                return;
            if (Core.StaminaEnabled)
                PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.SwingStaminaCost);
            StartSwing();
        }
    }

    // ════════════════════════════════════════════
    //  MELEE SWING
    // ════════════════════════════════════════════

    private void StartSwing()
    {
        _isSwinging = true;
        _swingElapsed = 0f;
        _hitChecked = false;
        _cooldownRemaining = SwingCooldown;
    }

    private void UpdateSwingAnimation()
    {
        if (!_isSwinging || _hammerModel == null)
            return;

        _swingElapsed += Time.deltaTime;
        float t = _swingElapsed / SwingDuration;

        if (t >= 1f)
        {
            _isSwinging = false;
            _hammerModel.transform.localRotation = _modelBaseRotation;
            return;
        }

        float angle = Mathf.Sin(t * Mathf.PI) * SwingAngle;
        _hammerModel.transform.localRotation = Quaternion.Euler(angle, 0f, 0f) * _modelBaseRotation;

        if (!_hitChecked && _swingElapsed >= HitTime)
        {
            _hitChecked = true;
            ExecuteMeleeHit();
        }
    }

    private void ExecuteMeleeHit()
    {
        if (!PlayerSingleton<PlayerCamera>.Instance.LookRaycast(
                Range, out var hit,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                includeTriggers: true, HitRadius))
        {
            return;
        }

        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        float force = Core.MeleeForce;

        var impact = new Impact(
            hit.point,
            PlayerSingleton<PlayerCamera>.Instance.transform.forward,
            force, Core.MeleeDamage,
            EImpactType.BluntMetal,
            Player.Local.NetworkObject,
            UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        damageable.SendImpact(impact);
        Singleton<FXManager>.Instance.CreateImpactFX(impact, damageable);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.3f, 0.2f);

        // Lightning on NPC melee hits
        var npc = hit.collider.GetComponentInParent<NPC>();
        if (npc != null)
        {
            StrikeLightningOnNPC(npc);
            Player.Local.VisualState.ApplyState("melee_attack", EVisualState.Brandishing, 2.5f);
        }
    }

    // ════════════════════════════════════════════
    //  WIND-UP (right-click hold → spin → throw or fly)
    // ════════════════════════════════════════════

    private void StartWindUp()
    {
        _state = HammerState.WindingUp;
        _windUpElapsed = 0f;
        _isSwinging = false;

        // Block jumping so space doesn't trigger a jump
        PlayerSingleton<PlayerMovement>.Instance.CanJump = false;

        // Zoom in
        float aimFov = Singleton<Settings>.Instance.CameraFOV - AimFOVReduction;
        PlayerSingleton<PlayerCamera>.Instance.OverrideFOV(aimFov, AimZoomDuration);
        _fovOverridden = true;
    }

    private void UpdateWindUp()
    {
        _windUpElapsed += Time.deltaTime;
        bool charged = _windUpElapsed >= Core.WindUpDuration;

        // Stamina drain during wind-up
        if (Core.StaminaEnabled)
        {
            PlayerSingleton<PlayerMovement>.Instance.ChangeStamina(-Core.WindUpStaminaRate * Time.deltaTime);
            if (PlayerSingleton<PlayerMovement>.Instance.CurrentStaminaReserve <= 0f)
            {
                CancelWindUp();
                return;
            }
        }

        // Accelerating spin
        float t = Mathf.Clamp01(_windUpElapsed / Core.WindUpDuration);
        float spinSpeed = t * t * WindUpMaxSpinSpeed;
        if (_hammerModel != null)
            _hammerModel.transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);

        // Electrify player when fully charged
        if (charged && !_playerCharged)
        {
            _playerCharged = true;
            Electrifying.ApplyToAvatar(Player.Local.Avatar);
        }

        // Right-click released
        if (!GameInput.GetButton(GameInput.ButtonCode.SecondaryClick))
        {
            if (charged)
                StartThrow();
            else
                CancelWindUp();
            return;
        }

        // Space while charged → fly
        if (charged && GameInput.GetButton(GameInput.ButtonCode.Jump))
        {
            StartFlight();
        }
    }

    private void CancelWindUp()
    {
        _state = HammerState.Idle;
        _windUpElapsed = 0f;
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;
        ClearPlayerCharged();

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(AimZoomDuration);
            _fovOverridden = false;
        }

        if (_hammerModel != null)
            _hammerModel.transform.localRotation = _modelBaseRotation;
    }

    private void ClearPlayerCharged()
    {
        if (_playerCharged)
        {
            _playerCharged = false;
            Electrifying.ClearFromAvatar(Player.Local.Avatar);
        }
    }

    // ════════════════════════════════════════════
    //  THROW
    // ════════════════════════════════════════════

    private void StartThrow()
    {
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;
        ClearPlayerCharged();

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(AimZoomDuration);
            _fovOverridden = false;
        }

        if (_hammerModel == null) return;

        _hammerModel.transform.localRotation = _modelBaseRotation;
        _hammerModel.SetActive(false);

        _projectile = UnityEngine.Object.Instantiate(_hammerModel);
        _projectile.SetActive(true);
        _projectile.transform.SetParent(null, false);
        SetLayerRecursive(_projectile, 0);

        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        _projectile.transform.position = cam.position + cam.forward * 1.0f;
        _projectile.transform.rotation = Quaternion.LookRotation(cam.forward) * Quaternion.Euler(0f, 90f, 0f);
        _projectile.transform.localScale = Vector3.one * 0.08f;

        _throwDirection = cam.forward;
        _throwDistance = 0f;

        _state = HammerState.FlyingOut;
    }

    private void UpdateFlyingOut()
    {
        if (_projectile == null)
        {
            CatchHammer();
            return;
        }

        float step = Core.ThrowSpeed * Time.deltaTime;
        _throwDistance += step;

        _projectile.transform.position += _throwDirection * step;
        _projectile.transform.Rotate(Vector3.forward, SpinSpeed * Time.deltaTime, Space.Self);

        if (Physics.SphereCast(
                _projectile.transform.position - _throwDirection * step,
                ThrowHitRadius,
                _throwDirection,
                out var hit,
                step,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                QueryTriggerInteraction.Collide))
        {
            ExecuteThrowHit(hit);
            _state = HammerState.FlyingBack;
            return;
        }

        if (_throwDistance >= Core.MaxThrowRange)
        {
            _state = HammerState.FlyingBack;
        }
    }

    private void UpdateFlyingBack()
    {
        if (_projectile == null)
        {
            CatchHammer();
            return;
        }

        var target = PlayerSingleton<PlayerCamera>.Instance.transform.position;
        var dir = (target - _projectile.transform.position).normalized;
        float step = ReturnSpeed * Time.deltaTime;

        _projectile.transform.position += dir * step;
        _projectile.transform.Rotate(Vector3.forward, SpinSpeed * Time.deltaTime, Space.Self);

        if (Vector3.Distance(_projectile.transform.position, target) < ReturnCatchDistance)
        {
            CatchHammer();
        }
    }

    private void ExecuteThrowHit(RaycastHit hit)
    {
        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            var impact = new Impact(
                hit.point,
                _throwDirection,
                Core.ThrowForce, Core.ThrowDamage,
                EImpactType.BluntMetal,
                Player.Local.NetworkObject,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue));

            damageable.SendImpact(impact);
            Singleton<FXManager>.Instance.CreateImpactFX(impact, damageable);
        }

        var npc = hit.collider.GetComponentInParent<NPC>();
        if (npc != null)
        {
            StrikeLightningOnNPC(npc);
            Player.Local.VisualState.ApplyState("melee_attack", EVisualState.Brandishing, 2.5f);
        }

        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.5f, 0.3f);
    }

    private void CatchHammer()
    {
        if (_projectile != null)
        {
            UnityEngine.Object.Destroy(_projectile);
            _projectile = null;
        }

        if (_hammerModel != null)
            _hammerModel.SetActive(true);

        _state = HammerState.Idle;
    }

    private void CleanupThrow()
    {
        if (_projectile != null)
        {
            UnityEngine.Object.Destroy(_projectile);
            _projectile = null;
        }

        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(0f);
            _fovOverridden = false;
        }

        if (_hammerModel != null)
        {
            _hammerModel.SetActive(true);
            _hammerModel.transform.localRotation = _modelBaseRotation;
        }

        _state = HammerState.Idle;
    }

    // ════════════════════════════════════════════
    //  LIGHTNING
    // ════════════════════════════════════════════

    // Lightning bolt FROM hammer TO aimed point (damages NPCs if hit)
    private void TryLightningZap()
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance;
        Vector3 targetPoint;
        NPC npc = null;

        if (cam.LookRaycast(LightningRange, out var hit,
                NetworkSingleton<CombatManager>.Instance.MeleeLayerMask,
                includeTriggers: true, LightningAimRadius))
        {
            targetPoint = hit.point;
            npc = hit.collider.GetComponentInParent<NPC>();

            if (npc != null)
            {
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    var impact = new Impact(
                        hit.point,
                        cam.transform.forward,
                        Core.LightningForce, Core.LightningDamage,
                        EImpactType.BluntMetal,
                        Player.Local.NetworkObject,
                        UnityEngine.Random.Range(int.MinValue, int.MaxValue));
                    damageable.SendImpact(impact);
                }
                Electrifying.ApplyToAvatar(npc.Avatar);
            }
        }
        else
        {
            // No hit — strike at max range in look direction
            targetPoint = cam.transform.position + cam.transform.forward * LightningRange;
        }

        LightningHelper.StrikeLightning(targetPoint);
        if (npc != null)
            MelonCoroutines.Start(LightningHelper.ClearElectrifyCoroutine(npc));
        PlayThunderSound(targetPoint);
        EmitLightningNoise(targetPoint);
        Player.Local.VisualState.ApplyState("melee_attack", EVisualState.Brandishing, 2.5f);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(0.5f, 0.3f);
    }

    // Lightning from sky to NPC (melee / throw hits)
    private void StrikeLightningOnNPC(NPC npc)
    {
        Vector3 position = npc.transform.position;
        LightningHelper.StrikeLightning(position);
        MelonCoroutines.Start(LightningHelper.ClearElectrifyCoroutine(npc));
        Electrifying.ApplyToAvatar(npc.Avatar);
        PlayThunderSound(position);
        EmitLightningNoise(position);
    }

    private static void EmitLightningNoise(Vector3 position)
    {
        if (Core.LightningPanicRadius > 0f)
            NoiseUtility.EmitNoise(position, ENoiseType.Explosion, Core.LightningPanicRadius, Player.Local.gameObject);
    }

    private void PlayThunderSound(Vector3 position)
    {
        if (!_thunderClipSearched)
        {
            _thunderClipSearched = true;

            // Try to grab a lightning clip from the ThunderController
            var tc = UnityEngine.Object.FindObjectOfType<ThunderController>();
            if (tc == null)
            {
                var all = Resources.FindObjectsOfTypeAll<ThunderController>();
                if (all.Length > 0) tc = all[0];
            }
            if (tc != null)
            {
                try
                {
#if IL2CPP
                    var audio = tc._lightningAudio;
                    if (audio != null && audio.Clips != null && audio.Clips.Count > 0)
                        _thunderClip = audio.Clips[0];
#else
                    var field = typeof(ThunderController).GetField("_lightningAudio",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field?.GetValue(tc) is AudioSourceController asc)
                    {
                        var clipsField = asc.GetType().GetField("Clips",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (clipsField?.GetValue(asc) is AudioClip[] clips && clips.Length > 0)
                            _thunderClip = clips[0];
                    }
#endif
                }
                catch { /* continue to fallback */ }
            }

            // Fallback: search all loaded audio clips for thunder/lightning sound
            if (_thunderClip == null)
            {
                try
                {
                    var allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
                    foreach (var clip in allClips)
                    {
                        if (clip == null) continue;
                        var name = clip.name.ToLowerInvariant();
                        if (name.Contains("thunder") || name.Contains("lightning"))
                        {
                            _thunderClip = clip;
                            break;
                        }
                    }
                }
                catch { /* no fallback available */ }
            }

            if (_thunderClip != null)
                Melon<Core>.Logger.Msg($"Thunder sound found: {_thunderClip.name}");
            else
                Melon<Core>.Logger.Warning("No thunder/lightning AudioClip found in game assets");
        }

        if (_thunderClip != null)
            AudioSource.PlayClipAtPoint(_thunderClip, position, 1f);
    }

    // ════════════════════════════════════════════
    //  FLIGHT (space during charged wind-up)
    // ════════════════════════════════════════════

    private void StartFlight()
    {
        if (_fovOverridden)
        {
            PlayerSingleton<PlayerCamera>.Instance.StopFOVOverride(AimZoomDuration);
            _fovOverridden = false;
        }

        _state = HammerState.Flying;
        _flightStartTime = Time.time;
        // Keep CanJump = false so the game's jump system doesn't interfere
        _savedGravityMultiplier = PlayerMovement.GravityMultiplier;
        PlayerMovement.GravityMultiplier = 0f;

        // Initial upward push to leave the ground
        PlayerSingleton<PlayerMovement>.Instance.Controller.Move(Vector3.up * 0.5f);

        // Point hammer head forward
        if (_hammerModel != null)
            _hammerModel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void UpdateFlying()
    {
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        var pm = PlayerSingleton<PlayerMovement>.Instance;

        // Stamina drain during flight
        if (Core.StaminaEnabled)
        {
            pm.ChangeStamina(-Core.FlightStaminaRate * Time.deltaTime);
            if (pm.CurrentStaminaReserve <= 0f)
            {
                StopFlight();
                return;
            }
        }

        pm.Controller.Move(cam.forward * Core.FlightSpeed * Time.deltaTime);

        // Grace period before checking IsGrounded so we don't stop immediately
        bool graceExpired = Time.time - _flightStartTime > FlightGracePeriod;
        if (!GameInput.GetButton(GameInput.ButtonCode.Jump) || (graceExpired && pm.IsGrounded))
        {
            StopFlight();
        }
    }

    private void StopFlight()
    {
        _state = HammerState.Idle;
        PlayerMovement.GravityMultiplier = _savedGravityMultiplier;
        PlayerSingleton<PlayerMovement>.Instance.CanJump = true;
        ClearPlayerCharged();

        if (_hammerModel != null)
            _hammerModel.transform.localRotation = _modelBaseRotation;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }
}
