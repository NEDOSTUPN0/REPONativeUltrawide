using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// Un-stretches REPO's HUD on ultrawide by widening the whole HUD render layer
    /// horizontally to the screen aspect ratio, so corner-anchored elements spread to
    /// the real screen edges instead of staying in a centered 16:9 box.
    ///
    /// The HUD is rebuilt on scene/spawn changes, so we re-acquire references whenever
    /// they go null and re-apply the sizes every frame (cheap RectTransform writes; the
    /// expensive render-texture resize only runs when its dimensions actually differ).
    /// Runs alongside FovUpdate (which owns the world layer); requires FovUpdate's
    /// DontStretchUI=true so it leaves the overlay alone.
    /// </summary>
    internal static class UltrawideHud
    {
        private const float BaseAspect = 16f / 9f;

        // Cached references; each is re-fetched when it goes null (Unity-null) after a rebuild.
        private static RectTransform? _mainDisplay;  // the Render Texture Main quad (the world view)
        private static RawImage? _overlayDisplay;   // RenderTextureMain.overlayRawImage (the on-screen quad)
        private static RenderTexture? _overlayRt;    // the texture the HUD camera renders into
        private static Camera? _hudCamera;           // "Camera Overlay" rendering into _overlayRt
        private static RectTransform? _gameHud;      // HUD Canvas/HUD/Game Hud (gameplay elements)
        private static RectTransform? _fade;         // FadeOverlay
        private static RectTransform? _vignette;     // HurtVignette
        private static RectTransform? _video;        // VideoOverlay
        private static RectTransform? _loading;      // LoadingUI root
        private static RectTransform? _splash;       // SplashScreenUI (startup studio/warning screen)
        private static Transform? _tutorial;         // TutorialUI (in-game tutorial bar)

        // Original (vanilla 16:9) sizes, captured the first time each rect is seen un-modified.
        private static readonly Dictionary<RectTransform, Vector2> _originalSizes = new();
        private static int _overlayRtBaseHeight;
        private static int _overlayRtBaseWidth;

        // The vanilla pixelation render texture is a fixed 16:9; we stretch its width to the
        // screen aspect via RenderTextureMain's internal originals (no FovUpdate needed).
        private static FieldInfo? _texWidthField;
        private static FieldInfo? _texHeightField;

        private static bool _wasModified;

        // Read by the SemiUI Harmony patch (which runs from the game's Start, not our Tick).
        internal static bool EnabledForPatch;

        internal static void Tick(bool enabled)
        {
            EnabledForPatch = enabled;
            AcquireReferences();

            float aspect = (float)Screen.width / Screen.height;

            // Disabled, or screen not wider than 16:9 → keep vanilla layout.
            if (!enabled || aspect <= BaseAspect + 0.001f)
            {
                if (_wasModified)
                {
                    Restore();
                    _wasModified = false;
                }
                return;
            }

            Apply(aspect);
            _wasModified = true;
        }

        private static void AcquireReferences()
        {
            if (_mainDisplay == null && RenderTextureMain.instance != null)
                _mainDisplay = RenderTextureMain.instance.GetComponent<RectTransform>();

            if (_overlayDisplay == null && RenderTextureMain.instance != null)
                _overlayDisplay = RenderTextureMain.instance.overlayRawImage;

            if (_overlayRt == null && _overlayDisplay != null)
                _overlayRt = _overlayDisplay.texture as RenderTexture;

            if (_overlayRt != null && (_hudCamera == null || _hudCamera.targetTexture != _overlayRt))
                _hudCamera = FindCameraTargeting(_overlayRt);

            if (_gameHud == null && HUDCanvas.instance != null)
                _gameHud = HUDCanvas.instance.transform.Find("HUD/Game Hud") as RectTransform;

            if (_fade == null)
                _fade = GetRect(FindComponent<FadeOverlay>());
            if (_vignette == null)
                _vignette = GetRect(FindComponent<HurtVignette>());
            if (_video == null)
                _video = GetRect(FindComponent<VideoOverlay>());
            if (_loading == null && LoadingUI.instance != null)
                _loading = LoadingUI.instance.GetComponent<RectTransform>();
            if (_splash == null)
                _splash = GetRect(FindComponent<SplashScreenUI>());
            if (_tutorial == null)
                _tutorial = FindComponent<TutorialUI>()?.transform;
        }

        private static void Apply(float aspect)
        {
            float k = aspect / BaseAspect;

            // 1. HUD render texture → wider, so the ortho HUD camera sees a wider area
            //    and the content isn't squished into a 16:9 texture.
            if (_overlayRt != null)
            {
                if (_overlayRtBaseHeight == 0)
                {
                    _overlayRtBaseHeight = _overlayRt.height;
                    _overlayRtBaseWidth = _overlayRt.width;
                }

                int desiredW = Mathf.RoundToInt(_overlayRtBaseHeight * aspect);
                if (_overlayRt.width != desiredW || _overlayRt.height != _overlayRtBaseHeight)
                    ResizeRenderTexture(_overlayRt, desiredW, _overlayRtBaseHeight, _hudCamera);

                // Match the camera aspect to the texture it actually renders into.
                if (_hudCamera != null)
                    _hudCamera.aspect = (float)_overlayRt.width / _overlayRt.height;
            }

            // 1b. World render texture → screen aspect. Vanilla pixelation renders the world into
            //     a fixed 16:9 texture; keeping its height (pixelation density) and widening it to
            //     the screen aspect makes the world fill ultrawide without stretching.
            FixMainTextureAspect(aspect);

            // 2a. World view quad → fill full screen width. With the texture now at screen aspect,
            //     this shows the world un-squished. This makes the mod self-sufficient (no FovUpdate).
            if (_mainDisplay != null)
                SetWidthFill(_mainDisplay, aspect);

            // 2b. HUD overlay quad → fill full screen width. Its aspect now equals the widened
            //     render texture's, so there is no horizontal stretch.
            if (_overlayDisplay != null)
                SetWidthFill(_overlayDisplay.rectTransform, aspect);

            // 3. Game Hud container → wider; corner-anchored elements move to the edges,
            //    keeping the same relative inset they had at 16:9.
            SetWidthScaled(_gameHud, k);

            // 4. Full-screen overlays → cover the new edges (fixes the loading-screen leak).
            SetWidthScaled(_fade, k);
            SetWidthScaled(_vignette, k);
            SetWidthScaled(_video, k);
            SetWidthScaled(_loading, k);
            SetWidthScaled(_splash, k);

            // In-game tutorial: stretch its horizontal bar backgrounds to the full width (their
            // centred content stays put); without this the bar is cut off at 16:9.
            if (_tutorial != null)
            {
                SetWidthScaled(_tutorial.Find("Tutorial Text Container") as RectTransform, k);
                SetWidthScaled(_tutorial.Find("Dummy Text Transform") as RectTransform, k);
            }

            // NOTE: menu repositioning is intentionally not done here. The menu is a per-page
            // system (left-aligned pages vs centred popups), so a blanket MenuHolder shift is
            // wrong. To be designed per page type.
        }

        private static void Restore()
        {
            foreach (var kvp in _originalSizes)
            {
                if (kvp.Key != null)
                    kvp.Key.sizeDelta = kvp.Value;
            }

            RestoreMainTextureAspect();

            if (_overlayRt != null && _overlayRtBaseHeight != 0
                && (_overlayRt.width != _overlayRtBaseWidth || _overlayRt.height != _overlayRtBaseHeight))
            {
                ResizeRenderTexture(_overlayRt, _overlayRtBaseWidth, _overlayRtBaseHeight, _hudCamera);
                if (_hudCamera != null)
                    _hudCamera.aspect = (float)_overlayRt.width / _overlayRt.height;
            }
        }

        // sizeDelta.x = originalWidth * factor (height unchanged)
        private static void SetWidthScaled(RectTransform? rt, float factor)
        {
            if (rt == null)
                return;
            var baseSize = OriginalSize(rt);
            var target = new Vector2(baseSize.x * factor, baseSize.y);
            if (rt.sizeDelta != target)
                rt.sizeDelta = target;
        }

        // sizeDelta.x = originalHeight * aspect (height unchanged) → fills a height-matched canvas
        private static void SetWidthFill(RectTransform rt, float aspect)
        {
            var baseSize = OriginalSize(rt);
            var target = new Vector2(baseSize.y * aspect, baseSize.y);
            if (rt.sizeDelta != target)
                rt.sizeDelta = target;
        }

        private static Vector2 OriginalSize(RectTransform rt)
        {
            if (!_originalSizes.TryGetValue(rt, out var size))
            {
                size = rt.sizeDelta;
                _originalSizes[rt] = size;
            }
            return size;
        }

        // Widen RenderTextureMain's render texture to the screen aspect (keeping its height).
        // RenderTextureMain.Update() recreates the texture when it differs from these originals.
        private static void FixMainTextureAspect(float aspect)
        {
            var rtm = RenderTextureMain.instance;
            if (rtm == null)
                return;

            if (_texWidthField == null)
            {
                var t = typeof(RenderTextureMain);
                _texWidthField = t.GetField("textureWidthOriginal", BindingFlags.NonPublic | BindingFlags.Instance);
                _texHeightField = t.GetField("textureHeightOriginal", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_texWidthField == null || _texHeightField == null)
                return;

            float height = (float)_texHeightField.GetValue(rtm);
            float width = (float)_texWidthField.GetValue(rtm);
            if (height <= 0f)
                return;

            float desiredWidth = Mathf.Round(height * aspect);
            if (Mathf.Abs(width - desiredWidth) > 0.5f)
                _texWidthField.SetValue(rtm, desiredWidth);
        }

        // The vanilla size depends on the Render Size setting, so let the game re-apply it
        // rather than restoring a width captured before the setting may have changed.
        private static void RestoreMainTextureAspect()
        {
            if (RenderTextureMain.instance == null || GraphicsManager.instance == null)
                return;
            GraphicsManager.instance.UpdateRenderSize();
        }

        private static void ResizeRenderTexture(RenderTexture rt, int width, int height, Camera? cam)
        {
            bool wasTarget = cam != null && cam.targetTexture == rt;
            if (wasTarget)
                cam!.targetTexture = null;

            rt.Release();
            rt.width = width;
            rt.height = height;
            rt.Create();

            if (wasTarget)
            {
                cam!.targetTexture = rt;
                cam.enabled = false;
                cam.enabled = true;
            }
        }

        private static Camera? FindCameraTargeting(RenderTexture rt)
        {
            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (cam != null && cam.targetTexture == rt && cam.gameObject.scene.IsValid())
                    return cam;
            }
            return null;
        }

        private static T? FindComponent<T>() where T : Component
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
            {
                if (c != null && c.gameObject.scene.IsValid())
                    return c;
            }
            return null;
        }

        private static RectTransform? GetRect(Component? c) => c != null ? c.GetComponent<RectTransform>() : null;

        /// <summary>
        /// Shifts a SemiUI HUD widget's pinned position outward to the real screen edge,
        /// based on its anchor. Called once per element from the SemiUI.Start postfix.
        /// </summary>
        internal static void OffsetHudElement(SemiUI ui)
        {
            if (!EnabledForPatch || ui == null)
                return;

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= BaseAspect + 0.001f)
                return;

            if (HUDCanvas.instance == null)
                return;

            var gameHud = HUDCanvas.instance.transform.Find("HUD/Game Hud");
            if (gameHud == null)
                return;

            // The transform SemiUI actually animates (whole object vs just the text rect).
            var t = (ui.animateTheEntireObject ? ui.transform : ui.textRectTransform) as RectTransform;
            if (t == null || !t.IsChildOf(gameHud))
                return;

            // Same pixel inset from the edge as 16:9: the HUD camera's half-width grows by
            // 200 * (aspect - 16/9) world units, so push edge widgets out by that amount.
            float extra = 200f * (aspect - BaseAspect);
            float offsetX = (t.anchorMin.x - 0.5f) * 2f * extra;
            if (Mathf.Approximately(offsetX, 0f))
                return;

            var delta = new Vector2(offsetX, 0f);
            ui.showPosition += delta;
            ui.hidePosition += delta;
        }
    }
}
