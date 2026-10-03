using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArScanner.Rendering
{
    // Geometry is never moved. Only locally supported, compatible observations
    // become display triangles; holes and temperature boundaries remain open.
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

        private struct Projected { public float u,v; public int source; }
        private struct TileKey : IEquatable<TileKey>
        {
            public int x,y,z,level;
            public bool Equals(TileKey other) => x==other.x && y==other.y && z==other.z && level==other.level;
            public override bool Equals(object obj) => obj is TileKey other && Equals(other);
            public override int GetHashCode() => (((x*397)^y)*397^z)*397^level;
        }
        private struct Edge : IEquatable<Edge>
        {
            public int a,b;
            public Edge(int first,int second) { a=Math.Min(first,second); b=Math.Max(first,second); }
            public bool Equals(Edge other) => a==other.a && b==other.b;
            public override bool Equals(object obj) => obj is Edge other && Equals(other);
            public override int GetHashCode() => (a*397)^b;
        }
        private struct Triangle { public int a,b,c; public float centerU,centerV,radiusSq; }

        public static Mesh Build(Sample[] samples,Vector3 cameraPosition,float nearMeters,
            float farMeters,float tileSize,float planeTolerance,float maxThermalSpread,
            out bool[] covered,out int polygonCount) =>
            Build(samples,cameraPosition,nearMeters,farMeters,tileSize,planeTolerance,
                maxThermalSpread,out covered,out polygonCount,out _);

        // Zero spacing means no local planar neighborhood was established.
        public static Mesh Build(Sample[] samples,Vector3 cameraPosition,float nearMeters,
            float farMeters,float tileSize,float planeTolerance,float maxThermalSpread,
            out bool[] covered,out int polygonCount,out float[] supportedSpacing)
        {
            covered=new bool[samples.Length];
            supportedSpacing=new float[samples.Length];
            polygonCount=0;
            var vertices=new List<Vector3>();
            var colors=new List<Color32>();
            var indices=new List<int>();
            var tiles=new Dictionary<TileKey,List<int>>();
            tileSize=Mathf.Max(.05f,tileSize);
            for (int index=0; index<samples.Length; index++)
            {
                Vector3 p=samples[index].position;
                if (!IsFinite(p)) continue;
                int level=Vector3.Distance(p,cameraPosition)>=farMeters ? 1 : 0;
                float cell=tileSize*(level==1 ? 2f : 1f);
                var key=new TileKey { x=Mathf.FloorToInt(p.x/cell),
                    y=Mathf.FloorToInt(p.y/cell),z=Mathf.FloorToInt(p.z/cell),level=level };
                if (!tiles.TryGetValue(key,out var tile)) tiles[key]=tile=new List<int>();
                tile.Add(index);
            }
            foreach (var entry in tiles)
            {
                var tile=entry.Value;
                if (tile.Count<6 || !TryFitPlane(samples,tile,planeTolerance,
                        out Vector3 center,out Vector3 normal,out Vector3 axisU)) continue;
                Vector3 observedFront=Vector3.zero;
                foreach (int index in tile) observedFront+=samples[index].viewDirection;
                if (observedFront.sqrMagnitude<.01f) observedFront=cameraPosition-center;
                if (Vector3.Dot(normal,observedFront)<0f) normal=-normal;
                bool compatible=true;
                foreach (int index in tile)
                {
                    Vector3 view=samples[index].viewDirection;
                    if (view.sqrMagnitude>.01f && Vector3.Dot(normal,view)<=0f)
                    { compatible=false; break; }
                }
                if (!compatible) continue;
                Vector3 axisV=Vector3.Cross(normal,axisU).normalized;
                var projected=new List<Projected>(tile.Count+3);
                foreach (int index in tile)
                {
                    Vector3 delta=samples[index].position-center;
                    projected.Add(new Projected { u=Vector3.Dot(delta,axisU),
                        v=Vector3.Dot(delta,axisV),source=index });
                }
                projected.Sort((a,b) => a.u==b.u ? a.v.CompareTo(b.v) : a.u.CompareTo(b.u));
                float[] nearest=NearestSpacing(projected);
                var triangles=Triangulate(projected);
                int sourceCount=tile.Count;
                int[] meshVertices=new int[sourceCount];
                for (int i=0; i<sourceCount; i++) meshVertices[i]=-1;
                bool emitted=false;
                foreach (Triangle triangle in triangles)
                {
                    if (triangle.a>=sourceCount || triangle.b>=sourceCount || triangle.c>=sourceCount) continue;
                    Projected a=projected[triangle.a],b=projected[triangle.b],c=projected[triangle.c];
                    // Bound local triangles, not a long outer hull with many
                    // close observations along its boundary.
                    float spacing=Median(nearest[triangle.a],nearest[triangle.b],nearest[triangle.c]);
                    float supportRadius=Mathf.Clamp(spacing*.75f,.026f,.035f);
                    float maxEdge=Mathf.Clamp(spacing*1.6f,.042f,.075f);
                    if (triangle.radiusSq>supportRadius*supportRadius ||
                        DistanceSq(a,b)>maxEdge*maxEdge || DistanceSq(b,c)>maxEdge*maxEdge ||
                        DistanceSq(c,a)>maxEdge*maxEdge ||
                        !ThermallyCompatible(samples[a.source],samples[b.source],samples[c.source],maxThermalSpread))
                        continue;
                    RecordSpacing(supportedSpacing,a.source,nearest[triangle.a]);
                    RecordSpacing(supportedSpacing,b.source,nearest[triangle.b]);
                    RecordSpacing(supportedSpacing,c.source,nearest[triangle.c]);
                    if (Vector3.Distance(samples[a.source].position,cameraPosition)<nearMeters ||
                        Vector3.Distance(samples[b.source].position,cameraPosition)<nearMeters ||
                        Vector3.Distance(samples[c.source].position,cameraPosition)<nearMeters) continue;
                    int ia=AddVertex(triangle.a,projected,samples,meshVertices,vertices,colors);
                    int ib=AddVertex(triangle.b,projected,samples,meshVertices,vertices,colors);
                    int ic=AddVertex(triangle.c,projected,samples,meshVertices,vertices,colors);
                    indices.Add(ia); indices.Add(ib); indices.Add(ic);
                    covered[a.source]=covered[b.source]=covered[c.source]=true;
                    emitted=true;
                }
                if (emitted) polygonCount++;
            }
            var mesh=new Mesh { indexFormat=IndexFormat.UInt32,name="ArScanner Surface LOD" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices,0);
            if (vertices.Count>0) mesh.RecalculateBounds();
            return mesh;
        }

        private static void RecordSpacing(float[] output,int index,float spacing)
        {
            if (spacing>0f && (output[index]==0f || spacing<output[index])) output[index]=spacing;
        }
        private static int AddVertex(int index,List<Projected> projected,Sample[] samples,
            int[] mapping,List<Vector3> vertices,List<Color32> colors)
        {
            if (mapping[index]>=0) return mapping[index];
            Sample sample=samples[projected[index].source];
            mapping[index]=vertices.Count;
            vertices.Add(sample.position);
            colors.Add(sample.color);
            return mapping[index];
        }
        private static bool ThermallyCompatible(Sample a,Sample b,Sample c,float maxSpread)
        {
            if (a.hasThermal!=b.hasThermal || a.hasThermal!=c.hasThermal) return false;
            if (!a.hasThermal) return true;
            if (!IsFinite(a.temperature) || !IsFinite(b.temperature) || !IsFinite(c.temperature)) return false;
            return Mathf.Max(a.temperature,Mathf.Max(b.temperature,c.temperature))-
                Mathf.Min(a.temperature,Mathf.Min(b.temperature,c.temperature))<=maxSpread;
        }

        // Least-squares plane over every return. Jacobi diagonalization of a
        // symmetric 3x3 covariance matrix avoids a normal from noisy extremes.
        private static bool TryFitPlane(Sample[] samples,List<int> tile,float tolerance,
            out Vector3 center,out Vector3 normal,out Vector3 axisU)
        {
            center=Vector3.zero; normal=axisU=Vector3.zero;
            foreach (int index in tile) center+=samples[index].position;
            center/=tile.Count;
            float[,] matrix=new float[3,3],basis=new float[3,3];
            for (int i=0; i<3; i++) basis[i,i]=1f;
            foreach (int index in tile)
            {
                Vector3 d=samples[index].position-center;
                for (int row=0; row<3; row++)
                    for (int col=row; col<3; col++) matrix[row,col]+=d[row]*d[col]/tile.Count;
            }
            matrix[1,0]=matrix[0,1]; matrix[2,0]=matrix[0,2]; matrix[2,1]=matrix[1,2];
            for (int iteration=0; iteration<12; iteration++)
            {
                int p=0,q=1;
                if (Mathf.Abs(matrix[0,2])>Mathf.Abs(matrix[p,q])) { p=0; q=2; }
                if (Mathf.Abs(matrix[1,2])>Mathf.Abs(matrix[p,q])) { p=1; q=2; }
                if (Mathf.Abs(matrix[p,q])<1e-9f) break;
                float angle=.5f*Mathf.Atan2(2f*matrix[p,q],matrix[q,q]-matrix[p,p]);
                float cosine=Mathf.Cos(angle),sine=Mathf.Sin(angle);
                float pp=matrix[p,p],qq=matrix[q,q],pq=matrix[p,q];
                matrix[p,p]=cosine*cosine*pp-2f*sine*cosine*pq+sine*sine*qq;
                matrix[q,q]=sine*sine*pp+2f*sine*cosine*pq+cosine*cosine*qq;
                matrix[p,q]=matrix[q,p]=0f;
                for (int k=0; k<3; k++)
                {
                    if (k!=p && k!=q)
                    {
                        float kp=matrix[k,p],kq=matrix[k,q];
                        matrix[k,p]=matrix[p,k]=cosine*kp-sine*kq;
                        matrix[k,q]=matrix[q,k]=sine*kp+cosine*kq;
                    }
                    float bp=basis[k,p],bq=basis[k,q];
                    basis[k,p]=cosine*bp-sine*bq; basis[k,q]=sine*bp+cosine*bq;
                }
            }
            int smallest=0,largest=0;
            for (int i=1; i<3; i++)
            {
                if (matrix[i,i]<matrix[smallest,smallest]) smallest=i;
                if (matrix[i,i]>matrix[largest,largest]) largest=i;
            }
            if (smallest==largest) return false;
            int middle=3-smallest-largest;
            // A scan line does not establish a measured surface.
            if (matrix[middle,middle]<.0001f || matrix[largest,largest]<.0004f) return false;
            normal=new Vector3(basis[0,smallest],basis[1,smallest],basis[2,smallest]).normalized;
            axisU=new Vector3(basis[0,largest],basis[1,largest],basis[2,largest]).normalized;
            foreach (int index in tile)
                if (Mathf.Abs(Vector3.Dot(samples[index].position-center,normal))>tolerance) return false;
            return true;
        }

        private static float[] NearestSpacing(List<Projected> points)
        {
            var output=new float[points.Count];
            for (int i=0; i<points.Count; i++)
            {
                float nearest=float.PositiveInfinity;
                for (int j=0; j<points.Count; j++)
                    if (i!=j) nearest=Mathf.Min(nearest,DistanceSq(points[i],points[j]));
                output[i]=Mathf.Sqrt(nearest);
            }
            return output;
        }
        private static List<Triangle> Triangulate(List<Projected> points)
        {
            int count=points.Count;
            float extent=.01f;
            foreach (Projected p in points) extent=Mathf.Max(extent,Mathf.Max(Mathf.Abs(p.u),Mathf.Abs(p.v)));
            extent*=8f;
            points.Add(new Projected { u=-extent,v=-extent });
            points.Add(new Projected { u=extent,v=-extent });
            points.Add(new Projected { u=0f,v=extent });
            var triangles=new List<Triangle>();
            TryTriangle(points,count,count+1,count+2,out Triangle initial);
            triangles.Add(initial);
            for (int index=0; index<count; index++)
            {
                var edges=new Dictionary<Edge,int>();
                Projected point=points[index];
                for (int t=triangles.Count-1; t>=0; t--)
                {
                    Triangle triangle=triangles[t];
                    float du=point.u-triangle.centerU,dv=point.v-triangle.centerV;
                    if (du*du+dv*dv>triangle.radiusSq+1e-8f) continue;
                    AddEdge(edges,triangle.a,triangle.b);
                    AddEdge(edges,triangle.b,triangle.c);
                    AddEdge(edges,triangle.c,triangle.a);
                    triangles.RemoveAt(t);
                }
                foreach (var edge in edges)
                    if (edge.Value==1 && TryTriangle(points,edge.Key.a,edge.Key.b,index,out Triangle triangle))
                        triangles.Add(triangle);
            }
            return triangles;
        }
        private static void AddEdge(Dictionary<Edge,int> edges,int a,int b)
        {
            var key=new Edge(a,b);
            edges.TryGetValue(key,out int count);
            edges[key]=count+1;
        }
        private static bool TryTriangle(List<Projected> points,int a,int b,int c,out Triangle triangle)
        {
            triangle=default;
            Projected first=points[a],second=points[b],third=points[c];
            float cross=Cross(first,second,third);
            if (Mathf.Abs(cross)<1e-8f) return false;
            if (cross<0f) { int old=b; b=c; c=old; second=points[b]; third=points[c]; }
            float bu=second.u-first.u,bv=second.v-first.v;
            float cu=third.u-first.u,cv=third.v-first.v;
            float denominator=2f*(bu*cv-bv*cu);
            float bSq=bu*bu+bv*bv,cSq=cu*cu+cv*cv;
            float u=(cv*bSq-bv*cSq)/denominator;
            float v=(bu*cSq-cu*bSq)/denominator;
            triangle=new Triangle { a=a,b=b,c=c,centerU=first.u+u,centerV=first.v+v,radiusSq=u*u+v*v };
            return IsFinite(triangle.radiusSq);
        }
        private static float Median(float a,float b,float c) => a+b+c-Mathf.Min(a,Mathf.Min(b,c))-Mathf.Max(a,Mathf.Max(b,c));
        private static float DistanceSq(Projected a,Projected b) => (a.u-b.u)*(a.u-b.u)+(a.v-b.v)*(a.v-b.v);
        private static float Cross(Projected a,Projected b,Projected c) => (b.u-a.u)*(c.v-a.v)-(b.v-a.v)*(c.u-a.u);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 p) => IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z);
    }
}