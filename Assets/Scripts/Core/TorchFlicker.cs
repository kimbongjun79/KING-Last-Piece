using UnityEngine;

namespace KingLastPiece
{
    /// <summary>A wall torch's fire: the light's intensity and the flame's size waver (Perlin noise, each torch its own beat).</summary>
    public class TorchFlicker : MonoBehaviour
    {
        Light lit;
        Transform flame;
        float baseIntensity, seed;
        Vector3 baseScale;

        public void Init(Light light, Transform flameShape)
        {
            lit = light;
            flame = flameShape;
            baseIntensity = light.intensity;
            baseScale = flameShape.localScale;
            seed = Random.value * 100f;
        }

        void Update()
        {
            if (lit == null) return;
            float t = Time.time * 7f + seed;
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, seed + 5f) * 0.3f;   // 0…1
            lit.intensity = baseIntensity * Mathf.Lerp(0.7f, 1.15f, n);
            if (flame != null) flame.localScale = new Vector3(baseScale.x, baseScale.y * Mathf.Lerp(0.85f, 1.2f, n), baseScale.z);
        }
    }
}
