using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArScanner.Rendering
{
    // Conservative local surfel-to-polygon merge. Each small world-space tile
    // must fit one plane; depth steps and thermal boundaries keep their points.
    public static class SurfaceLodBuilder
    {
        public struct Sample
        {
            public Vector3 position;
            public Vector3 viewDirection;
            public Color32 color;
            public float temperature;
            public bool hasThermal;
        }

        private struct Projected
        {
            public float u, v;
            public int source;
        }

        private struct TileKey : IEquatable<TileKey>
        {
            public int x,y,z,level;
            public bool Equals(TileKey other) => x==other.x && y==other.y && z==other.z && level==other.level;
            public override bool Equals(object obj) => obj is TileKey other && Equals(other);
            public override int GetHashCode() => (((x*397)^y)*397^z)*397^level;
        }

        public static Mesh Build(Sample[] samples, Vector3 cameraPosition, float nearMeters,
                                 float farMeters, float tileSize, float planeTolerance,
                                 float maxThermalSpread, out bool[] covered, out int polygonCount)
        {
            covered = new bool[samples.Length];
            polygonCount = 0;
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            var tiles = new Dictionary<TileKey, List<int>>();
            tileSize = Mathf.Max(.05f, tileSize);
            for (int index = 0; index < samples.Length; index++)
            {
                Vector3 p = samples[index].position;
                if (!IsFinite(p)) continue;
                float distance = Vector3.Distance(p,cameraPosition);
                if (distance < nearMeters) continue; // Preserve nearby detail.
                int level = distance >= farMeters ? 1 : 0;
                float cell = tileSize*(level == 1 ? 2f : 1f);
                var key = new TileKey {x=Mathf.FloorToInt(p.x/cell),
                    y=Mathf.FloorToInt(p.y/cell),z=Mathf.FloorToInt(p.z/cell),level=level};
                if (!tiles.TryGetValue(key, out var tile)) tiles[key] = tile = new List<int>();
                tile.Add(index);
            }

            foreach (var entry in tiles)
            {
                var tile = entry.Value;
                float cellSize = tileSize*(entry.Key.level == 1 ? 2f : 1f);
                if (tile.Count < 8) continue;
                bool thermal = samples[tile[0]].hasThermal;
                float minTemp = float.PositiveInfinity, maxTemp = float.NegativeInfinity;
                Vector3 center = Vector3.zero;
                bool compatible = true;
                foreach (int index in tile)
                {
                    Sample point = samples[index];
                    if (point.hasThermal != thermal) { compatible = false; break; }
                    if (thermal)
                    {
                        minTemp = Mathf.Min(minTemp, point.temperature);
                        maxTemp = Mathf.Max(maxTemp, point.temperature);
                    }
                    center += point.position;
                }
                if (!compatible || (thermal && maxTemp-minTemp > maxThermalSpread)) continue;
                center /= tile.Count;

                Vector3 first = samples[tile[0]].position;
                int farthest = tile[0];
                float farthestSq = 0f;
                foreach (int index in tile)
                {
                    float sq = (samples[index].position-first).sqrMagnitude;
                    if (sq > farthestSq) { farthestSq = sq; farthest = index; }
                }
                if (farthestSq < .0025f) continue;
                Vector3 axisU = (samples[farthest].position-first).normalized;
                Vector3 farthestFromLine = Vector3.zero;
                float transverseSq = 0f;
                foreach (int index in tile)
                {
                    Vector3 delta = samples[index].position-first;
                    Vector3 perpendicular = delta-axisU*Vector3.Dot(delta,axisU);
                    if (perpendicular.sqrMagnitude > transverseSq)
                    {
                        transverseSq = perpendicular.sqrMagnitude;
                        farthestFromLine = perpendicular;
                    }
                }
                if (transverseSq < .0016f) continue;
                Vector3 normal = Vector3.Cross(axisU, farthestFromLine).normalized;
                Vector3 observedFront = Vector3.zero;
                foreach (int index in tile) observedFront += samples[index].viewDirection;
                if (observedFront.sqrMagnitude < .01f)
                    observedFront = cameraPosition - center;
                if (Vector3.Dot(normal, observedFront) < 0f) normal = -normal;
                // Opposite sides of a thin surface must remain distinct. A single
                // one-sided polygon would hide the measurements on its back side.
                foreach (int index in tile)
                {
                    Vector3 view = samples[index].viewDirection;
                    if (view.sqrMagnitude > .01f && Vector3.Dot(normal, view) <= 0f)
                    {
                        compatible = false;
                        break;
                    }
                }
                if (!compatible) continue;
                Vector3 axisV = Vector3.Cross(normal,axisU);
                var projected = new List<Projected>(tile.Count);
                foreach (int index in tile)
                {
                    Vector3 delta = samples[index].position-center;
                    if (Mathf.Abs(Vector3.Dot(delta,normal)) > planeTolerance)
                    {
                        compatible = false;
                        break;
                    }
                    projected.Add(new Projected {u=Vector3.Dot(delta,axisU),
                        v=Vector3.Dot(delta,axisV),source=index});
                }
                if (!compatible) continue;
                projected.Sort((a,b) => a.u == b.u ? a.v.CompareTo(b.v) : a.u.CompareTo(b.u));
                var hull = new List<Projected>();
                foreach (Projected p in projected)
                {
                    while (hull.Count >= 2 && Cross(hull[hull.Count-2],hull[hull.Count-1],p) <= 0f)
                        hull.RemoveAt(hull.Count-1);
                    hull.Add(p);
                }
                int lowerCount = hull.Count;
                for (int i = projected.Count-2; i >= 0; i--)
                {
                    Projected p = projected[i];
                    while (hull.Count > lowerCount && Cross(hull[hull.Count-2],hull[hull.Count-1],p) <= 0f)
                        hull.RemoveAt(hull.Count-1);
                    hull.Add(p);
                }
                if (hull.Count > 1) hull.RemoveAt(hull.Count-1);
                if (hull.Count < 3) continue;
                float area2 = 0f;
                bool edgeTooLong = false;
                float maxEdge = Mathf.Min(cellSize*.75f, .24f);
                float maxExtent = Mathf.Min(cellSize*1.5f, .30f);
                for (int i = 0; i < hull.Count; i++)
                {
                    Projected a = hull[i], b = hull[(i+1)%hull.Count];
                    area2 += a.u*b.v-a.v*b.u;
                    if ((samples[a.source].position-samples[b.source].position).sqrMagnitude >
                        maxEdge*maxEdge)
                        edgeTooLong = true;
                    // Bound the polygon even when many short edges follow its outline.
                    for (int j = i+1; j < hull.Count; j++)
                    {
                        float du = a.u-hull[j].u, dv = a.v-hull[j].v;
                        if (du*du+dv*dv > maxExtent*maxExtent) edgeTooLong = true;
                    }
                }
                if (edgeTooLong || Mathf.Abs(area2) < .16f*cellSize*cellSize) continue;
                // A convex hull and center fan can fill a hole between otherwise
                // coplanar samples. Require nearby measurements across its interior.
                // Keep the support radius near the source voxel spacing. The
                // previous 4.8-5 cm radius filled measurable 9 cm holes.
                float maxSupportGap = Mathf.Min(cellSize*.16f, .026f);
                if (!HasDenseSupport(projected,hull,maxSupportGap)) continue;

                int centerVertex = vertices.Count;
                vertices.Add(center);
                int r=0,g=0,bColor=0,aColor=0;
                foreach (int index in tile)
                {
                    Color32 c = samples[index].color;
                    r += c.r; g += c.g; bColor += c.b; aColor += c.a;
                    covered[index] = true;
                }
                colors.Add(new Color32((byte)(r/tile.Count),(byte)(g/tile.Count),
                    (byte)(bColor/tile.Count),(byte)(aColor/tile.Count)));
                foreach (Projected p in hull)
                {
                    vertices.Add(samples[p.source].position);
                    colors.Add(samples[p.source].color);
                }
                for (int i = 0; i < hull.Count; i++)
                {
                    triangles.Add(centerVertex);
                    triangles.Add(centerVertex+1+i);
                    triangles.Add(centerVertex+1+(i+1)%hull.Count);
                }
                polygonCount++;
            }

            var mesh = new Mesh {indexFormat=IndexFormat.UInt32, name="ArScanner Surface LOD"};
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles,0);
            if (vertices.Count > 0) mesh.RecalculateBounds();
            return mesh;
        }

        private static float Cross(Projected a, Projected b, Projected c) =>
            (b.u-a.u)*(c.v-a.v)-(b.v-a.v)*(c.u-a.u);

        private static bool HasDenseSupport(List<Projected> projected, List<Projected> hull,
                                            float maxGap)
        {
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
            foreach (Projected p in hull)
            {
                minU = Mathf.Min(minU,p.u); maxU = Mathf.Max(maxU,p.u);
                minV = Mathf.Min(minV,p.v); maxV = Mathf.Max(maxV,p.v);
            }
            float maxGapSq = maxGap*maxGap;
            if (!HasNearbySample(projected,0f,0f,maxGapSq)) return false;
            float step = maxGap*.75f;
            int uSteps = Mathf.Max(1,Mathf.CeilToInt((maxU-minU)/step));
            int vSteps = Mathf.Max(1,Mathf.CeilToInt((maxV-minV)/step));
            for (int uIndex = 0; uIndex < uSteps; uIndex++)
            {
                float u = minU+(uIndex+.5f)*(maxU-minU)/uSteps;
                for (int vIndex = 0; vIndex < vSteps; vIndex++)
                {
                    float v = minV+(vIndex+.5f)*(maxV-minV)/vSteps;
                    var location = new Projected {u=u,v=v};
                    bool inside = true;
                    for (int edge = 0; edge < hull.Count; edge++)
                    {
                        if (Cross(hull[edge],hull[(edge+1)%hull.Count],location) < -1e-6f)
                        {
                            inside = false;
                            break;
                        }
                    }
                    if (inside && !HasNearbySample(projected,u,v,maxGapSq)) return false;
                }
            }
            return true;
        }

        private static bool HasNearbySample(List<Projected> projected, float u, float v,
                                            float maxGapSq)
        {
            foreach (Projected p in projected)
            {
                float du = p.u-u, dv = p.v-v;
                if (du*du+dv*dv <= maxGapSq) return true;
            }
            return false;
        }

        private static bool IsFinite(Vector3 p) =>
            !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
}
