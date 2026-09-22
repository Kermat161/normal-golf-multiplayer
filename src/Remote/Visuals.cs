using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>Shared meshes, materials, fonts and sounds for remote players, built from the game's own assets.</summary>
    internal static class Visuals
    {
        /// <summary>
        /// "IgnoreForRTEXCams" is drawn by the first-person camera, the golfer (FMV) camera and the ball-chase
        /// camera, but not by the small swing/spin/club panel cameras, which is exactly where avatars belong.
        /// </summary>
        public static int Layer { get; private set; } = 0;

        private static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
        private static readonly Dictionary<Color, Material> LitCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static Material _baseLit;
        private static TMP_FontAsset _font;

        public static readonly Color Skin = new Color(0.96f, 0.78f, 0.63f);
        public static readonly Color Pants = new Color(0.24f, 0.25f, 0.30f);
        public static readonly Color Shoes = new Color(0.93f, 0.93f, 0.90f);
        public static readonly Color Dark = new Color(0.08f, 0.08f, 0.1f);
        public static readonly Color Steel = new Color(0.78f, 0.79f, 0.82f);

        /// <summary>Call on each course load: materials are cloned from scene objects that only exist there.</summary>
        public static void Prepare()
        {
            int layer = LayerMask.NameToLayer("IgnoreForRTEXCams");
            Layer = layer >= 0 ? layer : 0;

            if (_baseLit == null)
            {
                var ballMat = HitManager.instance?.m_ball?.m_meshRenderer?.sharedMaterial;
                if (ballMat == null && Cosmetics.instance != null && Cosmetics.instance.m_balls.Length > 0)
                    ballMat = Cosmetics.instance.m_balls[0].mat;
                if (ballMat != null)
                {
                    // Cloning a material the game already renders guarantees the URP shader variant exists in the build.
                    _baseLit = new Material(ballMat) { name = "NGMP_BaseLit" };
                    if (_baseLit.HasProperty("_BaseMap")) _baseLit.SetTexture("_BaseMap", null);
                    if (_baseLit.HasProperty("_MainTex")) _baseLit.SetTexture("_MainTex", null);
                    if (_baseLit.HasProperty("_BumpMap")) _baseLit.SetTexture("_BumpMap", null);
                    if (_baseLit.HasProperty("_EmissionColor")) _baseLit.SetColor("_EmissionColor", Color.black);
                    if (_baseLit.HasProperty("_Smoothness")) _baseLit.SetFloat("_Smoothness", 0.2f);
                    // The ball uses the specular workflow; a bright specular colour steals from the diffuse and washes colours out.
                    if (_baseLit.HasProperty("_SpecColor")) _baseLit.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f));
                    Plugin.Log.LogInfo($"Avatar material from '{ballMat.name}' ({ballMat.shader.name}) keywords=[{string.Join(",", ballMat.shaderKeywords)}]");
                }
                else
                {
                    _baseLit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "NGMP_BaseLit" };
                    Plugin.Log.LogWarning("Ball material not found; using a fresh URP/Lit material");
                }
            }

            if (_font == null)
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                _font = fonts.FirstOrDefault(f => f.name == "Quantico-Regular SDF")
                        ?? TMP_Settings.defaultFontAsset
                        ?? fonts.FirstOrDefault(f => f.name.StartsWith("LiberationSans"))
                        ?? fonts.FirstOrDefault();
            }
        }

        public static Mesh GetMesh(PrimitiveType type)
        {
            if (Meshes.TryGetValue(type, out var mesh) && mesh != null)
                return mesh;
            var tmp = GameObject.CreatePrimitive(type);
            mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            Meshes[type] = mesh;
            return mesh;
        }

        public static Material Lit(Color c)
        {
            if (LitCache.TryGetValue(c, out var m) && m != null)
                return m;
            m = new Material(_baseLit) { name = "NGMP_Lit_" + ColorUtility.ToHtmlStringRGB(c) };
            m.color = c;
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", c);
            LitCache[c] = m;
            return m;
        }

        /// <summary>A mesh part with no collider, so it can never touch the player, the ball or raycasts.</summary>
        public static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color,
            Quaternion? rot = null, bool shadows = true)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = GetMesh(type);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Lit(color);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go.transform;
        }

        public static Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        public static TextMeshPro Label(string name, Transform parent, float fontSize, Color color)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null)
                tmp.font = _font;
            tmp.richText = false; // names come from other players; never interpret tags
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(20f, 3f);
            tmp.color = color;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = new Color32(0, 0, 0, 220);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tmp;
        }

        public static Mesh BallMesh(out Vector3 scale)
        {
            scale = Vector3.one * 0.1f;
            var renderer = HitManager.instance?.m_ball?.m_meshRenderer;
            if (renderer == null)
                return GetMesh(PrimitiveType.Sphere);
            scale = renderer.transform.lossyScale;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh : GetMesh(PrimitiveType.Sphere);
        }

        public static Material BallMaterial(int look)
        {
            var c = Cosmetics.instance;
            if (c != null && c.m_balls != null && c.m_balls.Length > 0)
                return c.m_balls[Mathf.Clamp(look, 0, c.m_balls.Length - 1)].mat;
            return HitManager.instance?.m_ball?.m_meshRenderer?.sharedMaterial;
        }

        public static TrailRenderer TrailTemplate => HitManager.instance?.m_ball?.m_trail;

        // ------------------------------------------------------------------ sound

        public static AudioClip Clip(string name)
        {
            if (Clips.TryGetValue(name, out var clip) && clip != null)
                return clip;
            var am = AudioManager.Instance;
            if (am == null)
                return null;
            var sounds = Traverse.Create(am).Field("m_sounds").GetValue<Sound[]>();
            clip = sounds?.FirstOrDefault(s => s.m_name == name)?.m_clip;
            Clips[name] = clip;
            return clip;
        }

        /// <summary>One-shot 3D sound routed through the game's SFX mixer, so the SFX volume slider applies.</summary>
        public static void PlayAt(string clipName, Vector3 pos, float volume = 1f)
        {
            if (!ModConfig.RemoteSounds.Value)
                return;
            var clip = Clip(clipName);
            if (clip == null)
                return;
            var go = new GameObject("NGMP_Sound_" + clipName);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 6f;
            src.maxDistance = 250f;
            src.dopplerLevel = 0f;
            if (AudioManager.Instance != null)
                src.outputAudioMixerGroup = AudioManager.Instance.m_sfxMixer;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        public static string HitClipFor(Clubs club, ShotType shot)
        {
            if (shot == ShotType.Ground)
                return "hitGround";
            switch (club)
            {
                case Clubs.Driver: return "driverHit";
                case Clubs.Putter: return "putterHit";
                case Clubs.Hybrid: return "hybridHit";
                default: return "ironHit";
            }
        }
    }
}
