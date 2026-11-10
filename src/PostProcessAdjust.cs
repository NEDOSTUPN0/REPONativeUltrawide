using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// REPO's "old TV" post-processing (a PPv2 <c>LensDistortion</c> and <c>ChromaticAberration</c>)
    /// is applied in normalized UV space and is radial, so on a wide screen the edges/corners get
    /// far more curvature and colour fringing than at 16:9 — and the mod pushes UI right into those
    /// corners, hurting readability. Both the world and the HUD overlay cameras have their own
    /// effects, so we scale them down across every loaded instance in proportion to the aspect
    /// ratio, bringing the edge look back to roughly its 16:9 strength.
    ///
    /// Optionally both effects can be switched off on the HUD. The game's own "Lens Effect" and
    /// "Chromatic Aberration" settings only reach the world volume (PostProcessing.Instance), so
    /// the HUD overlay volume would otherwise always keep its distortion.
    /// </summary>
    internal static class PostProcessAdjust
    {
        private const float BaseAspect = 16f / 9f;

        private static readonly Dictionary<LensDistortion, float> _baseLensX = new();
        private static readonly Dictionary<ChromaticAberration, float> _baseCa = new();
        private static LensDistortion[]? _lenses;
        private static ChromaticAberration[]? _aberrations;

        // The HUD overlay's volume (on its own layer, read only by the HUD camera).
        private static PostProcessVolume? _hudVolume;

        // HUD effects we deactivated, so that only those are turned back on when the option is cleared.
        private static readonly HashSet<PostProcessEffectSettings> _disabledByUs = new();

        internal static void Tick(bool enabled, bool disableHudEffects)
        {
            if (_lenses == null || HasNull(_lenses))
                _lenses = Resources.FindObjectsOfTypeAll<LensDistortion>();
            if (_aberrations == null || HasNull(_aberrations))
                _aberrations = Resources.FindObjectsOfTypeAll<ChromaticAberration>();

            UpdateHudEffects(disableHudEffects);

            float aspect = (float)Screen.width / Screen.height;
            bool active = enabled && aspect > BaseAspect + 0.001f;

            // Cancels the wider/taller framing so the edge effect matches its 16:9 strength.
            float factor = BaseAspect / aspect;

            // Lens distortion: only the horizontal component is over-amplified on ultrawide.
            foreach (var lens in _lenses)
            {
                if (lens == null)
                    continue;
                if (!_baseLensX.TryGetValue(lens, out var baseX))
                {
                    baseX = lens.intensityX.value;
                    _baseLensX[lens] = baseX;
                }
                float target = active ? baseX * factor : baseX;
                if (!Mathf.Approximately(lens.intensityX.value, target))
                {
                    lens.intensityX.value = target;
                    lens.intensityX.overrideState = true;
                }
            }

            // Chromatic aberration is radial: reduce intensity so corner fringing matches 16:9.
            foreach (var ca in _aberrations)
            {
                if (ca == null)
                    continue;
                if (!_baseCa.TryGetValue(ca, out var baseCa))
                {
                    baseCa = ca.intensity.value;
                    _baseCa[ca] = baseCa;
                }
                float target = active ? baseCa * factor : baseCa;
                if (!Mathf.Approximately(ca.intensity.value, target))
                {
                    ca.intensity.value = target;
                    ca.intensity.overrideState = true;
                }
            }
        }

        private static void UpdateHudEffects(bool disable)
        {
            if (disable)
            {
                var profile = HudProfile();
                if (profile == null)
                    return;
                // Enforced every frame in case anything re-activates them.
                Deactivate(profile.GetSetting<LensDistortion>());
                Deactivate(profile.GetSetting<ChromaticAberration>());
                return;
            }

            if (_disabledByUs.Count == 0)
                return;
            foreach (var effect in _disabledByUs)
            {
                if (effect != null)
                    effect.active = true;
            }
            _disabledByUs.Clear();
        }

        private static void Deactivate(PostProcessEffectSettings? effect)
        {
            if (effect == null || !effect.active)
                return;
            effect.active = false;
            _disabledByUs.Add(effect);
        }

        // The HUD volume is the global one on a different layer than the world volume the game
        // drives. Read its profile without instantiating a copy, like the scaling above does.
        private static PostProcessProfile? HudProfile()
        {
            if (_hudVolume == null)
            {
                var world = PostProcessing.Instance != null ? PostProcessing.Instance.volume : null;
                if (world == null)
                    return null;
                foreach (var v in Resources.FindObjectsOfTypeAll<PostProcessVolume>())
                {
                    if (v != null && v != world && v.isGlobal && v.gameObject.scene.IsValid()
                        && v.gameObject.layer != world.gameObject.layer)
                    {
                        _hudVolume = v;
                        break;
                    }
                }
                if (_hudVolume == null)
                    return null;
            }
            return _hudVolume.HasInstantiatedProfile() ? _hudVolume.profile : _hudVolume.sharedProfile;
        }

        private static bool HasNull<T>(T[] arr) where T : Object
        {
            foreach (var x in arr)
                if (x == null)
                    return true;
            return false;
        }
    }
}
