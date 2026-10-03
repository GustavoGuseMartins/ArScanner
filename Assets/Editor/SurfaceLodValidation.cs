#if UNITY_EDITOR
using System;
using System.Linq;
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
            var dense = new SurfaceLodBuilder.Sample[16];
            for (int i = 0; i < dense.Length; i++)
                dense[i] = Sample(new Vector3((i%4)*.035f+.005f,0f,(i/4)*.035f+.005f));

            Mesh mesh = SurfaceLodBuilder.Build(dense,new Vector3(0f,0f,-2f),.8f,2.5f,
                .16f,.018f,2.5f,out bool[] covered,out int polygons);
            try
            {
                Require(polygons == 1 && covered.All(value => value),
                    "A dense planar tile should merge into a surface.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            var farDense = new SurfaceLodBuilder.Sample[36];
            for (int i = 0; i < farDense.Length; i++)
                farDense[i] = Sample(new Vector3((i%6)*.035f+.02f,0f,(i/6)*.035f+.02f));
            mesh = SurfaceLodBuilder.Build(farDense,new Vector3(0f,0f,-3f),.8f,2.5f,
                .16f,.018f,2.5f,out covered,out polygons);
            try
            {
                Require(polygons == 1 && covered.All(value => value),
                    "A dense distant tile should keep the far LOD active.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            // A convex outline can have a real unmeasured hole at its center.
            // All outline edges are short enough to pass the edge-length check.
            var ring = new SurfaceLodBuilder.Sample[12];
            for (int i = 0; i < ring.Length; i++)
            {
                float angle = 2f*Mathf.PI*i/ring.Length;
                ring[i] = Sample(new Vector3(.08f+.075f*Mathf.Cos(angle),0f,
                    .08f+.075f*Mathf.Sin(angle)));
            }
            mesh = SurfaceLodBuilder.Build(ring,new Vector3(0f,0f,-2f),.8f,2.5f,
                .16f,.018f,2.5f,out covered,out polygons);
            try
            {
                Require(polygons == 0 && covered.All(value => !value),
                    "A tile with an unmeasured center must remain individual points.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            // A smaller ring used to pass the old 4.8 cm support radius,
            // closing a 9 cm hole despite having no measurement in its center.
            var smallRing = new SurfaceLodBuilder.Sample[12];
            for (int i = 0; i < smallRing.Length; i++)
            {
                float angle = 2f*Mathf.PI*i/smallRing.Length;
                smallRing[i] = Sample(new Vector3(.08f+.045f*Mathf.Cos(angle),0f,
                    .08f+.045f*Mathf.Sin(angle)));
            }
            mesh = SurfaceLodBuilder.Build(smallRing,new Vector3(0f,0f,-2f),.8f,2.5f,
                .16f,.018f,2.5f,out covered,out polygons);
            try
            {
                Require(polygons == 0 && covered.All(value => !value),
                    "A 9 cm unmeasured hole must remain open.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            // A thin surface scanned from both sides must not collapse into
            // one face that hides all samples on its back side.
            var opposingFaces = new SurfaceLodBuilder.Sample[32];
            for (int i = 0; i < 16; i++)
            {
                float x = (i%4)*.035f+.005f;
                float z = (i/4)*.035f+.005f;
                opposingFaces[2*i] = Sample(new Vector3(x,.005f,z));
                opposingFaces[2*i].viewDirection = Vector3.up;
                opposingFaces[2*i+1] = Sample(new Vector3(x,.015f,z));
                opposingFaces[2*i+1].viewDirection = Vector3.down;
            }
            mesh = SurfaceLodBuilder.Build(opposingFaces,new Vector3(0f,0f,-2f),.8f,2.5f,
                .16f,.018f,2.5f,out covered,out polygons);
            try
            {
                Require(polygons == 0 && covered.All(value => !value),
                    "Opposite-facing samples must remain independent points.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            Debug.Log("[Surface LOD validation] PASSED: near/far dense regions merge; interior gaps and opposite faces stay open.");
        }

        private static SurfaceLodBuilder.Sample Sample(Vector3 position) =>
            new SurfaceLodBuilder.Sample {
                position=position,viewDirection=Vector3.up,
                color=new Color32(235,235,235,255),hasThermal=false};

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("[Surface LOD validation] " + message);
        }
    }
}
#endif
