using System;
using UnityEngine;

namespace ArScanner.Spatial
{
    // Two-point correction is saved only after a controlled measurement.
    // Earlier v1 profiles and the bundled provisional profile used unverified
    // geometry/orientation, so they must never silently drive an AR pose.
    public static class UwbRangeCalibrationProfile
    {
        private const string Key = "arscanner.uwb.range.v2.";
        private const string LegacyKey = "arscanner.uwb.range.v1.";
        private static readonly float[] AnchorRadiusMeters = { .0484123f, .0875f, .0875f };

        public static bool HasSavedCalibration => PlayerPrefs.GetInt(Key + "valid", 0) == 1;

        public static void Load(float[] scales, float[] offsets, out bool saved)
        {
            ValidateDestination(scales, offsets);
            saved = HasSavedCalibration;
            if (saved)
            {
                for (int i = 0; i < 3; i++)
                {
                    scales[i] = PlayerPrefs.GetFloat(Key + "scale" + i, 1f);
                    offsets[i] = PlayerPrefs.GetFloat(Key + "offset" + i, 0f);
                    if (!Finite(scales[i]) || scales[i] < .5f || scales[i] > 1.5f ||
                        !Finite(offsets[i]) || Mathf.Abs(offsets[i]) > 2f)
                    {
                        saved = false;
                        break;
                    }
                }
                if (saved) return;
            }
            for (int i = 0; i < 3; i++)
            {
                scales[i] = 1f;
                offsets[i] = 0f;
            }
        }

        public static bool TrySaveTwoPoint(float nearMeters, Vector3 nearRaw,
            float farMeters, Vector3 farRaw, out string error)
        {
            var scales = new float[3];
            var offsets = new float[3];
            float[] near = { nearRaw.x, nearRaw.y, nearRaw.z };
            float[] far = { farRaw.x, farRaw.y, farRaw.z };
            if (!TryFitTwoPoint(nearMeters, near, farMeters, far, scales, offsets, out error))
                return false;
            for (int i = 0; i < 3; i++)
            {
                PlayerPrefs.SetFloat(Key + "scale" + i, scales[i]);
                PlayerPrefs.SetFloat(Key + "offset" + i, offsets[i]);
            }
            PlayerPrefs.SetInt(Key + "valid", 1);
            PlayerPrefs.Save();
            return true;
        }

        public static bool TryFitTwoPoint(float nearMeters, float[] nearRaw,
            float farMeters, float[] farRaw, float[] scales, float[] offsets,
            out string error)
        {
            ValidateDestination(scales, offsets);
            if (nearRaw == null || farRaw == null || nearRaw.Length != 3 || farRaw.Length != 3 ||
                !Finite(nearMeters) || !Finite(farMeters) || nearMeters < .3f ||
                farMeters - nearMeters < .3f || farMeters > 10f)
            {
                error = "Use duas distâncias medidas entre 0,3 e 10 m, separadas por pelo menos 30 cm.";
                return false;
            }
            for (int i = 0; i < 3; i++)
            {
                float deltaRaw = farRaw[i] - nearRaw[i];
                if (!Finite(nearRaw[i]) || !Finite(farRaw[i]) || nearRaw[i] < .08f ||
                    deltaRaw < .2f)
                {
                    error = "Os alcances UWB não aumentaram o suficiente entre as duas posições.";
                    return false;
                }
                // With the board centered and facing the tag, the real range
                // to each radio includes its known small offset from the center.
                float nearTarget = Mathf.Sqrt(nearMeters * nearMeters +
                    AnchorRadiusMeters[i] * AnchorRadiusMeters[i]);
                float farTarget = Mathf.Sqrt(farMeters * farMeters +
                    AnchorRadiusMeters[i] * AnchorRadiusMeters[i]);
                float scale = (farTarget - nearTarget) / deltaRaw;
                float offset = nearTarget - scale * nearRaw[i];
                if (!Finite(scale) || scale < .5f || scale > 1.5f ||
                    !Finite(offset) || Mathf.Abs(offset) > 2f)
                {
                    error = "A calibração resultou em uma correção implausível. Confira as distâncias e a orientação da placa.";
                    return false;
                }
                scales[i] = scale;
                offsets[i] = offset;
            }
            error = null;
            return true;
        }

        public static void Reset()
        {
            DeleteSavedKeys(Key);
            DeleteSavedKeys(LegacyKey);
            PlayerPrefs.Save();
        }

        private static void DeleteSavedKeys(string prefix)
        {
            PlayerPrefs.DeleteKey(prefix + "valid");
            for (int i = 0; i < 3; i++)
            {
                PlayerPrefs.DeleteKey(prefix + "scale" + i);
                PlayerPrefs.DeleteKey(prefix + "offset" + i);
            }
        }

        private static void ValidateDestination(float[] scales, float[] offsets)
        {
            if (scales == null || offsets == null || scales.Length != 3 || offsets.Length != 3)
                throw new ArgumentException("Three radio scales and offsets are required.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
