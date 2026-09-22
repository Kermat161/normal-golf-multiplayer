using System.Collections.Generic;
using NormalGolfMultiplayer.Net;
using UnityEngine;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>
    /// Snapshot interpolation: shows each remote player slightly in the past (the interpolation delay)
    /// so there is always a pair of real snapshots to blend between, even with jitter or a lost packet.
    /// </summary>
    internal class SnapshotBuffer
    {
        private const int Capacity = 48;

        private readonly List<PlayerState> _snapshots = new List<PlayerState>(Capacity);
        private float _clockOffset; // localTime - senderTime, tracking the fastest recent deliveries
        private bool _hasOffset;
        private ushort _lastSeq;
        private bool _hasSeq;

        public bool HasData => _snapshots.Count > 0;
        public PlayerState Latest => _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1] : default;
        public float LastReceiveTime { get; private set; } = -1000f;

        public void Add(PlayerState s, float localNow)
        {
            // Unreliable channel: drop duplicates and anything older than what we already have.
            if (_hasSeq && (short)(s.Seq - _lastSeq) <= 0)
                return;
            _hasSeq = true;
            _lastSeq = s.Seq;
            LastReceiveTime = localNow;

            float sample = localNow - s.Time;
            if (!_hasOffset)
            {
                _clockOffset = sample;
                _hasOffset = true;
            }
            else if (sample < _clockOffset)
            {
                _clockOffset += (sample - _clockOffset) * 0.2f;
            }
            else
            {
                _clockOffset += (sample - _clockOffset) * 0.02f; // latency rose: adapt slowly
            }

            _snapshots.Add(s);
            if (_snapshots.Count > Capacity)
                _snapshots.RemoveAt(0);
        }

        /// <summary>Finds the two snapshots around (now - delay) in the sender's timeline.</summary>
        public bool Sample(float localNow, float delay, out PlayerState from, out PlayerState to, out float t)
        {
            from = to = default;
            t = 0f;
            if (_snapshots.Count == 0)
                return false;

            float renderTime = localNow - _clockOffset - delay;
            while (_snapshots.Count >= 2 && _snapshots[1].Time <= renderTime)
                _snapshots.RemoveAt(0);

            from = _snapshots[0];
            if (_snapshots.Count == 1 || renderTime <= from.Time)
            {
                to = from; // starved (or not started yet): hold the newest/oldest pose
                return true;
            }

            to = _snapshots[1];
            float span = to.Time - from.Time;
            t = span > 1e-4f ? Mathf.Clamp01((renderTime - from.Time) / span) : 1f;
            return true;
        }

        public string Debug(float localNow, float delay)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"count={_snapshots.Count} offset={_clockOffset:0.000} now={localNow:0.000} renderTime={localNow - _clockOffset - delay:0.000} lastSeq={_lastSeq}");
            for (int i = 0; i < _snapshots.Count; i += Mathf.Max(1, _snapshots.Count / 8))
                sb.Append($"\n   [{i}] seq={_snapshots[i].Seq} t={_snapshots[i].Time:0.000} flags={_snapshots[i].Flags} pos={_snapshots[i].Pos}");
            if (_snapshots.Count > 0)
            {
                var last = _snapshots[_snapshots.Count - 1];
                sb.Append($"\n   [last] seq={last.Seq} t={last.Time:0.000} flags={last.Flags} pos={last.Pos}");
            }
            return sb.ToString();
        }

        public void Clear()
        {
            _snapshots.Clear();
            _hasSeq = false;
            _hasOffset = false;
        }
    }
}
