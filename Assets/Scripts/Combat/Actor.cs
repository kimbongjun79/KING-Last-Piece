using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Shared health + hit-flash/glow feedback for the player and enemy pieces.</summary>
    public abstract class Actor : MonoBehaviour
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public float MaxHealth { get; protected set; } = 100f;
        public float Health { get; protected set; }
        public bool IsDead { get; protected set; }
        public float Radius { get; protected set; } = 0.5f;

        public Vector3 FlatPos
        {
            get { var p = transform.position; return new Vector3(p.x, 0f, p.z); }
        }

        protected GameManager Game => GameManager.Instance;

        readonly List<Renderer> tintable = new List<Renderer>();
        MaterialPropertyBlock mpb;
        Color glow = Color.black, flashColor;
        float flashTimer;
        Color applied = new Color(-1f, 0f, 0f);

        protected void InitActor(float hp)
        {
            MaxHealth = hp;
            Health = hp;
            IsDead = false;
            mpb ??= new MaterialPropertyBlock();
            tintable.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (!MaterialLib.HasEmission(r.sharedMaterial)) tintable.Add(r);
            applied = new Color(-1f, 0f, 0f);
        }

        public void Flash(Color c, float duration)
        {
            flashColor = c;
            flashTimer = duration;
        }

        public void SetGlow(Color c) => glow = c;

        protected virtual void LateUpdate()
        {
            if (flashTimer > 0f) flashTimer -= Time.unscaledDeltaTime;
            Color c = flashTimer > 0f ? flashColor : glow;
            if (c == applied || mpb == null) return;
            applied = c;
            foreach (var r in tintable)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, c);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>Returns true if the hit connected (not dodged / not invulnerable).</summary>
        public abstract bool TakeHit(float damage, float poiseDamage, Vector3 source);

        protected void FaceTowards(Vector3 dir, float degPerSec)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            var target = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degPerSec * Time.deltaTime);
        }

        protected void ClampToBoard()
        {
            var p = transform.position;
            float h = GameManager.PlayableHalf;
            p.x = Mathf.Clamp(p.x, -h, h);
            p.z = Mathf.Clamp(p.z, -h, h);
            transform.position = p;
        }
    }
}
