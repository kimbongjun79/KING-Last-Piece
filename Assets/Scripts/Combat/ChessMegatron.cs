using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// 체스메가트론 (boss): a colossus built of black pawns only, three squares by three, standing on four legs — the King
    /// can walk under it (only its feet block him). Every attack strikes whole squares, shown on the board before they
    /// land. Below half health it promotes (a crown of pawns grows on its head): its attacks reach a little farther and
    /// it gains a two-armed sweep, a queen's lines and a storm of thrown pawns. The body is procedural (ChessMegatron.Rig).
    /// <list type="bullet">
    /// <item>Facing: the boss settles onto a square and squares up to one of the four board directions before most
    /// attacks, so "ahead" and "beside" are board squares. It only attacks what is roughly in front of it — slipping to
    /// its side or behind buys time while it turns.</item>
    /// <item>Reading it: big attacks hold their raised pose for a random moment (the telegraph keeps filling); the smash
    /// re-aims its second fist at where the King is by then; staying under its body for 1.5 s makes it sit down on him;
    /// now and then it leaps to the middle and sweeps the whole board with both arms (only under its body is safe).</item>
    /// </list>
    /// </summary>
    public partial class ChessMegatron : EnemyBase
    {
        public enum Pattern { Charge, Smash, Sweep, Drop, Sit, Wheel, Promotion, DoubleSweep, QueenLines, Storm }

        public const float MaxHp = 3000f;
        const float TurnSpeed = 70f;
        /// <summary>The King under the body this long (its 3×3 squares) makes it sit down on him.</summary>
        const float UnderLimit = 1.5f;
        /// <summary>It attacks what is within this angle of its facing (otherwise it turns first).</summary>
        const float FrontAngle = 75f;

        Pattern current;
        int step;           // which hit of a multi-hit pattern (smash 2, wheel 2, storm 3)
        int side = 1;       // +1 right, −1 left (in the attack's board frame)
        bool phase2;
        float underTime;    // seconds the King has stood under the body
        /// <summary>The wheel (leap to the middle, sweep the whole board) is a big move: not before this time.</summary>
        float nextWheelAt;
        readonly List<(Pattern p, int step)> queue = new List<(Pattern, int)>();

        // The board frame of the current attack: centre square, forward and right (one of the four board directions).
        Vector2Int center, fwd, right;
        /// <summary>Where the smash's fist comes down (the King's square when it was aimed).</summary>
        Vector2Int smashTarget;
        bool settle, lockFacing, reaim;
        Vector3 settleFrom, settleTo, advanceTo;
        Quaternion settleFromRot, lockRot;
        int advance;        // squares the charge carries the body forward
        /// <summary>Seconds the raised pose takes in the current wind-up (the rest of the wind-up is the hold).</summary>
        float raiseTime;

        public bool Phase2 => phase2;
        public Pattern CurrentPattern => current;
        public float UnderTime => underTime;
        /// <summary>Lab: no thinking (stands still).</summary>
        public bool DebugHold { get; set; }
        /// <summary>Lab: walks and turns but never attacks.</summary>
        public bool DebugNoAttack { get; set; }

        protected override void Configure()
        {
            DisplayName = "체스메가트론";
            IsBoss = true;
            SetHealth(MaxHp);
            SetRadius(2.75f);
            moveSpeed = 1.5f;
            maxPoise = 420f;
            staggerDuration = 2.4f;
            hitShove = 0f;
            nextWheelAt = Time.time + 12f;
            BuildRig();
        }

        /// <summary>The body doesn't shove the King (he can go under it): only its feet do (see PushKingOffFeet).</summary>
        public override bool IgnoresCollision => true;

        public override string AttackLabel => step > 0 ? $"{current}+{step}" : current.ToString();

        protected override string ReleaseSfx => current switch
        {
            Pattern.Drop => "QUEEN_Leap",
            Pattern.Charge => "ROOK_Ram",
            Pattern.Wheel => step == 0 ? "QUEEN_Leap" : "ROOK_Ram",
            Pattern.Sweep => "BISHOP_Charge",
            Pattern.DoubleSweep => "BISHOP_Charge",
            Pattern.Storm => "THR_Release",
            _ => null,
        };

        /// <summary>The wheel's arms strike each square (and the King) as they sweep over it, not all at once.</summary>
        protected override bool StrikesItself => current == Pattern.Wheel && step == 1;

        protected override string LandSfx => current switch
        {
            Pattern.Drop => "QUEEN_Land",
            Pattern.Charge => "ROOK_WallHit",
            Pattern.Wheel => step == 0 ? "QUEEN_Land" : null,
            Pattern.Sit => "QUEEN_Check_Slam",
            Pattern.Sweep => "HIT_Heavy",
            Pattern.DoubleSweep => "HIT_Heavy",
            Pattern.Storm => "ROOK_Slam",
            _ => "QUEEN_Check_Slam",
        };

        // ---------------------------------------------------------------- thinking

        protected override void Think(float dt)
        {
            if (DebugHold) return;
            Vector3 to = ToPlayer;
            float d = to.magnitude;
            if (!DebugNoAttack)
            {
                if (!phase2 && Health <= MaxHealth * 0.5f) { StartPattern(Pattern.Promotion); return; }
                // Camping under the body: it sits down on him, whatever else it was about to do.
                if (underTime >= UnderLimit) { StartPattern(Pattern.Sit); return; }
            }

            FaceTowards(to, TurnSpeed);
            bool inFront = Vector3.Angle(transform.forward, to) <= FrontAngle;

            if (cooldown <= 0f && !DebugNoAttack)
            {
                // Now and then the big move: leap to the middle and sweep the whole board.
                if (Time.time >= nextWheelAt && underTime <= 0f && Random.value < 0.3f) { StartPattern(Pattern.Wheel); return; }
                if (d > 9f)
                {
                    if (Random.value < (phase2 ? 0.35f : 0.55f)) { StartPattern(Pattern.Drop); return; }
                    if (phase2 && Random.value < 0.5f) { StartPattern(Pattern.Storm); return; }
                }
                else if (d <= 7.4f && inFront && underTime <= 0f)
                {
                    StartPattern(PickFront());
                    return;
                }
                cooldown = 0.3f;   // nothing fits yet: keep turning / walking and decide again shortly
            }

            if (d > 6.2f) MoveTowards(Player.FlatPos - to / Mathf.Max(0.01f, d) * 5.2f, moveSpeed, dt);
        }

        Pattern PickFront()
        {
            float charge = 3f, smash = 3f, sweep = 3f;
            float dbl = phase2 ? 3f : 0f, lines = phase2 ? 2f : 0f, storm = phase2 ? 1.2f : 0f;
            float r = Random.value * (charge + smash + sweep + dbl + lines + storm);
            if ((r -= charge) < 0f) return Pattern.Charge;
            if ((r -= smash) < 0f) return Pattern.Smash;
            if ((r -= sweep) < 0f) return Pattern.Sweep;
            if ((r -= dbl) < 0f) return Pattern.DoubleSweep;
            if ((r -= lines) < 0f) return Pattern.QueenLines;
            return Pattern.Storm;
        }

        protected override bool ContinueCombo()
        {
            if (queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);
                StartPattern(next.p, next.step);
                return true;
            }
            cooldown = phase2 ? Random.Range(0.5f, 1.1f) : Random.Range(0.9f, 1.6f);
            return false;
        }

        // ---------------------------------------------------------------- the board frame

        static int Dot(Vector2Int a, Vector2Int b) => a.x * b.x + a.y * b.y;

        static Vector2Int Orth(Vector2Int v) =>
            Mathf.Abs(v.x) >= Mathf.Abs(v.y) ? new Vector2Int(v.x >= 0 ? 1 : -1, 0) : new Vector2Int(0, v.y >= 0 ? 1 : -1);

        static Vector2Int Orth(Vector3 v) =>
            Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector2Int(v.x >= 0f ? 1 : -1, 0) : new Vector2Int(0, v.z >= 0f ? 1 : -1);

        /// <summary>A square the whole 3×3 body fits around.</summary>
        static Vector2Int ClampCenter(Vector2Int s) =>
            new Vector2Int(Mathf.Clamp(s.x, 1, BoardSlots.Size - 2), Mathf.Clamp(s.y, 1, BoardSlots.Size - 2));

        /// <summary>Sets the attack frame facing the King: centre square, forward toward him, right; and his offset.</summary>
        void FrameToKing(out Vector2Int rel)
        {
            center = ClampCenter(MySquare);
            rel = KingSquare - center;
            fwd = rel == Vector2Int.zero ? Orth(transform.forward) : Orth(rel);
            right = new Vector2Int(fwd.y, -fwd.x);
        }

        /// <summary>Sets the attack frame along the body's own facing (snapped to the board).</summary>
        void FrameAlongBody()
        {
            center = ClampCenter(MySquare);
            fwd = Orth(transform.forward);
            right = new Vector2Int(fwd.y, -fwd.x);
        }

        /// <summary>A square <paramref name="a"/> ahead and <paramref name="b"/> to the right of the centre.</summary>
        Vector2Int S(int a, int b) => center + fwd * a + right * b;

        /// <summary>The squares <paramref name="a0"/>..<paramref name="a1"/> ahead by <paramref name="b0"/>..<paramref name="b1"/> to the right.</summary>
        List<Vector2Int> Rect(int a0, int a1, int b0, int b1)
        {
            var list = new List<Vector2Int>();
            for (int a = a0; a <= a1; a++)
            for (int b = b0; b <= b1; b++) list.Add(S(a, b));
            return list;
        }

        static Vector2Int[] Clip(IEnumerable<Vector2Int> squares)
        {
            var list = new List<Vector2Int>();
            foreach (var s in squares)
                if (BoardSlots.OnBoard(s) && !list.Contains(s)) list.Add(s);
            return list.ToArray();
        }

        Vector2Int[] Ring(Vector2Int c, int r)
        {
            var list = new List<Vector2Int>();
            for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) == r) list.Add(c + new Vector2Int(x, y));
            return Clip(list);
        }

        /// <summary>A square and its four straight neighbours.</summary>
        static List<Vector2Int> Plus(Vector2Int c) =>
            new List<Vector2Int> { c, c + Vector2Int.up, c + Vector2Int.down, c + Vector2Int.left, c + Vector2Int.right };

        bool FitsAt(Vector2Int c) => c.x >= 1 && c.y >= 1 && c.x <= BoardSlots.Size - 2 && c.y <= BoardSlots.Size - 2;

        // ---------------------------------------------------------------- patterns

        float WindupScale => phase2 ? 0.85f : 1f;

        void StartPattern(Pattern p, int stepIndex = 0)
        {
            current = p;
            step = stepIndex;
            if (stepIndex == 0) queue.Clear();
            float wf = WindupScale;
            settle = lockFacing = reaim = false;
            advance = 0;
            AttackPlan plan;

            switch (p)
            {
                case Pattern.Charge:
                {
                    // Head down, then a charge three squares ahead through the lane in front of it (one more once promoted).
                    FrameToKing(out _);
                    raiseTime = 1.1f * wf;
                    float hold = Random.Range(0f, 0.5f);
                    var squares = Rect(2, phase2 ? 5 : 4, -1, 1);
                    advance = 3;
                    while (advance > 0 && !FitsAt(center + fwd * advance)) advance--;
                    bool follow = phase2 && Random.value < 0.5f;
                    if (follow) queue.Add((Pattern.Smash, 0));
                    plan = Zone(raiseTime + hold, 0.36f, follow ? 0.3f : 1.3f, 32f, 60f, squares, S(2, 0));
                    settle = lockFacing = true;
                    break;
                }

                case Pattern.Smash:
                {
                    // A fist raised high, then hammered onto the King's square; the second fist re-aims at where he is then.
                    FrameToKing(out var rel);
                    if (stepIndex == 0)
                    {
                        int b = Dot(rel, right);
                        side = b != 0 ? (b > 0 ? 1 : -1) : (Random.value < 0.5f ? -1 : 1);
                        queue.Insert(0, (Pattern.Smash, 1));
                        settle = true;
                    }
                    else
                    {
                        side = -side;   // the other fist
                        reaim = true;   // turn toward his new square during the short wind-up
                    }
                    lockFacing = true;
                    smashTarget = KingSquare;
                    raiseTime = (stepIndex == 0 ? 0.95f : 0.55f) * wf;
                    float hold = Random.Range(0f, stepIndex == 0 ? 0.5f : 0.45f);
                    var squares = phase2 ? new List<Vector2Int>(BlockOf(smashTarget, 1)) : Plus(smashTarget);
                    plan = Zone(raiseTime + hold, 0.14f, stepIndex == 0 ? 0.22f : 1.2f, 26f, 45f, squares, smashTarget);
                    break;
                }

                case Pattern.Sweep:
                {
                    // One arm raised out to the side, then swept low across the front.
                    FrameToKing(out var rel);
                    int b = Dot(rel, right);
                    side = b != 0 ? (b > 0 ? 1 : -1) : (Random.value < 0.5f ? -1 : 1);
                    raiseTime = 1.0f * wf;
                    float hold = Random.Range(0f, 0.35f);
                    plan = Zone(raiseTime + hold, 0.3f, 1.0f, 26f, 45f, Rect(2, phase2 ? 4 : 3, -2, 2), S(2, side * 2));
                    settle = lockFacing = true;
                    break;
                }

                case Pattern.DoubleSweep:
                {
                    // Promoted: both arms raised out to the sides, then swept in together — wider and quicker than the sweep.
                    FrameToKing(out _);
                    raiseTime = 0.7f;
                    var squares = Rect(2, 4, -3, 3);
                    squares.AddRange(Rect(0, 1, 2, 3));
                    squares.AddRange(Rect(0, 1, -3, -2));
                    plan = Zone(raiseTime, 0.25f, 1.1f, 28f, 50f, squares, S(2, 0));
                    settle = lockFacing = true;
                    break;
                }

                case Pattern.Drop:
                {
                    // Crouch, leap onto the King's square (the body's 3×3 there; promoted, plus one square out each way).
                    var target = ClampCenter(KingSquare);
                    raiseTime = 1.35f * wf;
                    var squares = new List<Vector2Int>(BlockOf(target, 1));
                    if (phase2) squares.AddRange(new[] { target + new Vector2Int(2, 0), target + new Vector2Int(-2, 0), target + new Vector2Int(0, 2), target + new Vector2Int(0, -2) });
                    plan = new AttackPlan
                    {
                        kind = AtkKind.Leap, target = BoardSlots.Center(target), windup = raiseTime, active = 0.8f, recover = 1.7f,
                        damage = 38f, poise = 70f, radius = 3f, squares = Clip(squares),
                    };
                    break;
                }

                case Pattern.Sit:
                {
                    // The King has camped under it: it rises on straightened legs, then sits down hard on the squares under
                    // and round the body, all at once (promoted, the middle of each side one square farther).
                    FrameAlongBody();
                    underTime = 0f;
                    raiseTime = 1.0f * wf;
                    var squares = new List<Vector2Int>(BlockOf(center, 2));
                    if (phase2)
                        for (int k = -1; k <= 1; k++)
                            squares.AddRange(new[] { S(3, k), S(-3, k), S(k, 3), S(k, -3) });
                    plan = Zone(raiseTime, 0.2f, 1.6f, 30f, 60f, squares, center);
                    settle = lockFacing = true;
                    break;
                }

                case Pattern.Wheel:
                {
                    if (stepIndex == 0)
                    {
                        // The big move: a leap to the middle of the board (landing on whatever is there)...
                        var mid = MiddleSquare();
                        raiseTime = 1.0f * wf;
                        queue.Insert(0, (Pattern.Wheel, 1));
                        nextWheelAt = Time.time + 20f;
                        plan = new AttackPlan
                        {
                            kind = AtkKind.Leap, target = BoardSlots.Center(mid), windup = raiseTime, active = 0.9f, recover = 0.25f,
                            damage = 20f, poise = 40f, radius = 3f, squares = Clip(BlockOf(mid, 1)),
                        };
                        break;
                    }
                    // ...then both arms flung out, a slow turn sweeping the whole board but the squares under the body.
                    // Each square (and the King) is struck as an arm reaches it.
                    FrameAlongBody();
                    raiseTime = 1.2f * wf;
                    var all = new List<Vector2Int>();
                    for (int x = 0; x < BoardSlots.Size; x++)
                    for (int y = 0; y < BoardSlots.Size; y++)
                    {
                        var sq = new Vector2Int(x, y);
                        if (BoardSlots.Chebyshev(sq, center) >= 2) all.Add(sq);
                    }
                    plan = Zone(raiseTime, phase2 ? 2.5f : 3.0f, 1.6f, 26f, 45f, all, center);
                    lockFacing = true;
                    break;
                }

                case Pattern.Promotion:
                {
                    phase2 = true;
                    FrameAlongBody();
                    raiseTime = 2.0f;
                    Game.ShowBanner("체스메가트론 — 프로모션");
                    Sfx.Play("QUEEN_Phase2");
                    Effects.DarkWave(transform.position, 6f, 0.9f, 3);
                    plan = Zone(raiseTime, 0.15f, 1.2f, 16f, 80f, Ring(center, 2), S(2, 0));
                    settle = lockFacing = true;
                    break;
                }

                case Pattern.QueenLines:
                {
                    FrameToKing(out _);
                    raiseTime = 1.25f * wf;
                    float hold = Random.Range(0f, 0.5f);
                    var squares = new List<Vector2Int>();
                    foreach (var s in LinesOf(center, true, true))
                        if (BoardSlots.Chebyshev(s, center) >= 2) squares.Add(s);
                    plan = Zone(raiseTime + hold, 0.15f, 1.3f, 30f, 55f, squares, S(2, 0));
                    settle = lockFacing = true;
                    break;
                }

                default: // Storm
                {
                    if (stepIndex == 0)
                    {
                        FrameToKing(out _);
                        queue.Insert(0, (Pattern.Storm, 1));
                        queue.Insert(1, (Pattern.Storm, 2));
                        side = Random.value < 0.5f ? -1 : 1;
                        settle = true;
                    }
                    else side = -side;   // throws with alternate hands
                    raiseTime = (stepIndex == 0 ? 0.9f : 0.55f) * wf;
                    var king = KingSquare;
                    var squares = new List<Vector2Int> { king };
                    for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                    {
                        var s = king + new Vector2Int(x, y);
                        if (s != king && Random.value < 0.3f) squares.Add(s);
                    }
                    squares.RemoveAll(s => BoardSlots.Chebyshev(s, center) < 2);
                    if (squares.Count == 0) squares.Add(S(3, 0));
                    plan = Zone(raiseTime, 0.55f, stepIndex < 2 ? 0.08f : 1.2f, 20f, 30f, squares, king);
                    break;
                }
            }

            BeginAttack(plan);
            settleFrom = FlatPos;
            settleFromRot = transform.rotation;
            settleTo = settle ? BoardSlots.Center(center) : FlatPos;
            lockRot = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.y));
            if (!settle && lockFacing && !reaim) settleFromRot = lockRot;
            advanceTo = settleTo + new Vector3(fwd.x, 0f, fwd.y) * (GameManager.TileSize * advance);
            cooldown = 99f;   // the chain decides when it ends (ContinueCombo)
        }

        AttackPlan Zone(float windup, float active, float recover, float damage, float poise, IEnumerable<Vector2Int> squares, Vector2Int focus) =>
            new AttackPlan
            {
                kind = AtkKind.Zone, windup = windup, active = active, recover = recover, damage = damage, poise = poise,
                squares = Clip(squares), target = BoardSlots.Center(focus),
            };

        // ---------------------------------------------------------------- per frame

        EState prevState = EState.Spawning;

        protected override void Update()
        {
            base.Update();
            if (state == EState.Stagger || state == EState.Dead) queue.Clear();

            if (!IsDead && state != EState.Spawning)
            {
                bool attacking = state == EState.Windup || state == EState.Attack || state == EState.Recover;
                if (state == EState.Windup && (settle || lockFacing))
                {
                    // Settle onto the square and square up to the board direction early in the wind-up.
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stateTime / Mathf.Max(0.01f, plan.windup * 0.35f)));
                    if (settle) transform.position = Vector3.Lerp(settleFrom, settleTo, k);
                    if (lockFacing) transform.rotation = Quaternion.Slerp(settleFromRot, lockRot, k);
                }
                else if (attacking && lockFacing) transform.rotation = lockRot;

                // The charge carries the body forward.
                if (advance > 0 && current == Pattern.Charge)
                {
                    if (state == EState.Attack)
                        transform.position = Vector3.Lerp(settleTo, advanceTo, PoseTrack.Curve(Ease.InOut, Mathf.Clamp01(stateTime / plan.active)));
                    else if (state == EState.Recover) transform.position = advanceTo;
                }

                // The whole 3×3 body stays on the board.
                float h = GameManager.BoardHalf - GameManager.TileSize * 1.5f;
                var p = transform.position;
                transform.position = new Vector3(Mathf.Clamp(p.x, -h, h), p.y, Mathf.Clamp(p.z, -h, h));

                // The King under the body (its 3×3 squares): the clock toward the wheel.
                bool under = Player != null && !Player.IsDead && BoardSlots.Chebyshev(KingSquare, ClampCenter(MySquare)) <= 1;
                underTime = under ? underTime + Time.deltaTime : 0f;

                if (prevState == EState.Spawning) Landed();
            }

            TickWheelStrike();
            TickRig(Time.deltaTime);
            PushKingOffFeet();
            prevState = state;
        }

        // ---------------------------------------------------------------- the wheel's sweep

        /// <summary>
        /// The upper body's turn during the wheel's sweep at progress <paramref name="a"/> (0–1): exactly one full turn at
        /// a steady pace, then it stops. With the arms straight out to both sides, every square is swept twice.
        /// </summary>
        public const float WheelFrom = 0f, WheelTurn = 360f;

        static float WheelYaw(float a) => WheelFrom + WheelTurn * Mathf.Clamp01(a);

        /// <summary>The middle of the board for the 3×3 body: the nearest of the four central squares.</summary>
        Vector2Int MiddleSquare()
        {
            int lo = BoardSlots.Size / 2 - 1, hi = BoardSlots.Size / 2;
            Vector2Int best = new Vector2Int(lo, lo);
            float bestD = float.MaxValue;
            foreach (var c in new[] { new Vector2Int(lo, lo), new Vector2Int(lo, hi), new Vector2Int(hi, lo), new Vector2Int(hi, hi) })
            {
                float d = (BoardSlots.Center(c) - FlatPos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        /// <summary>The arms stick straight out to the sides: their angles off the body's facing.</summary>
        static readonly float[] ArmAngles = { 90f, -90f };

        /// <summary>A square's warning lights this long before an arm reaches it, filling red as the arm comes.</summary>
        const float WarnLead = 0.8f;

        /// <summary>One arm passing over one square: when (seconds from the wind-up's start) and its warning.</summary>
        class WheelPass
        {
            public Vector2Int square;
            public float time;
            public Telegraph warning;
            public bool done;
        }

        readonly List<WheelPass> wheelPasses = new List<WheelPass>();
        float wheelPrevYaw, wheelNextHit;

        protected override bool DrawsOwnTelegraph => current == Pattern.Wheel && step == 1;

        /// <summary>
        /// The wheel's sweep, square by square: each square's warning lights <see cref="WarnLead"/> s before an arm
        /// reaches it and fills as the arm comes; when the arm passes, the square flares and goes out, and the King is hit
        /// if he stands on it. Both arms pass over every square once in the full turn, each pass warned on its own.
        /// </summary>
        void TickWheelStrike()
        {
            bool sweeping = !IsDead && current == Pattern.Wheel && step == 1 && (state == EState.Windup || state == EState.Attack);
            if (!sweeping)
            {
                ClearWheelPending();
                return;
            }
            if (state == EState.Windup && prevState != EState.Windup) PlanWheelPasses();
            float now = state == EState.Windup ? stateTime : plan.windup + stateTime;
            wheelPrevYaw = state == EState.Attack ? WheelYaw(stateTime / Mathf.Max(0.01f, plan.active)) : 0f;
            if (state == EState.Attack && prevState != EState.Attack) Sfx.Play("THR_Swing_Loop", transform.position);

            bool struck = false;
            foreach (var pass in wheelPasses)
            {
                if (pass.done) continue;
                if (pass.warning == null && now >= pass.time - WarnLead)
                    pass.warning = Telegraph.Squares(new[] { pass.square }, WarnLead, pass.square);
                if (pass.warning != null) pass.warning.SetProgress((now - (pass.time - WarnLead)) / WarnLead);
                if (state != EState.Attack || now < pass.time) continue;

                // The arm is over the square now.
                pass.done = true;
                if (pass.warning != null) Destroy(pass.warning.gameObject);
                pass.warning = null;
                Vector3 c = BoardSlots.Center(pass.square);
                SquareRingFx.Spawn(c, GameManager.TileSize * 0.55f, Vfx.Ember, 0.35f, 0f);
                Effects.Sparks(c + Vector3.up * 0.4f, new Color(0.7f, 0.12f, 0.05f), 3, 3f);
                struck = true;

                if (Player == null || Player.IsDead || Time.time < wheelNextHit) continue;
                float gap = SquaresGap(new[] { pass.square }, Player.FlatPos);
                if (gap <= Player.Radius)
                {
                    if (Player.TakeHitFrom(this, plan.damage, plan.poise))
                    {
                        wheelNextHit = Time.time + 0.5f;
                        Sfx.Play("HIT_Heavy", Player.transform.position + Vector3.up);
                    }
                }
                else if (gap <= Player.Radius + PlayerController.NearMissMargin) Player.NotifyNearMiss(this);
            }
            if (struck) Game.Rig.Shake(0.06f);
        }

        /// <summary>When each arm reaches each struck square, from the turn's steady pace and the facing it settles into.</summary>
        void PlanWheelPasses()
        {
            ClearWheelPending();
            wheelNextHit = 0f;
            if (plan.squares == null) return;
            var inv = Quaternion.Inverse(lockRot);
            foreach (var sq in plan.squares)
            {
                Vector3 l = inv * (BoardSlots.Center(sq) - FlatPos);
                float phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                foreach (float arm in ArmAngles)
                {
                    float turn = Mathf.Repeat(phi - (WheelFrom + arm), 360f);
                    wheelPasses.Add(new WheelPass { square = sq, time = plan.windup + plan.active * turn / WheelTurn });
                }
            }
        }

        static float SquaresGap(Vector2Int[] squares, Vector3 p)
        {
            if (squares == null) return float.MaxValue;
            float h = GameManager.TileSize * 0.5f, best = float.MaxValue;
            foreach (var s in squares)
            {
                Vector3 c = BoardSlots.Center(s);
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - c.x) - h), dz = Mathf.Max(0f, Mathf.Abs(p.z - c.z) - h);
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(best);
        }

        void ClearWheelPending()
        {
            foreach (var pass in wheelPasses) if (pass.warning != null) Destroy(pass.warning.gameObject);
            wheelPasses.Clear();
        }

        protected override void OnDestroy()
        {
            ClearWheelPending();
            base.OnDestroy();
        }

        /// <summary>The King walks under the body but not through its feet (rolls pass through, like every body).</summary>
        void PushKingOffFeet()
        {
            if (IsDead || Player == null || Player.IsDead || Player.IsDodging || state == EState.Spawning) return;
            const float FootRadius = 0.65f;
            for (int l = 0; l < 4; l++)
            {
                if (FootLifted(l)) continue;
                Vector3 d = Player.FlatPos - FootGround(l);
                float min = FootRadius + Player.Radius, dist = d.magnitude;
                if (dist >= min) continue;
                Vector3 n = dist > 0.001f ? d / dist : -transform.forward;
                Player.transform.position += n * (min - dist);
            }
        }

        /// <summary>Dropped onto the board: the whole hall shakes.</summary>
        void Landed()
        {
            Vfx.GroundImpact(FlatPos, 4f, 1.4f, hostile: true);
            Vfx.Dust(FlatPos, 4f);
            Sfx.Play("ROOK_Slam", transform.position);
            Game.Rig.Shake(0.7f);
        }

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            bool hit = base.TakeHit(damage, poiseDamage, source);
            if (hit && !IsDead) RigImpulse(source, damage);
            return hit;
        }

        // ---------------------------------------------------------------- lab hooks

        public static readonly Pattern[] AllPatterns =
        {
            Pattern.Charge, Pattern.Smash, Pattern.Sweep, Pattern.Drop, Pattern.Sit, Pattern.Wheel,
            Pattern.Promotion, Pattern.DoubleSweep, Pattern.QueenLines, Pattern.Storm,
        };

        /// <summary>Lab: start a pattern now (its follow-ups included), whatever the AI would pick.</summary>
        public void DebugPattern(Pattern p, int sideHint = 0)
        {
            if (IsDead || state == EState.Spawning) return;
            if (sideHint != 0) side = sideHint;
            StartPattern(p);
        }

        /// <summary>Lab: the upper body's turn in the wheel's spin as of this frame (arms at ±90° from it).</summary>
        public float DebugWheelYaw => wheelPrevYaw;
        /// <summary>Lab: square warnings lit right now (arms on their way to them).</summary>
        public int DebugWheelPending
        {
            get
            {
                int n = 0;
                foreach (var pass in wheelPasses) if (pass.warning != null) n++;
                return n;
            }
        }

        /// <summary>Lab: the squares the current (or last) attack strikes.</summary>
        public Vector2Int[] DebugSquares => plan.squares;

        /// <summary>Lab: straight to the promoted second phase (half health, crown grown).</summary>
        public void DebugPhase2()
        {
            phase2 = true;
            Health = Mathf.Min(Health, MaxHealth * 0.5f);
            crownGrown = 1f;
        }

        /// <summary>Lab: put the body on a square facing a board direction, feet planted, idle.</summary>
        public void DebugPlace(Vector2Int square, Vector2Int facing)
        {
            queue.Clear();
            Enter(EState.Move);
            SetGlow(Color.black);
            underTime = 0f;
            transform.SetPositionAndRotation(BoardSlots.Center(ClampCenter(square)), Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
            ResetRigPose();
            cooldown = 0.5f;
        }
    }
}
