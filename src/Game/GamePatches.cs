using System;
using System.Reflection;
using HarmonyLib;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.UI;
using UnityEngine;

namespace NormalGolfMultiplayer.Game
{
    internal static class GamePatches
    {
        private static float _lastHoledTime = -10f;

        /// <summary>
        /// Patches are applied one by one so a game update that renames a single method only disables
        /// that one feature (with a warning) instead of stopping the whole mod from loading.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            Patch(harmony, typeof(HitManager), nameof(HitManager.ResetBall), postfix: nameof(AfterResetBall));
            Patch(harmony, typeof(HitSequeneceManager), nameof(HitSequeneceManager.HitSequence), prefix: nameof(BeforeHitSequence));
            Patch(harmony, typeof(HitSequeneceManager), nameof(HitSequeneceManager.ShowBallInHoleFeedback), prefix: nameof(BeforeHoleFeedback));
            Patch(harmony, typeof(LMUGC), nameof(LMUGC.StartChallenge), postfix: nameof(AfterStartChallenge));
            Patch(harmony, typeof(LMUGC), nameof(LMUGC.CompleteHole), prefix: nameof(BeforeCompleteHole));
            Patch(harmony, typeof(SaveManager), nameof(SaveManager.AddScoreToCard), postfix: nameof(AfterAddScoreToCard));
            Patch(harmony, typeof(ClubSwing), "Update", prefix: nameof(SkipWhileUiCapturesInput));
            Patch(harmony, typeof(ClubSwingFPS), "Update", prefix: nameof(SkipWhileUiCapturesInput));
            Patch(harmony, typeof(HitSequeneceManager), "RotateCamera", prefix: nameof(SkipWhileUiCapturesInput));
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method);
                if (target == null)
                    throw new MissingMethodException(type.Name, method);
                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(GamePatches), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(GamePatches), postfix) : null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not patch {type.Name}.{method} (game updated?): {e.Message}");
            }
        }

        /// <summary>A reset teleports the ball to the player's feet; tell receivers to snap rather than slide it across the map.</summary>
        private static void AfterResetBall()
        {
            LocalPlayer.BallEpoch++;
        }

        /// <summary>HitSequence is the coroutine every real swing starts (ClubSwing.HandleShot).</summary>
        private static void BeforeHitSequence(float power, bool isMiss, ShotType shot)
        {
            if (isMiss || NetSession.Instance == null)
                return;
            NetSession.Instance.SendShot(new ShotEvent
            {
                Club = LocalPlayer.CurrentClub(),
                Power = power,
                ShotType = (byte)shot,
            });
        }

        /// <summary>Every "ball went in a hole" path (story holes, Play Nine, LMUGC) reports through here.</summary>
        private static void BeforeHoleFeedback(int par, bool showAnyway)
        {
            if (NetSession.Instance == null || Time.unscaledTime - _lastHoledTime < 2f)
                return;
            _lastHoledTime = Time.unscaledTime;

            int strokes = 0;
            try
            {
                // Same condition the game uses to decide whether this hole is scored.
                bool scored = showAnyway || SaveManager.instance.m_run.isInLMUGC ||
                              ChallengeSystem.instance.GetCurrentChallenge().m_isMultiShot;
                if (scored)
                    strokes = HitManager.instance.m_ball.m_currentShot + 1;
            }
            catch (NullReferenceException)
            {
                // no active challenge; report an unscored hole
            }

            NetSession.Instance.SendHoled(new HoledEvent
            {
                Strokes = (byte)Mathf.Clamp(strokes, 0, 255),
                Par = (byte)Mathf.Clamp(par, 0, 255),
            });
        }

        /// <summary>The red button at the first tee starts a Front Nine round and clears the scorecard.</summary>
        private static void AfterStartChallenge()
        {
            ScoreTracker.Instance?.BeginRound();
        }

        /// <summary>
        /// Runs before the game records the hole, because on the ninth hole the game clears the whole
        /// scorecard moments later. `id` is a hole target name ("Hole4...") or a plain hole number for skips.
        /// </summary>
        private static void BeforeCompleteHole(string id, int score)
        {
            var match = System.Text.RegularExpressions.Regex.Match(id ?? "", "\\d+");
            if (match.Success && int.TryParse(match.Value, out int hole))
                ScoreTracker.Instance?.RecordHole(hole, score);
        }

        /// <summary>Only called when a Play Nine round is completed, while the final card is still intact.</summary>
        private static void AfterAddScoreToCard()
        {
            ScoreTracker.Instance?.FinishRound();
        }

        // The swing panel reads the mouse through legacy Input, which PlayerInput.DeactivateInput() can't block.
        // Skip it while our menu/chat owns the mouse so clicking a button can't swing the club.
        private static bool SkipWhileUiCapturesInput() => !MultiplayerUI.CapturingInput;
    }
}
