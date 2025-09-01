using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Modifies the generated Android manifest after the Gradle project is created.
/// Adds eye‑tracking permission and related features, as well as the overlay keyboard feature.
/// </summary>
public class ManifestModifier : IPostGenerateGradleAndroidProject
{
    // Constants for manifest manipulation to avoid magic strings.
    private const string AndroidNamespaceUri = "http://schemas.android.com/apk/res/android";
    private const string LauncherManifestRelativePath = "launcher/src/main/AndroidManifest.xml";
    private const string LegacyManifestRelativePath = "src/main/AndroidManifest.xml";
    private const string EyeTrackingPermission = "com.oculus.permission.EYE_TRACKING";
    private const string EyeTrackingFeature = "oculus.software.eye_tracking";
    private const string OverlayKeyboardFeature = "oculus.software.overlay_keyboard";

    /// <summary>
    /// Determines the relative order in which this callback is invoked.
    /// </summary>
    public int callbackOrder => 1;

    /// <summary>
    /// Called after Unity generates the Gradle Android project.
    /// Ensures required Oculus permissions and features are present in the manifest.
    /// </summary>
    /// <param name="path">Absolute path to the root of the generated Gradle project.</param>
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("OnPostGenerateGradleAndroidProject received an empty path.");
            return;
        }

        // In recent Unity versions, the manifest resides in the "launcher" module.
        var manifestPath = Path.Combine(path, LauncherManifestRelativePath);

        // Fallback to the legacy location if needed.
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(path, LegacyManifestRelativePath);
            if (!File.Exists(manifestPath))
            {
                Debug.LogError(
                    "Could not find AndroidManifest.xml in the Gradle project. The build may fail or have incorrect permissions.");
                return;
            }
        }

        var doc = new XmlDocument();
        doc.Load(manifestPath);

        var manifestRoot = doc.DocumentElement;
        if (manifestRoot == null)
        {
            Debug.LogError("AndroidManifest.xml is empty or invalid.");
            return;
        }

        // The 'android' namespace URI is required for setting namespaced attributes.
        // Add <uses-permission> for eye tracking.
        var permissionElement = doc.CreateElement("uses-permission");
        permissionElement.SetAttribute("name", AndroidNamespaceUri, EyeTrackingPermission);
        manifestRoot.AppendChild(permissionElement);

        // Add <uses-feature> for eye tracking.
        var featureElement = doc.CreateElement("uses-feature");
        featureElement.SetAttribute("name", AndroidNamespaceUri, EyeTrackingFeature);
        featureElement.SetAttribute("required", AndroidNamespaceUri, "false");
        manifestRoot.AppendChild(featureElement);

        // Add <uses-feature> for the overlay keyboard.
        var keyboardFeatureElement = doc.CreateElement("uses-feature");
        keyboardFeatureElement.SetAttribute("name", AndroidNamespaceUri, OverlayKeyboardFeature);
        keyboardFeatureElement.SetAttribute("required", AndroidNamespaceUri, "false");
        manifestRoot.AppendChild(keyboardFeatureElement);

        doc.Save(manifestPath);
        Debug.Log("Eye-tracking and overlay keyboard entries successfully added to AndroidManifest.xml.");
    }
}
