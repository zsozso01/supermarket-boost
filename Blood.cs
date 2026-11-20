using System.Collections.Generic;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>Blood: droplets on a hit, splats on the floor / wall behind, and a pool under knocked-out bodies.</summary>
    internal static class Blood
    {
        private static Material _drop, _splat;
        private static readonly Queue<GameObject> Decals = new Queue<GameObject>();
        private const int MaxDecals = 60;

        private static void Init()
        {
            if (_drop != null) return;
            _drop = GameHelpers.MakeMaterial(new Color(0.45f, 0.02f, 0.02f), 0.8f);
            _splat = GameHelpers.MakeMaterial(new Color(0.32f, 0.01f, 0.01f), 0.85f);
        }

        public static void Spray(Vector3 point, Vector3 dir)
        {
            if (!Plugin.BloodEnabled.Value) return;
            Init();

            for (int i = 0; i < 12; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(d.GetComponent<Collider>());
                d.GetComponent<Renderer>().sharedMaterial = _drop;
                d.transform.position = point + Random.insideUnitSphere * 0.05f;
                d.transform.localScale = Vector3.one * Random.Range(0.015f, 0.04f);
                var rb = d.AddComponent<Rigidbody>();
                rb.mass = 0.01f;
                rb.velocity = (dir * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(0.5f, 2f));
                rb.detectCollisions = false;
                Object.Destroy(d, 0.8f);
            }

            // Splat on whatever is behind the target, and on the floor below.
            if (Physics.Raycast(point + dir * 0.4f, dir, out var wall, 2.5f, ~0, QueryTriggerInteraction.Ignore) && !IsBody(wall.collider))
                Decal(wall.point, wall.normal, Random.Range(0.25f, 0.5f));
            if (Physics.Raycast(point + dir * 0.3f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore) && !IsBody(floor.collider))
                Decal(floor.point, floor.normal, Random.Range(0.3f, 0.6f));
        }

        public static void Pool(Vector3 near)
        {
            if (!Plugin.BloodEnabled.Value) return;
            Init();
            if (Physics.Raycast(near + Vector3.up * 0.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                var pool = Decal(floor.point, floor.normal, 0.3f);
                pool.AddComponent<Grow>().Target = Random.Range(1.1f, 1.5f);
            }
        }

        private static bool IsBody(Collider c) => c.GetComponentInParent<NPC_Info>() != null || c.GetComponentInParent<PlayerNetwork>() != null;

        private static GameObject Decal(Vector3 p, Vector3 n, float size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "SupermarketBoost_Blood";
            q.GetComponent<Renderer>().sharedMaterial = _splat;
            q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.transform.position = p + n * (0.004f + Decals.Count * 0.00005f);
            q.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            q.transform.localScale = new Vector3(size, size * Random.Range(0.6f, 1f), 1f);
            Decals.Enqueue(q);
            while (Decals.Count > MaxDecals) { var old = Decals.Dequeue(); if (old) Object.Destroy(old); }
            Object.Destroy(q, Plugin.BloodSeconds.Value);
            return q;
        }

        private class Grow : MonoBehaviour
        {
            public float Target;
            private void Update()
            {
                float s = Mathf.MoveTowards(transform.localScale.x, Target, Time.deltaTime * 0.12f);
                transform.localScale = new Vector3(s, s, 1f);
                if (s >= Target) Destroy(this);
            }
        }
    }
}
