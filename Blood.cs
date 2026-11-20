using System.Collections.Generic;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// Blood. A hit throws droplets that fly ballistically; wherever one lands (floor, wall, shelf, ceiling)
    /// it leaves a round, irregular stain flat on that surface. Big splat on the wall behind the target,
    /// and a growing pool under knocked-out bodies.
    /// </summary>
    internal static class Blood
    {
        private static Material _dropMat;
        private static Material[] _stainMats;
        private static readonly Queue<GameObject> Stains = new Queue<GameObject>();
        private const int MaxStains = 120;

        private static void Init()
        {
            if (_dropMat != null) return;
            _dropMat = GameHelpers.MakeMaterial(new Color(0.42f, 0.01f, 0.01f), 0.85f);
            _stainMats = new Material[4];
            for (int i = 0; i < _stainMats.Length; i++)
            {
                var m = GameHelpers.MakeMaterial(new Color(0.36f, 0.015f, 0.015f), 0.9f);
                var tex = SplatTexture(1000 + i * 77);
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                // Cut-out: the round splat shape, no square edges.
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = 2450;
                _stainMats[i] = m;
            }
        }

        /// <summary>Irregular splat: a main blob, satellite drops and a few streaks, as an alpha mask.</summary>
        private static Texture2D SplatTexture(int seed)
        {
            const int n = 128;
            var rng = new System.Random(seed);
            var a = new float[n * n];
            void Blob(float cx, float cy, float r)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float ang = Mathf.Atan2(dy, dx);
                        float wobble = 1f + 0.18f * Mathf.Sin(ang * 5f + cx) + 0.1f * Mathf.Sin(ang * 11f + cy);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / (r * wobble);
                        if (d < 1f) a[y * n + x] = Mathf.Max(a[y * n + x], 1f - d * d * 0.4f);
                    }
            }
            Blob(n / 2f, n / 2f, n * 0.26f);
            for (int i = 0; i < 9; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = n * (0.25f + (float)rng.NextDouble() * 0.2f);
                Blob(n / 2f + Mathf.Cos(ang) * dist, n / 2f + Mathf.Sin(ang) * dist, n * (0.02f + (float)rng.NextDouble() * 0.05f));
            }
            for (int i = 0; i < 4; i++) // streaks
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (float t = 0.2f; t < 0.46f; t += 0.01f)
                    Blob(n / 2f + Mathf.Cos(ang) * n * t, n / 2f + Mathf.Sin(ang) * n * t, n * 0.025f * (0.5f - t) * 4f);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                float v = a[i];
                float shade = 0.75f + 0.25f * v; // darker edges, wetter middle
                px[i] = new Color(shade, shade, shade, v > 0.01f ? 1f : 0f);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static void Spray(Vector3 point, Vector3 dir)
        {
            if (!Plugin.BloodEnabled.Value) return;
            Init();

            for (int i = 0; i < 14; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(d.GetComponent<Collider>());
                d.GetComponent<Renderer>().sharedMaterial = _dropMat;
                d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                d.transform.position = point + Random.insideUnitSphere * 0.05f;
                float size = Random.Range(0.012f, 0.03f);
                d.transform.localScale = Vector3.one * size;
                var drop = d.AddComponent<Droplet>();
                drop.Velocity = dir * Random.Range(1.5f, 4.5f) + Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(0.3f, 2.2f);
                drop.StainSize = size * Random.Range(6f, 10f);
            }

            // The big one on whatever is right behind the target.
            if (Physics.Raycast(point + dir * 0.35f, dir, out var wall, 2.5f, ~0, QueryTriggerInteraction.Ignore) && !IsBody(wall.collider))
                Stain(wall.point, wall.normal, Random.Range(0.35f, 0.6f));
        }

        public static void Pool(Vector3 near)
        {
            if (!Plugin.BloodEnabled.Value) return;
            Init();
            if (Physics.Raycast(near + Vector3.up * 0.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                var pool = Stain(floor.point, floor.normal, 0.25f);
                pool.AddComponent<Grow>().Target = Random.Range(1.2f, 1.6f);
            }
        }

        private static bool IsBody(Collider c) =>
            c.GetComponentInParent<NPC_Info>() != null || c.GetComponentInParent<PlayerNetwork>() != null || c.GetComponentInParent<PistolModel>() != null;

        /// <summary>A flat, round splat lying on the surface.</summary>
        public static GameObject Stain(Vector3 p, Vector3 n, float size)
        {
            Init();
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "SupermarketBoost_Blood";
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = _stainMats[Random.Range(0, _stainMats.Length)];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.transform.position = p + n * (0.003f + (Stains.Count % 20) * 0.0002f);
            q.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            q.transform.localScale = new Vector3(size, size * Random.Range(0.75f, 1f), 1f);
            Stains.Enqueue(q);
            while (Stains.Count > MaxStains) { var old = Stains.Dequeue(); if (old) Object.Destroy(old); }
            Object.Destroy(q, Plugin.BloodSeconds.Value);
            return q;
        }

        /// <summary>Ballistic droplet; raycasts its own path and stains whatever it lands on.</summary>
        private class Droplet : MonoBehaviour
        {
            public Vector3 Velocity;
            public float StainSize;
            private float _age;

            private void Update()
            {
                float dt = Time.deltaTime;
                _age += dt;
                Velocity += Physics.gravity * dt;
                Vector3 from = transform.position, step = Velocity * dt;
                if (Physics.Raycast(from, step.normalized, out var hit, step.magnitude + 0.01f, ~0, QueryTriggerInteraction.Ignore) && !IsBody(hit.collider))
                {
                    // Glancing hits smear: stretch along the travel direction.
                    var s = Stain(hit.point, hit.normal, StainSize);
                    float glance = 1f - Mathf.Abs(Vector3.Dot(Velocity.normalized, hit.normal));
                    var along = Vector3.ProjectOnPlane(Velocity, hit.normal);
                    if (along.sqrMagnitude > 0.01f && glance > 0.2f)
                    {
                        s.transform.rotation = Quaternion.LookRotation(-hit.normal, along.normalized);
                        s.transform.localScale = new Vector3(StainSize, StainSize * (1f + glance * 1.5f), 1f);
                    }
                    Destroy(gameObject);
                    return;
                }
                transform.position = from + step;
                if (_age > 3f) Destroy(gameObject);
            }
        }

        private class Grow : MonoBehaviour
        {
            public float Target;
            private void Update()
            {
                float s = Mathf.MoveTowards(transform.localScale.x, Target, Time.deltaTime * 0.1f);
                transform.localScale = new Vector3(s, s * 0.85f, 1f);
                if (s >= Target) Destroy(this);
            }
        }
    }
}
