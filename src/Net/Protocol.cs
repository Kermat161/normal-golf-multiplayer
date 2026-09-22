using System;
using System.Text;
using LiteNetLib.Utils;
using UnityEngine;

namespace NormalGolfMultiplayer.Net
{
    internal static class Protocol
    {
        public const string Magic = "NGMP";
        /// <summary>Bump whenever the wire format changes; mismatched peers are refused with a clear message.</summary>
        public const ushort Version = 2;
        public const int DefaultPort = 7777;
        public const int MaxNameLength = 20;
        public const int MaxChatLength = 120;
        public const float SendInterval = 1f / 20f;
        public const byte HostId = 1;

        /// <summary>Strips control characters and rich-text brackets and clamps length, so names/chat can't break labels.</summary>
        public static string Clean(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new StringBuilder(Math.Min(s.Length, maxLength));
            foreach (char c in s)
            {
                if (char.IsControl(c) || c == '<' || c == '>')
                    continue;
                sb.Append(c);
                if (sb.Length >= maxLength)
                    break;
            }
            return sb.ToString().Trim();
        }
    }

    /// <summary>Every packet is [Msg][playerId][payload]. Clients may put anything in playerId; the host overwrites it.</summary>
    internal enum Msg : byte
    {
        Welcome = 1,      // host -> new client: [yourInfo][count][PlayerInfo...]
        PlayerJoined = 2, // host -> clients: [PlayerInfo]
        PlayerLeft = 3,   // host -> clients: (id only)
        PlayerInfo = 4,   // both ways: [PlayerInfo] (name/colour/ball changed)
        State = 5,        // both ways, unreliable: [PlayerState]
        Shot = 6,         // both ways: [club][power][shotType]
        Holed = 7,        // both ways: [strokes][par]
        Chat = 8,         // both ways: [text]
        Score = 9,        // both ways: [ScoreCard] — the sender's Front Nine round
    }

    [Flags]
    internal enum StateFlags : byte
    {
        None = 0,
        InWorld = 1,      // on the course (Main scene); otherwise hide the avatar and ball
        Golfing = 2,      // standing over the ball in golf mode (pos/yaw are the golfer holder's)
        Crouched = 4,
        BallVisible = 8,
        BallTrail = 16,   // trail emitting: ball is in flight/rolling after a hit
        BallMoving = 32,  // rigidbody is simulating (not resting)
        PlayNine = 64,    // playing "Play Nine" rather than story mode
    }

    internal class PlayerInfo
    {
        public byte Id;
        public string Name = "";
        public Color32 Color = new Color32(255, 255, 255, 255);
        public byte BallLook;

        public void Write(NetDataWriter w)
        {
            w.Put(Id);
            w.Put(Name, Protocol.MaxNameLength * 2);
            w.Put(Color.r);
            w.Put(Color.g);
            w.Put(Color.b);
            w.Put(BallLook);
        }

        public static PlayerInfo Read(NetDataReader r)
        {
            var p = new PlayerInfo { Id = r.GetByte() };
            p.Name = Protocol.Clean(r.GetString(Protocol.MaxNameLength * 2), Protocol.MaxNameLength);
            p.Color = new Color32(r.GetByte(), r.GetByte(), r.GetByte(), 255);
            p.BallLook = r.GetByte();
            if (p.Name.Length == 0)
                p.Name = "Golfer";
            return p;
        }

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();
    }

    /// <summary>One network tick of a player's pose and ball. ~45 bytes.</summary>
    internal struct PlayerState
    {
        public ushort Seq;
        public float Time;        // sender's session clock, used for interpolation
        public StateFlags Flags;
        public Vector3 Pos;       // feet position (walking) or golfer-holder position (golfing)
        public float Yaw;         // character yaw (walking) or golfer-holder yaw, i.e. target line (golfing)
        public float Pitch;       // camera pitch, degrees
        public byte Club;         // Clubs enum
        public Vector3 BallPos;
        public byte BallEpoch;    // bumps when the ball is teleported/reset, so receivers snap instead of sliding

        public bool Has(StateFlags f) => (Flags & f) != 0;

        public void Write(NetDataWriter w)
        {
            w.Put(Seq);
            w.Put(Time);
            w.Put((byte)Flags);
            PutVec(w, Pos);
            w.Put(Yaw);
            w.Put((sbyte)Mathf.Clamp(Mathf.RoundToInt(Pitch), -90, 90));
            w.Put(Club);
            PutVec(w, BallPos);
            w.Put(BallEpoch);
        }

        public static PlayerState Read(NetDataReader r)
        {
            return new PlayerState
            {
                Seq = r.GetUShort(),
                Time = r.GetFloat(),
                Flags = (StateFlags)r.GetByte(),
                Pos = GetVec(r),
                Yaw = r.GetFloat(),
                Pitch = r.GetSByte(),
                Club = r.GetByte(),
                BallPos = GetVec(r),
                BallEpoch = r.GetByte(),
            };
        }

