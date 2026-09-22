using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.Remote;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NormalGolfMultiplayer.UI
{
    /// <summary>IMGUI multiplayer menu (host/join by IP:port), HUD, toasts and chat.</summary>
    internal class MultiplayerUI : MonoBehaviour
    {
        private static MultiplayerUI _instance;

        /// <summary>True while our menu or chat box owns the keyboard/mouse; game input is paused meanwhile.</summary>
        public static bool CapturingInput => _instance != null && (_instance._menuOpen || _instance._chatOpen);

        private const float WindowWidth = 520f;
        private const int ChatHistory = 40;
        private static readonly Color Accent = new Color(0.42f, 0.83f, 0.46f);
        private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.4f);

        private bool _menuOpen;
        private bool _scoreboardOpen;
        private bool _chatOpen;
        private int _chatOpenedFrame;
        private string _chatText = "";
        private Rect _windowRect = new Rect(40f, 120f, WindowWidth, 10f);

        private string _nameField;
        private string _hostPortField;
        private string _joinAddressField;
        private string _joinPortField;
        private string _passwordField;
        private string _feedback = "";
        private bool _feedbackIsError;
        private string[] _lanAddresses;

        private bool _captured;
        private CursorLockMode _savedLockMode;
        private bool _savedCursorVisible;

        private readonly List<ToastEntry> _toasts = new List<ToastEntry>();
        private readonly List<ChatEntry> _chat = new List<ChatEntry>();
        private float _hintUntil;

        private GUIStyle _window, _title, _header, _label, _small, _button, _bigButton, _field, _swatch, _hud, _chatStyle;
        private GUIStyle _scoreHead, _scoreRowHead, _scorePar, _scoreCell, _scoreName;
        private Texture2D _white;

        private struct ToastEntry
        {
            public string Text;
            public Color Color;
            public float Until;
        }

        private struct ChatEntry
        {
            public string Name;
            public Color Color;
            public string Text;
            public float Time;
        }

        private void Awake()
        {
            _instance = this;
            _nameField = ModConfig.PlayerName.Value;
            _hostPortField = ModConfig.HostPort.Value.ToString(CultureInfo.InvariantCulture);
            _joinAddressField = ModConfig.JoinAddress.Value;
            _joinPortField = ModConfig.JoinPort.Value.ToString(CultureInfo.InvariantCulture);
            _passwordField = ModConfig.Password.Value;
            _hintUntil = Time.unscaledTime + 25f;
        }

        private void Start()
        {
            var s = NetSession.Instance;
            s.ChatReceived += OnChat;
            s.SessionEnded += reason =>
            {
                SetFeedback(reason, error: !reason.StartsWith("You left", StringComparison.Ordinal));
                Toast(reason, ErrorColor);
            };
            s.SessionStarted += () =>
            {
                SetFeedback(s.IsHost ? "Hosting! Share your IP and port with friends." : "Connected!", error: false);
                _hintUntil = Time.unscaledTime + 12f;
            };
        }

        public static void SetMenuOpen(bool open)
        {
            if (_instance != null)
                _instance._menuOpen = open;
        }

        public static void SetScoreboardOpen(bool open)
        {
            if (_instance != null)
                _instance._scoreboardOpen = open;
        }

        public static void Toast(string text, Color color)
        {
            if (_instance == null)
                return;
            _instance._toasts.Add(new ToastEntry { Text = text, Color = color, Until = Time.unscaledTime + 6f });
            if (_instance._toasts.Count > 6)
                _instance._toasts.RemoveAt(0);
        }

        private void OnChat(byte id, string text)
        {
            var s = NetSession.Instance;
            string name = s.Players.TryGetValue(id, out var p) ? p.Name : "?";
            Color color = p != null ? (Color)p.Color : Color.white;
            _chat.Add(new ChatEntry { Name = name, Color = color, Text = text, Time = Time.unscaledTime });
            if (_chat.Count > ChatHistory)
                _chat.RemoveAt(0);
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (!_chatOpen && kb[ModConfig.MenuKey.Value].wasPressedThisFrame)
                    _menuOpen = !_menuOpen;
                else if (!_chatOpen && kb[ModConfig.ScoreboardKey.Value].wasPressedThisFrame)
                    _scoreboardOpen = !_scoreboardOpen;
                else if (!_menuOpen && !_chatOpen && NetSession.Instance.InSession && kb[ModConfig.ChatKey.Value].wasPressedThisFrame)
                {
                    _chatOpen = true;
                    _chatText = "";
                    _chatOpenedFrame = Time.frameCount;
                }
            }
            UpdateInputCapture();
        }

        private void UpdateInputCapture()
        {
            bool want = CapturingInput;
            if (want && !_captured)
            {
                _captured = true;
                _savedLockMode = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                SetGameInput(false);
            }
            else if (!want && _captured)
            {
                _captured = false;
                SetGameInput(true);
                Cursor.lockState = _savedLockMode;
                Cursor.visible = _savedCursorVisible;
            }

            if (_captured)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void SetGameInput(bool active)
        {
            var im = InputManager.instance;
            if (im == null || im.input == null)
                return;
            if (active)
                im.input.ActivateInput();
            else
                im.input.DeactivateInput();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            DrawHud(w);
            DrawToasts(w);
            DrawChat(h);
            if (_scoreboardOpen && !_menuOpen)
                DrawScoreboardOverlay(w);

            if (_menuOpen)
            {
                _windowRect.width = WindowWidth;
                _windowRect = GUILayout.Window(0x4E474D50, _windowRect, DrawWindow, GUIContent.none, _window);
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, w - _windowRect.width);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, h - _windowRect.height));
            }
        }

        private void DrawHud(float screenW)
        {
            var s = NetSession.Instance;
            string line;
            if (s.Mode == SessionMode.Offline)
            {
                if (Time.unscaledTime > _hintUntil)
                    return;
                line = $"Multiplayer: press {ModConfig.MenuKey.Value}";
            }
            else if (s.Mode == SessionMode.Connecting || (s.Mode == SessionMode.Connected && !s.InSession))
            {
                line = "Multiplayer: connecting...";
            }
            else
            {
                int n = s.Players.Count;
                string who = n == 1 ? "just you" : $"{n} players";
                line = s.IsHost ? $"Multiplayer: hosting · {who}" : $"Multiplayer: connected · {who} · {s.PingMs} ms";
                if (Time.unscaledTime < _hintUntil)
                    line += $"\n{ModConfig.MenuKey.Value} menu · {ModConfig.ChatKey.Value} chat";
            }
            var rect = new Rect(screenW - 420f, 12f, 408f, 44f);
            ShadowLabel(rect, line, _hud, Color.white);
        }

        private void DrawToasts(float screenW)
        {
            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.Until < now);
            float y = 64f;
            foreach (var t in _toasts)
            {
                float alpha = Mathf.Clamp01((t.Until - now) / 0.6f);
                var c = Color.Lerp(t.Color, Color.white, 0.3f);
                c.a = alpha;
                ShadowLabel(new Rect(screenW - 620f, y, 608f, 26f), t.Text, _hud, c);
                y += 26f;
            }
        }

        private void DrawChat(float screenH)
        {
            float now = Time.unscaledTime;
            var visible = _chatOpen ? _chat.Skip(Math.Max(0, _chat.Count - 10)).ToList()
                : _chat.Where(c => now - c.Time < 12f).Skip(Math.Max(0, _chat.Count - 6)).ToList();

            float y = screenH - 200f - visible.Count * 24f;
            foreach (var c in visible)
            {
                float alpha = _chatOpen ? 1f : Mathf.Clamp01((12f - (now - c.Time)) / 1.5f);
                var nameColor = Color.Lerp(c.Color, Color.white, 0.3f);
                nameColor.a = alpha;
                var nameSize = _chatStyle.CalcSize(new GUIContent(c.Name + ":"));
                ShadowLabel(new Rect(20f, y, nameSize.x + 4f, 24f), c.Name + ":", _chatStyle, nameColor);
                ShadowLabel(new Rect(26f + nameSize.x, y, 700f, 24f), c.Text, _chatStyle, new Color(1f, 1f, 1f, alpha));
                y += 24f;
            }

            if (!_chatOpen)
                return;

            var e = Event.current;
            // The key that opened chat would otherwise be typed into the box on the same frame.
            if (Time.frameCount == _chatOpenedFrame && e.type == EventType.KeyDown)
            {
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                NetSession.Instance.SendChat(_chatText);
                _chatOpen = false;
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _chatOpen = false;
                e.Use();
                return;
            }

            GUI.Box(new Rect(16f, screenH - 190f, 720f, 36f), GUIContent.none, _window);
            GUI.SetNextControlName("ngmp_chat");
            _chatText = GUI.TextField(new Rect(24f, screenH - 185f, 704f, 26f), _chatText, Protocol.MaxChatLength, _field);
            GUI.FocusControl("ngmp_chat");
        }

        private void DrawWindow(int id)
        {
            var s = NetSession.Instance;

            GUILayout.BeginHorizontal();
            GUILayout.Label("NORMAL GOLF MULTIPLAYER", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"v{Plugin.Version} · {ModConfig.MenuKey.Value} to close", _small);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            DrawIdentity(s);
            Separator();

            if (s.Mode == SessionMode.Offline)
            {
                DrawHostSection();
                Separator();
                DrawJoinSection();
            }
            else if (!s.InSession)
            {
                GUILayout.Label(s.Status, _label);
                GUILayout.Space(6f);
                if (GUILayout.Button("Cancel", _button, GUILayout.Width(120f)))
                    s.Leave();
            }
            else
            {
                DrawSession(s);
            }

            if (!string.IsNullOrEmpty(_feedback))
            {
                Separator();
                var prev = GUI.contentColor;
                GUI.contentColor = _feedbackIsError ? ErrorColor : Accent;
                GUILayout.Label(_feedback, _label);
                GUI.contentColor = prev;
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 32));
        }

        private void DrawIdentity(NetSession s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _label, GUILayout.Width(70f));
            GUI.enabled = !s.InSession || s.IsHost;
            string newName = GUILayout.TextField(_nameField ?? "", Protocol.MaxNameLength, _field, GUILayout.Width(220f));
            GUI.enabled = true;
            if (newName != _nameField)
            {
                _nameField = newName;
                ModConfig.PlayerName.Value = newName.Trim();
            }
            if (string.IsNullOrWhiteSpace(_nameField))
                GUILayout.Label($"(using \"{ModConfig.ResolveName()}\")", _small);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Colour", _label, GUILayout.Width(70f));
            Color current = ModConfig.GetColor();
            foreach (var c in ModConfig.Palette)
            {
                bool selected = ColorsClose(c, current);
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = c;
                if (GUILayout.Button(selected ? "✓" : "", _swatch, GUILayout.Width(30f), GUILayout.Height(24f)))
                    ModConfig.PlayerColor.Value = "#" + ColorUtility.ToHtmlStringRGB(c);
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHostSection()
        {
            GUILayout.Label("HOST A GAME", _header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", _label, GUILayout.Width(70f));
            _hostPortField = GUILayout.TextField(_hostPortField, 5, _field, GUILayout.Width(80f));
            GUILayout.Space(16f);
            GUILayout.Label("Password", _label, GUILayout.Width(80f));
            _passwordField = GUILayout.PasswordField(_passwordField ?? "", '•', 32, _field, GUILayout.Width(150f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host game", _bigButton, GUILayout.Width(160f)))
                DoHost();
            GUILayout.Space(10f);
            GUILayout.Label("Password is optional.", _small);
            GUILayout.EndHorizontal();

            var lan = LanAddresses();
            if (lan.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Your LAN IP: " + string.Join(", ", lan), _small);
                if (GUILayout.Button("Copy", _button, GUILayout.Width(60f)))
                    GUIUtility.systemCopyBuffer = $"{lan[0]}:{_hostPortField}";
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Friends outside your network need your public IP and UDP port-forwarding on your router, " +
                            "or a shared VPN such as Tailscale, ZeroTier or Radmin.", _small);
        }

        private void DrawJoinSection()
        {
            GUILayout.Label("JOIN A GAME", _header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", _label, GUILayout.Width(70f));
            _joinAddressField = GUILayout.TextField(_joinAddressField ?? "", 100, _field, GUILayout.Width(220f));
            GUILayout.Space(8f);
            GUILayout.Label("Port", _label, GUILayout.Width(40f));
            _joinPortField = GUILayout.TextField(_joinPortField, 5, _field, GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Join", _bigButton, GUILayout.Width(160f)))
                DoJoin();
            GUILayout.Space(10f);
            GUILayout.Label("Uses the password above, if the host set one.", _small);
            GUILayout.EndHorizontal();
        }

        private void DrawSession(NetSession s)
        {
            GUILayout.Label(s.IsHost ? $"HOSTING ON UDP PORT {ModConfig.HostPort.Value}" : $"CONNECTED TO {s.Endpoint.ToUpperInvariant()}", _header);
            if (s.IsHost)
            {
                var lan = LanAddresses();
                if (lan.Length > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("LAN: " + string.Join(", ", lan.Select(a => $"{a}:{ModConfig.HostPort.Value}")), _small);
                    if (GUILayout.Button("Copy", _button, GUILayout.Width(60f)))
                        GUIUtility.systemCopyBuffer = $"{lan[0]}:{ModConfig.HostPort.Value}";
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label($"Ping {s.PingMs} ms", _small);
            }

            GUILayout.Space(6f);
            GUILayout.Label($"PLAYERS ({s.Players.Count})", _header);
            Vector3? me = LocalPlayer.InWorld ? LocalPlayer.Capture().Pos : (Vector3?)null;
            foreach (var info in s.Players.Values.OrderBy(p => p.Id))
            {
                GUILayout.BeginHorizontal();
                var prev = GUI.contentColor;
                GUI.contentColor = Color.Lerp(info.Color, Color.white, 0.25f);
                GUILayout.Label("●", _label, GUILayout.Width(18f));
                GUI.contentColor = prev;

                string role = info.Id == Protocol.HostId ? " (host)" : "";
                if (info.Id == s.LocalId)
                {
                    GUILayout.Label($"{info.Name}{role} — you", _label);
                }
                else
                {
                    string detail = "";
                    RemotePlayer rp = null;
                    if (RemoteWorld.Instance != null && RemoteWorld.Instance.Players.TryGetValue(info.Id, out rp))
                    {
                        detail = RemoteWorld.Describe(rp);
                        if (me.HasValue && rp.HasPose)
                            detail += $" · {Vector3.Distance(me.Value, rp.Position):0}m";
                    }
                    int ping = s.GetPlayerPing(info.Id);
                    GUILayout.Label($"{info.Name}{role}", _label, GUILayout.Width(150f));
                    GUILayout.Label(detail, _small);
                    GUILayout.FlexibleSpace();
                    if (s.IsHost || info.Id == Protocol.HostId)
                        GUILayout.Label($"{ping} ms", _small, GUILayout.Width(52f));
                    GUI.enabled = rp != null && rp.HasPose && LocalPlayer.CanTeleport;
                    if (GUILayout.Button("Go to", _button, GUILayout.Width(56f)))
                    {
                        LocalPlayer.TeleportNear(rp.Position);
                        _menuOpen = false;
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }

            if (s.Players.Count > 1 && LocalPlayer.InWorld && !LocalPlayer.CanTeleport)
                GUILayout.Label("\"Go to\" works while you're walking (not golfing or in a cutscene).", _small);

            Separator();
            DrawScoreboard(s);

            GUILayout.Space(8f);
            if (GUILayout.Button(s.IsHost ? "Stop hosting" : "Leave session", _bigButton, GUILayout.Width(180f)))
                s.Leave();
        }

        // ------------------------------------------------------------------ Front Nine scoreboard

        private const float ScoreNameWidth = 108f;
        private const float ScoreCellWidth = 26f;
        private const float ScoreTotalWidth = 44f;

        private struct ScoreRow
        {
            public string Name;
            public Color Color;
            public ScoreCard Card;
        }

        private List<ScoreRow> CollectScoreRows(NetSession s)
        {
            var rows = new List<ScoreRow>();
            var local = ScoreTracker.Instance;
            if (local != null && local.Local.HasRound)
                rows.Add(new ScoreRow { Name = s.LocalInfo.Name, Color = s.LocalInfo.Color, Card = local.Local });
            if (RemoteWorld.Instance != null)
            {
                foreach (var p in RemoteWorld.Instance.Players.Values.OrderBy(p => p.Info.Id))
                    if (p.Score != null && p.Score.HasRound)
                        rows.Add(new ScoreRow { Name = p.Info.Name, Color = p.Info.Color, Card = p.Score });
            }
            return rows;
        }

        private void DrawScoreboard(NetSession s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("FRONT NINE", _header);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{ModConfig.ScoreboardKey.Value} for overlay", _small);
            GUILayout.EndHorizontal();

            var rows = CollectScoreRows(s);
            if (rows.Count == 0)
            {
                GUILayout.Label("No round yet. Press the red button at the first tee to start one; scores appear here as holes are completed.", _small);
                return;
            }
            DrawScoreTable(rows);
        }

        private void DrawScoreTable(List<ScoreRow> rows)
        {
            int[] pars = ScoreTracker.Pars;

            GUILayout.BeginHorizontal();
            GUILayout.Label("HOLE", _scoreRowHead, GUILayout.Width(ScoreNameWidth));
            for (int h = 1; h <= ScoreCard.HoleCount; h++)
                GUILayout.Label(h.ToString(), _scoreHead, GUILayout.Width(ScoreCellWidth));
            GUILayout.Label("TOT", _scoreHead, GUILayout.Width(ScoreTotalWidth));
            GUILayout.Label("+/-", _scoreHead, GUILayout.Width(ScoreTotalWidth));
            GUILayout.EndHorizontal();

            if (pars != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("PAR", _scoreRowHead, GUILayout.Width(ScoreNameWidth));
                int parTotal = 0;
                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    GUILayout.Label(pars[h - 1].ToString(), _scorePar, GUILayout.Width(ScoreCellWidth));
                    parTotal += pars[h - 1];
                }
                GUILayout.Label(parTotal.ToString(), _scorePar, GUILayout.Width(ScoreTotalWidth));
                GUILayout.Label("", _scorePar, GUILayout.Width(ScoreTotalWidth));
                GUILayout.EndHorizontal();
            }

            Color previous = GUI.contentColor;
            foreach (var row in rows)
            {
                var card = row.Card;
                GUILayout.BeginHorizontal();

                GUI.contentColor = Color.Lerp(row.Color, Color.white, 0.35f);
                GUILayout.Label(Truncate(row.Name, 13), _scoreName, GUILayout.Width(ScoreNameWidth));

                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    int score = card.Scores[h - 1];
                    string text;
                    if (score > 0)
                    {
                        text = score.ToString();
                        GUI.contentColor = ScoreTracker.ColorFor(score, h);
                    }
                    else if (card.Active && card.CurrentHole == h)
                    {
                        // Playing this hole now: show strokes used so far.
                        text = card.Strokes > 0 ? card.Strokes.ToString() : "·";
                        GUI.contentColor = new Color(0.78f, 0.80f, 0.84f); // playing now, not a final score
                    }
                    else
                    {
                        text = "-";
                        GUI.contentColor = ScoreTracker.NoScore;
                    }
                    GUILayout.Label(text, _scoreCell, GUILayout.Width(ScoreCellWidth));
                }

                int played = card.PlayedCount;
                GUI.contentColor = card.Active ? Color.white : Accent;
                GUILayout.Label(played > 0 ? card.Total.ToString() : "-", _scoreCell, GUILayout.Width(ScoreTotalWidth));

                int diff = card.Total - ScoreTracker.ParForPlayed(card);
                string diffText = played == 0 || ScoreTracker.Pars == null ? "" : diff == 0 ? "E" : diff > 0 ? "+" + diff : diff.ToString();
                GUI.contentColor = diff < 0 ? ScoreTracker.UnderPar : diff > 0 ? ScoreTracker.OverPar : ScoreTracker.AtPar;
                GUILayout.Label(diffText, _scoreCell, GUILayout.Width(ScoreTotalWidth));

                GUILayout.EndHorizontal();
            }
            GUI.contentColor = previous;
        }

        /// <summary>Passive overlay: no controls, so it never steals the mouse while you play.</summary>
        private void DrawScoreboardOverlay(float screenW)
        {
            var rows = CollectScoreRows(NetSession.Instance);
            if (rows.Count == 0)
                return;
            float width = ScoreNameWidth + ScoreCard.HoleCount * ScoreCellWidth + ScoreTotalWidth * 2f + 36f;
            // Generous row height: clipping a player's row is much worse than a little extra padding.
            const float titleHeight = 30f, rowHeight = 23f, padding = 30f;
            float height = titleHeight + rowHeight * (1 + (ScoreTracker.Pars != null ? 1 : 0) + rows.Count) + padding;
            var area = new Rect((screenW - width) * 0.5f, 8f, width, height);
            GUI.Box(area, GUIContent.none, _window);
            GUILayout.BeginArea(new Rect(area.x + 18f, area.y + 12f, area.width - 36f, area.height - 16f));
            GUILayout.Label("FRONT NINE", _header);
            DrawScoreTable(rows);
            GUILayout.EndArea();
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ------------------------------------------------------------------ actions

        private void DoHost()
        {
            if (!TryParsePort(_hostPortField, out int port))
                return;
            ModConfig.HostPort.Value = port;
            ModConfig.Password.Value = _passwordField ?? "";
            string err = NetSession.Instance.Host(port);
            if (err != null)
                SetFeedback(err, error: true);
        }

        private void DoJoin()
        {
            string address = (_joinAddressField ?? "").Trim();
            string portText = _joinPortField;
            // Accept a pasted "ip:port" (IPv4 or hostname) or "[ipv6]:port" in the address box.
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address.Substring(colon + 1), out _))
            {
                portText = address.Substring(colon + 1);
                address = address.Substring(0, colon);
            }
            else if (address.StartsWith("[") && address.Contains("]:"))
            {
                int end = address.IndexOf("]:", StringComparison.Ordinal);
                portText = address.Substring(end + 2);
                address = address.Substring(1, end - 1);
            }
            _joinAddressField = address;
            _joinPortField = portText;

            if (!TryParsePort(portText, out int port))
                return;
            ModConfig.JoinAddress.Value = address;
            ModConfig.JoinPort.Value = port;
            ModConfig.Password.Value = _passwordField ?? "";
            string err = NetSession.Instance.Join(address, port);
            SetFeedback(err ?? $"Connecting to {address}:{port}...", error: err != null);
        }

        private bool TryParsePort(string text, out int port)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port >= 1 && port <= 65535)
                return true;
            SetFeedback("Port must be a number from 1 to 65535", error: true);
            return false;
        }

        private void SetFeedback(string text, bool error)
        {
            _feedback = text;
            _feedbackIsError = error;
        }

        // ------------------------------------------------------------------ helpers

        private string[] LanAddresses()
        {
            if (_lanAddresses != null)
                return _lanAddresses;
            var found = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            found.Add(ua.Address.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("LAN address lookup failed: " + e.Message);
            }
            // Typical home-network ranges first; VPN/virtual adapters after.
            _lanAddresses = found.Distinct()
                .OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2)
                .Take(3).ToArray();
            return _lanAddresses;
        }

        private static bool ColorsClose(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        private void Separator()
        {
            GUILayout.Space(6f);
            var r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
            GUILayout.Space(6f);
        }

        private void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.75f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = prev;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void EnsureStyles()
        {
            if (_window != null)
                return;
            _white = Tex(Color.white);
            var panel = Tex(new Color(0.07f, 0.08f, 0.1f, 0.95f));
            var fieldBg = Tex(new Color(0.16f, 0.17f, 0.21f, 1f));
            var fieldFocus = Tex(new Color(0.2f, 0.22f, 0.27f, 1f));
            var btn = Tex(new Color(0.2f, 0.36f, 0.24f, 1f));
            var btnHover = Tex(new Color(0.26f, 0.46f, 0.3f, 1f));
            var btnActive = Tex(new Color(0.16f, 0.28f, 0.19f, 1f));

            _window = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panel },
                padding = new RectOffset(18, 18, 14, 16),
                border = new RectOffset(0, 0, 0, 0),
            };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.8f, 0.85f, 0.9f) } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(0.92f, 0.93f, 0.95f) } };
            _small = new GUIStyle(_label) { fontSize = 13, normal = { textColor = new Color(0.65f, 0.68f, 0.72f) } };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 16,
                padding = new RectOffset(8, 8, 5, 5),
                normal = { background = fieldBg, textColor = Color.white },
                focused = { background = fieldFocus, textColor = Color.white },
                hover = { background = fieldFocus, textColor = Color.white },
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                normal = { background = btn, textColor = Color.white },
                hover = { background = btnHover, textColor = Color.white },
                active = { background = btnActive, textColor = Color.white },
            };
            _bigButton = new GUIStyle(_button) { fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 34f };
            _swatch = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { background = _white, textColor = Color.black },
                hover = { background = _white, textColor = Color.black },
                active = { background = _white, textColor = Color.black },
                margin = new RectOffset(2, 2, 2, 2),
            };
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperRight, normal = { textColor = Color.white } };
            _chatStyle = new GUIStyle(_hud) { alignment = TextAnchor.UpperLeft };
            _scoreHead = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.62f, 0.66f, 0.7f) },
                margin = new RectOffset(0, 0, 1, 1), padding = new RectOffset(0, 0, 0, 0),
            };
            _scoreRowHead = new GUIStyle(_scoreHead) { alignment = TextAnchor.MiddleLeft };
            _scoreCell = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white },
                margin = new RectOffset(0, 0, 1, 1), padding = new RectOffset(0, 0, 0, 0),
            };
            _scorePar = new GUIStyle(_scoreCell) { fontSize = 13, normal = { textColor = new Color(0.86f, 0.78f, 0.45f) } };
            _scoreName = new GUIStyle(_scoreCell) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        }
    }
}
