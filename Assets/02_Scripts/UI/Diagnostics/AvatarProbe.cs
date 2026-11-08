using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Build-vs-editor check of the combat avatar size (OP session). Off unless the player is started with
    /// <c>-avatarProbe &lt;png path&gt;</c>. Two seconds after each of the first three combats it logs the avatar's
    /// scale inputs (read by reflection: UI does not reference the Game assembly) and saves a screenshot of the
    /// game frame only. In the editor, call <see cref="Describe"/> during Play for the same text.
    /// </summary>
    public sealed class AvatarProbe : MonoBehaviour
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private string _png;
        private IGamePhaseSource _phase;
        private GamePhase _last;
        private float _shootAt = -1f;
        private int _shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string png = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-avatarProbe", StringComparison.OrdinalIgnoreCase)) png = args[i + 1];
            if (string.IsNullOrEmpty(png)) return;

            var go = new GameObject("AvatarProbe (-avatarProbe)");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<AvatarProbe>();
            probe._png = png;
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IGamePhaseSource p) { probe._phase = p; break; }
            Debug.Log("[AvatarProbe] at start\n" + Describe());
        }

        private void Update()
        {
            if (_phase == null) return;
            GamePhase now = _phase.Phase;
            if (now == GamePhase.Combat && _last != GamePhase.Combat && _shots < 3) _shootAt = Time.unscaledTime + 2f;
            _last = now;
            if (_shootAt < 0f || Time.unscaledTime < _shootAt) return;
            _shootAt = -1f;
            _shots++;
            string path = _png.Replace(".png", "_" + _shots + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AvatarProbe] combat " + _shots + " -> " + path + "\n" + Describe());
        }

        /// <summary>Avatar scale inputs, overlay camera and screen. Allocates; diagnostics only.</summary>
        public static string Describe()
        {
            var sb = new StringBuilder(512);
            sb.Append("screen ").Append(Screen.width).Append('x').Append(Screen.height)
              .Append(" mode ").Append(Screen.fullScreenMode)
              .Append(" quality ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
              .Append(" skinWeights ").Append(QualitySettings.skinWeights).Append('\n');

            Type t = Type.GetType("VisionToWall.Game.PlayerAvatarView, VisionToWall.Game");
            if (t == null) return sb.Append("PlayerAvatarView type not found").ToString();
            UnityEngine.Object[] views = FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (UnityEngine.Object o in views)
            {
                var c = (Component)o;
                sb.Append("avatar ").Append(c.name)
                  .Append(" TorsoWorldWidth ").Append(Get(t, o, "TorsoWorldWidth"))
                  .Append(" _scale ").Append(Get(t, o, "_scale"))
                  .Append(" _anchorY ").Append(Get(t, o, "_anchorY"))
                  .Append(" _bounds ").Append(Get(t, o, "_bounds"))
                  .Append(" torsoOverBounds ").Append(Get(t, o, "torsoOverBounds"))
                  .Append(" pointCount ").Append(Get(t, o, "pointCount"))
                  .Append("\n  localScale ").Append(c.transform.localScale.ToString("F4"))
                  .Append(" lossyScale ").Append(c.transform.lossyScale.ToString("F4"))
                  .Append(" parent ").Append(c.transform.parent != null ? c.transform.parent.name : "-");
                var skin = c.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null)
                {
                    sb.Append("\n  skin ").Append(skin.name).Append(" enabled ").Append(skin.enabled)
                      .Append(" mesh ").Append(skin.sharedMesh != null ? skin.sharedMesh.name + " v" + skin.sharedMesh.vertexCount : "null")
                      .Append(" world bounds ").Append(skin.bounds.size.ToString("F3"))
                      .Append(" root ").Append(skin.rootBone != null ? skin.rootBone.name + " lossy " + skin.rootBone.lossyScale.ToString("F4") : "-");
                }
                var mr = c.GetComponent<MeshRenderer>();
                if (mr != null) sb.Append("\n  meshRenderer enabled ").Append(mr.enabled).Append(" bounds ").Append(mr.bounds.size.ToString("F3"));

                object combat = Get(t, o, "_combat");
                if (combat != null)
                {
                    Type ct = combat.GetType();
                    var cam = ct.GetProperty("OverlayCamera", Any)?.GetValue(combat) as Camera;
                    object dist = ct.GetProperty("PlayerPlaneDistance", Any)?.GetValue(combat);
                    if (cam != null)
                        sb.Append("\n  overlay camera ").Append(cam.name).Append(" fov ").Append(cam.fieldOfView)
                          .Append(" aspect ").Append(cam.aspect).Append(" pixels ").Append(cam.pixelWidth).Append('x')
                          .Append(cam.pixelHeight).Append(" physical ").Append(cam.usePhysicalProperties)
                          .Append(" planeDistance ").Append(dist);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static object Get(Type t, object o, string name)
        {
            for (Type x = t; x != null; x = x.BaseType)
            {
                PropertyInfo p = x.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(o);
                FieldInfo f = x.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return "?";
        }
    }
}
