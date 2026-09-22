using System;
using HarmonyLib;
using NormalGolfMultiplayer.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NormalGolfMultiplayer.Game
{
    /// <summary>
    /// Mirrors the local player's "Front Nine" round (the game's LMUGC system) and broadcasts it.
    ///
    /// Scores are captured as the game records them rather than read at the end, because
    /// LMUGC.CompleteNormalGolfRound clears lmugcScore immediately after the ninth hole.
    /// </summary>
    internal class ScoreTracker : MonoBehaviour
    {
        public static ScoreTracker Instance { get; private set; }

        public readonly ScoreCard Local = new ScoreCard();

        private readonly ScoreCard _lastSent = new ScoreCard();
        private bool _everSent;
        private byte _roundId;
        private float _nextPoll;

        // Read from the game's own scorecard UI so our table matches it.
        private static int[] _pars;
        private static Color? _under, _par, _over, _none;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            var session = NetSession.Instance;
            session.PlayerJoined += _ => Broadcast(force: true); // let newcomers see the round in progress
            session.SessionStarted += () => Broadcast(force: true);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _pars = null;
            _under = _par = _over = _none = null;
            if (scene.name == LocalPlayer.CourseScene)
                ImportRoundInProgress();
        }

        /// <summary>Picks up a round that was already running (rejoined, restarted the game, or enabled the mod mid-round).</summary>
        private void ImportRoundInProgress()
        {
            try
            {
                var save = SaveManager.instance;
                if (save == null || !save.m_run.isInLMUGC)
                    return;
                int[] scores = save.m_run.lmugcScore;
                if (scores == null)
                    return;
                Local.Reset(++_roundId, active: true);
                for (int i = 0; i < ScoreCard.HoleCount && i < scores.Length; i++)
                    Local.Scores[i] = (byte)Mathf.Clamp(scores[i], 0, 255);
                RefreshProgress();
                Broadcast(force: true);
                Plugin.Log.LogInfo($"Picked up a Front Nine round already in progress ({Local.PlayedCount} holes played)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the round in progress: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ called by patches

        /// <summary>The red button at the first tee: LMUGC.StartChallenge.</summary>
        public void BeginRound()
        {
            Local.Reset(++_roundId, active: true);
            Broadcast(force: true);
            Plugin.Log.LogInfo("Front Nine round started");
        }

        /// <summary>A hole was recorded: holed out, or skipped with a ticket (which scores par).</summary>
        public void RecordHole(int hole, int score)
        {
            if (hole < 1 || hole > ScoreCard.HoleCount)
                return;
            if (!Local.HasRound)
                Local.Reset(++_roundId, active: true);
            Local.Scores[hole - 1] = (byte)Mathf.Clamp(score, 0, 255);
            Local.Strokes = 0;
            Local.CurrentHole = (byte)Mathf.Min(hole + 1, ScoreCard.HoleCount);
            if (hole >= ScoreCard.HoleCount)
                Local.Active = false; // ninth hole done; wait for the button again
            Broadcast(force: true);
        }

        /// <summary>
        /// End of a Play Nine round, from SaveManager.AddScoreToCard. At this point the game has filled any
        /// unplayed holes with par+2 and has not cleared the card yet, so this is its final scorecard.
        /// </summary>
        public void FinishRound()
        {
            try
            {
                int[] scores = SaveManager.instance?.m_run.lmugcScore;
                if (scores != null)
                {
                    for (int i = 0; i < ScoreCard.HoleCount && i < scores.Length; i++)
                        if (scores[i] > 0)
                            Local.Scores[i] = (byte)Mathf.Clamp(scores[i], 0, 255);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the final scorecard: " + e.Message);
            }
            Local.Active = false;
            Local.CurrentHole = 0;
            Local.Strokes = 0;
            Broadcast(force: true);
            Plugin.Log.LogInfo($"Front Nine round finished: {Local.Total}");
        }

        // ------------------------------------------------------------------ polling

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + 0.25f;

            if (Local.Active)
            {
                RefreshProgress();
                // Leaving the round any other way (story mode ending, quitting the challenge) closes it too.
                try
                {
                    if (LocalPlayer.InWorld && SaveManager.instance != null && !SaveManager.instance.m_run.isInLMUGC)
                    {
                        Local.Active = false;
                        Local.CurrentHole = 0;
                        Local.Strokes = 0;
                    }
                }
                catch (NullReferenceException)
                {
                    // save not ready
                }
            }
            Broadcast(force: false);
        }

        /// <summary>Updates which hole we're on and how many strokes it has cost so far.</summary>
        private void RefreshProgress()
        {
            byte hole = 0;
            for (int i = 0; i < ScoreCard.HoleCount; i++)
            {
                if (Local.Scores[i] == 0)
                {
                    hole = (byte)(i + 1);
                    break;
                }
            }
            Local.CurrentHole = hole;

            byte strokes = 0;
            try
            {
                if (hole > 0 && LocalPlayer.InWorld && SaveManager.instance != null && SaveManager.instance.m_run.isInLMUGC)
                    strokes = (byte)Mathf.Clamp(HitManager.instance.m_ball.m_currentShot, 0, 255);
            }
            catch (NullReferenceException)
            {
                // between scenes
            }
            Local.Strokes = strokes;
        }

        private void Broadcast(bool force)
        {
            var session = NetSession.Instance;
            if (session == null || !session.InSession || !Local.HasRound)
                return;
            if (!force && _everSent && Local.Matches(_lastSent))
                return;
            _lastSent.CopyFrom(Local);
            _everSent = true;
            session.SendScore(Local);
        }

        // ------------------------------------------------------------------ course data for the table

        /// <summary>Par for each hole, read from the game's own scorecard panel. Null until the course is loaded.</summary>
        public static int[] Pars
        {
            get
            {
                if (_pars != null)
                    return _pars;
                try
                {
                    var panel = PanelManager.instance?.m_LMUGCPanel;
                    if (panel == null)
                        return null;
                    int[] pars = Traverse.Create(panel).Field("m_pars").GetValue<int[]>();
                    if (pars != null && pars.Length >= ScoreCard.HoleCount)
                        _pars = pars;
                }
                catch (Exception)
                {
                    // panel not built yet
                }
                return _pars;
            }
        }

        public static Color UnderPar => GameColor(ref _under, "m_underColor", new Color(0.45f, 0.85f, 1f));
        public static Color AtPar => GameColor(ref _par, "m_parColor", Color.white);
        public static Color OverPar => GameColor(ref _over, "m_overColor", new Color(1f, 0.6f, 0.35f));
        public static Color NoScore => GameColor(ref _none, "m_noScoreColor", new Color(0.55f, 0.58f, 0.62f));

        private static Color GameColor(ref Color? cache, string field, Color fallback)
        {
            if (cache.HasValue)
                return cache.Value;
            try
            {
                var panel = PanelManager.instance?.m_LMUGCPanel;
                if (panel != null)
                    cache = Traverse.Create(panel).Field(field).GetValue<Color>();
            }
            catch (Exception)
            {
                // fall through to the default
            }
            return cache ?? fallback;
        }

        public static Color ColorFor(int score, int hole)
        {
            int[] pars = Pars;
            if (score <= 0 || pars == null || hole < 1 || hole > pars.Length)
                return NoScore;
            int diff = score - pars[hole - 1];
            if (diff < 0) return UnderPar;
            return diff == 0 ? AtPar : OverPar;
        }

        /// <summary>Total par for the holes this card has actually scored, for a meaningful +/- figure.</summary>
        public static int ParForPlayed(ScoreCard card)
        {
            int[] pars = Pars;
            if (pars == null)
                return 0;
            int total = 0;
            for (int i = 0; i < ScoreCard.HoleCount && i < pars.Length; i++)
                if (card.Scores[i] > 0)
                    total += pars[i];
            return total;
        }
    }
}
