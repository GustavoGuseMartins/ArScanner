#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ArScanner.Rendering;
using UnityEditor;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class SurfaceLodValidation
    {
        [MenuItem("Tools/AR Scanner/Validate Surface LOD")]
        public static void Run()
        {
            var dense=Grid(4,4,(x,y)=>new Vector3(x*.035f+.005f,0f,y*.035f+.005f),Vector3.up);
            Check(dense,new Vector3(0f,0f,-2f),.8f,.16f,(mesh,covered,polygons,spacing)=>
                Require(polygons==1 && mesh.triangles.Length==6 && covered.All(value=>value),
                    "A dense plane must become exactly one rectangle, including its boundary samples."));
            var workerData=Task.Run(()=>SurfaceLodBuilder.BuildGeometryData(dense,new Vector3(0f,0f,-2f),
                .8f,2.5f,.16f,.018f,2.5f)).GetAwaiter().GetResult();
            Require(workerData.polygonCount==1 && workerData.vertices.Length==4 && workerData.indices.Length==6 &&
                workerData.covered.All(value=>value),"The worker API must build the same rectangle without creating Unity objects.");
            Require(workerData.coveredRectangle.All(value=>value==0) && workerData.rectangles.Length==1 &&
                !workerData.rectangles[0].hasThermal,
                "Every covered observation must identify the rectangle whose support and thermal classification it can invalidate.");
            Mesh workerMesh=SurfaceLodBuilder.CreateMesh(workerData);
            try {Require(workerMesh.vertexCount==4,"Worker geometry must upload on the main thread.");}
            finally {UnityEngine.Object.DestroyImmediate(workerMesh);}
            var farDense=Grid(6,6,(x,y)=>new Vector3(x*.035f+.02f,0f,y*.035f+.02f),Vector3.up);
            Check(farDense,new Vector3(0f,0f,-3f),.8f,.16f,(mesh,covered,polygons,spacing)=>
                Require(polygons==1 && covered.All(value=>value),"Distant planar samples must retain their far LOD."));

            // This wall crosses many spatial tiles AND the near/far tile-size boundary.
            var wall=Wall(61,41);
            Check(wall,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons==1 && mesh.vertexCount==4 && covered.All(value=>value),
                    "A 1.5 by 1 m wall across adjacent tiles must become one rectangle, not one per tile."));
            var rotated=wall.Select(sample=>
            {
                sample.position=Quaternion.Euler(0f,31f,0f)*sample.position;
                sample.viewDirection=Quaternion.Euler(0f,31f,0f)*sample.viewDirection;
                return sample;
            }).ToArray();
            Check(rotated,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons==1 && covered.Count(value=>value)>rotated.Length*.98f,
                    "An oblique coplanar wall must also merge across voxel boundaries."));

            var hole=Wall(61,41,(x,y)=>!(x>=24 && x<=36 && y>=14 && y<=26));
            Check(hole,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
            {
                Require(polygons>1 && polygons<=20 && covered.Count(value=>value)>hole.Length*.9f,
                    "A wall around an opening should still use a small number of large rectangles.");
                Require(!Covers(mesh,new Vector3(.76f,.51f,2f)),"No rectangle may close an unmeasured opening in a wall.");
            });
            var disconnected=Wall(41,21,(x,y)=>x<=15 || x>=25);
            Check(disconnected,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>=2 && polygons<=6 && !Covers(mesh,new Vector3(.51f,.26f,2f)),
                    "Separate coplanar objects must retain the gap between them."));
            foreach (float radius in new[] {.075f,.045f})
            {
                var ring=new SurfaceLodBuilder.Sample[12];
                for (int i=0;i<ring.Length;i++)
                {
                    float angle=2f*Mathf.PI*i/ring.Length;
                    ring[i]=Sample(new Vector3(.08f+radius*Mathf.Cos(angle),0f,.08f+radius*Mathf.Sin(angle)),Vector3.up);
                }
                Check(ring,new Vector3(0f,0f,-2f),.8f,.16f,(mesh,covered,polygons,spacing)=>
                    Require(!Covers(mesh,new Vector3(.08f,0f,.08f)),"A coplanar ring must retain its unmeasured center."));
            }

            var discontinuity=(SurfaceLodBuilder.Sample[])dense.Clone();
            discontinuity[15].position+=Vector3.up*.1f;
            Check(discontinuity,new Vector3(0f,0f,-2f),.8f,.16f,(mesh,covered,polygons,spacing)=>
                Require(polygons==0 && covered.All(value=>!value),"A depth discontinuity must fall back to individual quads."));
            var opposing=new SurfaceLodBuilder.Sample[32];
            for (int i=0;i<16;i++)
            {
                opposing[2*i]=Sample(dense[i].position+Vector3.up*.005f,Vector3.up);
                opposing[2*i+1]=Sample(dense[i].position+Vector3.up*.015f,Vector3.down);
            }
            Check(opposing,new Vector3(0f,0f,-2f),.8f,.16f,(mesh,covered,polygons,spacing)=>
                Require(polygons==0 && covered.All(value=>!value),"Opposite-facing samples must remain independent quads."));
            Check(dense,new Vector3(.055f,.3f,.055f),.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons==0 && covered.All(value=>!value) && spacing.All(value=>value>0f),
                    "Near surfaces retain quads while still providing measured support for adaptive sizing."));
            var sparse=Wall(8,8,null,.12f);
            Check(sparse,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons==0 && covered.All(value=>!value),"Sparse returns cannot establish an unmeasured solid wall."));

            var traces=TraceWall(21,21);
            Check(traces,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons<=4 && covered.Count(value=>value)>traces.Length*.95f,
                    "Repeated 35 by 80 mm scan traces must establish a large measured plane instead of isolated vertical strips."));
            var traceDoor=TraceWall(21,21,(x,y)=>!(x>=8 && x<=12 && y>=4 && y<=16));
            Check(traceDoor,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>1 && polygons<=20 && !Covers(mesh,new Vector3(.81f,.36f,2f)) &&
                    covered.Count(value=>value)>traceDoor.Length*.85f,
                    "Directional trace support must retain a measured opening while merging the surrounding wall."));
            var missingTracePoint=TraceWall(21,21,(x,y)=>x!=10 || y!=10);
            Check(missingTracePoint,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(!Covers(mesh,new Vector3(.81f,.36f,2f)),
                    "A missing sample inside the measured trace grid must retain unsupported space at its position."));
            var isolatedLines=Grid(2,21,(x,y)=>new Vector3(.01f+x*.2f,.01f+y*.035f,2f),Vector3.back);
            Check(isolatedLines,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(!Covers(mesh,new Vector3(.11f,.36f,2f)),
                    "Isolated traces without repeated nearby cross-trace neighbors must retain the gap between them."));
            var isolatedLine=Grid(1,21,(x,y)=>new Vector3(.01f,.01f+y*.035f,2f),Vector3.back);
            Check(isolatedLine,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons==0 && covered.All(value=>!value),
                    "A single scan line cannot establish a plane or directional interpolation support."));
            var thermalTraces=TraceWall(21,21);
            for (int i=0;i<thermalTraces.Length;i++)
            {
                thermalTraces[i].hasThermal=true;
                thermalTraces[i].temperature=thermalTraces[i].position.x<.8f ? 20f : 60f;
            }
            Check(thermalTraces,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>1 && !Covers(mesh,new Vector3(.77f,.36f,2f)),
                    "Directional trace support must not merge or paint across a measured temperature step."));

            var thermal=Wall(61,41);
            for (int i=0;i<thermal.Length;i++)
            {
                thermal[i].hasThermal=true; thermal[i].temperature=thermal[i].position.x<.76f ? 20f : 40f;
                thermal[i].color=thermal[i].temperature<30f ? new Color32(0,0,255,255) : new Color32(255,0,0,255);
            }
            Check(thermal,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>=2 && polygons<=8 && !Covers(mesh,new Vector3(.7475f,.51f,2f)) &&
                    covered.Count(value=>value)>thermal.Length*.85f,
                    "A temperature step must split the large rectangles and retain individual boundary samples."));
            var hotspot=Wall(61,41);
            for (int i=0;i<hotspot.Length;i++) {hotspot[i].hasThermal=true; hotspot[i].temperature=20f;}
            int hotIndex=20*61+30; hotspot[hotIndex].temperature=40f;
            Check(hotspot,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>1 && !covered[hotIndex] && !Covers(mesh,hotspot[hotIndex].position),
                    "An isolated hot observation must not disappear inside a merged wall."));
            var gradient=Wall(61,41);
            for (int i=0;i<gradient.Length;i++) {gradient[i].hasThermal=true; gradient[i].temperature=20f+gradient[i].position.x*10f;}
            Check(gradient,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>=6 && polygons<30 && covered.Count(value=>value)>gradient.Length*.85f,
                    "A gradual thermal gradient cannot chain local compatibility into a rectangle with a large total spread."));
            var unavailable=(SurfaceLodBuilder.Sample[])thermal.Clone();
            for (int i=0;i<unavailable.Length;i++) if (unavailable[i].temperature>30f) unavailable[i].hasThermal=false;
            Check(unavailable,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons>=2 && !Covers(mesh,new Vector3(.7475f,.51f,2f)),
                    "Known and unavailable thermal observations must not collapse into the same rectangle."));

            // Maximum cloud size: exposes accidental whole-wall quadratic searches.
            var large=Wall(400,200);
            var timer=Stopwatch.StartNew();
            Check(large,Vector3.zero,.45f,.24f,(mesh,covered,polygons,spacing)=>
                Require(polygons<=4 && covered.Count(value=>value)>large.Length*.99f,
                    "An 80,000-point plane must simplify to a few rectangles."));
            timer.Stop();
            SurfaceLodAsyncValidation.Run();
            UnityEngine.Debug.Log("[Surface LOD validation] PASSED: multi-tile and directional-trace walls, holes, isolated lines, depth, thermal boundaries and regional async publication; 80,000 points in "+timer.ElapsedMilliseconds+" ms (Editor; device timing still required).");
        }
        private static void Check(SurfaceLodBuilder.Sample[] samples,Vector3 camera,float near,float tile,
            Action<Mesh,bool[],int,float[]> validate)
        {
            Mesh mesh=SurfaceLodBuilder.Build(samples,camera,near,2.5f,tile,.018f,2.5f,
                out bool[] covered,out int polygons,out float[] spacing);
            try
            {
                UnityEngine.Debug.Log("[Surface LOD case] samples="+samples.Length+" polygons="+polygons+
                    " vertices="+mesh.vertexCount+" covered="+covered.Count(value=>value)+" supported="+spacing.Count(value=>value>0f));
                validate(mesh,covered,polygons,spacing);
            }
            finally {UnityEngine.Object.DestroyImmediate(mesh);}
        }
        private static SurfaceLodBuilder.Sample[] Grid(int width,int height,
            Func<int,int,Vector3> position,Vector3 front)
        {
            var result=new SurfaceLodBuilder.Sample[width*height];
            for (int y=0;y<height;y++) for (int x=0;x<width;x++) result[y*width+x]=Sample(position(x,y),front);
            return result;
        }
        private static SurfaceLodBuilder.Sample[] Wall(int width,int height,Func<int,int,bool> include=null,float spacing=.025f)
        {
            var result=new List<SurfaceLodBuilder.Sample>();
            for (int y=0;y<height;y++) for (int x=0;x<width;x++)
                if (include==null || include(x,y)) result.Add(Sample(new Vector3(.01f+x*spacing,.01f+y*spacing,2f),Vector3.back));
            return result.ToArray();
        }
        private static SurfaceLodBuilder.Sample[] TraceWall(int width,int height,Func<int,int,bool> include=null)
        {
            var result=new List<SurfaceLodBuilder.Sample>();
            for (int y=0;y<height;y++) for (int x=0;x<width;x++)
                if (include==null || include(x,y)) result.Add(Sample(new Vector3(.01f+x*.08f,.01f+y*.035f,2f),Vector3.back));
            return result.ToArray();
        }
        private static SurfaceLodBuilder.Sample Sample(Vector3 position,Vector3 front) => new SurfaceLodBuilder.Sample {
            position=position,viewDirection=front,color=new Color32(235,235,235,255),hasThermal=false};
        private static bool Covers(Mesh mesh,Vector3 point)
        {
            var vertices=mesh.vertices; var triangles=mesh.triangles;
            for (int i=0;i<triangles.Length;i+=3)
            {
                Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                if (Mathf.Abs(Vector3.Dot(point-a,normal))>.001f) continue;
                Vector3 ab=b-a,ac=c-a,ap=point-a;
                float d00=Vector3.Dot(ab,ab),d01=Vector3.Dot(ab,ac),d11=Vector3.Dot(ac,ac);
                float denominator=d00*d11-d01*d01;
                if (denominator<1e-12f) continue;
                float v=(d11*Vector3.Dot(ap,ab)-d01*Vector3.Dot(ap,ac))/denominator;
                float w=(d00*Vector3.Dot(ap,ac)-d01*Vector3.Dot(ap,ab))/denominator;
                if (v>=-.0001f && w>=-.0001f && v+w<=1.0001f) return true;
            }
            return false;
        }
        private static void Require(bool condition,string message)
        {
            if (!condition) throw new Exception("[Surface LOD validation] "+message);
        }
    }
}
#endif
