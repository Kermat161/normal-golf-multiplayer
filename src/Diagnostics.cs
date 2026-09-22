using System;
using System.Linq;
using System.Text;
using ECM2;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NormalGolfMultiplayer
{
    /// <summary>Text report of the live scene, used to learn the game's runtime layout.</summary>
    internal static class Diagnostics
    {
        public static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== NGMP diagnostics {DateTime.Now:O} scene={SceneManager.GetActiveScene().name} ===");
            sb.AppendLine($"Screen {Screen.width}x{Screen.height}  timeScale={Time.timeScale}");

            Section(sb, "Layers", () =>
            {
                for (int i = 0; i < 32; i++)
                {
                    string n = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(n)) sb.AppendLine($"  {i,2}: {n}");
                }
            });

            Section(sb, "Cameras (all, incl. disabled)", () =>
            {
                foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>()
                             .Where(c => c.gameObject.scene.IsValid())
                             .OrderBy(c => c.depth))
                {
                    sb.AppendLine($"  [{(cam.isActiveAndEnabled ? "ON " : "off")}] {PathOf(cam.transform)}");
                    sb.AppendLine($"      depth={cam.depth} ortho={cam.orthographic} fov={cam.fieldOfView} rect={cam.rect} " +
                                  $"target={(cam.targetTexture ? cam.targetTexture.name + " " + cam.targetTexture.width + "x" + cam.targetTexture.height : "SCREEN")}");
                    sb.AppendLine($"      pos={cam.transform.position} culling=[{MaskNames(cam.cullingMask)}] tag={cam.tag}");
                }
                sb.AppendLine($"  Camera.main = {(Camera.main ? PathOf(Camera.main.transform) : "null")}");
            });

            Section(sb, "MoveAndHitController", () =>
            {
                var mhc = MoveAndHitController.instance;
                if (mhc == null) { sb.AppendLine("  (none)"); return; }
                sb.AppendLine($"  mode={mhc.m_mode} moveLocked={mhc.m_moveLocked}");
                sb.AppendLine($"  FPSController={PathOf(mhc.m_FPSController.transform)} active={mhc.m_FPSController.activeInHierarchy} pos={mhc.m_FPSController.transform.position} rot={mhc.m_FPSController.transform.eulerAngles}");
                sb.AppendLine($"  golferHolder={PathOf(mhc.m_golferHolder)} active={mhc.m_golferHolder.gameObject.activeInHierarchy} pos={mhc.m_golferHolder.position} rot={mhc.m_golferHolder.eulerAngles}");
                sb.AppendLine($"  ballPosition local={mhc.m_ballPosition.localPosition} world={mhc.m_ballPosition.position}");
                var fpsCam = Traverse.Create(mhc).Field("m_FPSCam").GetValue<Camera>();
                var tpCam = Traverse.Create(mhc).Field("m_3rdPersonCam").GetValue<Camera>();
                var chase = Traverse.Create(mhc).Field("m_chaseCamera").GetValue<GameObject>();
                sb.AppendLine($"  m_FPSCam={(fpsCam ? PathOf(fpsCam.transform) : "null")}");
                sb.AppendLine($"  m_3rdPersonCam={(tpCam ? PathOf(tpCam.transform) : "null")}");
                sb.AppendLine($"  m_chaseCamera={(chase ? PathOf(chase.transform) : "null")}");

                var ch = mhc.m_fpsCharacter;
                if (ch != null)
                {
                    var cm = ch.GetComponent<CharacterMovement>();
                    sb.AppendLine($"  Character pos={ch.GetPosition()} vel={ch.GetVelocity()} grounded={ch.IsOnGround()} crouched={ch.IsCrouched()}");
                    if (cm != null) sb.AppendLine($"  CharacterMovement radius={cm.radius} height={cm.height} layer={LayerMask.LayerToName(cm.gameObject.layer)}");
                }
                sb.AppendLine("  -- FPSController hierarchy --");
                Hierarchy(sb, mhc.m_FPSController.transform, 2, 4);
                sb.AppendLine("  -- golferHolder hierarchy --");
                Hierarchy(sb, mhc.m_golferHolder, 2, 4);
            });

            Section(sb, "Ball", () =>
            {
                var hm = HitManager.instance;
                if (hm == null || hm.m_ball == null) { sb.AppendLine("  (none)"); return; }
                var ball = hm.m_ball;
                sb.AppendLine($"  {PathOf(ball.transform)} pos={ball.transform.position} scale={ball.transform.lossyScale} layer={LayerMask.LayerToName(ball.gameObject.layer)}");
                sb.AppendLine($"  kinematic={ball.m_rb.isKinematic} hit={ball.m_ballHit} multi={ball.m_isMultiShot} meshVisible={ball.m_meshRenderer.enabled}");
                var mat = ball.m_meshRenderer.sharedMaterial;
                sb.AppendLine($"  material={(mat ? mat.name + " shader=" + mat.shader.name : "null")}");
                var mf = ball.m_meshRenderer.GetComponent<MeshFilter>();
                sb.AppendLine($"  mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + " bounds=" + mf.sharedMesh.bounds : "null")} rendererGO={PathOf(ball.m_meshRenderer.transform)}");
                sb.AppendLine($"  trail mat={(ball.m_trail.sharedMaterial ? ball.m_trail.sharedMaterial.name + " shader=" + ball.m_trail.sharedMaterial.shader.name : "null")} width={ball.m_trail.widthMultiplier} time={ball.m_trail.time}");
                sb.AppendLine($"  chasingTrail mat={(ball.m_chasingTrail.sharedMaterial ? ball.m_chasingTrail.sharedMaterial.name : "null")} width={ball.m_chasingTrail.widthMultiplier} time={ball.m_chasingTrail.time}");
                sb.AppendLine("  -- ball hierarchy --");
                Hierarchy(sb, ball.transform, 2, 3);
                sb.AppendLine($"  ballsInPlay={ChallengeSystem.instance?.m_ballsInPlay?.Count}");
                var allBalls = UnityEngine.Object.FindObjectsByType<Ball>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                sb.AppendLine($"  all Ball components: {string.Join(", ", allBalls.Select(b => PathOf(b.transform)))}");
            });

            Section(sb, "Cosmetics", () =>
            {
                var c = Cosmetics.instance;
                if (c == null) { sb.AppendLine("  (none)"); return; }
                for (int i = 0; i < c.m_balls.Length; i++)
                {
                    var look = c.m_balls[i];
                    sb.AppendLine($"  [{i}] mat={(look.mat ? look.mat.name + " / " + look.mat.shader.name : "null")}");
                }
                sb.AppendLine($"  selectedBall={SaveManager.instance?.m_unlocks.selectedBall}");
            });

            Section(sb, "Shaders", () =>
            {
                foreach (string s in new[]
                         {
                             "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit",
                             "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "UI/Default", "Unlit/Color",
                             "Hidden/Internal-Colored", "TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field", "GUI/Text Shader",
                         })
                    sb.AppendLine($"  {s}: {(Shader.Find(s) != null ? "yes" : "NO")}");
            });

            Section(sb, "Fonts", () =>
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Take(15))
                    sb.AppendLine($"  TMP: {f.name} mat={(f.material ? f.material.shader.name : "null")}");
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>().Take(10))
                    sb.AppendLine($"  Font: {f.name}");
            });

            Section(sb, "Input", () =>
            {
                try
                {
                    _ = Input.GetKey(KeyCode.Space);
                    sb.AppendLine("  legacy Input: available");
                }
                catch (Exception e)
                {
                    sb.AppendLine("  legacy Input: UNAVAILABLE (" + e.GetType().Name + ")");
                }
                var im = InputManager.instance;
                sb.AppendLine($"  InputManager={(im != null)} gamepad={im?.m_isUsingGamePad} actionMap={im?.input?.currentActionMap?.name}");
                sb.AppendLine($"  cursor lock={Cursor.lockState} visible={Cursor.visible}");
            });

            Section(sb, "Game state", () =>
            {
                var sm = SaveManager.instance;
                if (sm == null) { sb.AppendLine("  (no SaveManager)"); return; }
                sb.AppendLine($"  mode={sm.m_gamemodeState?.mode} canGolf={sm.m_run.m_canGolf} inLMUGC={sm.m_run.isInLMUGC} sandbox={SaveSandbox.Active}");
                var cs = ChallengeSystem.instance;
                sb.AppendLine($"  challengeSet={(cs?.m_challengeSet ? cs.m_challengeSet.name : "null")} currentTee={cs?.m_currentTee}");
            });

            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, Action body)
        {
            sb.AppendLine();
            sb.AppendLine($"--- {title} ---");
            try { body(); }
            catch (Exception e) { sb.AppendLine("  !! " + e); }
        }

        private static void Hierarchy(StringBuilder sb, Transform t, int indent, int maxDepth)
        {
            var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
            sb.AppendLine($"{new string(' ', indent * 2)}{t.name} [{(t.gameObject.activeSelf ? "on" : "off")}] layer={LayerMask.LayerToName(t.gameObject.layer)} lpos={t.localPosition} {{{string.Join(",", comps)}}}");
            if (maxDepth <= 0) return;
            foreach (Transform child in t)
                Hierarchy(sb, child, indent + 1, maxDepth - 1);
        }

        private static string MaskNames(int mask)
        {
            if (mask == -1) return "Everything";
            return string.Join(",", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0)
                .Select(i => string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? i.ToString() : LayerMask.LayerToName(i)));
        }

        internal static string PathOf(Transform t)
        {
            if (t == null) return "null";
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
