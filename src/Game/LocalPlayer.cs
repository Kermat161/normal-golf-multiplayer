using System;
using ECM2;
using ECM2.Examples.FirstPerson;
using NormalGolfMultiplayer.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NormalGolfMultiplayer.Game
{
    /// <summary>Reads the local player's pose and ball out of the game each network tick.</summary>
    internal static class LocalPlayer
    {
        public const string CourseScene = "Main";

        /// <summary>Incremented whenever the game teleports/resets the ball (see GamePatches).</summary>
        public static byte BallEpoch;

        public static bool InWorld =>
            SceneManager.GetActiveScene().name == CourseScene &&
            MoveAndHitController.instance != null &&
            HitManager.instance != null &&
            HitManager.instance.m_ball != null;

        public static PlayerState Capture()
        {
            var s = new PlayerState();
            if (!InWorld)
                return s;

            var mhc = MoveAndHitController.instance;
            s.Flags |= StateFlags.InWorld;

            if (mhc.m_mode == ControlMode.Golf)
            {
                s.Flags |= StateFlags.Golfing;
                s.Pos = mhc.m_golferHolder.position;
                s.Yaw = mhc.m_golferHolder.eulerAngles.y;
                s.Pitch = 25f; // looking down at the ball
            }
            else
            {
                Character ch = mhc.m_fpsCharacter;
                s.Pos = ch.GetPosition();
                s.Yaw = ch.GetRotation().eulerAngles.y;
                if (ch is FirstPersonCharacter fpc)
                    s.Pitch = fpc._cameraPitch;
                if (ch.IsCrouched())
                    s.Flags |= StateFlags.Crouched;
            }

            s.Club = CurrentClub();

            Ball ball = HitManager.instance.m_ball;
            s.BallPos = ball.transform.position;
            if (ball.gameObject.activeInHierarchy && ball.m_meshRenderer != null && ball.m_meshRenderer.enabled)
                s.Flags |= StateFlags.BallVisible;
            if (ball.m_trail != null && ball.m_trail.emitting)
                s.Flags |= StateFlags.BallTrail;
            if (ball.m_rb != null && !ball.m_rb.isKinematic)
                s.Flags |= StateFlags.BallMoving;
            s.BallEpoch = BallEpoch;

            var save = SaveManager.instance;
            if (save != null && save.m_gamemodeState != null && save.m_gamemodeState.mode == GameMode.JustGolf)
                s.Flags |= StateFlags.PlayNine;

            return s;
        }

        /// <summary>Teleporting mid-swing or during a cutscene would confuse the game, so only while freely walking.</summary>
        public static bool CanTeleport =>
            InWorld && MoveAndHitController.instance.m_mode == ControlMode.Walk && !MoveAndHitController.instance.m_moveLocked;

        /// <summary>Puts us a couple of metres from <paramref name="target"/> (on our side of it), facing it.</summary>
        public static void TeleportNear(Vector3 target)
        {
            if (!CanTeleport)
                return;
            Character ch = MoveAndHitController.instance.m_fpsCharacter;
            Vector3 away = ch.GetPosition() - target;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;
            Vector3 pos = target + away.normalized * 2.2f + Vector3.up * 0.3f; // a little high; gravity settles us
            ch.TeleportPosition(pos, interpolating: false, updateGround: true);
            Vector3 look = target - pos;
            look.y = 0f;
            ch.TeleportRotation(Quaternion.LookRotation(look), interpolating: false);
        }

        public static byte CurrentClub()
        {
            try
            {
                Club club = PanelManager.instance?.m_clubSwingPanel?.m_currentClub;
                if (club != null && Enum.TryParse(club.clubName, true, out Clubs c))
                    return (byte)c;
            }
            catch (NullReferenceException)
            {
                // panels not built yet
            }
            return (byte)Clubs.Iron;
        }

        public static byte CurrentBallLook()
        {
            var save = SaveManager.instance;
            if (save == null)
                return 0;
            return (byte)Mathf.Clamp(save.m_unlocks.selectedBall, 0, 254);
        }
    }
}
