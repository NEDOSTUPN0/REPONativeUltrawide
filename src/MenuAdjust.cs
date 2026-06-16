using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RepoNativeUltrawide
{
    /// <summary>
    /// Ultrawide adjustments for the menu pages (main menu, pause, popups, regions, …).
    /// Phase 1: widen the full-page backdrop/dimmer of each page so it covers the whole screen
    /// instead of a centred 16:9 rectangle. A backdrop is detected structurally (a drawable that
    /// fills the page), so it works regardless of its name ("Background", "Panel Back", …) or
    /// anchor type, while leaving smaller decorative panels (e.g. the main-menu gradient) alone.
    /// </summary>
    internal static class MenuAdjust
    {
        private const float BaseAspect = 16f / 9f;

        private static readonly Dictionary<RectTransform, Vector2> _originalSizes = new();
        private static readonly Dictionary<Transform, float> _originalLocalX = new();

        internal static void Tick(bool enabled)
        {
            float aspect = (float)Screen.width / Screen.height;
            bool active = enabled && aspect > BaseAspect + 0.001f;

            // Width (in canvas units) the HUD camera shows, plus margin; and the per-side shift
            // that keeps an element the same pixel inset from the edge it had at 16:9.
            float targetWidth = 400f * aspect + 40f;
            float shift = 200f * (aspect - BaseAspect);

            foreach (var page in Object.FindObjectsOfType<MenuPage>())
            {
                var pageRt = page.transform as RectTransform;
                if (pageRt == null)
                    continue;

                WidenBackdrops(page, pageRt, active, targetWidth);

                // Only full-screen, side-aligned pages get their elements pushed to the edges.
                // Popups / region dialogs stay centred (just their backdrop is widened).
                if (page.GetComponent<MenuPageMain>() != null
                    || page.GetComponent<MenuPageLobby>() != null
                    || page.GetComponent<MenuPageEsc>() != null
                    || page.GetComponent<MenuPageSettings>() != null
                    || page.GetComponent<MenuPageSettingsPage>() != null)
                    ShiftElements(page, pageRt, active, shift);
            }
        }

        private static void WidenBackdrops(MenuPage page, RectTransform pageRt, bool active, float targetWidth)
        {
            float pageW = pageRt.rect.width;
            float pageH = pageRt.rect.height;

            foreach (Transform c in page.transform)
            {
                if (c is not RectTransform rt || rt.GetComponent<Graphic>() == null)
                    continue;

                // Only full-page backdrops (cover almost the whole page).
                if (rt.rect.width < pageW * 0.9f || rt.rect.height < pageH * 0.9f)
                    continue;

                var baseDelta = OriginalSize(rt);

                if (!active)
                {
                    if (rt.sizeDelta != baseDelta)
                        rt.sizeDelta = baseDelta;
                    continue;
                }

                // rect.width = (anchorMax.x - anchorMin.x) * parentWidth + sizeDelta.x
                float anchorSpan = rt.anchorMax.x - rt.anchorMin.x;
                float newDeltaX = targetWidth - anchorSpan * pageW;
                if (!Mathf.Approximately(rt.sizeDelta.x, newDeltaX))
                    rt.sizeDelta = new Vector2(newDeltaX, baseDelta.y);
            }
        }

        // Push each page element to the screen edge on its side (left content left, right content
        // right, centred content untouched). Shifting individual elements — not the page — keeps
        // the cursor and the button hit-tests aligned.
        private static void ShiftElements(MenuPage page, RectTransform pageRt, bool active, float shift)
        {
            float pageW = pageRt.rect.width;
            float pageH = pageRt.rect.height;

            // A full-page scroll list (settings) sits centred but belongs to the page's side panel;
            // give it the direction of that panel so the rows follow their background.
            int panelDir = 0;
            var panel = page.transform.Find("Panel");
            if (panel != null)
                panelDir = ClassifyDirection(panel, pageRt, pageW);

            foreach (Transform c in page.transform)
            {
                // Skip full-page backdrops (handled by WidenBackdrops) and the selection
                // highlight (it tracks the active button on its own).
                if (c is RectTransform brt && brt.GetComponent<Graphic>() != null
                    && brt.rect.width >= pageW * 0.9f && brt.rect.height >= pageH * 0.9f)
                    continue;
                if (c.GetComponent<MenuSelectionBox>() != null)
                    continue;

                int dir = ClassifyDirection(c, pageRt, pageW);
                if (dir == 0 && c.GetComponent<MenuScrollBox>() != null)
                    dir = panelDir;
                if (dir == 0)
                    continue;

                float baseX = OriginalLocalX(c);
                float targetX = active ? baseX + dir * shift : baseX;
                var lp = c.localPosition;
                if (!Mathf.Approximately(lp.x, targetX))
                    c.localPosition = new Vector3(targetX, lp.y, lp.z);
            }
        }

        // -1 left, +1 right, 0 leave (centred). Based on the element's centre within the page.
        private static int ClassifyDirection(Transform c, RectTransform pageRt, float pageW)
        {
            // The player model is shown via a render texture; its transform doesn't reflect where
            // it visually sits, so pin it to the right explicitly.
            if (c.name == "Menu Element Player Avatar")
                return 1;

            Vector3 worldCenter = c is RectTransform rt ? rt.TransformPoint(rt.rect.center) : c.position;
            float centerX = pageRt.InverseTransformPoint(worldCenter).x;
            float half = pageW * 0.5f;
            float deadzone = pageW * 0.12f;

            if (centerX < half - deadzone)
                return -1;
            if (centerX > half + deadzone)
                return 1;
            return 0;
        }

        private static float OriginalLocalX(Transform t)
        {
            if (!_originalLocalX.TryGetValue(t, out var x))
            {
                x = t.localPosition.x;
                _originalLocalX[t] = x;
            }
            return x;
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
    }
}
