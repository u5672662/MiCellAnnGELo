using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

    /// <summary>
    /// Common utilities for logging and simple formatting helpers in the XR multiplayer context.
    /// </summary>
    public class Utils : MonoBehaviour
    {
        public const string k_LogPrefix = "<color=#33FF64>[XRMultiplayer]</color> ";
        public static LogLevel s_LogLevel = LogLevel.Developer;

        /// <summary>Logs an error with the standard prefix.</summary>
        public static void LogError(string message) => Log(message, 2);
        /// <summary>Logs a warning with the standard prefix.</summary>
        public static void LogWarning(string message) => Log(message, 1);
        /// <summary>
        /// Logs a message according to the specified level and current global <see cref="s_LogLevel"/>.
        /// </summary>
        public static void Log(string message, int logLevel = 0)
        {
            if (s_LogLevel == LogLevel.Nothing) return;
            StringBuilder sb = new(k_LogPrefix);
            sb.Append(message);

            switch (logLevel)
            {
                case 0:
                    if (s_LogLevel == 0)
                        Debug.Log(sb);
                    break;
                case 1:
                    if ((int)s_LogLevel < 2)
                        Debug.LogWarning(sb);
                    break;
                case 2:
                    Debug.LogError(sb);
                    break;
            }
        }

        /// <summary>
        /// Returns the English ordinal suffix (st, nd, rd, th) for a positive integer.
        /// Returns "th" for zero or negatives.
        /// </summary>
        public static string GetOrdinal(int num)
        {
            if (num <= 0) return num.ToString();

            switch (num % 100)
            {
                case 11:
                case 12:
                case 13:
                    return "th";
            }

            switch (num % 10)
            {
                case 1:
                    return "st";
                case 2:
                    return "nd";
                case 3:
                    return "rd";
                default:
                    return "th";
            }
        }

        /// <summary>
        /// Real modulo that is always non-negative for integer inputs.
        /// </summary>
        public static int RealMod(int a, int b)
        {
            return (a % b + b) % b;
        }

        /// <summary>
        /// Normalises an input value to [0..1] between the provided min and max, clamped.
        /// </summary>
        public static float GetPercentOfValueBetweenTwoValues(float min, float max, float input)
        {
            input = Mathf.Clamp(input, min, max);

            return (input - min) / (max - min);
        }
    }

    [System.Serializable]
    public class TextButton
    {
        public Button button;
        public TMP_Text buttonText;

        /// <summary>
        /// Updates button text, interactability, and click listener in one call.
        /// </summary>
        /// <param name="clickFunction">Listener to add.</param>
        /// <param name="newText">New text to display.</param>
        /// <param name="removeAllListeners">Whether to remove existing listeners first.</param>
        /// <param name="isInteractable">Whether the button is interactable.</param>
        public void UpdateButton(UnityAction clickFunction, string newText, bool removeAllListeners = true, bool isInteractable = true)
        {
            if (removeAllListeners)
                button.onClick.RemoveAllListeners();

            button.interactable = isInteractable;
            button.onClick.AddListener(clickFunction);
            buttonText.text = newText;
        }
    }