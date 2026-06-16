using HarmonyLib;
using UnityEngine;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// Drives the per-frame work from the game's own update loop instead of the BepInEx plugin's
    /// MonoBehaviour.Update. With some loaders/setups a lone BepInEx plugin's Update isn't ticked,
    /// which broke the mod when it was the only plugin installed. GraphicsManager.Update always
    /// runs, so hooking it makes the mod reliable standalone.
    /// </summary>
    [HarmonyPatch(typeof(GraphicsManager), "Update")]
    internal static class GameTickPatch
    {
        private static void Postfix()
        {
            bool enabled = Plugin.Enabled.Value;
            UltrawideHud.Tick(enabled);
            MenuAdjust.Tick(enabled);
            PostProcessAdjust.Tick(enabled && Plugin.LensDistortionFix.Value);

            if (Plugin.DumpKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.DumpKey.Value))
                Plugin.DumpHierarchy();
        }
    }
}
