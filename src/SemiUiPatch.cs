using HarmonyLib;
using UnityEngine;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// REPO's HUD text widgets (Health, Energy, Goal, Haul, Stats, Big Message, …) all derive
    /// from <c>SemiUI</c>, which pins each element's localPosition to a position captured at Start
    /// (relative to Game Hud's centre). Widening Game Hud therefore can't move them. Instead we
    /// shift that captured position horizontally based on the element's anchor so edge widgets
    /// move out to the real screen edges, keeping the same pixel inset they had at 16:9.
    /// </summary>
    [HarmonyPatch(typeof(SemiUI), "Start")]
    internal static class SemiUiStartPatch
    {
        private static void Postfix(SemiUI __instance) => UltrawideHud.OffsetHudElement(__instance);
    }
}
