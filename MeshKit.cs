using System;
using System.Collections.Generic;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>Tiny procedural mesh toolkit: beveled side-profile extrusions and swept tubes.</summary>
    internal class MeshKit
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _t = new List<int>();

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var n = Vector3.Cross(b - a, d - a);
            if (n.sqrMagnitude < 1e-14f) n = Vector3.Cross(c - b, a - b);
            n.Normalize();
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) _n.Add(n);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        private static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.x * r.y - r.x * q.y; }
            return a * 0.5f;
        }

        /// <summary>Polygon offset (negative = inward for CCW), miter clamped.</summary>
        private static Vector2[] Offset(Vector2[] p, float d)
        {
            int n = p.Length;
            var o = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = p[(i + n - 1) % n], cur = p[i], next = p[(i + 1) % n];
                Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
                Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x); // outward for CCW
                Vector2 bis = (n0 + n1);
                if (bis.sqrMagnitude < 1e-8f) bis = n0;
                bis.Normalize();
                float cos = Mathf.Max(0.35f, Vector2.Dot(bis, n0));
                o[i] = cur + bis * (d / cos);
            }
            return o;
        }

        private static List<int> Triangulate(Vector2[] p)
        {
            var idx = new List<int>();
            for (int i = 0; i < p.Length; i++) idx.Add(i);
            var tris = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    Vector2 A = p[a], B = p[b], C = p[c];
                    if ((B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x) <= 1e-12f) continue; // reflex
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == a || j == b || j == c) continue;
                        if (InTri(p[j], A, B, C)) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>
        /// Extrudes a side profile (points are (z, y), any winding) across X from -halfW to +halfW,
        /// with a 45-degree chamfer of size bevel on both side edges.
        /// </summary>
        public MeshKit Extrude(Vector2[] profile, float halfW, float bevel, float xCenter = 0f, float uvScale = 30f)
        {
            var p = (Vector2[])profile.Clone();
            if (SignedArea(p) < 0) Array.Reverse(p); // CCW in (z, y)
            var inner = Offset(p, -bevel);
            int n = p.Length;
            float xo = halfW - bevel;
            Vector3 P(Vector2 q, float x) => new Vector3(xCenter + x, q.y, q.x);
            Vector2 U(Vector2 q) => q * uvScale;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // Main band (outer outline between the two chamfers).
                // (Vertex order makes the faces point outward for a CCW profile.)
                Quad(P(p[i], xo), P(p[j], xo), P(p[j], -xo), P(p[i], -xo),
                     new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0));
                // Chamfers.
                Quad(P(inner[i], halfW), P(inner[j], halfW), P(p[j], xo), P(p[i], xo), U(inner[i]), U(inner[j]), U(p[j]), U(p[i]));
                Quad(P(p[i], -xo), P(p[j], -xo), P(inner[j], -halfW), P(inner[i], -halfW), U(p[i]), U(p[j]), U(inner[j]), U(inner[i]));
            }
            // Caps.
            var tri = Triangulate(inner);
            foreach (float side in new[] { 1f, -1f })
            {
                int baseI = _v.Count;
                var nrm = new Vector3(side, 0, 0);
                foreach (var q in inner) { _v.Add(P(q, side * halfW)); _n.Add(nrm); _uv.Add(U(q)); }
                for (int k = 0; k < tri.Count; k += 3)
                {
                    // CCW in (z, y) seen from +X: (z right, y up) looking down -X means z points left; flip for +X.
                    if (side > 0) { _t.Add(baseI + tri[k]); _t.Add(baseI + tri[k + 2]); _t.Add(baseI + tri[k + 1]); }
                    else { _t.Add(baseI + tri[k]); _t.Add(baseI + tri[k + 1]); _t.Add(baseI + tri[k + 2]); }
                }
            }
            return this;
        }

        /// <summary>Axis-aligned beveled box (min/max corners).</summary>
        public MeshKit Box(Vector3 min, Vector3 max, float bevel)
        {
            var prof = new[] { new Vector2(min.z, min.y), new Vector2(max.z, min.y), new Vector2(max.z, max.y), new Vector2(min.z, max.y) };
            return Extrude(prof, (max.x - min.x) * 0.5f, Mathf.Min(bevel, (max.x - min.x) * 0.45f), (max.x + min.x) * 0.5f);
        }

        /// <summary>
        /// Sweeps a superellipse cross-section along a path lying in the (z, y) plane. halfX = half width
        /// across X, halfN = half depth in the path plane; per path point.
        /// </summary>
        public MeshKit Sweep(Vector3[] path, float[] halfX, float[] halfN, float exponent = 4f, int seg = 20, bool caps = true, float uvScale = 25f)
        {
            int n = path.Length;
            int start = _v.Count;
            float len = 0f;
            var rings = new Vector3[n][];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.right;
                Vector3 nrm = Vector3.Cross(side, t).normalized;
                if (i > 0) len += Vector3.Distance(path[i], path[i - 1]);
                rings[i] = new Vector3[seg + 1];
                for (int s = 0; s <= seg; s++)
                {
                    float ang = s / (float)seg * Mathf.PI * 2f;
                    float c = Mathf.Cos(ang), si = Mathf.Sin(ang);
                    float ex = 2f / exponent;
                    float cx = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), ex), cy = Mathf.Sign(si) * Mathf.Pow(Mathf.Abs(si), ex);
                    var pt = path[i] + side * (cx * halfX[i]) + nrm * (cy * halfN[i]);
                    rings[i][s] = pt;
                    _v.Add(pt);
                    _n.Add(Vector3.zero);
                    _uv.Add(new Vector2(s / (float)seg * (halfX[i] + halfN[i]) * 4f * uvScale, len * uvScale));
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < seg; s++)
                {
                    int a = start + i * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    _t.Add(a); _t.Add(c); _t.Add(b);
                    _t.Add(b); _t.Add(c); _t.Add(d);
                }
            // Smooth normals for the tube part.
            for (int k = start; k < _v.Count; k++) _n[k] = Vector3.zero;
            for (int k = 0; k < _t.Count; k += 3)
            {
                int a = _t[k], b = _t[k + 1], c = _t[k + 2];
                if (a < start) continue;
                var fn = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                _n[a] += fn; _n[b] += fn; _n[c] += fn;
            }
            for (int i = 0; i < n; i++) // seam
            {
                int a = start + i * (seg + 1), b = a + seg;
                var sum = _n[a] + _n[b];
                _n[a] = sum; _n[b] = sum;
            }
            for (int k = start; k < _v.Count; k++) _n[k] = _n[k].normalized;

            if (caps)
            {
                foreach (int i in new[] { 0, n - 1 })
                {
                    Vector3 t = (i == 0 ? path[0] - path[1] : path[n - 1] - path[n - 2]).normalized;
                    int ci = _v.Count;
                    _v.Add(path[i]); _n.Add(t); _uv.Add(Vector2.zero);
                    for (int s = 0; s <= seg; s++) { _v.Add(rings[i][s]); _n.Add(t); _uv.Add(Vector2.zero); }
                    for (int s = 0; s < seg; s++)
                    {
                        // Orientation: compute and fix by checking against t.
                        Vector3 A = _v[ci], B = _v[ci + 1 + s], C = _v[ci + 2 + s];
                        if (Vector3.Dot(Vector3.Cross(B - A, C - A), t) > 0) { _t.Add(ci); _t.Add(ci + 1 + s); _t.Add(ci + 2 + s); }
                        else { _t.Add(ci); _t.Add(ci + 2 + s); _t.Add(ci + 1 + s); }
                    }
                }
            }
            return this;
        }

        /// <summary>Cylinder along Z.</summary>
        public MeshKit Cylinder(Vector3 center, float radius, float z0, float z1, int seg = 24, bool caps = true)
        {
            var path = new[] { new Vector3(center.x, center.y, z0), new Vector3(center.x, center.y, z1) };
            // Sweep's cross-section lies in the X / (X x t) plane; with t along Z that is X and -Y: a circle.
            return Sweep(path, new[] { radius, radius }, new[] { radius, radius }, 2f, seg, caps);
        }

        /// <summary>Flat disc facing direction n.</summary>
        public MeshKit Disc(Vector3 c, Vector3 n, float r, int seg = 16)
        {
            n.Normalize();
            var u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var w = Vector3.Cross(n, u);
            int ci = _v.Count;
            _v.Add(c); _n.Add(n); _uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                _v.Add(c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * r); _n.Add(n); _uv.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f + Vector2.one * 0.5f);
            }
            for (int s = 0; s < seg; s++)
            {
                Vector3 A = _v[ci], B = _v[ci + 1 + s], C = _v[ci + 2 + s];
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), n) > 0) { _t.Add(ci); _t.Add(ci + 1 + s); _t.Add(ci + 2 + s); }
                else { _t.Add(ci); _t.Add(ci + 2 + s); _t.Add(ci + 1 + s); }
            }
            return this;
        }
    }
}
