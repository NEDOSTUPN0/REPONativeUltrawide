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
        private static readonly List<Transform> _elements = new();

        internal static void Tick(bool enabled)
        {
            float aspect = (float)Screen.width / Screen.height;
            bool active = enabled && aspect > BaseAspect + 0.001f;

            // Width (in canvas units) the HUD camera shows, plus margin; and the per-side shift
            // that keeps an element the same pixel inset from the edge it had at 16:9.
            float targetWidth = 400f * aspect + 40f;
            float shift = 200f * (aspect - BaseAspect);

            foreach (var page in Object.FindObjectsByType<MenuPage>(FindObjectsSortMode.None))
            {
                var pageRt = page.transform as RectTransform;
                if (pageRt == null)
                    continue;

                var layoutRt = LayoutRect(pageRt);
                var pageSize = layoutRt.rect.size;
                CollectElements(page.transform, pageSize);

                WidenBackdrops(pageSize, active, targetWidth);

                // Only full-screen, side-aligned pages get their elements pushed to the edges.
                // Popups / region dialogs stay centred (just their backdrop is widened).
                if (page.GetComponent<MenuPageMain>() != null
                    || page.GetComponent<MenuPageLobby>() != null
                    || page.GetComponent<MenuPageEsc>() != null
                    || page.GetComponent<MenuPageSettings>() != null
                    || page.GetComponent<MenuPageSettingsPage>() != null)
                    ShiftElements(layoutRt, pageSize, active, shift);
            }
        }

        // Some pages stretch over the Menu Holder with a negative sizeDelta that cancels out
        // their own rect (width 0); their content is still laid out in the holder's space.
        private static RectTransform LayoutRect(RectTransform pageRt)
        {
            var size = pageRt.rect.size;
            if ((size.x < 1f || size.y < 1f) && pageRt.parent is RectTransform parent)
                return parent;
            return pageRt;
        }

        // The page's layout elements: its direct children, except that plain full-page groups
        // (e.g. "LeftTransform" / "RightTransform") are looked through, since they hold both
        // sides' content and only their children sit on a definite side.
        private static void CollectElements(Transform parent, Vector2 pageSize)
        {
            _elements.Clear();
            CollectInto(parent, pageSize);
        }

        private static void CollectInto(Transform parent, Vector2 pageSize)
        {
            foreach (Transform c in parent)
            {
                if (IsPageContainer(c, pageSize))
                    CollectInto(c, pageSize);
                else
                    _elements.Add(c);
            }
        }

        private static bool IsPageContainer(Transform t, Vector2 pageSize)
            => t is RectTransform rt
               && t.childCount > 0
               && rt.GetComponent<Graphic>() == null
               && rt.GetComponent<Selectable>() == null
               && rt.rect.width >= pageSize.x * 0.9f
               && rt.rect.height >= pageSize.y * 0.9f;

        private static bool IsBackdrop(Transform t, Vector2 pageSize)
            => t is RectTransform rt
               && rt.GetComponent<Graphic>() != null
               && rt.rect.width >= pageSize.x * 0.9f
               && rt.rect.height >= pageSize.y * 0.9f;

        private static void WidenBackdrops(Vector2 pageSize, bool active, float targetWidth)
        {
            foreach (var c in _elements)
            {
                // Only full-page backdrops (cover almost the whole page).
                if (!IsBackdrop(c, pageSize))
                    continue;

                var rt = (RectTransform)c;
                var baseDelta = OriginalSize(rt);

                if (!active)
                {
                    if (rt.sizeDelta != baseDelta)
                        rt.sizeDelta = baseDelta;
                    continue;
                }

                // rect.width = (anchorMax.x - anchorMin.x) * parentWidth + sizeDelta.x
                float parentW = rt.parent is RectTransform prt ? prt.rect.width : pageSize.x;
                float anchorSpan = rt.anchorMax.x - rt.anchorMin.x;
                float newDeltaX = targetWidth - anchorSpan * parentW;
                if (!Mathf.Approximately(rt.sizeDelta.x, newDeltaX))
                    rt.sizeDelta = new Vector2(newDeltaX, baseDelta.y);
            }
        }

        // Push each page element to the screen edge on its side (left content left, right content
        // right, centred content untouched). Shifting individual elements — not the page — keeps
        // the cursor and the button hit-tests aligned.
        private static void ShiftElements(RectTransform layoutRt, Vector2 pageSize, bool active, float shift)
        {
            // A full-page scroll list (settings) sits centred but belongs to the page's side panel;
            // give it the direction of that panel so the rows follow their background.
            int panelDir = 0;
            foreach (var c in _elements)
            {
                if (c.name == "Panel")
                {
                    panelDir = ClassifyDirection(c, layoutRt);
                    break;
                }
            }

            foreach (var c in _elements)
            {
                // Skip full-page backdrops (handled by WidenBackdrops) and the selection
                // highlight (it tracks the active button on its own).
                if (IsBackdrop(c, pageSize))
                    continue;
                if (c.GetComponent<MenuSelectionBox>() != null)
                    continue;

                int dir = ClassifyDirection(c, layoutRt);
                if (dir == 0 && c.GetComponent<MenuScrollBox>() != null)
                    dir = panelDir;

                // Centred elements stay put (restored if an earlier pass had moved them).
                if (dir == 0 && !_originalLocalX.ContainsKey(c))
                    continue;

                float baseX = OriginalLocalX(c);
                float targetX = active ? baseX + dir * shift : baseX;
                var lp = c.localPosition;
                if (!Mathf.Approximately(lp.x, targetX))
                    c.localPosition = new Vector3(targetX, lp.y, lp.z);
            }
        }

        // -1 left, +1 right, 0 leave (centred). Based on the element's centre within the page.
        private static int ClassifyDirection(Transform c, RectTransform layoutRt)
        {
            // The player model is shown via a render texture; its transform doesn't reflect where
            // it visually sits, so pin it to the right explicitly.
            if (c.name == "Menu Element Player Avatar")
                return 1;

            Vector3 worldCenter = c is RectTransform rt ? rt.TransformPoint(rt.rect.center) : c.position;
            var layout = layoutRt.rect;
            float centerX = layoutRt.InverseTransformPoint(worldCenter).x - layout.xMin;
            float pageW = layout.width;
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
