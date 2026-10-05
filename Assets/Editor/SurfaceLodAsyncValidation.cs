#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArScanner.Network;
using ArScanner.Rendering;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class SurfaceLodAsyncValidation
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public;

        public static void Run()
        {
            var go=new GameObject("AsyncSurfaceLodValidation");
            go.SetActive(false);
            ThermalPointCloudRenderer renderer=null;
            ParticleSystem fixtureParticles=null;
            try
            {
                var receiver=go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect=false;
                renderer=go.AddComponent<ThermalPointCloudRenderer>();
                renderer.maxPoints=1000;
                renderer.pointYawOffset=0f;
                renderer.invertVerticalLidar=false;
                Invoke(renderer,"Awake");
                // The renderer/receptor stay inactive to prevent automatic Awake,
                // Update and networking. Native particle readback needs its own
                // initialized simulation, also in Edit Mode with -nographics.
                fixtureParticles=renderer.targetParticleSystem;
                fixtureParticles.transform.SetParent(null,false);
                fixtureParticles.gameObject.SetActive(true);
                fixtureParticles.Simulate(0f,false,true,false);
                fixtureParticles.Pause(false);
                ValidatePointsOnlyModeAndSizeControls(renderer);
                renderer.SetSurfaceLodEnabled(true);
                ValidateGradualObservationUpdates(renderer);

                AddGrid(renderer,16);
                var delayed=Delay(renderer,out var data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null,"An unfinished worker must not publish a surface.");
                var originalJob=Get(renderer,"lodJob");
                Invoke(renderer,"RebuildSurfaceLod",Vector3.zero);
                Require(ReferenceEquals(originalJob,Get(renderer,"lodJob")),
                    "Starting another rebuild must retain the one already running.");
                AddPoint(renderer,new Vector3(.22f,.12f,2f));
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(renderer.activePointsCount==17 && renderer.surfaceLodMergedPoints==16 &&
                    Particles(renderer)[16].startSize>0f && Mesh(renderer)!=null,
                    "A smaller snapshot must preserve new points as visible quads and merge only its own observations.");
                AddPoint(renderer,new Vector3(.0575f,.0575f,2.001f),true,60f);
                Require(Mesh(renderer)==null && renderer.surfaceLodMergedPoints==0,
                    "A new hotspot inside the displayed surface must invalidate it before depth can occlude the quad.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data);
                AddPoint(renderer,new Vector3(.0575f,.0575f,2.001f),true,60f);
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && renderer.activePointsCount==17,
                    "A hotspot appended inside an unfinished snapshot must prevent that old surface from being published.");

                renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                delayed=Delay(renderer,out data);
                Require(data.polygonCount==2,"The separated-wall fixture must produce two independent rectangles.");
                AddPoint(renderer,new Vector3(.0575f,.0575f,3f));
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)!=null && renderer.surfaceLodMergedPoints==32 && renderer.activePointsCount==33 &&
                    Particles(renderer)[32].startSize>0f,
                    "A point between walls must not reject the pending result merely because it lies inside their combined bounds.");
                AddPoint(renderer,new Vector3(.09f,.0575f,3.05f));
                Require(Mesh(renderer)!=null && renderer.surfaceLodMergedPoints==32 && Particles(renderer)[33].startSize>0f,
                    "A point between displayed walls must keep their LOD and its own visible quad.");

                renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                delayed=Delay(renderer,out data); delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                var stableMesh=Mesh(renderer);
                AddPoint(renderer,new Vector3(.0575f,.0575f,2.001f));
                Require(ReferenceEquals(stableMesh,Mesh(renderer)) && renderer.surfaceLodPolygons==2 &&
                    renderer.surfaceLodMergedPoints==33 && Particles(renderer)[32].startSize==0f,
                    "A newly measured coplanar return must retain the same mesh and join only its supported rectangle.");
                AddPoint(renderer,new Vector3(.014f,.014f,2.01f));
                Require(ReferenceEquals(stableMesh,Mesh(renderer)) && renderer.surfaceLodPolygons==2,
                    "Refining an existing voxel within the measured plane must not toggle all surfaces back to quads.");
                AddPoint(renderer,new Vector3(.09f,.0575f,2.001f),true,60f);
                Require(ReferenceEquals(stableMesh,Mesh(renderer)) && renderer.surfaceLodPolygons==1 &&
                    renderer.surfaceLodMergedPoints==16 && Mesh(renderer).triangles.Length==6 &&
                    Particles(renderer)[0].startSize>0f && Particles(renderer)[33].startSize>0f &&
                    Particles(renderer).Skip(16).Take(16).All(p=>p.startSize==0f),
                    "A hotspot must restore its own wall's quads while preserving the independent wall's mesh and coverage.");

                renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                delayed=Delay(renderer,out data);
                AddPoint(renderer,new Vector3(.0575f,.0575f,4.001f));
                AddPoint(renderer,new Vector3(.0575f,.0575f,2.001f),true,60f);
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)!=null && renderer.surfaceLodPolygons==1 && renderer.surfaceLodMergedPoints==17 &&
                    Particles(renderer)[32].startSize==0f && Particles(renderer)[33].startSize>0f,
                    "Changes during a delayed build must reject only the hotspot rectangle and keep a compatible appended return covered.");

                renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                delayed=Delay(renderer,out data); delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                AddPoint(renderer,new Vector3(.005f,.005f,2f),true,20f);
                Require(Mesh(renderer)!=null && renderer.surfaceLodPolygons==1 && renderer.surfaceLodMergedPoints==16 &&
                    Particles(renderer)[0].startSize>0f,
                    "A newly available temperature must remain visible without retiring an unrelated gray wall.");

                renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                delayed=Delay(renderer,out data); delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Invoke(renderer,"RefreshPointVisibility",new Vector3(0f,0f,1.57f));
                Require(Mesh(renderer)!=null && renderer.surfaceLodPolygons==1 && renderer.surfaceLodMergedPoints==16 &&
                    Particles(renderer)[0].startSize>0f,
                    "Approaching one wall must restore its near quads while a distant independent wall retains its LOD.");

                foreach (bool publishBeforeReplacement in new[] {false,true})
                {
                    renderer.ClearPointCloud(); AddGrid(renderer,16); AddGrid(renderer,16,new Vector3(0f,0f,2f));
                    for (int i=0;i<968;i++) AddPoint(renderer,new Vector3(20f+i*.035f,.005f,2f));
                    delayed=Delay(renderer,out data);
                    Require(data.polygonCount==2,"Circular replacement fixture must retain two walls beside a scan line.");
                    if (publishBeforeReplacement)
                    {delayed.SetResult(data); Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);}
                    AddPoint(renderer,new Vector3(50f,5f,2f));
                    if (!publishBeforeReplacement)
                    {delayed.SetResult(data); Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);}
                    Require(Mesh(renderer)!=null && renderer.activePointsCount==1000 && renderer.surfaceLodPolygons==1 &&
                        renderer.surfaceLodMergedPoints==16 && Particles(renderer)[0].position.y>4f &&
                        Particles(renderer)[0].startSize>0f,
                        "Evicting support must retire only its former wall, before or after the worker publishes, and keep the replacement quad visible.");
                }

                renderer.ClearPointCloud();
                AddPoint(renderer,new Vector3(0f,0f,25f)); AddPoint(renderer,new Vector3(0f,0f,.1f));
                AddPoint(renderer,Vector3.zero); AddPoint(renderer,new Vector3(float.NaN,0f,1f));
                Require(renderer.activePointsCount==2 && Particles(renderer)[0].position.z==25f &&
                    Mathf.Abs(Particles(renderer)[1].position.z-.1f)<.00001f,
                    "Valid sensor returns must have no viewer distance cap; zero and nonfinite coordinates remain invalid.");
                renderer.invertVerticalLidar=true;
                AddPoint(renderer,Vector3.zero);
                Require(renderer.activePointsCount==2,"Vertical display inversion must not turn an invalid zero packet into a real return.");
                renderer.invertVerticalLidar=false;

                renderer.ClearPointCloud(); AddPoint(renderer,new Vector3(2f,0f,5f));
                Invoke(renderer,"RefreshPointVisibility",new Vector3(1f,0f,7f));
                Require(Particles(renderer)[0].startSize>0f,
                    "Walking parallel to a wall must not hide a quad by treating its acquisition ray as a surface normal.");
                Invoke(renderer,"RefreshPointVisibility",new Vector3(3f,0f,7f));
                Require(Particles(renderer)[0].startSize>0f,
                    "An unfitted quad must remain visible from both sides; only measured planes may apply backface culling.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data);
                renderer.ClearPointCloud(); AddGrid(renderer,16,new Vector3(1f,0f,0f));
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && renderer.activePointsCount==16 &&
                    renderer.surfaceLodMergedPoints==0 && Particles(renderer)[0].position.x>1f,
                    "A completed pre-clear worker must not restore the old cloud or hide newly collected points.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data);
                Vector3 before=Particles(renderer)[0].position;
                Require(renderer.RebaseWorldPoints(new Pose(Vector3.zero,Quaternion.identity),
                    new Pose(Vector3.right,Quaternion.identity)),"The rebase fixture must move the cloud.");
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && renderer.surfaceLodMergedPoints==0 &&
                    Vector3.Distance(Particles(renderer)[0].position,before+Vector3.right)<.00001f,
                    "A pre-rebase surface cannot be applied in the former coordinate frame.");

                renderer.ClearPointCloud(); AddGrid(renderer,1000);
                delayed=Delay(renderer,out data);
                AddPoint(renderer,new Vector3(10f,.1f,2f));
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(renderer.activePointsCount==1000 && Particles(renderer)[0].startSize>0f &&
                    Mathf.Abs(Particles(renderer)[0].position.x-10f)<.00001f,
                    "A circular-buffer replacement must invalidate coverage for its former slot identity and preserve the new quad.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data);
                renderer.pointOpacity=.25f;
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null,"A delayed surface cannot restore colors from a former opacity setting.");
                renderer.pointOpacity=1f;

                renderer.ClearPointCloud(); AddGrid(renderer,16,Vector3.zero,true);
                delayed=Delay(renderer,out data);
                AddPoint(renderer,new Vector3(.005f,.005f,2f),true,60f);
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && renderer.surfaceLodMergedPoints==0,
                    "A newly measured hotspot must discard the former wall mesh instead of hiding beneath it.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data,new Vector3(0f,0f,1.96f));
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",new Vector3(0f,0f,2.04f));
                Require(renderer.surfaceLodPolygons==0 && Particles(renderer).Take(16).All(p=>p.startSize>0f),
                    "The applied result must restore two-sided quads after the camera crosses the surface's near region.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                delayed=Delay(renderer,out data,new Vector3(0f,0f,1.51f));
                Require(data.polygonCount>0,"The near-boundary fixture must initially produce a surface.");
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",new Vector3(0f,0f,1.57f));
                Require(Mesh(renderer)==null,"A camera crossing the near threshold must reject the formerly distant surface.");

                renderer.ClearPointCloud(); AddGrid(renderer,16);
                Invoke(renderer,"RebuildSurfaceLod",Vector3.zero);
                var job=Get(renderer,"lodJob");
                var worker=(Task<SurfaceLodBuilder.GeometryData>)job.GetType().GetField("task",Fields).GetValue(job);
                Require(worker.Wait(TimeSpan.FromSeconds(20)),"The actual managed geometry worker did not finish.");
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)!=null && renderer.surfaceLodPolygons==1 && renderer.surfaceLodMergedPoints==16,
                    "The real background worker must publish its managed geometry as a main-thread mesh.");
                Invoke(renderer,"RefreshPointVisibility",new Vector3(0f,0f,1.57f));
                Require(Mesh(renderer)==null && renderer.surfaceLodMergedPoints==0,
                    "Entering the near region must restore quads for an already displayed surface.");
            }
            finally
            {
                // The inactive fixture initialized its runtime resources explicitly.
                if (renderer!=null)
                    foreach (string name in new[] {"axesRoot","lodMesh","lodObject","pointMaterial","surfaceMaterial"})
                    {
                        var field=typeof(ThermalPointCloudRenderer).GetField(name,Private);
                        var resource=field.GetValue(renderer) as UnityEngine.Object;
                        if (resource is Transform transform) UnityEngine.Object.DestroyImmediate(transform.gameObject);
                        else if (resource!=null) UnityEngine.Object.DestroyImmediate(resource);
                        field.SetValue(renderer,null);
                    }
                if (fixtureParticles!=null) UnityEngine.Object.DestroyImmediate(fixtureParticles.gameObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log("[Async Surface LOD validation] PASSED: points-only mode without geometry jobs, exact sizes and optional acquisition-side hiding while paused, gradual thermal/position publication and noise stability, stable compatible returns, regional hotspot/availability/near/eviction handling, delayed identity protection, two-sided quads, uncapped distance, clear/rebase/settings rejection and main-thread publication.");
        }

        private static void ValidatePointsOnlyModeAndSizeControls(ThermalPointCloudRenderer renderer)
        {
            bool originalAdaptive=renderer.adaptivePointSizing;
            bool originalPause=renderer.isPaused;
            try
            {
                Require(!renderer.enableSurfaceLod && !renderer.hideBackFacingPoints &&
                    Mathf.Approximately(renderer.PointSizeMultiplier,1f),
                    "The default must show two-sided points at the original size, with LOD disabled.");
                AddGrid(renderer,16);
                var observations=(Array)((Array)Get(renderer,"pointsBuffer")).Clone();
                renderer.adaptivePointSizing=true;
                for (int i=0;i<3;i++)
                {
                    Invoke(renderer,"RebuildSurfaceLod",Vector3.zero);
                    Invoke(renderer,"Update");
                    Require(Get(renderer,"lodJob")==null && Mesh(renderer)==null &&
                        renderer.surfaceLodPolygons==0 && renderer.surfaceLodMergedPoints==0 &&
                        Particles(renderer).Take(16).All(point=>point.startSize>0f),
                        "Points-only mode must never run geometry analysis, even when adaptive sizing is enabled.");
                }
                Require(!((GameObject)Get(renderer,"lodObject")).activeSelf,
                    "Points-only mode must keep the surface object inactive.");

                renderer.SetSurfaceLodEnabled(true);
                var delayed=Delay(renderer,out var data);
                object abandonedJob=Get(renderer,"lodJob");
                int formerGeneration=(int)Get(renderer,"lodGeneration");
                renderer.SetSurfaceLodEnabled(false);
                Require(Get(renderer,"lodJob")==null && (int)Get(renderer,"lodGeneration")>formerGeneration,
                    "Disabling LOD must detach its pending snapshot and invalidate its generation immediately.");
                delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && renderer.surfaceLodMergedPoints==0,
                    "A detached result must not publish geometry while LOD is disabled.");
                renderer.SetSurfaceLodEnabled(true);
                typeof(ThermalPointCloudRenderer).GetField("lodJob",Private).SetValue(renderer,abandonedJob);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Require(Mesh(renderer)==null && Get(renderer,"lodJob")==null,
                    "Even after re-enabling LOD, a result from its former generation must remain rejected.");

                delayed=Delay(renderer,out data); delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Mesh stableMesh=Mesh(renderer);
                Require(stableMesh!=null && renderer.surfaceLodMergedPoints==16,
                    "Re-enabling LOD must permit a fresh surface for the same observations.");
                int stableGeneration=(int)Get(renderer,"lodGeneration");
                renderer.isPaused=true;
                renderer.SetPointSizeMultiplier(3f);
                renderer.SetHideBackFacingPoints(true);
                renderer.SetHideBackFacingPoints(false);
                Require(!renderer.adaptivePointSizing && ReferenceEquals(stableMesh,Mesh(renderer)) &&
                    (int)Get(renderer,"lodGeneration")==stableGeneration && Get(renderer,"lodJob")==null &&
                    Mathf.Approximately((float)Invoke(renderer,"DisplayPointSize",0,Vector3.zero),
                        ThermalPointCloudRenderer.DefaultPointSize*3f),
                    "Explicit size and point-side visibility must leave fitted geometry unchanged and bypass adaptive caps.");
                RequirePublishedParticles(renderer,16,0f,
                    "The displayed LOD must initially suppress all individual quads.");
                renderer.SetSurfaceLodEnabled(false);
                Require(Mesh(renderer)==null && renderer.surfaceLodPolygons==0 && renderer.surfaceLodMergedPoints==0,
                    "Turning off LOD must immediately clear its mesh and coverage counters while paused.");
                RequirePublishedParticles(renderer,16,ThermalPointCloudRenderer.DefaultPointSize*3f,
                    "Turning off a displayed LOD must restore all quads immediately, including while capture is paused.");

                foreach (float multiplier in new[] {.25f,.5f,1f,2f,3f,4f})
                {
                    renderer.SetPointSizeMultiplier(multiplier);
                    Require(Mathf.Approximately(renderer.PointSizeMultiplier,multiplier),
                        "Every explicit size multiplier must update the existing particles exactly while paused.");
                    RequirePublishedParticles(renderer,16,ThermalPointCloudRenderer.DefaultPointSize*multiplier,
                        "Every explicit size multiplier must update the existing particles exactly while paused.");
                }
                for (int i=0;i<16;i++)
                    Require(((Array)Get(renderer,"pointsBuffer")).GetValue(i).Equals(observations.GetValue(i)),
                        "LOD and size controls must preserve stored positions, temperatures, identities and export observations.");

                renderer.ClearPointCloud();
                Vector3 camera=(Vector3)Invoke(renderer,"CurrentCameraPosition");
                Vector3 position=camera+Vector3.forward*2f;
                if (position.sqrMagnitude<.0001f) position+=Vector3.right;
                AddPoint(renderer,position);
                Vector3 world=StoredField<Vector3>(renderer,0,"worldPosition");
                SetStoredField(renderer,0,"viewDirection",(world-camera).normalized);
                object beforeHide=((Array)Get(renderer,"pointsBuffer")).GetValue(0);
                renderer.SetHideBackFacingPoints(true);
                RequirePublishedParticles(renderer,1,0f,
                    "The opt-in acquisition-side control must immediately hide a clearly opposite-side point while paused.");
                renderer.SetHideBackFacingPoints(false);
                RequirePublishedParticles(renderer,1,renderer.pointSize,
                    "Disabling the experimental control must immediately restore the displayed quad.");
                Require(renderer.activePointsCount==1 && beforeHide.Equals(((Array)Get(renderer,"pointsBuffer")).GetValue(0)),
                    "Disabling the experimental control must restore two-sided points without changing stored observations or counters.");
                SetStoredField(renderer,0,"viewDirection",Vector3.back);
                renderer.SetHideBackFacingPoints(true);
                Invoke(renderer,"RefreshPointVisibility",world+Vector3.back*2f);
                Require(Particles(renderer)[0].startSize>0f,"The observed acquisition side must remain visible.");
                Invoke(renderer,"RefreshPointVisibility",world+Vector3.forward*2f);
                Require(Particles(renderer)[0].startSize==0f,"Walking to the opposite side must apply the opt-in visibility estimate.");
                Invoke(renderer,"RefreshPointVisibility",world+Vector3.right);
                Require(Particles(renderer)[0].startSize>0f,"A point viewed parallel to its acquisition side must not flicker off.");
                Invoke(renderer,"RefreshPointVisibility",world);
                Require(Particles(renderer)[0].startSize>0f,"A camera coincident with a point has no valid side and must keep it visible.");
                SetStoredField(renderer,0,"viewDirection",Vector3.zero);
                Invoke(renderer,"RefreshPointVisibility",world+Vector3.forward*2f);
                Require(Particles(renderer)[0].startSize>0f,"A point with unknown acquisition direction must remain visible.");
            }
            finally
            {
                renderer.SetHideBackFacingPoints(false);
                renderer.SetSurfaceLodEnabled(false);
                renderer.SetPointSizeMultiplier(1f);
                renderer.adaptivePointSizing=originalAdaptive;
                renderer.isPaused=originalPause;
                renderer.ClearPointCloud();
            }
        }

        private static void RequirePublishedParticles(ThermalPointCloudRenderer renderer,int count,float size,string message)
        {
            Require(renderer.activePointsCount==count && Particles(renderer).Take(count).All(
                point=>Mathf.Approximately(point.startSize,size)),message+" Managed particle state differs.");
            var displayed=new ParticleSystem.Particle[renderer.maxPoints];
            int published=renderer.targetParticleSystem.GetParticles(displayed);
            Require(published==count,message+$" Native particle count: expected {count}, got {published}; "+
                $"active={renderer.targetParticleSystem.gameObject.activeInHierarchy}, paused={renderer.targetParticleSystem.isPaused}.");
            Require(displayed.Take(count).All(point=>Mathf.Approximately(point.startSize,size)),
                message+$" Native size: expected {size}, first={displayed[0].startSize}.");
        }


        private static void ValidateGradualObservationUpdates(ThermalPointCloudRenderer renderer)
        {
            bool originalInversion=renderer.invertVerticalLidar;
            bool originalAbsolute=renderer.useAbsoluteThermalScale;
            float originalOpacity=renderer.pointOpacity;
            try
            {
                renderer.invertVerticalLidar=true;
                renderer.useAbsoluteThermalScale=false;
                Vector3 position=new Vector3(.005f,.05f,2f);
                renderer.ClearPointCloud(); AddPoint(renderer,position,true,20f);
                Color32 initialColor=Particles(renderer)[0].startColor;
                for (int i=1;i<=80;i++) AddPoint(renderer,position,true,20f+i*.1f);
                float temperature=StoredField<float>(renderer,0,"temperature");
                float published=StoredField<float>(renderer,0,"publishedTemperature");
                Require(Mathf.Abs(temperature-27.7f)<.001f && Mathf.Abs(temperature-published)<=.30001f &&
                    !initialColor.Equals(Particles(renderer)[0].startColor) &&
                    Particles(renderer)[0].startColor.Equals(ThermalPointCloudRenderer.ThermalPalette(
                        published,renderer.thermalDisplayMinC,renderer.thermalDisplayMaxC)),
                    "Gradual heating must publish accumulated EMA changes and keep the isolated quad's color current.");

                renderer.ClearPointCloud(); AddPoint(renderer,position,true,20f);
                initialColor=Particles(renderer)[0].startColor;
                typeof(ThermalPointCloudRenderer).GetField("lodDirty",Private).SetValue(renderer,false);
                typeof(ThermalPointCloudRenderer).GetField("bufferDirty",Private).SetValue(renderer,false);
                for (int i=0;i<20;i++) AddPoint(renderer,position,true,20.2f);
                Require(initialColor.Equals(Particles(renderer)[0].startColor) &&
                    !(bool)Get(renderer,"lodDirty") && !(bool)Get(renderer,"bufferDirty"),
                    "Thermal noise below the accumulated publication threshold must not dirty quads or rebuild LOD.");

                renderer.ClearPointCloud(); AddPoint(renderer,position,true,20f);
                AddPoint(renderer,position,true,20.8f);
                renderer.pointOpacity=.5f;
                Invoke(renderer,"Update");
                Require(Mathf.Abs(StoredField<float>(renderer,0,"publishedTemperature")-
                    StoredField<float>(renderer,0,"temperature"))<.00001f,
                    "Refreshing colors for display settings must synchronize the published temperature.");
                initialColor=Particles(renderer)[0].startColor;
                AddPoint(renderer,position,true,20.8f);
                Require(initialColor.Equals(Particles(renderer)[0].startColor),
                    "A settings refresh must reset the accumulated temperature threshold to its newly displayed value.");
                renderer.pointOpacity=originalOpacity;
                renderer.invertVerticalLidar=false;

                renderer.ClearPointCloud(); AddGrid(renderer,16,Vector3.zero,true);
                var delayed=Delay(renderer,out var data); delayed.SetResult(data);
                Invoke(renderer,"CompleteSurfaceLodBuild",Vector3.zero);
                Mesh stableMesh=Mesh(renderer);
                Require(stableMesh!=null && renderer.surfaceLodMergedPoints==16,
                    "The gradual-hotspot fixture must begin with a displayed thermal wall.");
                Vector3 wallPoint=new Vector3(.005f,.005f,2f);
                for (int i=1;i<=8;i++) AddPoint(renderer,wallPoint,true,20f+i*.1f);
                Require(ReferenceEquals(stableMesh,Mesh(renderer)) && renderer.surfaceLodMergedPoints==16,
                    "Gradual changes within the wall's thermal spread must retain the existing rectangle.");
                for (int i=9;i<=80;i++) AddPoint(renderer,wallPoint,true,20f+i*.1f);
                Require(Mesh(renderer)==null && renderer.surfaceLodPolygons==0 && renderer.surfaceLodMergedPoints==0 &&
                    Particles(renderer)[0].startSize>0f,
                    "A hotspot that warms gradually must retire its incompatible wall and restore visible quads.");

                renderer.ClearPointCloud(); AddPoint(renderer,position);
                for (int i=0;i<31;i++) AddPoint(renderer,position);
                Vector3 before=Particles(renderer)[0].position;
                for (int i=0;i<8;i++) AddPoint(renderer,new Vector3(.024f,.05f,2f));
                Vector3 stored=StoredField<Vector3>(renderer,0,"worldPosition");
                Require(renderer.activePointsCount==1 && Mathf.Abs(stored.x-.009261702f)<.00001f &&
                    Vector3.Distance(Particles(renderer)[0].position,before)>.002f &&
                    Vector3.Distance(Particles(renderer)[0].position,stored)<=.00201f,
                    "Submillimeter position refinements must accumulate against the published quad without bypassing the merge deadband.");
            }
            finally
            {
                renderer.ClearPointCloud();
                renderer.invertVerticalLidar=originalInversion;
                renderer.useAbsoluteThermalScale=originalAbsolute;
                renderer.pointOpacity=originalOpacity;
            }
        }

        private static T StoredField<T>(ThermalPointCloudRenderer renderer,int index,string field)
        {
            object point=((Array)Get(renderer,"pointsBuffer")).GetValue(index);
            return (T)point.GetType().GetField(field,Fields).GetValue(point);
        }

        private static void SetStoredField<T>(ThermalPointCloudRenderer renderer,int index,string field,T value)
        {
            var points=(Array)Get(renderer,"pointsBuffer");
            object point=points.GetValue(index);
            point.GetType().GetField(field,Fields).SetValue(point,value);
            points.SetValue(point,index);
        }

        private static TaskCompletionSource<SurfaceLodBuilder.GeometryData> Delay(ThermalPointCloudRenderer renderer,
            out SurfaceLodBuilder.GeometryData data,Vector3 camera=default)
        {
            var stored=(Array)Get(renderer,"pointsBuffer");
            var samples=new SurfaceLodBuilder.Sample[renderer.activePointsCount];
            var identities=new uint[renderer.activePointsCount];
            for (int i=0;i<samples.Length;i++)
            {
                object point=stored.GetValue(i);
                var type=point.GetType();
                float temperature=(float)type.GetField("temperature",Fields).GetValue(point);
                byte flags=(byte)type.GetField("surfaceFlags",Fields).GetValue(point);
                identities[i]=(uint)type.GetField("identity",Fields).GetValue(point);
                samples[i]=new SurfaceLodBuilder.Sample {
                    position=(Vector3)type.GetField("worldPosition",Fields).GetValue(point),
                    viewDirection=(Vector3)type.GetField("viewDirection",Fields).GetValue(point),
                    temperature=temperature,hasThermal=(flags&ScanPointData.ThermalUnavailableFlag)==0,
                    color=new Color32(235,235,235,255)
                };
            }
            data=SurfaceLodBuilder.BuildGeometryData(samples,camera,renderer.lodNearMeters,renderer.lodFarMeters,
                renderer.lodTileSize,renderer.lodPlaneTolerance,renderer.lodMaxThermalSpreadC);
            var completion=new TaskCompletionSource<SurfaceLodBuilder.GeometryData>();
            Type jobType=typeof(ThermalPointCloudRenderer).GetNestedType("LodJob",BindingFlags.NonPublic);
            object job=Activator.CreateInstance(jobType,true);
            jobType.GetField("samples",Fields).SetValue(job,samples);
            jobType.GetField("identities",Fields).SetValue(job,identities);
            jobType.GetField("settings",Fields).SetValue(job,Invoke(renderer,"CaptureLodSettings"));
            jobType.GetField("camera",Fields).SetValue(job,camera);
            jobType.GetField("generation",Fields).SetValue(job,Get(renderer,"lodGeneration"));
            jobType.GetField("task",Fields).SetValue(job,completion.Task);
            typeof(ThermalPointCloudRenderer).GetField("lodJob",Private).SetValue(renderer,job);
            return completion;
        }
        private static void AddGrid(ThermalPointCloudRenderer renderer,int count,Vector3 offset=default,bool thermal=false)
        {
            int width=count==16 ? 4 : 32;
            for (int i=0;i<count;i++) AddPoint(renderer,new Vector3(.005f+(i%width)*.035f,.005f+(i/width)*.035f,2f)+offset,thermal);
        }
        private static void AddPoint(ThermalPointCloudRenderer renderer,Vector3 position,bool thermal=false,float temperature=20f)
        {
            Invoke(renderer,"ProcessScanPoint",new ScanPointData {posX_mm=position.x*1000f,posY_mm=position.y*1000f,
                posZ_mm=position.z*1000f,temperatureC=thermal ? temperature : float.NaN,
                surfaceFlags=thermal ? (byte)0 : ScanPointData.ThermalUnavailableFlag});
            Invoke(renderer,"ApplySurfaceLodChanges",Vector3.zero);
        }
        private static ParticleSystem.Particle[] Particles(ThermalPointCloudRenderer renderer) =>
            (ParticleSystem.Particle[])Get(renderer,"particlesBuffer");
        private static Mesh Mesh(ThermalPointCloudRenderer renderer) => (Mesh)Get(renderer,"lodMesh");
        private static object Get(object instance,string field) => instance.GetType().GetField(field,Private).GetValue(instance);
        private static object Invoke(object instance,string method,params object[] args) =>
            instance.GetType().GetMethod(method,Private).Invoke(instance,args);
        private static void Require(bool condition,string message)
        {if (!condition) throw new Exception("[Async Surface LOD validation] "+message);}
    }
}
#endif
