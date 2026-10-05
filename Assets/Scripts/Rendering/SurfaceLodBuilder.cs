using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArScanner.Rendering
{
    // Only display geometry is simplified; observations and exports stay intact.
    // Every rectangle needs local support throughout its area, including its center.
    public static class SurfaceLodBuilder
    {
        public struct Sample
        {
            public Vector3 position,viewDirection;
            public Color32 color;
            public float temperature;
            public bool hasThermal;
        }
        // Geometry construction is pure managed data and can run in a worker.
        // CreateMesh must run on Unity's main thread.
        public sealed class GeometryData
        {
            public Vector3[] vertices;
            public Color32[] colors;
            public int[] indices;
            public bool[] covered;
            public int[] coveredRectangle;
            public RectangleInfo[] rectangles;
            public float[] spacing;
            public int polygonCount;
        }
        public struct RectangleInfo
        {
            public bool hasThermal;
            public float minimumTemperature,maximumTemperature;
            public float supportRadiusU,supportRadiusV;
        }
        private struct Projected { public float u,v; public int source; }
        private struct TileKey : IEquatable<TileKey>
        {
            public int x,y,z,level;
            public bool Equals(TileKey other) => x==other.x && y==other.y && z==other.z && level==other.level;
            public override bool Equals(object obj) => obj is TileKey other && Equals(other);
            public override int GetHashCode() => (((x*397)^y)*397^z)*397^level;
        }
        private struct CellKey : IEquatable<CellKey>
        {
            public int x,y;
            public CellKey(int x,int y) { this.x=x; this.y=y; }
            public bool Equals(CellKey other) => x==other.x && y==other.y;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => (x*397)^y;
        }
        private sealed class PlaneTile
        {
            public TileKey key;
            public List<int> sources;
            public Vector3 center,normal;
            public bool assigned;
        }
        private struct Cell { public bool thermal,outsideNear; public float minimum,maximum; }
        private struct SupportCoverage { public bool supported; public int kinds; public float minimum,maximum; }
        private struct SupportFootprint
        {
            public float radiusU,radiusV;
            public bool Contains(float du,float dv) =>
                du*du/(radiusU*radiusU)+dv*dv/(radiusV*radiusV)<=1f;
        }
        private sealed class SupportBin
        {
            // Bound representatives to keep duplicate observations from creating
            // a quadratic nearest-neighbor search. Thermal extrema include ALL returns.
            public readonly Projected[] representatives=new Projected[8];
            public int count,thermalKinds;
            public float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
            public void Add(Projected point,Sample sample)
            {
                thermalKinds|=sample.hasThermal ? 2 : 1;
                if (sample.hasThermal)
                {
                    if (!IsFinite(sample.temperature)) thermalKinds|=4;
                    else { minimum=Mathf.Min(minimum,sample.temperature); maximum=Mathf.Max(maximum,sample.temperature); }
                }
                for (int i=0;i<count;i++)
                    if (DistanceSq(point,representatives[i])<.000004f) return;
                if (count<representatives.Length) representatives[count++]=point;
            }
        }
        private const float SupportBinMeters=.02f,CoverageCellMeters=.025f;

        public static Mesh Build(Sample[] samples,Vector3 cameraPosition,float nearMeters,
            float farMeters,float tileSize,float planeTolerance,float maxThermalSpread,
            out bool[] covered,out int polygonCount) =>
            Build(samples,cameraPosition,nearMeters,farMeters,tileSize,planeTolerance,
                maxThermalSpread,out covered,out polygonCount,out _);

        // Zero spacing means no locally supported planar area was established.
        public static Mesh Build(Sample[] samples,Vector3 cameraPosition,float nearMeters,
            float farMeters,float tileSize,float planeTolerance,float maxThermalSpread,
            out bool[] covered,out int polygonCount,out float[] supportedSpacing)
        {
            GeometryData data=BuildGeometryData(samples,cameraPosition,nearMeters,farMeters,tileSize,planeTolerance,maxThermalSpread);
            covered=data.covered; supportedSpacing=data.spacing; polygonCount=data.polygonCount;
            return CreateMesh(data);
        }

        public static GeometryData BuildGeometryData(Sample[] samples,Vector3 cameraPosition,float nearMeters,
            float farMeters,float tileSize,float planeTolerance,float maxThermalSpread)
        {
            var covered=new bool[samples.Length]; var supportedSpacing=new float[samples.Length]; int polygonCount=0;
            var coveredRectangle=new int[samples.Length];
            for (int i=0;i<coveredRectangle.Length;i++) coveredRectangle[i]=-1;
            var rectangles=new List<RectangleInfo>();
            var vertices=new List<Vector3>(); var colors=new List<Color32>(); var indices=new List<int>();
            var sourcesByTile=new Dictionary<TileKey,List<int>>();
            tileSize=Mathf.Max(.05f,tileSize); planeTolerance=Mathf.Max(.001f,planeTolerance);
            maxThermalSpread=Mathf.Max(0f,maxThermalSpread);
            for (int index=0;index<samples.Length;index++)
            {
                Vector3 p=samples[index].position;
                if (!IsFinite(p)) continue;
                int level=Vector3.Distance(p,cameraPosition)>=farMeters ? 1 : 0;
                float cell=tileSize*(level==1 ? 2f : 1f);
                var key=new TileKey {x=Mathf.FloorToInt(p.x/cell),y=Mathf.FloorToInt(p.y/cell),
                    z=Mathf.FloorToInt(p.z/cell),level=level};
                if (!sourcesByTile.TryGetValue(key,out var tile)) sourcesByTile[key]=tile=new List<int>();
                tile.Add(index);
            }
            var planes=new Dictionary<TileKey,PlaneTile>();
            foreach (var entry in sourcesByTile)
            {
                var tile=entry.Value;
                if (tile.Count<6 || !TryFitPlane(samples,tile,planeTolerance,out Vector3 center,out Vector3 normal)) continue;
                Vector3 front=Vector3.zero;
                foreach (int index in tile) front+=samples[index].viewDirection;
                if (front.sqrMagnitude<.01f) front=cameraPosition-center;
                if (Vector3.Dot(normal,front)<0f) normal=-normal;
                bool compatible=true;
                foreach (int index in tile)
                {
                    Vector3 view=samples[index].viewDirection;
                    if (view.sqrMagnitude>.01f && Vector3.Dot(normal,view)<=0f) {compatible=false; break;}
                }
                if (compatible) planes.Add(entry.Key,new PlaneTile {key=entry.Key,sources=tile,center=center,normal=normal});
            }
            // Grow compatible adjacent tiles into a single plane before rasterizing.
            // Check every neighbor against the seed to avoid drift around a curved wall.
            var pending=new Queue<PlaneTile>(); var regionTiles=new List<PlaneTile>(); var regionSources=new List<int>();
            var attachedSparseTiles=new HashSet<TileKey>();
            foreach (PlaneTile seed in planes.Values)
            {
                if (seed.assigned) continue;
                pending.Clear(); regionTiles.Clear(); regionSources.Clear();
                seed.assigned=true; pending.Enqueue(seed);
                while (pending.Count>0)
                {
                    PlaneTile tile=pending.Dequeue(); regionTiles.Add(tile); regionSources.AddRange(tile.sources);
                    int scale=tile.key.level==1 ? 2 : 1;
                    int sx=tile.key.x*scale,sy=tile.key.y*scale,sz=tile.key.z*scale;
                    for (int x=sx-1;x<=sx+scale;x++)
                    for (int y=sy-1;y<=sy+scale;y++)
                    for (int z=sz-1;z<=sz+scale;z++)
                    for (int level=0;level<2;level++)
                    {
                        var key=new TileKey {x=level==0 ? x : Mathf.FloorToInt(x*.5f),
                            y=level==0 ? y : Mathf.FloorToInt(y*.5f),z=level==0 ? z : Mathf.FloorToInt(z*.5f),level=level};
                        if (!planes.TryGetValue(key,out var next))
                        {
                            // The near/far radius and voxel edges can leave 1-5
                            // boundary samples in a tile. An adjacent measured
                            // plane may support them without fitting a plane
                            // from that sparse tile alone or hiding a depth edge.
                            if (!attachedSparseTiles.Contains(key) && sourcesByTile.TryGetValue(key,out var sparse) && sparse.Count<6 &&
                                FitsPlane(samples,sparse,seed.center,seed.normal,planeTolerance) && FitsFront(samples,sparse,seed.normal))
                            { attachedSparseTiles.Add(key); regionSources.AddRange(sparse); }
                            continue;
                        }
                        if (next.assigned || Vector3.Dot(seed.normal,next.normal)<.98f ||
                            !FitsPlane(samples,next.sources,seed.center,seed.normal,planeTolerance*1.5f)) continue;
                        next.assigned=true; pending.Enqueue(next);
                    }
                }
                bool regionFits=TryFitPlane(samples,regionSources,planeTolerance,out Vector3 center,out Vector3 normal);
                if (regionFits && Vector3.Dot(normal,seed.normal)<0f) normal=-normal;
                if (regionFits && FitsFront(samples,regionSources,normal))
                {
                    BuildRectangles(samples,regionSources,center,normal,cameraPosition,nearMeters,maxThermalSpread,
                        covered,coveredRectangle,supportedSpacing,vertices,colors,indices,rectangles,ref polygonCount);
                }
                else
                    foreach (PlaneTile tile in regionTiles)
                        BuildRectangles(samples,tile.sources,tile.center,tile.normal,cameraPosition,nearMeters,maxThermalSpread,
                            covered,coveredRectangle,supportedSpacing,vertices,colors,indices,rectangles,ref polygonCount);
            }
            return new GeometryData { vertices=vertices.ToArray(),colors=colors.ToArray(),indices=indices.ToArray(),
                covered=covered,coveredRectangle=coveredRectangle,rectangles=rectangles.ToArray(),
                spacing=supportedSpacing,polygonCount=polygonCount };
        }

        public static Mesh CreateMesh(GeometryData data)
        {
            var mesh=new Mesh {indexFormat=IndexFormat.UInt32,name="ArScanner Surface LOD"};
            mesh.SetVertices(data.vertices); mesh.SetColors(data.colors); mesh.SetTriangles(data.indices,0);
            if (data.vertices.Length>0) mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildRectangles(Sample[] samples,List<int> sources,Vector3 center,Vector3 normal,
            Vector3 camera,float nearMeters,float maxSpread,bool[] covered,int[] coveredRectangle,float[] spacing,
            List<Vector3> vertices,List<Color32> colors,List<int> indices,List<RectangleInfo> rectangles,ref int polygonCount)
        {
            // Stable world axes prevent PCA's arbitrary major-axis rotation on square walls.
            Vector3 axisU=Mathf.Abs(normal.y)<.9f ? Vector3.Cross(Vector3.up,normal).normalized :
                (Vector3.right-normal*Vector3.Dot(Vector3.right,normal)).normalized;
            Vector3 axisV=Vector3.Cross(normal,axisU).normalized;
            var projected=new List<Projected>(sources.Count); var bins=new Dictionary<CellKey,SupportBin>();
            float minU=float.PositiveInfinity,minV=minU,maxU=float.NegativeInfinity,maxV=maxU;
            foreach (int source in sources)
            {
                Vector3 d=samples[source].position-center;
                var p=new Projected {u=Vector3.Dot(d,axisU),v=Vector3.Dot(d,axisV),source=source};
                projected.Add(p); minU=Mathf.Min(minU,p.u); minV=Mathf.Min(minV,p.v);
                maxU=Mathf.Max(maxU,p.u); maxV=Mathf.Max(maxV,p.v);
                var key=new CellKey(Mathf.FloorToInt(p.u/SupportBinMeters),Mathf.FloorToInt(p.v/SupportBinMeters));
                if (!bins.TryGetValue(key,out var bin)) bins[key]=bin=new SupportBin();
                bin.Add(p,samples[source]);
            }
            if (maxU-minU<.02f || maxV-minV<.02f) return;
            var estimates=new List<float>(Mathf.Min(512,projected.Count));
            int stride=Mathf.Max(1,projected.Count/512);
            for (int i=0;i<projected.Count;i+=stride)
            {
                float distance=NearestDistanceSq(projected[i],bins,.075f);
                if (!float.IsInfinity(distance)) estimates.Add(Mathf.Sqrt(distance));
            }
            if (estimates.Count==0) return;
            estimates.Sort(); float localSpacing=estimates[estimates.Count/2];
            float radius=Mathf.Clamp(localSpacing*.75f,.026f,.035f);
            var footprint=new SupportFootprint {radiusU=radius,radiusV=radius};
            float crossTolerance=Mathf.Clamp(localSpacing*.35f,.005f,.016f);
            // LiDAR traces can be dense vertically but several centimeters apart
            // horizontally. Expand only the measured sparse direction, requiring
            // neighbors on both sides in repeated traces; a lone line or gap
            // cannot establish that spacing. The dense direction stays capped.
            if (TryDirectionalSpacing(projected,bins,true,crossTolerance,localSpacing,out float spacingU) &&
                TryDirectionalSpacing(projected,bins,false,crossTolerance,localSpacing,out float spacingV))
            {
                if (spacingU>spacingV*1.4f) footprint.radiusU=Mathf.Max(radius,spacingU*.75f);
                else if (spacingV>spacingU*1.4f) footprint.radiusV=Mathf.Max(radius,spacingV*.75f);
            }
            int width=Mathf.Max(1,Mathf.CeilToInt((maxU-minU)/CoverageCellMeters));
            int height=Mathf.Max(1,Mathf.CeilToInt((maxV-minV)/CoverageCellMeters));
            float du=(maxU-minU)/width,dv=(maxV-minV)/height;
            int reachU=Mathf.CeilToInt(footprint.radiusU/du),reachV=Mathf.CeilToInt(footprint.radiusV/dv);
            var candidates=new HashSet<CellKey>();
            foreach (Projected p in projected)
            {
                int px=Mathf.Clamp(Mathf.FloorToInt((p.u-minU)/du),0,width-1);
                int py=Mathf.Clamp(Mathf.FloorToInt((p.v-minV)/dv),0,height-1);
                for (int x=Mathf.Max(0,px-reachU);x<=Mathf.Min(width-1,px+reachU);x++)
                for (int y=Mathf.Max(0,py-reachV);y<=Mathf.Min(height-1,py+reachV);y++)
                {
                    // Center support is necessary for every accepted cell.
                    // Generate only those candidates, not its whole 5x5 box.
                    float dx=minU+(x+.5f)*du-p.u,dy=minV+(y+.5f)*dv-p.v;
                    if (footprint.Contains(dx,dy)) candidates.Add(new CellKey(x,y));
                }
            }
            var cells=new Dictionary<CellKey,Cell>();
            var cornerCoverage=new Dictionary<CellKey,SupportCoverage>();
            foreach (CellKey key in candidates)
            {
                float u=minU+key.x*du,v=minV+key.y*dv;
                if (!TrySupportedCell(key,minU,minV,du,dv,bins,cornerCoverage,footprint,maxSpread,out Cell cell)) continue;
                // Test the closest point in the cell, not just its four corners.
                Vector3 d=camera-center;
                float closestU=Mathf.Clamp(Vector3.Dot(d,axisU),u,u+du),closestV=Mathf.Clamp(Vector3.Dot(d,axisV),v,v+dv);
                cell.outsideNear=Vector3.Distance(center+axisU*closestU+axisV*closestV,camera)>=nearMeters;
                cells.Add(key,cell);
            }
            var ordered=new List<CellKey>(cells.Keys);
            ordered.Sort((a,b)=>a.y==b.y ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var consumed=new HashSet<CellKey>();
            var cellRectangles=new Dictionary<CellKey,int>();
            foreach (CellKey start in ordered)
            {
                Cell first=cells[start];
                if (!first.outsideNear || consumed.Contains(start)) continue;
                float minimum=first.minimum,maximum=first.maximum;
                int endX=start.x+1,endY=start.y+1;
                while (endX<width && TryExtend(cells,consumed,new CellKey(endX,start.y),first.thermal,maxSpread,
                    ref minimum,ref maximum)) endX++;
                while (endY<height)
                {
                    float rowMin=minimum,rowMax=maximum; bool compatible=true;
                    for (int x=start.x;x<endX;x++)
                        if (!TryExtend(cells,consumed,new CellKey(x,endY),first.thermal,maxSpread,ref rowMin,ref rowMax))
                        {compatible=false; break;}
                    if (!compatible) break;
                    minimum=rowMin; maximum=rowMax; endY++;
                }
                AddRectangle(center,axisU,axisV,minU+start.x*du,minV+start.y*dv,minU+endX*du,minV+endY*dv,
                    bins,samples,vertices,colors,indices,Mathf.Max(.05f,Mathf.Max(footprint.radiusU,footprint.radiusV)));
                rectangles.Add(new RectangleInfo {hasThermal=first.thermal,minimumTemperature=minimum,maximumTemperature=maximum,
                    supportRadiusU=footprint.radiusU,supportRadiusV=footprint.radiusV});
                int rectangle=polygonCount;
                polygonCount++;
                for (int y=start.y;y<endY;y++)
                for (int x=start.x;x<endX;x++)
                {
                    var key=new CellKey(x,y);
                    consumed.Add(key); cellRectangles.Add(key,rectangle);
                }
            }
            foreach (Projected p in projected)
            {
                var key=new CellKey(Mathf.Clamp(Mathf.FloorToInt((p.u-minU)/du),0,width-1),
                    Mathf.Clamp(Mathf.FloorToInt((p.v-minV)/dv),0,height-1));
                if (!cells.ContainsKey(key)) continue;
                spacing[p.source]=localSpacing;
                if (cellRectangles.TryGetValue(key,out int rectangle) && Vector3.Distance(samples[p.source].position,camera)>=nearMeters)
                {covered[p.source]=true; coveredRectangle[p.source]=rectangle;}
            }
        }

        private static bool TrySupportedCell(CellKey key,float minU,float minV,float du,float dv,
            Dictionary<CellKey,SupportBin> bins,Dictionary<CellKey,SupportCoverage> cornerCoverage,
            SupportFootprint footprint,float maxSpread,out Cell cell)
        {
            cell=new Cell {minimum=float.PositiveInfinity,maximum=float.NegativeInfinity}; int kinds=0;
            for (int i=0;i<5;i++)
            {
                SupportCoverage support;
                if (i==4)
                    support=QuerySupport(new Projected {u=minU+(key.x+.5f)*du,v=minV+(key.y+.5f)*dv},bins,footprint);
                else
                {
                    // Interior corners are shared by four cells. Reuse their
                    // exact support/thermal checks instead of querying again.
                    var corner=new CellKey(key.x+i%2,key.y+i/2);
                    if (!cornerCoverage.TryGetValue(corner,out support))
                    {
                        support=QuerySupport(new Projected {u=minU+corner.x*du,v=minV+corner.y*dv},bins,footprint);
                        cornerCoverage.Add(corner,support);
                    }
                }
                kinds|=support.kinds;
                cell.minimum=Mathf.Min(cell.minimum,support.minimum); cell.maximum=Mathf.Max(cell.maximum,support.maximum);
                if (!support.supported || (kinds!=1 && kinds!=2) || (kinds==2 && cell.maximum-cell.minimum>maxSpread)) return false;
            }
            cell.thermal=kinds==2;
            return true;
        }
        private static SupportCoverage QuerySupport(Projected p,Dictionary<CellKey,SupportBin> bins,SupportFootprint footprint)
        {
            var support=new SupportCoverage {minimum=float.PositiveInfinity,maximum=float.NegativeInfinity};
            int bx=Mathf.FloorToInt(p.u/SupportBinMeters),by=Mathf.FloorToInt(p.v/SupportBinMeters);
            int reachU=Mathf.CeilToInt(footprint.radiusU/SupportBinMeters),reachV=Mathf.CeilToInt(footprint.radiusV/SupportBinMeters);
            for (int x=bx-reachU;x<=bx+reachU;x++)
            for (int y=by-reachV;y<=by+reachV;y++)
            {
                if (!bins.TryGetValue(new CellKey(x,y),out var bin)) continue;
                bool close=false;
                for (int j=0;j<bin.count;j++)
                    if (footprint.Contains(p.u-bin.representatives[j].u,p.v-bin.representatives[j].v)) {close=true; break;}
                if (!close) continue;
                support.supported=true; support.kinds|=bin.thermalKinds;
                support.minimum=Mathf.Min(support.minimum,bin.minimum); support.maximum=Mathf.Max(support.maximum,bin.maximum);
            }
            return support;
        }
        private static bool TryExtend(Dictionary<CellKey,Cell> cells,HashSet<CellKey> consumed,CellKey key,
            bool thermal,float maxSpread,ref float minimum,ref float maximum)
        {
            if (!cells.TryGetValue(key,out Cell cell) || !cell.outsideNear || cell.thermal!=thermal || consumed.Contains(key)) return false;
            float lo=Mathf.Min(minimum,cell.minimum),hi=Mathf.Max(maximum,cell.maximum);
            if (thermal && hi-lo>maxSpread) return false;
            minimum=lo; maximum=hi;
            return true;
        }
        private static void AddRectangle(Vector3 center,Vector3 axisU,Vector3 axisV,float u0,float v0,float u1,float v1,
            Dictionary<CellKey,SupportBin> bins,Sample[] samples,List<Vector3> vertices,List<Color32> colors,List<int> indices,float colorReach)
        {
            int start=vertices.Count;
            for (int i=0;i<4;i++)
            {
                float u=i==0 || i==3 ? u0 : u1,v=i<2 ? v0 : v1;
                vertices.Add(center+axisU*u+axisV*v);
                int source=NearestSource(new Projected {u=u,v=v},bins,colorReach);
                colors.Add(source>=0 ? samples[source].color : new Color32(235,235,235,255));
            }
            indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            indices.Add(start); indices.Add(start+2); indices.Add(start+3);
        }
        private static int NearestSource(Projected p,Dictionary<CellKey,SupportBin> bins,float radius)
        {
            float nearest=float.PositiveInfinity; int source=-1;
            int bx=Mathf.FloorToInt(p.u/SupportBinMeters),by=Mathf.FloorToInt(p.v/SupportBinMeters),reach=Mathf.CeilToInt(radius/SupportBinMeters);
            for (int x=bx-reach;x<=bx+reach;x++)
            for (int y=by-reach;y<=by+reach;y++)
            {
                if (!bins.TryGetValue(new CellKey(x,y),out var bin)) continue;
                for (int i=0;i<bin.count;i++)
                {
                    float distance=DistanceSq(p,bin.representatives[i]);
                    if (distance<nearest) {nearest=distance; source=bin.representatives[i].source;}
                }
            }
            return source;
        }
        private static float NearestDistanceSq(Projected p,Dictionary<CellKey,SupportBin> bins,float radius)
        {
            float nearest=float.PositiveInfinity;
            int bx=Mathf.FloorToInt(p.u/SupportBinMeters),by=Mathf.FloorToInt(p.v/SupportBinMeters),reach=Mathf.CeilToInt(radius/SupportBinMeters);
            for (int x=bx-reach;x<=bx+reach;x++)
            for (int y=by-reach;y<=by+reach;y++)
            {
                if (!bins.TryGetValue(new CellKey(x,y),out var bin)) continue;
                for (int i=0;i<bin.count;i++)
                {
                    float distance=DistanceSq(p,bin.representatives[i]);
                    if (distance>1e-8f && distance<nearest && distance<=radius*radius) nearest=distance;
                }
            }
            return nearest;
        }
        private static bool TryDirectionalSpacing(List<Projected> projected,Dictionary<CellKey,SupportBin> bins,
            bool alongU,float crossTolerance,float denseSpacing,out float spacing)
        {
            const float maximumStep=.12f;
            float minimumStep=Mathf.Max(.01f,denseSpacing*.6f);
            var estimates=new List<float>(128);
            int stride=Mathf.Max(1,projected.Count/128);
            int alongReach=Mathf.CeilToInt(maximumStep/SupportBinMeters);
            int crossReach=Mathf.CeilToInt(crossTolerance/SupportBinMeters);
            for (int i=0;i<projected.Count;i+=stride)
            {
                Projected point=projected[i];
                int bx=Mathf.FloorToInt(point.u/SupportBinMeters),by=Mathf.FloorToInt(point.v/SupportBinMeters);
                float negative=float.PositiveInfinity,positive=float.PositiveInfinity;
                int reachX=alongU ? alongReach : crossReach,reachY=alongU ? crossReach : alongReach;
                for (int x=bx-reachX;x<=bx+reachX;x++)
                for (int y=by-reachY;y<=by+reachY;y++)
                {
                    if (!bins.TryGetValue(new CellKey(x,y),out var bin)) continue;
                    for (int j=0;j<bin.count;j++)
                    {
                        Projected neighbor=bin.representatives[j];
                        float along=alongU ? neighbor.u-point.u : neighbor.v-point.v;
                        float across=alongU ? neighbor.v-point.v : neighbor.u-point.u;
                        float distance=Mathf.Abs(along);
                        if (Mathf.Abs(across)>crossTolerance || distance<minimumStep || distance>maximumStep) continue;
                        if (along<0f) negative=Mathf.Min(negative,distance);
                        else positive=Mathf.Min(positive,distance);
                    }
                }
                if (!float.IsInfinity(negative) && !float.IsInfinity(positive) &&
                    Mathf.Max(negative,positive)<=Mathf.Min(negative,positive)*1.5f)
                    estimates.Add((negative+positive)*.5f);
            }
            spacing=0f;
            if (estimates.Count<Mathf.Min(16,Mathf.Max(4,projected.Count/16))) return false;
            estimates.Sort(); spacing=estimates[estimates.Count/2];
            return true;
        }
        private static bool FitsPlane(Sample[] samples,List<int> sources,Vector3 center,Vector3 normal,float tolerance)
        {
            foreach (int source in sources) if (Mathf.Abs(Vector3.Dot(samples[source].position-center,normal))>tolerance) return false;
            return true;
        }
        private static bool FitsFront(Sample[] samples,List<int> sources,Vector3 normal)
        {
            foreach (int source in sources)
            {
                Vector3 view=samples[source].viewDirection;
                if (view.sqrMagnitude>.01f && Vector3.Dot(normal,view)<=0f) return false;
            }
            return true;
        }
        // Least-squares plane, including every observation, using Jacobi covariance diagonalization.
        private static bool TryFitPlane(Sample[] samples,List<int> tile,float tolerance,out Vector3 center,out Vector3 normal)
        {
            center=Vector3.zero; normal=Vector3.zero;
            foreach (int index in tile) center+=samples[index].position;
            center/=tile.Count;
            float[,] matrix=new float[3,3],basis=new float[3,3];
            for (int i=0;i<3;i++) basis[i,i]=1f;
            foreach (int index in tile)
            {
                Vector3 d=samples[index].position-center;
                for (int row=0;row<3;row++) for (int col=row;col<3;col++) matrix[row,col]+=d[row]*d[col]/tile.Count;
            }
            matrix[1,0]=matrix[0,1]; matrix[2,0]=matrix[0,2]; matrix[2,1]=matrix[1,2];
            for (int iteration=0;iteration<12;iteration++)
            {
                int p=0,q=1;
                if (Mathf.Abs(matrix[0,2])>Mathf.Abs(matrix[p,q])) {p=0; q=2;}
                if (Mathf.Abs(matrix[1,2])>Mathf.Abs(matrix[p,q])) {p=1; q=2;}
                if (Mathf.Abs(matrix[p,q])<1e-9f) break;
                float angle=.5f*Mathf.Atan2(2f*matrix[p,q],matrix[q,q]-matrix[p,p]);
                float cosine=Mathf.Cos(angle),sine=Mathf.Sin(angle),pp=matrix[p,p],qq=matrix[q,q],pq=matrix[p,q];
                matrix[p,p]=cosine*cosine*pp-2f*sine*cosine*pq+sine*sine*qq;
                matrix[q,q]=sine*sine*pp+2f*sine*cosine*pq+cosine*cosine*qq; matrix[p,q]=matrix[q,p]=0f;
                for (int k=0;k<3;k++)
                {
                    if (k!=p && k!=q)
                    {
                        float kp=matrix[k,p],kq=matrix[k,q];
                        matrix[k,p]=matrix[p,k]=cosine*kp-sine*kq; matrix[k,q]=matrix[q,k]=sine*kp+cosine*kq;
                    }
                    float bp=basis[k,p],bq=basis[k,q]; basis[k,p]=cosine*bp-sine*bq; basis[k,q]=sine*bp+cosine*bq;
                }
            }
            int smallest=0,largest=0;
            for (int i=1;i<3;i++) {if (matrix[i,i]<matrix[smallest,smallest]) smallest=i; if (matrix[i,i]>matrix[largest,largest]) largest=i;}
            if (smallest==largest) return false;
            int middle=3-smallest-largest;
            // A single scan line cannot establish a measured surface.
            if (matrix[middle,middle]<.0001f || matrix[largest,largest]<.0004f) return false;
            normal=new Vector3(basis[0,smallest],basis[1,smallest],basis[2,smallest]).normalized;
            return FitsPlane(samples,tile,center,normal,tolerance);
        }
        private static float DistanceSq(Projected a,Projected b) => (a.u-b.u)*(a.u-b.u)+(a.v-b.v)*(a.v-b.v);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 p) => IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z);
    }
}
