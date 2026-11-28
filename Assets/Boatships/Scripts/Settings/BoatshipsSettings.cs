using UnityEngine;

namespace Boatships
{
    /// <summary>Player control preferences, saved between sessions.</summary>
    public static class BoatshipsSettings
    {
        private const string SensitivityKey = "Boatships.MouseSensitivity";
        private const string InvertKey = "Boatships.InvertMouseY";
        private static float sensitivity = -1f;
        private static int invert = -1;

        /// <summary>Multiplies first-person mouse look (0.25 to 2.5; 1 is the default).</summary>
        public static float MouseSensitivity
        {
            get { if (sensitivity < 0f) sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), 0.25f, 2.5f); return sensitivity; }
            set { sensitivity = Mathf.Clamp(value, 0.25f, 2.5f); PlayerPrefs.SetFloat(SensitivityKey, sensitivity); PlayerPrefs.Save(); }
        }

        /// <summary>Moving the mouse forward looks down instead of up.</summary>
        public static bool InvertMouseY
        {
            get { if (invert < 0) invert = PlayerPrefs.GetInt(InvertKey, 0); return invert == 1; }
            set { invert = value ? 1 : 0; PlayerPrefs.SetInt(InvertKey, invert); PlayerPrefs.Save(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { sensitivity = -1f; invert = -1; }
    }
}
