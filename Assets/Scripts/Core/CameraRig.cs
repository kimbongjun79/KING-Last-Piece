using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Third-person orbit camera with souls-style lock-on framing and screen shake.</summary>
    [DefaultExecutionOrder(200)]
    public class CameraRig : MonoBehaviour
    {
        const float Distance = 7.2f;

        public Camera Cam { get; private set; }
        public Vector3 FlatForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public Vector3 FlatRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        PlayerController player;
        float yaw, pitch = 22f, shake;
        Vector3 focus;

        public void Init(Camera cam, PlayerController p)
        {
            Cam = cam;
            player = p;
            Snap();
        }

        public void Snap()
        {
            yaw = player.transform.eulerAngles.y;
            pitch = 22f;
            focus = player.transform.position + Vector3.up * 1.5f;
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        void LateUpdate()
        {
            if (player == null) return;
            float dt = Time.unscaledDeltaTime;
            var game = GameManager.Instance;
            var state = game != null ? game.State : GameManager.GameState.Title;
            bool playing = state == GameManager.GameState.Playing && !game.Paused;

            var lockT = player.LockTarget;
            if (lockT != null)
            {
                Vector3 to = lockT.FlatPos - player.FlatPos;
                if (to.sqrMagnitude > 0.01f)
                {
                    float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-8f * dt));
                }
                pitch = Mathf.Lerp(pitch, 24f, 1f - Mathf.Exp(-4f * dt));
            }
            else if (playing)
            {
                Vector2 look = GameInput.Look;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, 4f, 65f);
            }
            else if (state == GameManager.GameState.Title || state == GameManager.GameState.Victory)
            {
                yaw += 8f * dt;
            }

            focus = Vector3.Lerp(focus, player.transform.position + Vector3.up * 1.5f, 1f - Mathf.Exp(-12f * dt));
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * Distance;

            if (lockT != null)
            {
                Vector3 lookAt = Vector3.Lerp(focus, lockT.transform.position + Vector3.up * 1.0f, 0.3f);
                rot = Quaternion.LookRotation(lookAt - pos);
            }

            shake = Mathf.MoveTowards(shake, 0f, dt * 1.6f);
            Vector3 offset = shake > 0f ? Random.insideUnitSphere * (shake * 0.35f) : Vector3.zero;
            transform.SetPositionAndRotation(pos + offset, rot);
        }
    }
}