        private static void PutVec(NetDataWriter w, Vector3 v)
        {
            w.Put(v.x);
            w.Put(v.y);
            w.Put(v.z);
        }

        private static Vector3 GetVec(NetDataReader r) => new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
    }

    /// <summary>
    /// A player's "Luke Muscat's Front Nine" round. Scores come straight from the game's own counters
    /// (<c>RunSave.lmugcScore</c> / <c>Ball.m_currentShot</c>), so retake and water penalties are already included.
    /// </summary>
    internal class ScoreCard
    {
        public const int HoleCount = 9;

        public byte RoundId;      // increments per round, so a restart is distinguishable from an update
        public bool HasRound;     // false until the player starts their first round this session
        public bool Active;       // round still in progress
        public byte CurrentHole;  // 1-9, or 0 when between rounds
        public byte Strokes;      // strokes played so far on CurrentHole
        public readonly byte[] Scores = new byte[HoleCount]; // per hole; 0 = not played yet

        public int PlayedCount
        {
            get
            {
                int n = 0;
                foreach (byte s in Scores)
                    if (s > 0) n++;
                return n;
            }
        }

        public int Total
        {
            get
            {
                int t = 0;
                foreach (byte s in Scores)
                    t += s;
                return t;
            }
        }

        public void Reset(byte roundId, bool active)
        {
            RoundId = roundId;
            HasRound = true;
            Active = active;
            CurrentHole = (byte)(active ? 1 : 0);
            Strokes = 0;
            for (int i = 0; i < Scores.Length; i++)
                Scores[i] = 0;
        }

        public void CopyFrom(ScoreCard other)
        {
            RoundId = other.RoundId;
            HasRound = other.HasRound;
            Active = other.Active;
            CurrentHole = other.CurrentHole;
            Strokes = other.Strokes;
            Array.Copy(other.Scores, Scores, HoleCount);
        }

        public bool Matches(ScoreCard other)
        {
            if (other == null || RoundId != other.RoundId || HasRound != other.HasRound || Active != other.Active ||
                CurrentHole != other.CurrentHole || Strokes != other.Strokes)
                return false;
            for (int i = 0; i < HoleCount; i++)
                if (Scores[i] != other.Scores[i])
                    return false;
            return true;
        }

        public void Write(NetDataWriter w)
        {
            w.Put(RoundId);
            w.Put((byte)((HasRound ? 1 : 0) | (Active ? 2 : 0)));
            w.Put(CurrentHole);
            w.Put(Strokes);
            foreach (byte s in Scores)
                w.Put(s);
        }

        public static ScoreCard Read(NetDataReader r)
        {
            var c = new ScoreCard { RoundId = r.GetByte() };
            byte flags = r.GetByte();
            c.HasRound = (flags & 1) != 0;
            c.Active = (flags & 2) != 0;
            c.CurrentHole = Math.Min(r.GetByte(), (byte)HoleCount);
            c.Strokes = r.GetByte();
            for (int i = 0; i < HoleCount; i++)
                c.Scores[i] = r.GetByte();
            return c;
        }
    }

    internal struct ShotEvent
    {
        public byte Club;
        public float Power;
        public byte ShotType;

        public void Write(NetDataWriter w)
        {
            w.Put(Club);
            w.Put(Power);
            w.Put(ShotType);
        }

        public static ShotEvent Read(NetDataReader r) =>
            new ShotEvent { Club = r.GetByte(), Power = r.GetFloat(), ShotType = r.GetByte() };
    }

    internal struct HoledEvent
    {
        public byte Strokes; // 0 = not a scored hole (e.g. a story challenge)
        public byte Par;

        public void Write(NetDataWriter w)
        {
            w.Put(Strokes);
            w.Put(Par);
        }

        public static HoledEvent Read(NetDataReader r) => new HoledEvent { Strokes = r.GetByte(), Par = r.GetByte() };

        public string Describe()
        {
            if (Strokes == 0)
                return "holed it!";
            if (Strokes == 1)
                return "got a HOLE IN ONE!";
            string shots = $"{Strokes} strokes";
            if (Par == 0)
                return $"holed out in {shots}";
            int diff = Strokes - Par;
            string name = diff switch
            {
                -3 => "Albatross",
                -2 => "Eagle",
                -1 => "Birdie",
                0 => "Par",
                1 => "Bogey",
                2 => "Double bogey",
                3 => "Triple bogey",
                _ => diff < 0 ? $"{-diff} under par" : $"{diff} over par",
            };
            return $"holed out in {shots} ({name})";
        }
    }
}
