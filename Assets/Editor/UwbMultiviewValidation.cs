#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ArScanner.Spatial;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class UwbMultiviewValidation
    {
        public static void BuildValidated()
        {
            Run();
            AndroidBuildScript.BuildAndroidApk();
        }

        public static void Run()
        {
            var pause = new UwbPhonePauseGate();
            Require(!pause.Update(Vector3.zero, Quaternion.identity, 0f), "Must wait for a pause.");
            Require(pause.Update(Vector3.zero, Quaternion.identity, .7f), "Stationary phone should become ready.");
            Require(!pause.Update(new Vector3(.1f,0f,0f), Quaternion.identity, .8f), "Translation must restart the pause.");
            Require(!pause.Update(new Vector3(.1f,0f,0f), Quaternion.Euler(0f,10f,0f), 1.5f), "Rotation must restart the pause.");
            Require(pause.Update(new Vector3(.1f,0f,0f), Quaternion.Euler(0f,10f,0f), 2.2f), "A new pause should be accepted.");
            var estimator = new UwbArMultiviewEstimator();
            Vector3 target = new Vector3(.15f, 1.1f, 1.5f);
            Vector3[] views = {
                new Vector3(-1f,.4f,0f), new Vector3(1f,.4f,0f),
                new Vector3(-.8f,1.8f,.4f), new Vector3(.8f,1.8f,.2f),
                new Vector3(0f,.5f,2.5f), new Vector3(.5f,1.6f,2.7f)
            };
            foreach (Vector3 view in views)
            {
                Vector3[] anchors = Anchors(view, Quaternion.identity);
                Vector3 ranges = Ranges(anchors, target);
                for (int i = 0; i < 4; i++) Require(estimator.AddSample(anchors, ranges), "Repeated pause sample rejected.");
            }
            Require(estimator.TryEstimate(out Vector3 found, out _, out _, out _, out _), "Diverse known views rejected: " + estimator.RejectionReason);
            Require(Vector3.Distance(found, target) < .01f, "Known target was misplaced.");
            var heightEstimator = new UwbArMultiviewEstimator();
            Vector3[] levelViews = {
                new Vector3(-.8f, 1.4f, 0f), new Vector3(.8f, 1.4f, 0f),
                new Vector3(-.6f, 1.4f, .65f), new Vector3(.7f, 1.4f, .75f)
            };
            foreach (Vector3 view in levelViews)
            {
                Vector3[] anchors = Anchors(view, Quaternion.identity);
                for (int i = 0; i < 4; i++)
                    Require(heightEstimator.AddSample(anchors, Ranges(anchors, target)),
                        "Level-view range sample rejected.");
            }
            Require(heightEstimator.TryEstimateAtHeight(target.y, out Vector3 heightFound,
                    out _, out _, out _, out _) &&
                Vector3.Distance(heightFound, target) < .02f,
                "An observed support height must resolve a level phone trajectory.");
            Require(!heightEstimator.TryEstimateAtHeight(target.y + 3f, out _,
                    out _, out _, out _, out _),
                "A physically impossible support height must not authorize a position.");
            int count = estimator.SampleCount;
            Require(!estimator.AddSample(Anchors(Vector3.zero, Quaternion.identity), new Vector3(1f,2f,1f)) && estimator.SampleCount == count,
                "Impossible cycle must not pollute the position history.");
            estimator.Clear();
            for (int i = 0; i < 12; i++) estimator.AddSample(Anchors(Vector3.zero, Quaternion.identity), new Vector3(2f,2f,2f));
            Require(!estimator.TryEstimate(out _, out _, out _, out _, out _) && estimator.RejectionReason == "samples",
                "An unmoving phone must not create a multiview fix.");
            estimator.Clear();
            for (int i = 0; i < 20; i++)
            {
                Vector3[] anchors = Anchors(new Vector3(0f,1f,i*.1f), Quaternion.identity);
                estimator.AddSample(anchors, Ranges(anchors,new Vector3(3f,1f,1f)));
            }
            Require(!estimator.TryEstimate(out _, out _, out _, out _, out _), "A narrow straight path must remain uncertain.");
            Replay("diagnostics/20260927/Uwb_20260927_173953_782.csv");
            Debug.Log("[Multiview Validation] PASS: pause after translation/rotation, known target, static phone, weak geometry, incompatible cycle and recorded trajectory replay.");
        }

        private static Vector3[] Anchors(Vector3 camera, Quaternion rotation)
        {
            Vector3 center = camera + rotation * new Vector3(0f,-.05f,.02f);
            return new[] { center + rotation * new Vector3(0f,.0484123f,0f),
                center + rotation * new Vector3(-.0875f,0f,0f), center + rotation * new Vector3(.0875f,0f,0f) };
        }
        private static Vector3 Ranges(Vector3[] anchors, Vector3 target) => new Vector3(
            Vector3.Distance(anchors[0],target),Vector3.Distance(anchors[1],target),Vector3.Distance(anchors[2],target));
        private static float Number(string text) => float.Parse(text,CultureInfo.InvariantCulture);
        private static void Replay(string path)
        {
            var estimator = new UwbArMultiviewEstimator();
            int tracked = 0, added = 0, accepted = 0;
            var reasons = new Dictionary<string,int>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] c = line.Split(',');
                if (c.Length < 30 || c[22] != "1" || c[10] != "1") continue;
                tracked++;
                if (string.IsNullOrEmpty(c[7]) || string.IsNullOrEmpty(c[8]) || string.IsNullOrEmpty(c[9])) continue;
                var camera = new Vector3(Number(c[23]),Number(c[24]),Number(c[25]));
                var q = new Quaternion(Number(c[26]),Number(c[27]),Number(c[28]),Number(c[29]));
                if (estimator.AddSample(Anchors(camera,q), new Vector3(Number(c[7]),Number(c[8]),Number(c[9])))) added++;
                if (estimator.TryEstimate(out _,out _,out _,out _,out _)) accepted++;
                string reason = estimator.RejectionReason;
                reasons[reason] = reasons.TryGetValue(reason,out int count) ? count+1 : 1;
            }
            var report = new List<string> { "Replay matemático com montagem padrão; estado do pan e montagem efetiva não foram registrados nesta versão do CSV.",
                $"AR tracked={tracked}; added={added}; accepted estimates={accepted}; retained={estimator.SampleCount}" };
            foreach (var item in reasons) report.Add(item.Key + "=" + item.Value);
            File.WriteAllLines("diagnostics/20260927/multiview-replay.txt",report);
            Debug.Log("[Multiview Replay] " + string.Join("; ",report));
        }
        private static void Require(bool condition,string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
