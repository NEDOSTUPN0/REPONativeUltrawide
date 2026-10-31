using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// The valuable discover highlight (the corner brackets drawn over a newly seen item) maps the
    /// item's viewport position onto the HUD by spreading viewport 0..1 across the 16:9 canvas
    /// width. With the HUD view widened to the screen aspect, that compresses positions toward the
    /// centre, so the brackets drift off the item the further it is from the middle of the screen.
    /// We rescale the horizontal mapping (and the middle bar's width) to the real aspect.
    /// </summary>
    internal static class ValuableDiscoverPatch
    {
        private const float BaseAspect = 16f / 9f;

        // Ratio between the visible HUD width and the width vanilla assumes; 1 when inactive.
        internal static float WidthScale()
        {
            if (!UltrawideHud.EnabledForPatch || ValuableDiscover.instance == null)
                return 1f;

            float aspect = (float)Screen.width / Screen.height;
            if (aspect <= BaseAspect + 0.001f)
                return 1f;

            var size = ValuableDiscover.instance.canvasRect != null
                ? ValuableDiscover.instance.canvasRect.sizeDelta
                : Vector2.zero;
            if (size.x <= 0f || size.y <= 0f)
                return 1f;

            return size.y * aspect / size.x;
        }
    }

    [HarmonyPatch(typeof(ValuableDiscoverGraphic), "GetScreenPosition")]
    internal static class ValuableDiscoverGetScreenPositionPatch
    {
        // Vanilla x is centred on 0, so scaling it widens the mapping symmetrically.
        private static void Postfix(ref Vector3 __result) => __result.x *= ValuableDiscoverPatch.WidthScale();
    }

    [HarmonyPatch(typeof(ValuableDiscoverGraphic), "Update")]
    internal static class ValuableDiscoverUpdatePatch
    {
        private static readonly AccessTools.FieldRef<ValuableDiscoverGraphic, Vector2> MiddleTargetSizeNew =
            AccessTools.FieldRefAccess<ValuableDiscoverGraphic, Vector2>("middleTargetSizeNew");

        // The value we last wrote per instance, so a size Update didn't recompute isn't scaled twice.
        private static readonly ConditionalWeakTable<ValuableDiscoverGraphic, StrongBox<Vector2>> _lastScaled = new();

        private static void Postfix(ValuableDiscoverGraphic __instance)
        {
            float scale = ValuableDiscoverPatch.WidthScale();
            if (Mathf.Approximately(scale, 1f))
                return;

            ref var size = ref MiddleTargetSizeNew(__instance);
            var last = _lastScaled.GetOrCreateValue(__instance);
            if (size == last.Value)
                return;

            size.x *= scale;
            last.Value = size;
        }
    }
}
