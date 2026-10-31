using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace RepoNativeUltrawide
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.github.NEDOSTUPN0.REPONativeUltrawide";
        public const string Name = "REPONativeUltrawide";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log = null!;

        internal static ConfigEntry<KeyCode> DumpKey = null!;
        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<bool> LensDistortionFix = null!;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Adapt the game and UI to ultrawide instead of stretching them. " +
                "Spreads HUD/menu elements to the real screen edges.");

            LensDistortionFix = Config.Bind(
                "General",
                "Lens Distortion Fix",
                true,
                "Reduce the 'old TV' lens distortion and chromatic aberration on ultrawide so the " +
                "edges/corners stay as moderate and readable as they are at 16:9.");

            DumpKey = Config.Bind(
                "Debug",
                "Dump UI Hierarchy Key",
                KeyCode.None,
                "Bind a key to dump the full UI (Canvas/RectTransform) hierarchy + cameras to a file. Unbound by default.");

            ApplyPatches(new Harmony(Guid));

            Log.LogInfo($"{Name} {Version} loaded (enabled={Enabled.Value}).");
        }

        // Patch each class separately: if a game update breaks one patch (renamed or overloaded
        // method), the rest of the mod still loads instead of PatchAll aborting everything.
        private static void ApplyPatches(Harmony harmony)
        {
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to apply patch {type.Name}: {e.Message}");
                }
            }
        }

        internal static void DumpHierarchy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== REPONativeUltrawide UI dump ===");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Screen: {Screen.width}x{Screen.height}  aspect={(float)Screen.width / Screen.height:0.0000}");
            sb.AppendLine($"Fullscreen: {Screen.fullScreenMode}");
            sb.AppendLine();

            // Includes inactive objects and DontDestroyOnLoad; filter out assets/prefabs.
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            int dumped = 0;

            foreach (var canvas in canvases)
            {
                if (canvas == null)
                    continue;

                // Skip prefabs / assets that are not part of a loaded scene.
                if (!canvas.gameObject.scene.IsValid())
                    continue;

                // Only dump root canvases; nested canvases are reached via recursion.
                if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>() != null)
                    continue;

                sb.AppendLine($"########## CANVAS '{canvas.name}'  scene='{canvas.gameObject.scene.name}'  renderMode={canvas.renderMode}  sortingOrder={canvas.sortingOrder}");
                DumpNode(canvas.transform, 0, sb);
                sb.AppendLine();
                dumped++;
            }

            DumpCameras(sb);
            DumpPostProcessing(sb);

            var dir = Paths.BepInExRootPath;
            var file = Path.Combine(dir, $"ultrawide-ui-dump-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            try
            {
                File.WriteAllText(file, sb.ToString());
                Log.LogMessage($"Dumped {dumped} root canvas/canvases → {file}");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to write dump file: {e}");
                Log.LogInfo(sb.ToString());
            }
        }

        private static void DumpCameras(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("########## CAMERAS");
            var cameras = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (var cam in cameras)
            {
                if (cam == null || !cam.gameObject.scene.IsValid())
                    continue;

                var tex = cam.targetTexture;
                var texInfo = tex == null
                    ? "targetTexture=NULL (renders to screen)"
                    : $"targetTexture='{tex.name}' {tex.width}x{tex.height} fmt={tex.format}";

                sb.AppendLine(
                    $"[{(cam.enabled ? "+" : "-")}] CAM '{FullPath(cam.transform)}'" +
                    $" depth={cam.depth} ortho={cam.orthographic} orthoSize={cam.orthographicSize:0.##}" +
                    $" aspect={cam.aspect:0.0000} fov={cam.fieldOfView:0.#}" +
                    $" rect={R(cam.rect)} clear={cam.clearFlags} cullMask=0x{cam.cullingMask:X8}");
                sb.AppendLine($"        {texInfo}");

                var comps = new StringBuilder();
                foreach (var comp in cam.GetComponents<Component>())
                {
                    if (comp == null || comp is Transform || comp is Camera)
                        continue;
                    comps.Append(comp.GetType().Name).Append(' ');
                }
                if (comps.Length > 0)
                    sb.AppendLine($"        components: {comps}");
            }

            // Materials/shaders on the world + HUD display quads (where a UI 'CRT' effect may live).
            sb.AppendLine();
            sb.AppendLine("########## DISPLAY QUAD MATERIALS");
            foreach (var ri in Resources.FindObjectsOfTypeAll<RawImage>())
            {
                if (ri == null || !ri.gameObject.scene.IsValid())
                    continue;
                var n = ri.name;
                if (!n.Contains("Render Texture"))
                    continue;
                var mat = ri.material;
                sb.AppendLine($"  RawImage '{n}' material='{(mat != null ? mat.name : "null")}' shader='{(mat != null && mat.shader != null ? mat.shader.name : "null")}'");
                if (mat != null && mat.shader != null)
                    DumpMaterialProps(mat, sb);
            }

            sb.AppendLine();
            sb.AppendLine("########## RENDER TEXTURES (loaded)");
            foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (rt == null)
                    continue;
                sb.AppendLine($"  RT '{rt.name}' {rt.width}x{rt.height} fmt={rt.format} created={rt.IsCreated()}");
            }
        }

        private static void DumpPostProcessing(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("########## POST-PROCESS EFFECTS (all loaded instances)");
            foreach (var ca in Resources.FindObjectsOfTypeAll<ChromaticAberration>())
                if (ca != null) sb.AppendLine($"  ChromaticAberration active={ca.active} enabled={ca.enabled} intensity={ca.intensity.value:0.###} (override={ca.intensity.overrideState})");
            foreach (var g in Resources.FindObjectsOfTypeAll<Grain>())
                if (g != null) sb.AppendLine($"  Grain active={g.active} intensity={g.intensity.value:0.###} size={g.size.value:0.###}");
            foreach (var v in Resources.FindObjectsOfTypeAll<Vignette>())
                if (v != null) sb.AppendLine($"  Vignette active={v.active} intensity={v.intensity.value:0.###} smoothness={v.smoothness.value:0.###}");
            foreach (var b in Resources.FindObjectsOfTypeAll<Bloom>())
                if (b != null) sb.AppendLine($"  Bloom active={b.active} intensity={b.intensity.value:0.###}");
            foreach (var l in Resources.FindObjectsOfTypeAll<LensDistortion>())
                if (l != null) sb.AppendLine($"  LensDistortion active={l.active} intensity={l.intensity.value:0.###} X={l.intensityX.value:0.###} Y={l.intensityY.value:0.###} scale={l.scale.value:0.###}");

            sb.AppendLine();
            sb.AppendLine("########## POST-PROCESS VOLUMES + LAYERS");
            foreach (var vol in Resources.FindObjectsOfTypeAll<PostProcessVolume>())
            {
                if (vol == null || !vol.gameObject.scene.IsValid())
                    continue;
                var prof = vol.sharedProfile != null ? vol.sharedProfile : vol.profile;
                string ca = "-", ld = "-";
                if (prof != null)
                {
                    if (prof.TryGetSettings<ChromaticAberration>(out var c)) ca = $"{c.intensity.value:0.##}(ov={c.intensity.overrideState})";
                    if (prof.TryGetSettings<LensDistortion>(out var l2)) ld = $"int={l2.intensity.value:0.#},X={l2.intensityX.value:0.##}";
                }
                sb.AppendLine($"  VOL '{FullPath(vol.transform)}' global={vol.isGlobal} prio={vol.priority:0.#} layer={vol.gameObject.layer} profile='{(prof != null ? prof.name : "null")}' CA={ca} LD={ld}");
            }
            foreach (var ppl in Resources.FindObjectsOfTypeAll<PostProcessLayer>())
            {
                if (ppl == null || !ppl.gameObject.scene.IsValid())
                    continue;
                sb.AppendLine($"  LAYER on '{FullPath(ppl.transform)}' volumeLayer.mask=0x{ppl.volumeLayer.value:X8}");
            }
        }

        private static void DumpMaterialProps(Material mat, StringBuilder sb)
        {
            var sh = mat.shader;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                var name = sh.GetPropertyName(i);
                var type = sh.GetPropertyType(i);
                string val = type switch
                {
                    UnityEngine.Rendering.ShaderPropertyType.Float => mat.GetFloat(name).ToString("0.###"),
                    UnityEngine.Rendering.ShaderPropertyType.Range => mat.GetFloat(name).ToString("0.###"),
                    UnityEngine.Rendering.ShaderPropertyType.Color => mat.GetColor(name).ToString(),
                    UnityEngine.Rendering.ShaderPropertyType.Vector => mat.GetVector(name).ToString(),
                    UnityEngine.Rendering.ShaderPropertyType.Texture => "<tex>",
                    _ => "?"
                };
                sb.AppendLine($"      {type} {name} = {val}");
            }
        }

        private static string FullPath(Transform t)
        {
            var s = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                s = t.name + "/" + s;
            }
            return s;
        }

        private static string R(Rect r) => $"({r.x:0.##},{r.y:0.##},{r.width:0.##},{r.height:0.##})";

        private static void DumpNode(Transform t, int depth, StringBuilder sb)
        {
            var indent = new string(' ', depth * 2);
            var rt = t as RectTransform;

            sb.Append(indent);
            sb.Append(t.gameObject.activeSelf ? "[+] " : "[-] ");
            sb.Append(t.name);

            if (rt != null)
            {
                sb.Append(
                    $"  aMin={V2(rt.anchorMin)} aMax={V2(rt.anchorMax)} pivot={V2(rt.pivot)}" +
                    $" pos={V2(rt.anchoredPosition)} size={V2(rt.sizeDelta)} scale={V3(rt.localScale)}");
            }

            // Note components of interest, with extra detail for CanvasScaler.
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                    continue;

                var type = c.GetType().Name;

                if (c is CanvasScaler cs)
                {
                    sb.Append(
                        $" {{CanvasScaler mode={cs.uiScaleMode} ref={V2(cs.referenceResolution)}" +
                        $" match={cs.matchWidthOrHeight:0.##} scaleFactor={cs.scaleFactor:0.##}}}");
                }
                else if (c is Canvas nested && nested.transform != t)
                {
                    sb.Append($" {{Canvas {nested.renderMode}}}");
                }
                else if (c is Image || c is RawImage || c is Canvas || c is CanvasGroup)
                {
                    sb.Append($" <{type}>");
                }
                else
                {
                    sb.Append($" <{type}>");
                }
            }

            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                DumpNode(t.GetChild(i), depth + 1, sb);
        }

        private static string V2(Vector2 v) => $"({v.x:0.#},{v.y:0.#})";
        private static string V3(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";
    }
}
