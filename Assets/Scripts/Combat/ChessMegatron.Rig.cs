using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The colossus's body, made of 27 black pawns and driven procedurally every frame. Every bone is one pawn spanning
    /// exactly from joint to joint (stretched a little lengthwise when the limb is pulled long), so the body never comes
    /// apart: elbows, ankles and the waist are two pawn heads meeting, shoulders and hips a pawn's base seated on
    /// another's head.
    /// <list type="bullet">
    /// <item>Hips: an upright belly pawn and four hip sockets reaching to where the legs hang;
    /// a crossed pair of spine pawns up to a shoulder bar of two pawns; a big pawn for a head wearing a crown of small
    /// pawns (it grows when it promotes).</item>
    /// <item>Arms: an upper-arm pawn and a forearm pawn (two-bone IK); the forearm's broad base is the fist.</item>
    /// <item>Legs: four of them, one pawn each from the hip socket down to a big foot pawn planted on a square:
    /// the feet step square to square, two diagonal feet at a time.</item>
    /// <item>Every channel (hip height and tilt, torso lean and twist, head, both fists) follows its target through a
    /// spring, so the body lags, overshoots and wobbles; strikes drive the fists exactly so they land as the squares
    /// are hit. Stunned, the springs go slack and the colossus sags onto its knuckles.</item>
    /// </list>
    /// Boss space: origin on the board under the body's centre, +Z forward.
    /// </summary>
    public partial class ChessMegatron
    {
        // ---------------------------------------------------------------- dimensions

        const float PawnH = 1.35f;   // the designed pawn model's height at scale 1
        const float HipRest = 2.0f;
        /// <summary>The lowest the hips go (sitting, crouching): just above the tops of the feet, so the legs never point up.</summary>
        const float HipLow = 1.55f;
        const float FootH = 1.3f, HeadScale = 1.0f;
        // Bone widths (pawn scale across the bone): slim limbs between the joints, big feet to stand on.
        const float ArmWidth = 1.9f, LegWidth = 0.95f, SpineWidth = 1.35f, ShoulderWidth = 1.35f, SocketWidth = 1.0f;
        const float UpperArm = 2.75f, ForeArm = 2.75f;
        const float SpineBase = 0.4f, SpineLen = 1.2f, ShoulderSpan = 1.95f;
        const float StepTime = 0.34f, StepHeight = 1.1f;

        // Feet stand on the four corner squares of the 3×3 body; legs hang from the corners of the hips.
        static readonly Vector3[] FootRest =
        {
            new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f), new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f),
        };
        static readonly Vector3[] HipCorner =
        {
            new Vector3(-1.3f, -0.3f, 1.3f), new Vector3(1.3f, -0.3f, 1.3f), new Vector3(-1.3f, -0.3f, -1.3f), new Vector3(1.3f, -0.3f, -1.3f),
        };

        static int Side(int arm) => arm == 0 ? -1 : 1;
        /// <summary>Diagonal pairs step together: front-left with back-right, front-right with back-left.</summary>
        static int Pair(int leg) => leg == 0 || leg == 3 ? 0 : 1;

        // ---------------------------------------------------------------- parts

        Transform rig;
        Transform belly, spineLo, spineHi, head;
        readonly Transform[] hipSocket = new Transform[4], shoulders = new Transform[2], crown = new Transform[5];
        readonly Transform[] upper = new Transform[2], fore = new Transform[2];
        readonly Transform[] leg = new Transform[4], foot = new Transform[4];
        readonly List<Transform> pawns = new List<Transform>();

        // ---------------------------------------------------------------- springs

        struct Spring
        {
            public float x, v;

            /// <param name="freq">Oscillations per second.</param>
            /// <param name="damp">Damping ratio (1: no overshoot, lower: wobblier).</param>
            public void Step(float target, float freq, float damp, float dt)
            {
                float w = 2f * Mathf.PI * freq;
                v += (-2f * damp * w * v - w * w * (x - target)) * dt;
                x += v * dt;
            }
        }

        struct Spring3
        {
            public Vector3 x, v;

            public void Step(Vector3 target, float freq, float damp, float dt)
            {
                float w = 2f * Mathf.PI * freq;
                v += (-2f * damp * w * v - w * w * (x - target)) * dt;
                x += v * dt;
            }

            /// <summary>Follows a curve exactly (a strike), keeping its velocity for the follow-through.</summary>
            public void Drive(Vector3 p, float dt)
            {
                if (dt > 0f) v = (p - x) / dt;
                x = p;
            }
        }

        Spring hipY, hipPitch, hipRoll, torsoPitch, torsoYaw, headYaw, headPitch, crownS;
        readonly Spring3[] hand = new Spring3[2];   // world space
        /// <summary>0 → 1 once promoted (the crown grows).</summary>
        float crownGrown;

        // Pose targets for this frame.
        float tHipY, tHipPitch, tHipRoll, tTorsoPitch, tTorsoYaw, tHeadDown, tRootLift;
        readonly Vector3[] tHand = new Vector3[2];
        readonly bool[] tHandDrive = new bool[2];
        float bodyFreq, bodyDamp, handFreq, handDamp;
        bool gaitAllowed;
        readonly bool[] footDriven = new bool[4];
        readonly Vector3[] footTarget = new Vector3[4];   // world, for driven feet

        // ---------------------------------------------------------------- feet

        readonly Vector3[] planted = new Vector3[4], stepFrom = new Vector3[4], stepTo = new Vector3[4];
        readonly float[] stepT = new float[4];
        readonly bool[] stepping = new bool[4];
        int lastPair;
        float walkPhase, rootLift;
        bool collapsed;

        static Vector3 Snap(Vector3 world) => BoardSlots.Center(BoardSlots.ToSquare(world));

        Vector3 W(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        // ---------------------------------------------------------------- build

        void BuildRig()
        {
            rig = Visual;
            for (int i = rig.childCount - 1; i >= 0; i--) DestroyImmediate(rig.GetChild(i).gameObject);

            belly = Pawn(1.6f);
            for (int l = 0; l < hipSocket.Length; l++) hipSocket[l] = Pawn(1.3f);
            spineLo = Pawn(1.6f);
            spineHi = Pawn(1.6f);
            for (int i = 0; i < 2; i++) shoulders[i] = Pawn(ShoulderSpan / PawnH);
            head = Pawn(HeadScale);
            for (int k = 0; k < crown.Length; k++) crown[k] = Pawn(0.3f);
            for (int i = 0; i < 2; i++)
            {
                upper[i] = Pawn(UpperArm / PawnH);
                fore[i] = Pawn(ForeArm / PawnH);
            }
            for (int l = 0; l < 4; l++)
            {
                leg[l] = Pawn(LegWidth);
                foot[l] = Pawn(FootH / PawnH);
            }
            ResetRigPose();
        }

        Transform Pawn(float scale)
        {
            var t = PieceFactory.BuildEnemy(PieceType.Pawn, rig);
            t.localScale = Vector3.one * scale;
            pawns.Add(t);
            return t;
        }

        void ResetRigPose()
        {
            for (int l = 0; l < 4; l++)
            {
                planted[l] = Snap(transform.TransformPoint(FootRest[l]));
                stepping[l] = false;
                footDriven[l] = false;
            }
            hipY = new Spring { x = HipRest };
            hipPitch = new Spring { x = 6f };
            torsoPitch = new Spring { x = 8f };
            hipRoll = torsoYaw = headYaw = headPitch = default;
            crownS.x = crownS.v = 0f;
            for (int i = 0; i < 2; i++) hand[i] = new Spring3 { x = IdleHand(i) };
            rootLift = 0f;
            ApplyRig();
        }

        Vector3 IdleHand(int i) => W(Side(i) * 3.3f, 0.7f, 2.6f);

        // ---------------------------------------------------------------- per frame

        void TickRig(float dt)
        {
            if (rig == null) return;
            if (IsDead)
            {
                if (!collapsed) Collapse();
                return;
            }
            if (prevState == EState.Attack && state != EState.Attack) StrikeEnded();
            // The wheel's turn ends a full circle round once it has recovered: unwind so it doesn't spin back.
            if (prevState == EState.Recover && state != EState.Recover && current == Pattern.Wheel && step == 1)
            {
                torsoYaw.x -= 360f;
            }

            ComputePose();
            float bf = bodyFreq, bd = bodyDamp;
            hipY.Step(tHipY, bf, bd, dt);
            hipPitch.Step(tHipPitch, bf, bd, dt);
            hipRoll.Step(tHipRoll, bf, bd, dt);
            torsoPitch.Step(tTorsoPitch, bf, bd, dt);
            torsoYaw.Step(tTorsoYaw, bf * 1.2f, bd, dt);
            bool promoting = current == Pattern.Promotion && state == EState.Windup;
            crownS.Step(phase2 && !promoting ? 1f : crownGrown, 1.1f, 0.45f, dt);
            for (int i = 0; i < 2; i++)
            {
                if (tHandDrive[i]) hand[i].Drive(tHand[i], dt);
                else hand[i].Step(tHand[i], handFreq, handDamp, dt);
            }

            // The head keeps watching the King (it can't turn all the way round).
            if (Player != null)
            {
                Vector3 to = transform.InverseTransformPoint(Player.transform.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - torsoYaw.x, -70f, 70f);
                float horiz = new Vector2(to.x, to.z).magnitude;
                float pitch = Mathf.Clamp(Mathf.Atan2(5f, Mathf.Max(1f, horiz)) * Mathf.Rad2Deg * 0.7f, 0f, 45f);
                headYaw.Step(yaw, 1.4f, 0.7f, dt);
                headPitch.Step(pitch + tHeadDown, 1.4f, 0.7f, dt);
            }

            TickFeet(dt);
            rootLift = tRootLift;
            if (state != EState.Spawning)
            {
                rig.localPosition = new Vector3(0f, rootLift, 0f);
                rig.localRotation = Quaternion.identity;
                rig.localScale = Vector3.one;
            }
            ApplyRig();
        }

        /// <summary>A blow rocks the torso away from it and jolts the hips.</summary>
        void RigImpulse(Vector3 source, float damage)
        {
            Vector3 local = transform.InverseTransformPoint(source);
            float k = Mathf.Clamp(damage * 2.5f, 25f, 150f);
            torsoPitch.v -= Mathf.Sign(local.z) * k;
            torsoYaw.v += Mathf.Sign(local.x) * k * 0.6f;
            hipY.v -= 1.2f;
        }

        /// <summary>The strike's last frame passed: feet that came down are planted where they landed.</summary>
        void StrikeEnded()
        {
            if (current == Pattern.Charge)
            {
                for (int l = 0; l < 4; l++) Plant(l, Snap(transform.TransformPoint(FootRest[l])), true);
                hipY.v -= 4f;
                Vfx.Dust(FlatPos + transform.forward * 2f, 3.5f);
                Game.Rig.Shake(0.45f);
            }
            else if (current == Pattern.Drop || (current == Pattern.Wheel && step == 0))
            {
                for (int l = 0; l < 4; l++) Plant(l, Snap(transform.TransformPoint(FootRest[l])), true);
                hipY.v -= 6f;
                Vfx.Dust(FlatPos, 4.5f);
            }
            else if (current == Pattern.Sit)
            {
                Vfx.GroundImpact(FlatPos, 5f, 1.4f, hostile: true);
                Vfx.Dust(FlatPos, 5f);
                Game.Rig.Shake(0.6f);
            }
            else if (current == Pattern.Promotion)
            {
                hipY.v -= 3f;
                crownGrown = 1f;
            }
        }

        void Plant(int leg, Vector3 at, bool fx)
        {
            planted[leg] = new Vector3(at.x, 0f, at.z);
            stepping[leg] = false;
            if (fx) Vfx.StepDust(planted[leg], -transform.forward);
        }

        // ---------------------------------------------------------------- poses

        void ComputePose()
        {
            float time = Time.time;
            float breath = Mathf.Sin(time * 1.6f) * 0.08f;
            if (moving) walkPhase += Time.deltaTime * 4.5f;

            // Idle / walking: a heavy ape on its knuckles, swaying with its steps.
            tHipY = HipRest + breath;
            tHipPitch = 6f;
            tHipRoll = 0f;
            tTorsoPitch = 8f + breath * 10f;
            tTorsoYaw = 0f;
            tHeadDown = 0f;
            tRootLift = 0f;
            bodyFreq = 1.6f; bodyDamp = 0.55f;
            handFreq = 1.4f; handDamp = 0.5f;
            gaitAllowed = state == EState.Move || state == EState.Recover || state == EState.Stagger || state == EState.Spawning;
            for (int l = 0; l < 4; l++) footDriven[l] = false;
            for (int i = 0; i < 2; i++)
            {
                float swing = moving ? Mathf.Sin(walkPhase + i * Mathf.PI) * 0.9f : 0f;
                tHand[i] = W(Side(i) * 3.3f, 0.7f + breath, 2.6f + swing);
                tHandDrive[i] = false;
            }
            // Steps dip the hips and roll them toward the lifted side.
            float liftL = 0f, liftR = 0f, liftF = 0f, liftB = 0f;
            for (int l = 0; l < 4; l++)
            {
                if (!stepping[l]) continue;
                float h = Mathf.Sin(stepT[l] * Mathf.PI);
                if (l % 2 == 0) liftL += h; else liftR += h;
                if (l < 2) liftF += h; else liftB += h;
            }
            tHipY -= 0.12f * (liftL + liftR);
            tHipRoll += (liftR - liftL) * 3f;
            tHipPitch += (liftB - liftF) * 2f;

            if (state == EState.Stagger)
            {
                // Stunned: the springs go slack and it sags onto its knuckles, head hanging.
                tHipY = HipLow + 0.1f;
                tHipPitch = 22f;
                tTorsoPitch = 30f;
                tHeadDown = 35f;
                bodyFreq = 0.8f; bodyDamp = 0.28f;
                handFreq = 0.8f; handDamp = 0.3f;
                for (int i = 0; i < 2; i++) tHand[i] = W(Side(i) * 2.8f, 0.2f, 2.3f);
                return;
            }
            if (state != EState.Windup && state != EState.Attack && state != EState.Recover) return;

            float t = stateTime;
            float raise = state == EState.Windup ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, raiseTime * 0.8f))) : 1f;
            bool holding = state == EState.Windup && t > raiseTime * 0.8f;
            float tremble = holding ? Mathf.Sin(time * 38f) * 0.06f : 0f;
            float a = state == EState.Attack ? Mathf.Clamp01(t / Mathf.Max(0.01f, plan.active)) : state == EState.Recover ? 1f : 0f;
            float r = state == EState.Recover ? Mathf.Clamp01(t / Mathf.Max(0.01f, plan.recover)) : 0f;
            bool windup = state == EState.Windup, attack = state == EState.Attack, recover = state == EState.Recover;
            // Recovery: the struck pose holds a while (the punish window), then eases back to idle.
            float back = recover ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r)) : 0f;

            switch (current)
            {
                case Pattern.Charge:
                {
                    // Head down like a bull, fists drawn back, a front foot pawing the board — then the whole body charges.
                    if (windup)
                    {
                        tHipY = Mathf.Lerp(HipRest, HipRest - 0.3f, raise) + tremble;
                        tHipPitch = Mathf.Lerp(6f, 16f, raise);
                        tTorsoPitch = Mathf.Lerp(8f, 32f, raise);
                        tHeadDown = -20f * raise;   // the head stays up, glaring
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(IdleHand(i), W(Side(i) * 3.4f, 1.2f, -1.0f), raise);
                        if (holding)
                        {
                            float paw = Mathf.Abs(Mathf.Sin(t * 8f));
                            footDriven[0] = true;
                            footTarget[0] = planted[0] + Vector3.up * (0.45f * paw) - transform.forward * (0.3f * paw);
                        }
                    }
                    else if (attack)
                    {
                        tHipY = HipRest - 0.2f;
                        tHipPitch = 18f;
                        tTorsoPitch = 34f;
                        bodyFreq = 4f;
                        handFreq = 6f;
                        for (int i = 0; i < 2; i++) tHand[i] = W(Side(i) * 1.8f, 2.8f, 5.6f);
                        for (int l = 0; l < 4; l++)
                        {
                            // Galloping: the feet tuck under the body as it rushes forward.
                            footDriven[l] = true;
                            float lift = Mathf.Sin((a * 2f + (Pair(l) == 0 ? 0f : 0.5f)) * Mathf.PI);
                            footTarget[l] = transform.TransformPoint(FootRest[l] * 0.85f + Vector3.up * (0.2f + 0.9f * Mathf.Abs(lift)));
                        }
                    }
                    else
                    {
                        tHipY = Mathf.Lerp(HipRest - 0.2f, HipRest, back);
                        tHipPitch = Mathf.Lerp(18f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(34f, 8f, back);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(W(Side(i) * 1.8f, 0.6f, 5.2f), IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.Smash:
                {
                    // One fist raised high behind, the torso coiled away — then hammered down onto the King's square.
                    int i = side > 0 ? 1 : 0;
                    Vector3 raised = W(side * 1.4f, 9.6f, -1.2f);
                    Vector3 hit = BoardPoint(smashTarget, 0.6f);
                    Vector3 toHit = transform.InverseTransformPoint(hit);
                    float aim = Mathf.Clamp(Mathf.Atan2(toHit.x, toHit.z) * Mathf.Rad2Deg, -60f, 60f) * 0.6f;
                    if (windup)
                    {
                        tTorsoYaw = -side * 28f * raise;
                        tHipRoll = -side * 6f * raise;
                        tHipPitch = Mathf.Lerp(6f, -4f, raise);
                        tHand[i] = Vector3.Lerp(IdleHand(i), raised, raise) + Vector3.up * tremble;
                    }
                    else if (attack)
                    {
                        float e = a * a;
                        tTorsoYaw = Mathf.Lerp(-side * 28f, aim, e);
                        tHipRoll = side * 8f;
                        tHipPitch = 18f;
                        tTorsoPitch = 22f;
                        bodyFreq = 4f;
                        tHand[i] = Vector3.Lerp(raised, hit, e);
                        tHandDrive[i] = true;
                    }
                    else
                    {
                        tTorsoYaw = aim * (1f - back);
                        tHipPitch = Mathf.Lerp(16f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(20f, 8f, back);
                        tHand[i] = Vector3.Lerp(hit, IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.Sweep:
                case Pattern.DoubleSweep:
                {
                    // The sweeping arm (both, promoted) raised out to the side — then swept low across the front.
                    bool both = current == Pattern.DoubleSweep;
                    float reach = both ? 9f : phase2 ? 8.6f : 7.2f;
                    for (int i = 0; i < 2; i++)
                    {
                        int sd = Side(i);
                        if (!both && sd != side) continue;
                        Vector3 raised = W(sd * 5.2f, both ? 8.6f : 8.2f, both ? -0.4f : 0.2f);
                        Vector3 p0 = W(sd * (both ? 6.4f : 6.0f), 1.0f, both ? 0f : 1.6f);
                        Vector3 p1 = W(sd * (both ? 1.0f : 0f), 0.6f, reach);
                        Vector3 p2 = both ? W(-sd * 3.0f, 1.0f, 4.0f) : W(-sd * 5.4f, 1.0f, 2.0f);
                        if (windup) tHand[i] = Vector3.Lerp(IdleHand(i), raised, raise) + Vector3.up * tremble;
                        else if (attack)
                        {
                            float u = Mathf.InverseLerp(0.15f, 1f, a);
                            Vector3 arc = (1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * p1 + u * u * p2;
                            tHand[i] = a < 0.15f ? Vector3.Lerp(raised, p0, a / 0.15f) : arc;
                            tHandDrive[i] = true;
                        }
                        else tHand[i] = Vector3.Lerp(p2, IdleHand(i), back);
                    }
                    if (both)
                    {
                        tHipY = windup ? Mathf.Lerp(HipRest, HipRest + 0.4f, raise) : attack ? HipRest - 0.15f : Mathf.Lerp(HipRest - 0.15f, HipRest, back);
                        tTorsoPitch = windup ? Mathf.Lerp(8f, -12f, raise) : attack ? 24f : Mathf.Lerp(24f, 8f, back);
                        tHipPitch = windup ? -6f * raise : attack ? 14f : Mathf.Lerp(14f, 6f, back);
                    }
                    else
                    {
                        tTorsoYaw = windup ? side * 40f * raise : attack ? Mathf.Lerp(side * 40f, -side * 55f, a) : -side * 55f * (1f - back);
                        tHipRoll = windup ? side * 6f * raise : attack ? Mathf.Lerp(side * 6f, -side * 6f, a) : -side * 6f * (1f - back);
                        tTorsoPitch = attack ? 18f : windup ? 4f : Mathf.Lerp(18f, 8f, back);
                    }
                    if (attack) bodyFreq = 4f;
                    break;
                }

                case Pattern.Drop:
                case Pattern.Wheel when step == 0:
                {
                    // Crouch on its knuckles, spring into the air with the fists overhead, crash down on the King's square.
                    if (windup)
                    {
                        tHipY = Mathf.Lerp(HipRest, HipLow, raise) + tremble;
                        tHipPitch = 14f * raise;
                        tTorsoPitch = Mathf.Lerp(8f, 20f, raise);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(IdleHand(i), W(Side(i) * 3.2f, 0.3f, 1.0f), raise);
                    }
                    else if (attack)
                    {
                        tRootLift = Mathf.Sin(a * Mathf.PI) * 4.5f;
                        tHipY = HipRest + 0.3f;
                        tHipPitch = Mathf.Lerp(-10f, 16f, a);
                        tTorsoPitch = Mathf.Lerp(-12f, 24f, a);
                        bodyFreq = 3f;
                        float slam = Mathf.InverseLerp(0.65f, 1f, a);
                        for (int i = 0; i < 2; i++)
                        {
                            tHand[i] = Vector3.Lerp(W(Side(i) * 2.4f, 9.5f + tRootLift, 0.5f), W(Side(i) * 2.8f, 0.4f, 2.4f), slam * slam);
                            tHandDrive[i] = true;
                        }
                        for (int l = 0; l < 4; l++)
                        {
                            footDriven[l] = true;
                            footTarget[l] = transform.TransformPoint(FootRest[l] * 0.75f + Vector3.up * (tRootLift + 0.4f + Mathf.Sin(a * Mathf.PI) * 0.8f));
                        }
                    }
                    else
                    {
                        tHipY = Mathf.Lerp(HipLow, HipRest, back);
                        tHipPitch = Mathf.Lerp(16f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(24f, 8f, back);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(W(Side(i) * 2.8f, 0.4f, 2.4f), IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.Sit:
                {
                    // Rise up on straightened legs, arms spread — then drop the whole body onto the board.
                    Vector3 spread(int i) => W(Side(i) * 4.6f, 7.5f, 0.3f);
                    Vector3 braced(int i) => W(Side(i) * 4.8f, 0.3f, 0.4f);
                    if (windup)
                    {
                        tHipY = Mathf.Lerp(HipRest, HipRest + 0.8f, raise) + tremble;
                        tHipPitch = Mathf.Lerp(6f, -4f, raise);
                        tTorsoPitch = Mathf.Lerp(8f, -10f, raise);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(IdleHand(i), spread(i), raise) + Vector3.up * tremble;
                    }
                    else if (attack)
                    {
                        float e = a * a;
                        tHipY = Mathf.Lerp(HipRest + 0.8f, HipLow, e);
                        tHipPitch = 0f;
                        tTorsoPitch = Mathf.Lerp(-10f, 12f, e);
                        bodyFreq = 6f; bodyDamp = 0.5f;
                        for (int i = 0; i < 2; i++)
                        {
                            tHand[i] = Vector3.Lerp(spread(i), braced(i), e);
                            tHandDrive[i] = true;
                        }
                    }
                    else
                    {
                        // Sits there a while (the punish window), then heaves itself back up.
                        tHipY = Mathf.Lerp(HipLow, HipRest, back);
                        tHipPitch = Mathf.Lerp(0f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(12f, 8f, back);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(braced(i), IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.Wheel:
                {
                    // Arms flung out wide and low — then a slow turn sweeping the whole board, finishing the turn as it recovers.
                    float yaw = windup ? WheelFrom * raise : attack ? WheelYaw(a) : Mathf.Lerp(WheelFrom + WheelTurn, 360f, back);
                    tTorsoYaw = yaw;
                    tHipY = HipRest - 0.25f;
                    tHipPitch = 6f;
                    tTorsoPitch = recover ? Mathf.Lerp(14f, 8f, back) : 14f;
                    if (attack) { bodyFreq = 4f; bodyDamp = 0.7f; }
                    handFreq = 4f;
                    handDamp = 0.6f;
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 spun = transform.TransformPoint(Quaternion.Euler(0f, yaw, 0f) * new Vector3(Side(i) * 8f, 1.0f, 0.4f));
                        tHand[i] = recover ? Vector3.Lerp(spun, IdleHand(i), back) : windup ? Vector3.Lerp(IdleHand(i), spun, raise) : spun;
                        if (attack && Random.value < 0.35f) Vfx.StepDust(new Vector3(hand[i].x.x, 0f, hand[i].x.z), transform.right * Side(i));
                    }
                    break;
                }

                case Pattern.Promotion:
                {
                    // Rear up to full height, arms spread wide while the crown of pawns grows — then a slam that shakes the board.
                    if (windup)
                    {
                        crownGrown = Mathf.Max(crownGrown, raise);
                        tHipY = Mathf.Lerp(HipRest, HipRest + 0.9f, raise) + tremble;
                        tHipPitch = Mathf.Lerp(6f, -16f, raise);
                        tTorsoPitch = Mathf.Lerp(8f, -24f, raise);
                        tHeadDown = -25f * raise;
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(IdleHand(i), W(Side(i) * 4.5f, 10f, -0.5f), raise) + Vector3.up * tremble;
                        if (Random.value < 0.35f) Effects.Sparks(head.position + Vector3.up * 2f, new Color(1f, 0.25f, 0.1f), 2, 3f);
                    }
                    else if (attack)
                    {
                        float e = a * a;
                        tHipY = Mathf.Lerp(HipRest + 0.9f, HipRest - 0.3f, e);
                        tHipPitch = Mathf.Lerp(-16f, 20f, e);
                        tTorsoPitch = Mathf.Lerp(-24f, 24f, e);
                        bodyFreq = 4f;
                        for (int i = 0; i < 2; i++)
                        {
                            tHand[i] = Vector3.Lerp(W(Side(i) * 4.5f, 10f, -0.5f), W(Side(i) * 3.0f, 0.4f, 2.6f), e);
                            tHandDrive[i] = true;
                        }
                    }
                    else
                    {
                        tHipY = Mathf.Lerp(HipRest - 0.3f, HipRest, back);
                        tHipPitch = Mathf.Lerp(20f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(24f, 8f, back);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(W(Side(i) * 3.0f, 0.4f, 2.6f), IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.QueenLines:
                {
                    // Both fists clasped high overhead — brought down together on the square in front: the queen's lines burst out.
                    Vector3 hitL = BoardPoint(S(2, 0), 0.5f) + transform.right * -0.7f, hitR = BoardPoint(S(2, 0), 0.5f) + transform.right * 0.7f;
                    if (windup)
                    {
                        tHipY = HipRest + 0.5f * raise + tremble;
                        tHipPitch = Mathf.Lerp(6f, -14f, raise);
                        tTorsoPitch = Mathf.Lerp(8f, -14f, raise);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(IdleHand(i), W(Side(i) * 0.8f, 10.6f, 1.5f), raise) + Vector3.up * tremble;
                    }
                    else if (attack)
                    {
                        float e = a * a;
                        tHipY = Mathf.Lerp(HipRest + 0.5f, HipRest - 0.2f, e);
                        tHipPitch = Mathf.Lerp(-14f, 22f, e);
                        tTorsoPitch = Mathf.Lerp(-14f, 30f, e);
                        bodyFreq = 4f;
                        for (int i = 0; i < 2; i++)
                        {
                            tHand[i] = Vector3.Lerp(W(Side(i) * 0.8f, 10.6f, 1.5f), i == 0 ? hitL : hitR, e);
                            tHandDrive[i] = true;
                        }
                    }
                    else
                    {
                        tHipY = Mathf.Lerp(HipRest - 0.2f, HipRest, back);
                        tHipPitch = Mathf.Lerp(20f, 6f, back);
                        tTorsoPitch = Mathf.Lerp(28f, 8f, back);
                        for (int i = 0; i < 2; i++) tHand[i] = Vector3.Lerp(i == 0 ? hitL : hitR, IdleHand(i), back);
                    }
                    break;
                }

                case Pattern.Storm:
                {
                    // Reach back over the shoulder, pluck pawns off its own back, hurl them high at the King's squares.
                    int i = side > 0 ? 1 : 0;
                    Vector3 pluck = W(side * 1.2f, 7.6f, -2.2f), cocked = W(side * 2.6f, 9.2f, 1.2f), thrown = W(side * 2.4f, 4.2f, 5.2f);
                    if (windup)
                    {
                        float k1 = Mathf.Clamp01(raise * 1.6f), k2 = Mathf.Clamp01(raise * 1.6f - 0.6f);
                        tHand[i] = Vector3.Lerp(Vector3.Lerp(IdleHand(i), pluck, k1), cocked, k2);
                        tTorsoYaw = side * -20f * raise;
                        tHipPitch = Mathf.Lerp(6f, -6f, raise);
                    }
                    else if (attack)
                    {
                        if (prevState == EState.Windup) ThrowPawns(hand[i].x);
                        tHand[i] = Vector3.Lerp(cocked, thrown, Mathf.Clamp01(a * 3f));
                        tHandDrive[i] = true;
                        tTorsoYaw = side * 25f;
                        tHipPitch = 14f;
                        bodyFreq = 3.5f;
                    }
                    else
                    {
                        tTorsoYaw = side * 25f * (1f - back);
                        tHand[i] = Vector3.Lerp(thrown, IdleHand(i), back);
                    }
                    break;
                }
            }
        }

        static Vector3 BoardPoint(Vector2Int square, float height) => BoardSlots.Center(square) + Vector3.up * height;

        /// <summary>Hurls one pawn at each struck square; they land as the squares are hit (the active phase is their flight).</summary>
        void ThrowPawns(Vector3 from)
        {
            if (plan.squares == null) return;
            foreach (var s in plan.squares)
                MegatronShell.Launch(from, BoardSlots.Center(s), plan.active);
        }

        // ---------------------------------------------------------------- feet

        void TickFeet(float dt)
        {
            for (int l = 0; l < 4; l++)
            {
                if (!stepping[l]) continue;
                stepT[l] += dt / StepTime;
                if (stepT[l] < 1f) continue;
                Plant(l, stepTo[l], false);
                Vfx.StepDust(planted[l], -transform.forward);
                Sfx.Play("KNIGHT_Hop", planted[l]);
                if (Player != null && (Player.FlatPos - planted[l]).magnitude < 7f) Game.Rig.Shake(0.05f);
            }
            if (!gaitAllowed) return;

            // A foot steps when the square under its corner of the body changes; one diagonal pair at a time.
            var need = new bool[4];
            for (int l = 0; l < 4; l++)
            {
                if (stepping[l] || footDriven[l]) continue;
                Vector3 want = Snap(transform.TransformPoint(FootRest[l]));
                need[l] = (want - planted[l]).sqrMagnitude > 0.5f;
            }
            for (int k = 0; k < 2; k++)
            {
                int pair = (lastPair + 1 + k) % 2;
                bool otherBusy = false;
                for (int l = 0; l < 4; l++) if (stepping[l] && Pair(l) != pair) otherBusy = true;
                if (otherBusy) continue;
                bool started = false;
                for (int l = 0; l < 4; l++)
                {
                    if (Pair(l) != pair || !need[l]) continue;
                    stepping[l] = true;
                    stepT[l] = 0f;
                    stepFrom[l] = planted[l];
                    stepTo[l] = Snap(transform.TransformPoint(FootRest[l]));
                    started = true;
                }
                if (started) { lastPair = pair; break; }
            }
        }

        /// <summary>A foot off the board (stepping, pawing, tucked in a leap or charge): it doesn't block the King.</summary>
        bool FootLifted(int l) => FootWorld(l).y > 0.4f;

        Vector3 FootGround(int l)
        {
            var f = FootWorld(l);
            return new Vector3(f.x, 0f, f.z);
        }

        Vector3 FootWorld(int l)
        {
            if (footDriven[l]) return footTarget[l];
            if (!stepping[l]) return planted[l];
            float u = Mathf.Clamp01(stepT[l]);
            return Vector3.Lerp(stepFrom[l], stepTo[l], PoseTrack.Curve(Ease.InOut, u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * StepHeight);
        }

        // ---------------------------------------------------------------- applying the pose

        /// <summary>World → rig space (boss space, minus the leap's lift that the rig root carries).</summary>
        Vector3 RigPoint(Vector3 world) => transform.InverseTransformPoint(world) - new Vector3(0f, rootLift, 0f);

        void ApplyRig()
        {
            Vector3 up = Vector3.up, fw = Vector3.forward;
            Quaternion hipRot = Quaternion.Euler(hipPitch.x, 0f, hipRoll.x);
            Vector3 hip = new Vector3(0f, hipY.x, 0f);
            Quaternion chestRot = hipRot * Quaternion.Euler(torsoPitch.x, torsoYaw.x, 0f);

            // Hips: an upright belly pawn and a socket pawn reaching to the top of each leg.
            Place(belly, hip + hipRot * new Vector3(0f, -0.9f, 0f), hipRot * up, hipRot * fw);
            Vector3 hipMid = hip + hipRot * new Vector3(0f, 0.1f, 0f);
            for (int l = 0; l < hipSocket.Length; l++)
                Span(hipSocket[l], hipMid, hip + hipRot * HipCorner[l], hipRot * up, SocketWidth);

            // Spine: two pawns crossed head to base between the hips and the chest; the shoulder bar.
            Vector3 spineBase = hip + hipRot * new Vector3(0f, SpineBase, 0f);
            Vector3 chest = spineBase + chestRot * new Vector3(0f, SpineLen, 0f);
            Span(spineLo, spineBase, chest, chestRot * fw, SpineWidth);
            Span(spineHi, chest, spineBase, chestRot * fw, SpineWidth);
            var shoulderPt = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                Vector3 outward = chestRot * new Vector3(Side(i), 0f, 0f);
                shoulderPt[i] = chest + outward * ShoulderSpan;
                Span(shoulders[i], chest + outward * 0.1f, shoulderPt[i], chestRot * up, ShoulderWidth);
            }
            Quaternion headRot = chestRot * Quaternion.Euler(headPitch.x, headYaw.x, 0f);
            Vector3 headBase = chest + chestRot * new Vector3(0f, 0.15f, 0f);
            Place(head, headBase, headRot * up, headRot * fw);

            // Crown: small pawns round the top of the head, leaning out — bigger once promoted.
            Vector3 top = headBase + headRot * up * (HeadScale * PawnH * 0.9f);
            float cs = Mathf.Lerp(0.28f, 0.62f, Mathf.Clamp01(crownS.x));
            for (int k = 0; k < crown.Length; k++)
            {
                float ang = k * 72f * Mathf.Deg2Rad;
                Vector3 o = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                crown[k].localScale = Vector3.one * cs;
                Place(crown[k], top + headRot * (o * 0.3f), headRot * (o * 0.5f + up).normalized, headRot * o);
            }

            // Arms: two-bone IK from the shoulder to the hand (elbows out and back). The upper arm's base sits on the shoulder
            // and its head is the elbow; the forearm hangs from the elbow by its head, its broad base the fist.
            for (int i = 0; i < 2; i++)
            {
                Vector3 s = shoulderPt[i], target = RigPoint(hand[i].x);
                Vector3 pole = chestRot * new Vector3(Side(i) * 1f, -0.4f, -1f);
                Vector3 elbow = Ik(s, target, UpperArm, ForeArm, pole, out _);
                Span(upper[i], s, elbow, chestRot * up, ArmWidth);
                Span(fore[i], Reach(elbow, target, ForeArm), elbow, chestRot * up, ArmWidth);
            }

            // Legs: one pawn each, from the hip socket (its base) to the top of the foot pawn (its head).
            for (int l = 0; l < 4; l++)
            {
                Vector3 hc = hip + hipRot * HipCorner[l];
                Vector3 f = RigPoint(FootWorld(l));
                Span(leg[l], hc, f + up * (FootH * 0.92f), up, LegWidth);
                Place(foot[l], f, up, fw);
            }
        }

        /// <summary>How far a bone may stretch past its length to stay on its joint when a limb is pulled long.</summary>
        const float MaxStretch = 1.4f;

        /// <summary>The joint end <paramref name="to"/> as seen from <paramref name="from"/>, no farther than the bone's longest stretch.</summary>
        static Vector3 Reach(Vector3 from, Vector3 to, float length)
        {
            Vector3 d = to - from;
            float max = length * MaxStretch;
            return d.magnitude <= max ? to : from + d.normalized * max;
        }

        /// <summary>
        /// A pawn bone from joint <paramref name="baseAt"/> (its base) to joint <paramref name="headAt"/> (its head), its
        /// width <paramref name="width"/> (as a scale) and its length exactly the distance between them.
        /// </summary>
        static void Span(Transform t, Vector3 baseAt, Vector3 headAt, Vector3 faceHint, float width)
        {
            Vector3 d = headAt - baseAt;
            Place(t, baseAt, d, faceHint);
            t.localScale = new Vector3(width, Mathf.Max(0.05f, d.magnitude) / PawnH, width);
        }

        /// <summary>Two-bone IK: the middle joint for a chain from <paramref name="a"/> toward <paramref name="target"/>; <paramref name="end"/> is where the chain actually reaches.</summary>
        static Vector3 Ik(Vector3 a, Vector3 target, float l1, float l2, Vector3 pole, out Vector3 end)
        {
            Vector3 to = target - a;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 0.05f, l1 + l2 - 0.001f);
            Vector3 dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.down;
            end = a + dir * d;
            float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
            Vector3 p = pole - dir * Vector3.Dot(pole, dir);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.Cross(dir, Vector3.right);
            return a + dir * x + p.normalized * h;
        }

        /// <summary>Stands a pawn with its base at <paramref name="basePos"/> and its head along <paramref name="dir"/> (rig space).</summary>
        static void Place(Transform t, Vector3 basePos, Vector3 dir, Vector3 faceHint)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
            dir.Normalize();
            Vector3 z = faceHint - dir * Vector3.Dot(faceHint, dir);
            if (z.sqrMagnitude < 1e-4f) z = Vector3.Cross(dir, Vector3.right);
            if (z.sqrMagnitude < 1e-4f) z = Vector3.Cross(dir, Vector3.forward);
            t.localPosition = basePos;
            t.localRotation = Quaternion.LookRotation(z.normalized, dir);
        }

        // ---------------------------------------------------------------- death

        /// <summary>Captured: the colossus comes apart from the head down, its pawns tumbling over the board and shattering.</summary>
        void Collapse()
        {
            collapsed = true;
            var remains = new GameObject("ChessMegatron_Remains").transform;
            Vector3 c = transform.position + Vector3.up * 3f;
            for (int k = 0; k < pawns.Count; k++)
            {
                var p = pawns[k];
                if (p == null) continue;
                p.SetParent(remains, true);
                Vector3 outward = p.position - c;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Random.insideUnitSphere;
                float delay = Mathf.Clamp01(1f - p.position.y / 8f) * 0.7f;   // the top lets go first
                p.gameObject.AddComponent<FallingPawn>().Init(
                    outward * Random.Range(1.5f, 4.5f) + Vector3.up * Random.Range(1f, 5f),
                    Random.onUnitSphere * Random.Range(90f, 420f), delay, k % 2 == 0);
            }
            Sfx.Play("EN_Crumble", transform.position);
            Game.Rig.Shake(0.8f);
        }
    }

    /// <summary>One of the colossus's pawns falling free after its death: tumbles, bounces, then shatters or sinks away.</summary>
    public class FallingPawn : MonoBehaviour
    {
        Vector3 velocity, spin;
        float delay, settled = -1f;
        bool shatter;
        int bounces;

        public void Init(Vector3 v, Vector3 angular, float wait, bool breaks)
        {
            velocity = v;
            spin = angular;
            delay = wait;
            shatter = breaks;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (delay > 0f) { delay -= dt; return; }
            if (settled >= 0f)
            {
                settled += dt;
                if (settled > 1.5f) transform.position += Vector3.down * (dt * 1.2f);
                if (settled > 2.6f) Destroy(gameObject);
                return;
            }
            velocity.y -= 20f * dt;
            transform.position += velocity * dt;
            transform.rotation = Quaternion.Euler(spin * dt) * transform.rotation;
            if (transform.position.y > 0.05f || velocity.y > 0f) return;

            var p = transform.position;
            transform.position = new Vector3(p.x, 0.05f, p.z);
            if (shatter)
            {
                Crumble.Spawn(GetComponentsInChildren<Renderer>(), new Vector3(velocity.x, 0f, velocity.z), new Color(0.02f, 0.02f, 0.025f), 0.9f);
                Sfx.Play("EN_Debris", transform.position);
                Destroy(gameObject);
                return;
            }
            bounces++;
            velocity = new Vector3(velocity.x * 0.5f, -velocity.y * 0.3f, velocity.z * 0.5f);
            spin *= 0.4f;
            if (bounces >= 2) settled = 0f;
        }
    }

    /// <summary>A pawn the colossus hurls (its pawn storm): flies in a high arc and shatters on the square it was thrown at.</summary>
    public class MegatronShell : MonoBehaviour
    {
        Vector3 from, to, spin;
        float flight, t;

        public static void Launch(Vector3 from, Vector3 to, float flight)
        {
            var go = new GameObject("MegatronShell");
            go.transform.position = from;
            var model = PieceFactory.BuildEnemy(PieceType.Pawn, go.transform);
            model.localScale = Vector3.one * 1.1f;
            var s = go.AddComponent<MegatronShell>();
            s.from = from;
            s.to = to;
            s.flight = Mathf.Max(0.1f, flight);
            s.spin = Random.onUnitSphere * Random.Range(300f, 700f);
        }

        void Update()
        {
            t += Time.deltaTime / flight;
            float u = Mathf.Clamp01(t);
            transform.position = Vector3.Lerp(from, to, u) + Vector3.up * (4f * 5f * u * (1f - u));
            transform.rotation = Quaternion.Euler(spin * Time.deltaTime) * transform.rotation;
            if (u < 1f) return;
            Crumble.Spawn(GetComponentsInChildren<Renderer>(), to - from, new Color(0.02f, 0.02f, 0.025f), 0.8f);
            Vfx.Dust(to, 1.2f);
            Destroy(gameObject);
        }
    }
}
