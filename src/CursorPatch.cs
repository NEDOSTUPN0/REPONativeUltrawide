using HarmonyLib;
using UnityEngine;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// REPO maps the mouse to UI space in <c>SemiFunc.UIMousePosToUIPos()</c> using
    /// <c>overlayCamera.ScreenToViewportPoint</c> divided by <c>Screen.width</c>. Because we
    /// widened the overlay render texture, that math shrinks the cursor's horizontal range, so
    /// the cursor can't reach the right edge on ultrawide. We replace it with an equivalent
    /// mapping: identical to vanilla at 16:9, but spanning the full widened HUD view otherwise.
    /// </summary>
    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.UIMousePosToUIPos))]
    internal static class UIMousePosToUIPosPatch
    {
        private const float BaseAspect = 16f / 9f;

        private static bool Prefix(ref Vector2 __result)
        {
            if (!UltrawideHud.EnabledForPatch || HUDCanvas.instance == null)
                return true; // run the original

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= BaseAspect + 0.001f)
                return true;

            var size = HUDCanvas.instance.GetComponent<RectTransform>().sizeDelta; // ~712 x 400

            // Mouse as a 0..1 viewport over the actual screen.
            float vx = Input.mousePosition.x / Screen.width;
            float vy = Input.mousePosition.y / Screen.height;

            // Centre of the HUD canvas + offset across the visible (aspect-scaled) width/height.
            // At 16:9 this reproduces vanilla's 0..sizeDelta range; wider screens extend past it.
            float x = size.x * 0.5f + (vx - 0.5f) * size.y * aspect;
            float y = size.y * 0.5f + (vy - 0.5f) * size.y;

            __result = new Vector2(x, y);
            return false; // skip original
        }
    }
}
