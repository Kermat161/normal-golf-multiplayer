using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NormalGolfMultiplayer
{
    /// <summary>
    /// Developer/testing helpers driven by command-line flags and a command file.
    ///
    /// Flags:
    ///   -ngmp-profile NAME     isolate saves into a sandbox folder (see SaveSandbox)
    ///   -ngmp-autoplay MODE    from the main menu, start "story" or "nine" automatically
    ///   -ngmp-host PORT        host as soon as the course loads
    ///   -ngmp-join IP:PORT     join as soon as the course loads
    ///   -ngmp-name NAME        player name override
    ///   -ngmp-quit SECONDS     quit this many seconds after the course loads
    ///   -ngmp-loopback         host on 127.0.0.1 only (local testing without a firewall prompt)
    ///
    /// While running, lines written to BepInEx/ngmp_cmd_PROFILE.txt are executed once
    /// (the file is deleted after reading). See ExecuteCommand for the verbs.
    /// </summary>
    internal class DevTools : MonoBehaviour
    {
        public static string Profile;
        public static string AutoPlay;
        public static int AutoHostPort = -1;
        public static string AutoJoin;
        public static string NameOverride;
        public static float QuitAfter = -1f;
        public static bool LoopbackOnly;

        public static readonly Dictionary<string, Func<string[], string>> ExtraCommands =
            new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase);

        private string _cmdFile;
        private string _outDir;
        private float _nextCmdPoll;

        public static string Tag => string.IsNullOrEmpty(Profile) ? "main" : Profile;

        public static void ParseCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i].ToLowerInvariant())
                {
                    case "-ngmp-profile": Profile = next; break;
                    case "-ngmp-autoplay": AutoPlay = next; break;
                    case "-ngmp-host": AutoHostPort = int.Parse(next, CultureInfo.InvariantCulture); break;
                    case "-ngmp-join": AutoJoin = next; break;
                    case "-ngmp-name": NameOverride = next; break;
                    case "-ngmp-quit": QuitAfter = float.Parse(next, CultureInfo.InvariantCulture); break;
                    case "-ngmp-loopback": LoopbackOnly = true; break;
                }
            }
        }

        private void Awake()
        {
            // Test profiles run two copies side by side; the unfocused one must keep updating.
            if (!string.IsNullOrEmpty(Profile))
                Application.runInBackground = true;
            _outDir = Path.Combine(Paths.BepInExRootPath, "ngmp_debug");
            _cmdFile = Path.Combine(Paths.BepInExRootPath, $"ngmp_cmd_{Tag}.txt");
            if (File.Exists(_cmdFile))
                File.Delete(_cmdFile); // left over from a previous run; don't replay it
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Plugin.Log.LogInfo($"Scene loaded: {scene.name}");
            if (scene.name == "Menu2")
            {
                var session = Net.NetSession.Instance;
                if (AutoHostPort > 0 && session.Mode == Net.SessionMode.Offline)
                    Plugin.Log.LogInfo("Auto-host: " + (session.Host(AutoHostPort) ?? "ok"));
                else if (!string.IsNullOrEmpty(AutoJoin) && session.Mode == Net.SessionMode.Offline)
                {
                    int colon = AutoJoin.LastIndexOf(':');
                    Plugin.Log.LogInfo("Auto-join: " + (session.Join(AutoJoin.Substring(0, colon), int.Parse(AutoJoin.Substring(colon + 1))) ?? "started"));
                }
                AutoHostPort = -1;
                AutoJoin = null;
                if (!string.IsNullOrEmpty(AutoPlay))
                    StartCoroutine(AutoPlayFromMenu());
            }
            if (scene.name == "Main" && QuitAfter > 0f)
                StartCoroutine(QuitLater(QuitAfter));
        }

        private IEnumerator AutoPlayFromMenu()
        {
            yield return new WaitForSeconds(1.5f);
            var menu = FindFirstObjectByType<MainMenu>();
            if (menu == null)
            {
                Plugin.Log.LogWarning("Autoplay: MainMenu not found");
                yield break;
            }
            Plugin.Log.LogInfo($"Autoplay: starting '{AutoPlay}'");
            if (AutoPlay.Equals("nine", StringComparison.OrdinalIgnoreCase))
                menu.PlayNine();
            else
                menu.PlayGame();
            AutoPlay = null; // only once per launch
        }

        private IEnumerator QuitLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Plugin.Log.LogInfo("Quitting (requested by -ngmp-quit)");
            Application.Quit();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCmdPoll)
                return;
            _nextCmdPoll = Time.unscaledTime + 0.5f;
            if (!File.Exists(_cmdFile))
                return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_cmdFile);
                File.Delete(_cmdFile);
            }
            catch (IOException)
            {
                return; // writer still has it open; try again next poll
            }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                string result;
                try
                {
                    result = ExecuteCommand(line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (Exception e)
                {
                    result = "ERROR " + e;
                }
                Plugin.Log.LogInfo($"[cmd] {line} -> {result}");
            }
        }

        private string ExecuteCommand(string[] a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "screenshot":
                {
                    Directory.CreateDirectory(_outDir);
                    string name = a.Length > 1 ? a[1] : DateTime.Now.ToString("HHmmss");
                    string path = Path.Combine(_outDir, $"{Tag}_{name}.png");
                    ScreenCapture.CaptureScreenshot(path);
                    return path;
                }
                case "dump":
                {
                    Directory.CreateDirectory(_outDir);
                    string path = Path.Combine(_outDir, $"{Tag}_dump.txt");
                    File.WriteAllText(path, Diagnostics.BuildReport());
                    return path;
                }
                case "quit":
                    Application.Quit();
                    return "bye";
                case "timescale":
                    Time.timeScale = F(a[1]);
                    return "ok";
                case "host":
                    return Net.NetSession.Instance.Host(a.Length > 1 ? int.Parse(a[1]) : ModConfig.HostPort.Value) ?? "hosting";
                case "join":
                {
                    int colon = a[1].LastIndexOf(':');
                    return Net.NetSession.Instance.Join(a[1].Substring(0, colon), int.Parse(a[1].Substring(colon + 1))) ?? "joining";
                }
                case "leave":
                    Net.NetSession.Instance.Leave();
                    return "left";
                case "chat":
                    Net.NetSession.Instance.SendChat(string.Join(" ", a, 1, a.Length - 1));
                    return "sent";
                case "tp":
                {
                    // tp x y z [yaw]  (walk mode)
                    var ch = MoveAndHitController.instance.m_fpsCharacter;
                    ch.TeleportPosition(new Vector3(F(a[1]), F(a[2]), F(a[3])));
                    if (a.Length > 4)
                        ch.TeleportRotation(Quaternion.Euler(0f, F(a[4]), 0f));
                    return "ok";
                }
                case "tpnear":
                {
                    // tpnear ID DIST: stand DIST metres in front of remote player ID, facing them
                    var rp = Remote.RemoteWorld.Instance.Players[byte.Parse(a[1])];
                    var ch = MoveAndHitController.instance.m_fpsCharacter;
                    Vector3 target = rp.Position;
                    Vector3 from = target + Quaternion.Euler(0f, rp.View.transform.eulerAngles.y, 0f) * Vector3.forward * F(a[2]);
                    ch.TeleportPosition(from);
                    Vector3 look = target - from;
                    look.y = 0f;
                    ch.TeleportRotation(Quaternion.LookRotation(look));
                    return $"at {from}";
                }
                case "goto":
                {
                    // goto ID: same as the menu's "Go to" button
                    var rp = Remote.RemoteWorld.Instance.Players[byte.Parse(a[1])];
                    if (!Game.LocalPlayer.CanTeleport)
                        return "can't teleport now (not walking?)";
                    Game.LocalPlayer.TeleportNear(rp.Position);
                    return "teleported near " + rp.Info.Name;
                }
                case "yaw":
                    MoveAndHitController.instance.m_fpsCharacter.TeleportRotation(Quaternion.Euler(0f, F(a[1]), 0f));
                    return "ok";
                case "golf":
                {
                    // Same steps as MoveAndHitController.Update when the player presses Switch in walk mode.
                    var mhc = MoveAndHitController.instance;
                    mhc.m_mode = ControlMode.Golf;
                    HarmonyLib.Traverse.Create(mhc).Field("m_fpsInput").GetValue<ECM2.Examples.FirstPerson.FirstPersonCharacterInput>().TurnOffGrid();
                    mhc.GolfTransition(false);
                    return "golf mode";
                }
                case "walk":
                    MoveAndHitController.instance.FPSTransitionForBounty(); // public wrapper around the walk transition
                    return "walk mode";
                case "hit":
                {
                    // hit POWER: fire a shot exactly like ClubSwing.HandleShot does
                    var swing = PanelManager.instance.m_clubSwingPanel;
                    swing.m_power = F(a[1]);
                    HitSequeneceManager.instance.StartCoroutine(HitSequeneceManager.instance.HitSequence(0f, swing.m_power, false, ShotType.Good));
                    return "swing started";
                }
                case "ball":
                {
                    // ball N: switch our ball cosmetic the way the ball-select menu does
                    int look = int.Parse(a[1]);
                    SaveManager.instance.m_unlocks.selectedBall = look;
                    Cosmetics.instance.SetBall(look);
                    return "ball look " + look;
                }
                case "password":
                    ModConfig.Password.Value = a.Length > 1 ? a[1] : "";
                    return "password set";
                case "scoreboard":
                    UI.MultiplayerUI.SetScoreboardOpen(a.Length < 2 || a[1] != "off");
                    return "ok";
                case "retake":
                    // The water/retake penalty path: LMUGC.RetakeShot(false) adds a stroke
                    PanelManager.instance.m_LMUGCPanel.RetakeShot(false);
                    return "retaken (+1 stroke)";
                case "startnine":
                    // What the red button at the first tee does
                    PanelManager.instance.m_LMUGCPanel.StartChallenge();
                    return "round started";
                case "hole":
                    // hole N SCORE: record a hole exactly as holing out would
                    PanelManager.instance.m_LMUGCPanel.CompleteHole("Hole" + a[1], int.Parse(a[2]));
                    return $"hole {a[1]} = {a[2]}";
                case "scores":
                {
                    var sb = new System.Text.StringBuilder();
                    var card = Game.ScoreTracker.Instance.Local;
                    sb.Append($"local round={card.RoundId} active={card.Active} hole={card.CurrentHole} strokes={card.Strokes} scores=[{string.Join(",", card.Scores)}] total={card.Total}");
                    sb.Append($" pars=[{(Game.ScoreTracker.Pars == null ? "?" : string.Join(",", Game.ScoreTracker.Pars))}]");
                    foreach (var rp in Remote.RemoteWorld.Instance.Players.Values)
                        sb.Append($" | #{rp.Info.Id} {rp.Info.Name} " + (rp.Score == null ? "no card" :
                            $"round={rp.Score.RoundId} active={rp.Score.Active} hole={rp.Score.CurrentHole} strokes={rp.Score.Strokes} scores=[{string.Join(",", rp.Score.Scores)}] total={rp.Score.Total}"));
                    return sb.ToString();
                }
                case "holed":
                    // holed PAR: fire the game's hole-out feedback path
                    HitSequeneceManager.instance.ShowBallInHoleFeedback(int.Parse(a[1]), true);
                    return "holed";
                case "bindings":
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var action in InputManager.instance.input.actions)
                    {
                        sb.Append($"\n   {action.actionMap.name}/{action.name}:");
                        foreach (var b in action.bindings)
                            sb.Append(" " + b.effectivePath);
                    }
                    return sb.ToString();
                }
                case "buf":
                {
                    var rp = Remote.RemoteWorld.Instance.Players[byte.Parse(a[1])];
                    return rp.Buffer.Debug(Remote.RemoteWorld.Now, ModConfig.InterpolationDelay.Value) + $"\n   sessionTime={Net.NetSession.Instance.SessionTime:0.000}";
                }
                case "walkto":
                    // walkto X Z [SPEED]: glide the character there so remote players see us walk
                    StartCoroutine(WalkTo(new Vector3(F(a[1]), 0f, F(a[2])), a.Length > 3 ? F(a[3]) : 4f));
                    return "walking";
                case "menu":
                    UI.MultiplayerUI.SetMenuOpen(a.Length < 2 || a[1] != "off");
                    return "ok";
                case "tomenu":
                    // Same as PauseMenu.QuitToMenu (minus the save, sandbox or not).
                    Time.timeScale = 1f;
                    SceneManager.LoadScene("Menu2");
                    return "loading menu";
                case "play":
                    AutoPlay = a.Length > 1 ? a[1] : "story";
                    StartCoroutine(AutoPlayFromMenu());
                    return "starting";
                case "state":
                {
                    var sb = new System.Text.StringBuilder();
                    var local = Game.LocalPlayer.Capture();
                    var input = InputManager.instance != null ? InputManager.instance.input : null;
                    sb.Append($"local flags={local.Flags} pos={local.Pos} yaw={local.Yaw:0} ball={local.BallPos} epoch={local.BallEpoch} " +
                              $"cursor={Cursor.lockState} inputActive={(input != null && input.inputIsActive)} uiCapture={UI.MultiplayerUI.CapturingInput}");
                    foreach (var rp in Remote.RemoteWorld.Instance.Players.Values)
                        sb.Append($" | #{rp.Info.Id} {rp.Info.Name} pose={rp.HasPose} flags={rp.Flags} pos={rp.Position} ball={rp.BallPosition} view={(rp.View != null)}");
                    return sb.ToString();
                }
            }

            if (ExtraCommands.TryGetValue(a[0], out var handler))
                return handler(a);
            return "unknown command";
        }

        private static IEnumerator WalkTo(Vector3 target, float speed)
        {
            var ch = MoveAndHitController.instance.m_fpsCharacter;
            while (ch != null)
            {
                Vector3 pos = ch.GetPosition();
                Vector3 d = target - pos;
                d.y = 0f;
                if (d.magnitude < 0.25f)
                    yield break;
                ch.SetRotation(Quaternion.LookRotation(d));
                ch.SetPosition(pos + d.normalized * Mathf.Min(speed * Time.deltaTime, d.magnitude), updateGround: true);
                yield return null;
            }
        }

        internal static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
