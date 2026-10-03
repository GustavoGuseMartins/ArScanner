#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class TemporalPoseValidation
    {
        public static void Run()
        {
            Assembly runtime = typeof(UwbDataReceiver).Assembly;
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type clockType = runtime.GetType("ArScanner.Network.UwbDeviceClockMapper", true);
            object clock = Activator.CreateInstance(clockType, true);
            MethodInfo map = clockType.GetMethod("TryMap", methods);
            MethodInfo clear = clockType.GetMethod("Clear", methods);
            Require(map != null && clear != null, "UWB clock mapping API is missing.");

            bool ready = false;
            for (int i = 0; i < 10; i++)
            {
                object[] args = { (uint)(1000 + i * 200), 10.03 + i * .2, 0d, 0d };
                ready = (bool)map.Invoke(clock, args);
            }
            Require(ready, "Stable device arrivals did not establish relative clock alignment.");
            object[] delayed = { 3000u, 12.18, 0d, 0d };
            Require(!(bool)map.Invoke(clock, delayed) && (double)delayed[3] > .1,
                "A delayed USB packet was paired with a later AR pose.");
            clear.Invoke(clock, null);
            for (int i = 0; i < 10; i++)
            {
                object[] args = { unchecked((uint)(uint.MaxValue - 800 + i * 200)),
                    30.02 + i * .2, 0d, 0d };
                ready = (bool)map.Invoke(clock, args);
            }
            Require(ready, "Uint32 millis rollover broke relative clock alignment.");

            Type historyType = runtime.GetType("ArScanner.Spatial.ArCameraPoseHistory", true);
            object history = Activator.CreateInstance(historyType, true);
            MethodInfo add = historyType.GetMethod("Add", methods);
            MethodInfo get = historyType.GetMethod("TryGet", methods);
            Require(add != null && get != null, "AR camera pose history API is missing.");
            Quaternion rotation = Quaternion.identity;
            add.Invoke(history, new object[] { 10d, new Pose(Vector3.zero, rotation) });
            add.Invoke(history, new object[] { 10.02d, new Pose(new Vector3(.02f, 0f, 0f), rotation) });
            object[] nextFrame = { 10.03d, new Pose() };
            Require(!(bool)get.Invoke(history, nextFrame),
                "Pose history extrapolated beyond the latest LateUpdate sample.");
            add.Invoke(history, new object[] { 10.04d, new Pose(new Vector3(.04f, 0f, 0f), rotation) });
            nextFrame[1] = new Pose();
            Require((bool)get.Invoke(history, nextFrame) &&
                Mathf.Abs(((Pose)nextFrame[1]).position.x - .03f) < .0001f,
                "A range waiting one frame was not matched to interpolated AR pose.");
            add.Invoke(history, new object[] { 10.30d, new Pose(new Vector3(.05f, 0f, 0f), rotation) });
            nextFrame[1] = new Pose();
            Require(!(bool)get.Invoke(history, nextFrame),
                "Tracking gap retained an obsolete AR frame.");
            Require((string)historyType.GetProperty("LastResetReason").GetValue(history) == "tracking_gap" &&
                (long)historyType.GetProperty("EpochRevision").GetValue(history) == 1,
                "A temporal gap must be identifiable separately from an AR world-origin jump.");
            add.Invoke(history, new object[] { 10.31d, new Pose(new Vector3(.06f, 0f, 0f), rotation) });
            add.Invoke(history, new object[] { 10.32d, new Pose(new Vector3(1f, 0f, 0f), rotation) });
            object[] beforeJump = { 10.315d, new Pose() };
            Require(!(bool)get.Invoke(history, beforeJump),
                "AR relocalization jump was interpolated as physical phone motion.");
            Require((string)historyType.GetProperty("LastResetReason").GetValue(history) == "spatial_discontinuity" &&
                (long)historyType.GetProperty("EpochRevision").GetValue(history) == 2,
                "A relocalization jump must signal pose and heading invalidation.");

            Debug.Log("[TemporalPoseValidation] PASS: clock, delay, rollover, AR interpolation, gaps, relocalization.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
