using System.Collections.Generic;
using NormalGolfMultiplayer.Net;
using TMPro;
using UnityEngine;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>Everything we know about one other player; survives scene changes (views don't).</summary>
    internal class RemotePlayer
    {
        public PlayerInfo Info;
        public readonly SnapshotBuffer Buffer = new SnapshotBuffer();
        public RemotePlayerView View;

        public float SwingStart = -1000f; // local time the swing animation starts
        public Clubs SwingClub = Clubs.Iron;

        /// <summary>Their Front Nine scorecard, or null if they haven't played a round this session.</summary>
        public ScoreCard Score;

        public bool HasPose;
        public StateFlags Flags;
        public Vector3 Position;
        public Vector3 BallPosition;
    }

    /// <summary>
    /// A remote player's avatar and ball on the course. The game has no player model (hands and golfer are FMV),
    /// so the avatar is a small golfer built from primitives, coloured with the player's colour.
    /// Root sits at the feet and faces +Z.
    /// </summary>
    internal class RemotePlayerView : MonoBehaviour
    {
        private const float BallOffsetFromGolfer = 0.79f; // MoveAndHitController.m_ballPosition.localPosition.x

        private RemotePlayer _player;

        // avatar rig
        private Transform _scaler, _legL, _legR, _torso, _head, _swing, _armL, _armR, _handR, _hands, _club, _clubParent;
        private readonly List<MeshRenderer> _colorParts = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _darkColorParts = new List<MeshRenderer>();
        private TextMeshPro _nameTag;

        // ball
        private Transform _ballRoot, _ballMesh;
        private MeshRenderer _ballRenderer;
        private TrailRenderer _trail;
        private TextMeshPro _ballLabel;
        private float _ballRadius = 0.04f;
        private bool _ballPlaced;
        private byte _ballEpoch;
        private Vector3 _lastBallPos;

        // animation
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _speed, _walkPhase, _crouch, _golfBlend;
        private bool _visible = true;

        // what's currently shown, to detect PlayerInfo changes
        private string _shownName;
        private Color32 _shownColor;
        private int _shownLook = -1;

        public static RemotePlayerView Create(RemotePlayer player, Transform parent)
        {
            var go = new GameObject($"NGMP Player {player.Info.Id}") { layer = Visuals.Layer };
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RemotePlayerView>();
            view._player = player;
            view.BuildAvatar();
            view.BuildBall(parent);
            view.RefreshInfo();
            return view;
        }

        private void OnDestroy()
        {
            if (_ballRoot != null)
                Destroy(_ballRoot.gameObject);
        }

        // ------------------------------------------------------------------ construction

        private void BuildAvatar()
        {
            var S = PrimitiveType.Sphere;
            var C = PrimitiveType.Capsule;
            var Y = PrimitiveType.Cylinder;
            var B = PrimitiveType.Cube;
            Color white = Color.white;

            _scaler = Visuals.Pivot("Scaler", transform, Vector3.zero);

            _legL = Visuals.Pivot("LegL", _scaler, new Vector3(-0.14f, 0.95f, 0f));
            Visuals.Part("Leg", C, _legL, new Vector3(0f, -0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), Visuals.Pants);
            Visuals.Part("Shoe", B, _legL, new Vector3(0f, -0.9f, 0.05f), new Vector3(0.17f, 0.09f, 0.31f), Visuals.Shoes);
            _legR = Visuals.Pivot("LegR", _scaler, new Vector3(0.14f, 0.95f, 0f));
            Visuals.Part("Leg", C, _legR, new Vector3(0f, -0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), Visuals.Pants);
            Visuals.Part("Shoe", B, _legR, new Vector3(0f, -0.9f, 0.05f), new Vector3(0.17f, 0.09f, 0.31f), Visuals.Shoes);

            _torso = Visuals.Pivot("Torso", _scaler, new Vector3(0f, 0.95f, 0f));
            Colored(Visuals.Part("Shirt", C, _torso, new Vector3(0f, 0.42f, 0f), new Vector3(0.56f, 0.46f, 0.36f), white));
            Visuals.Part("Belt", Y, _torso, new Vector3(0f, 0.03f, 0f), new Vector3(0.52f, 0.035f, 0.36f), Visuals.Dark);

            _head = Visuals.Pivot("Head", _torso, new Vector3(0f, 0.82f, 0f));
            Visuals.Part("Neck", Y, _head, new Vector3(0f, 0.03f, 0f), new Vector3(0.14f, 0.06f, 0.14f), Visuals.Skin);
            Visuals.Part("Face", S, _head, new Vector3(0f, 0.2f, 0f), new Vector3(0.38f, 0.4f, 0.38f), Visuals.Skin);
            Visuals.Part("EyeL", S, _head, new Vector3(-0.075f, 0.22f, 0.17f), Vector3.one * 0.05f, Visuals.Dark, shadows: false);
            Visuals.Part("EyeR", S, _head, new Vector3(0.075f, 0.22f, 0.17f), Vector3.one * 0.05f, Visuals.Dark, shadows: false);
            Colored(Visuals.Part("Cap", S, _head, new Vector3(0f, 0.31f, -0.005f), new Vector3(0.41f, 0.24f, 0.41f), white));
            DarkColored(Visuals.Part("Visor", B, _head, new Vector3(0f, 0.285f, 0.2f), new Vector3(0.3f, 0.025f, 0.2f), white,
                Quaternion.Euler(-10f, 0f, 0f)));

            _swing = Visuals.Pivot("Swing", _torso, new Vector3(0f, 0.72f, 0.02f));
            _armL = BuildArm("ArmL", -0.31f, glove: true, out _);
            _armR = BuildArm("ArmR", 0.31f, glove: false, out _handR);
            _hands = Visuals.Pivot("Hands", _swing, new Vector3(0f, -0.62f, 0.3f));

            _club = Visuals.Pivot("Club", _hands, Vector3.zero);
            Visuals.Part("Grip", Y, _club, new Vector3(0f, -0.08f, 0f), new Vector3(0.036f, 0.08f, 0.036f), Visuals.Dark);
            Visuals.Part("Shaft", Y, _club, new Vector3(0f, -0.5f, 0f), new Vector3(0.022f, 0.5f, 0.022f), Visuals.Steel);
            Visuals.Part("Clubhead", B, _club, new Vector3(0f, -1f, 0.035f), new Vector3(0.05f, 0.07f, 0.13f), Visuals.Steel);
            _clubParent = _hands;

            _nameTag = Visuals.Label("NameTag", transform, 3f, white);
            _nameTag.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        }

        private Transform BuildArm(string name, float x, bool glove, out Transform hand)
        {
            var arm = Visuals.Pivot(name, _swing, new Vector3(x, 0f, 0f));
            Colored(Visuals.Part("Sleeve", PrimitiveType.Capsule, arm, new Vector3(0f, -0.14f, 0f), new Vector3(0.18f, 0.16f, 0.18f), Color.white));
            Visuals.Part("Arm", PrimitiveType.Capsule, arm, new Vector3(0f, -0.4f, 0f), new Vector3(0.13f, 0.3f, 0.13f), Visuals.Skin);
            hand = Visuals.Pivot("Hand", arm, new Vector3(0f, -0.7f, 0f));
            Visuals.Part("Hand", PrimitiveType.Sphere, hand, Vector3.zero, Vector3.one * 0.12f, glove ? Visuals.Shoes : Visuals.Skin);
            return arm;
        }

        private void BuildBall(Transform parent)
        {
            var root = new GameObject($"NGMP Ball {_player.Info.Id}") { layer = Visuals.Layer };
            root.transform.SetParent(parent, false);
            _ballRoot = root.transform;

            Mesh mesh = Visuals.BallMesh(out Vector3 scale);
            var meshGo = new GameObject("Mesh") { layer = Visuals.Layer };
            meshGo.transform.SetParent(_ballRoot, false);
            meshGo.transform.localScale = scale;
            meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            _ballRenderer = meshGo.AddComponent<MeshRenderer>();
            _ballMesh = meshGo.transform;
            _ballRadius = Mathf.Max(0.01f, mesh.bounds.extents.x * scale.x);

            _trail = root.AddComponent<TrailRenderer>();
            TrailRenderer template = Visuals.TrailTemplate;
            if (template != null)
            {
                _trail.sharedMaterial = template.sharedMaterial;
                _trail.time = template.time;
                _trail.widthMultiplier = template.widthMultiplier;
                _trail.widthCurve = template.widthCurve;
                _trail.minVertexDistance = template.minVertexDistance;
                _trail.numCapVertices = template.numCapVertices;
                _trail.numCornerVertices = template.numCornerVertices;
                _trail.textureMode = template.textureMode;
                _trail.alignment = template.alignment;
            }
            else
            {
                _trail.time = 6f;
                _trail.widthMultiplier = 0.25f;
            }
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.emitting = false;

            _ballLabel = Visuals.Label("BallLabel", _ballRoot, 1.6f, Color.white);
        }

        private void Colored(Transform part) => _colorParts.Add(part.GetComponent<MeshRenderer>());
        private void DarkColored(Transform part) => _darkColorParts.Add(part.GetComponent<MeshRenderer>());

        /// <summary>Applies name/colour/ball-cosmetic changes.</summary>
        public void RefreshInfo()
        {
            var info = _player.Info;
            if (info.Name == _shownName && info.Color.Equals(_shownColor) && info.BallLook == _shownLook)
                return;
            _shownName = info.Name;
            _shownColor = info.Color;
            _shownLook = info.BallLook;

            Color c = info.Color;
            var mat = Visuals.Lit(c);
            var dark = Visuals.Lit(c * 0.6f + new Color(0f, 0f, 0f, 0.4f));
            foreach (var r in _colorParts) r.sharedMaterial = mat;
            foreach (var r in _darkColorParts) r.sharedMaterial = dark;

            Color labelColor = Color.Lerp(c, Color.white, 0.5f);
            _nameTag.text = info.Name;
            _nameTag.color = labelColor;
            _ballLabel.text = info.Name;
            _ballLabel.color = labelColor;

            _ballRenderer.sharedMaterial = Visuals.BallMaterial(info.BallLook);
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.15f, 1f) });
            _trail.colorGradient = grad;
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(float now, float delay, float dt)
        {
            RefreshInfo();

            if (!_player.Buffer.Sample(now, delay, out PlayerState a, out PlayerState b, out float t) || !b.Has(StateFlags.InWorld))
            {
                SetVisible(false);
                SetBallVisible(false);
                _player.HasPose = false;
                return;
            }
            SetVisible(true);

            bool golfing = b.Has(StateFlags.Golfing);
            // Mode switches and teleports (bunkers, respawns) must snap, not slide across the course.
            bool snap = a.Has(StateFlags.Golfing) != golfing || (b.Pos - a.Pos).sqrMagnitude > 64f;
            Vector3 pos = snap ? b.Pos : Vector3.Lerp(a.Pos, b.Pos, t);
            float yaw = snap ? b.Yaw : Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            float pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);

            if (_hasLastPos && !snap && dt > 0f)
            {
                Vector3 d = pos - _lastPos;
                d.y = 0f;
                _speed = Mathf.Lerp(_speed, Mathf.Min(d.magnitude / dt, 12f), 1f - Mathf.Exp(-dt * 8f));
            }
            else
            {
                _speed = 0f;
            }
            _lastPos = pos;
            _hasLastPos = true;

            // Golfing: pos/yaw are the golfer holder's; the golfer faces the ball, 90 degrees right of the target line.
            float bodyYaw = golfing ? yaw + 90f : yaw;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, bodyYaw, 0f));

            _crouch = Mathf.MoveTowards(_crouch, b.Has(StateFlags.Crouched) ? 1f : 0f, dt * 5f);
            _golfBlend = Mathf.MoveTowards(_golfBlend, golfing ? 1f : 0f, dt * 4f);
            Animate(now, dt, pitch, golfing);
            _nameTag.gameObject.SetActive(ModConfig.ShowNameTags.Value);
            // Leaning over the ball (or crouching) lowers the head, so the tag follows it down.
            float tagHeight = Mathf.Lerp(Mathf.Lerp(2.5f, 2.3f, _golfBlend), 1.95f, _crouch);
            _nameTag.transform.localPosition = new Vector3(0f, tagHeight, 0f);

            UpdateBall(a, b, t, dt);

            _player.HasPose = true;
            _player.Flags = b.Flags;
            _player.Position = pos;
            _player.BallPosition = _ballRoot.position;
        }

        private void Animate(float now, float dt, float pitch, bool golfing)
        {
            _scaler.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.72f, _crouch), 1f);

            float walk = golfing ? 0f : Mathf.Clamp01(_speed / 3f);
            _walkPhase += dt * _speed * 3.2f;
            float legSwing = Mathf.Sin(_walkPhase) * 38f * walk;
            _legL.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
            float bob = Mathf.Abs(Mathf.Cos(_walkPhase)) * 0.05f * walk;
            _torso.localPosition = new Vector3(0f, 0.95f + bob, 0f);

            float swing = 0f;
            float st = now - _player.SwingStart;
            if (golfing && st >= 0f && st < SwingDuration)
                swing = SwingCurve(st) * ClubAmplitude(_player.SwingClub);

            float lean = Mathf.Lerp(walk * 6f, 28f, _golfBlend) + _crouch * 15f;
            float turn = swing * 0.3f;
            _torso.localRotation = Quaternion.Euler(lean, turn, 0f);
            float look = golfing ? 22f : Mathf.Clamp(pitch, -60f, 60f);
            _head.localRotation = Quaternion.Euler(look - lean * 0.7f, -turn * 0.8f, 0f);

            _swing.localRotation = Quaternion.identity;
            if (_golfBlend > 0.5f)
            {
                SetClubParent(_hands);
                AimArm(_armL, _hands.localPosition + new Vector3(-0.03f, 0.02f, 0f));
                AimArm(_armR, _hands.localPosition + new Vector3(0.03f, -0.04f, 0.02f));
                // Address pose: shaft from the hands to the ball, face square to the target (golfer's left).
                Vector3 ballTarget = transform.TransformPoint(new Vector3(0f, 0.04f, BallOffsetFromGolfer));
                Vector3 dir = (ballTarget - _hands.position).normalized;
                _club.rotation = Quaternion.LookRotation(-transform.right, -dir);
                // The swing rotates arms+club around the (leaned) chest axis; positive = backswing to the right.
                _swing.localRotation = Quaternion.Euler(0f, 0f, swing);
            }
            else
            {
                SetClubParent(_handR);
                float armSwing = Mathf.Sin(_walkPhase) * 32f * walk;
                _armL.localRotation = Quaternion.Euler(-armSwing, 0f, -5f);
                _armR.localRotation = Quaternion.Euler(armSwing, 0f, 5f);
                _club.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            }
        }

        private void SetClubParent(Transform parent)
        {
            if (_clubParent == parent)
                return;
            _club.SetParent(parent, false);
            _club.localPosition = Vector3.zero;
            _clubParent = parent;
        }

        private static void AimArm(Transform arm, Vector3 targetInParent)
        {
            Vector3 dir = targetInParent - arm.localPosition;
            if (dir.sqrMagnitude > 1e-6f)
                arm.localRotation = Quaternion.FromToRotation(Vector3.down, dir);
        }

        private const float SwingDuration = 2.6f;

        /// <summary>Swing angle in degrees (+ = backswing). Impact at 0.87 s matches HitSequence's launch delay.</summary>
        private static float SwingCurve(float t)
        {
            const float top = 0.55f, impact = 0.87f, finish = 1.15f, hold = 2.05f;
            if (t < top) return Mathf.SmoothStep(0f, 110f, t / top);
            if (t < impact)
            {
                float u = (t - top) / (impact - top);
                return Mathf.Lerp(110f, 0f, u * u); // accelerate into the ball
            }
            if (t < finish)
            {
                float u = (t - impact) / (finish - impact);
                return Mathf.Lerp(0f, -125f, 1f - (1f - u) * (1f - u));
            }
            if (t < hold) return -125f;
            return Mathf.SmoothStep(-125f, 0f, (t - hold) / (SwingDuration - hold));
        }

        private static float ClubAmplitude(Clubs club)
        {
            switch (club)
            {
                case Clubs.Putter: return 0.22f;
                case Clubs.Wedge: return 0.75f;
                case Clubs.Iron: return 0.88f;
                case Clubs.Hybrid: return 0.95f;
                default: return 1f;
            }
        }

        private void UpdateBall(PlayerState a, PlayerState b, float t, float dt)
        {
            bool visible = b.Has(StateFlags.BallVisible);
            SetBallVisible(visible);
            if (!visible)
            {
                _ballPlaced = false;
                return;
            }

            // A reset/teleport shows up as a new epoch (or an impossible jump): snap and wipe the trail.
            bool jump = a.BallEpoch != b.BallEpoch || (b.BallPos - a.BallPos).sqrMagnitude > 400f;
            Vector3 target = jump ? b.BallPos : Vector3.Lerp(a.BallPos, b.BallPos, t);
            bool snap = !_ballPlaced || b.BallEpoch != _ballEpoch || (jump && (target - _ballRoot.position).sqrMagnitude > 1f);

            if (snap)
            {
                _trail.emitting = false;
                _ballRoot.position = target;
                _trail.Clear();
            }
            else
            {
                if (dt > 0f && b.Has(StateFlags.BallMoving))
                {
                    Vector3 v = (target - _lastBallPos) / dt;
                    v.y = 0f;
                    if (v.sqrMagnitude > 0.01f)
                        _ballMesh.Rotate(Vector3.Cross(Vector3.up, v.normalized), v.magnitude / _ballRadius * Mathf.Rad2Deg * dt, Space.World);
                }
                _ballRoot.position = target;
                _trail.emitting = b.Has(StateFlags.BallTrail);
            }

            _ballPlaced = true;
            _ballEpoch = b.BallEpoch;
            _lastBallPos = target;
            // Right at its owner's feet the name tag already says whose ball it is.
            bool nearOwner = (target - transform.position).sqrMagnitude < 16f;
            _ballLabel.gameObject.SetActive(ModConfig.ShowBallLabels.Value && !nearOwner);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;
            _visible = visible;
            _scaler.gameObject.SetActive(visible);
            _nameTag.gameObject.SetActive(visible && ModConfig.ShowNameTags.Value);
            if (!visible)
                _hasLastPos = false;
        }

        private void SetBallVisible(bool visible)
        {
            if (_ballRoot.gameObject.activeSelf != visible)
                _ballRoot.gameObject.SetActive(visible);
        }

        /// <summary>Called right before each camera renders, so labels face whichever view is drawing them.</summary>
        public void FaceCamera(Camera cam)
        {
            if (_visible && _nameTag.gameObject.activeInHierarchy)
                Billboard(_nameTag.transform, cam, 10f, 1f, 8f, 0f);
            if (_ballLabel.gameObject.activeInHierarchy)
                Billboard(_ballLabel.transform, cam, 6f, 1f, 60f, 0.18f);
        }

        private static void Billboard(Transform label, Camera cam, float referenceDistance, float minScale, float maxScale, float liftPerScale)
        {
            Transform ct = cam.transform;
            float scale = Mathf.Clamp(Vector3.Distance(label.parent.position, ct.position) / referenceDistance, minScale, maxScale);
            label.localScale = Vector3.one * scale;
            if (liftPerScale > 0f)
                label.localPosition = new Vector3(0f, 0.12f + liftPerScale * scale, 0f);
            label.rotation = Quaternion.LookRotation(ct.forward, ct.up);
        }

        public Vector3 AvatarPosition => transform.position;
    }
}
