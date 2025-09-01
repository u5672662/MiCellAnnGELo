using SimpleFileBrowser;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Minimal Supabase Storage helper for uploading, listing, downloading, deleting, and signing URLs.
/// Uses UnityWebRequest coroutines and supports Android SAF paths. Logging is verbose when enabled.
/// </summary>
public class SupabaseInteractor : MonoBehaviour
{
    private const string DEFAULT_URL = "https://xgwpqkzqhxcwejopvlbu.supabase.co";

    private void Awake()
    {
        projectUrl = DEFAULT_URL;
        if (verboseLogging)
            Debug.Log($"[SupabaseInteractor] projectUrl = {projectUrl}");
    }
    
    [Header("Supabase")]
    public string serviceKey   = "sb_service_eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inhnd3Bxa3pxaHhjd2Vqb3B2bGJ1Iiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc1MzAyOTIzMiwiZXhwIjoyMDY4NjA1MjMyfQ.qcb53j12HrkInJc7yT19q1Nu66GGSn3crfGRw2_leTM"; // your server key
    public string projectUrl   = DEFAULT_URL;
    public string bucketName   = "uploads";

    [Header("Debug")]
    public bool verboseLogging = true;

    [Header("UI")]
    [SerializeField] private Slider progressBar;

    // ───────────────────────── Helpers ─────────────────────────
    [Serializable]
    public class SupabaseObject
    {
        public string name;
    }

    public static class JsonHelper
    {
        [Serializable]
        private class Wrapper<T> { public T[] items; }

        public static T[] FromJsonArray<T>(string jsonArray)
        {
            string newJson = "{\"items\":" + jsonArray + "}";
            var w = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return w.items;
        }
    }

    // ───────────────────────── Public API ─────────────────────────
    /// <summary>
    /// Uploads a file to Supabase Storage. Supports standard file paths and Android SAF URIs.
    /// </summary>
    /// <param name="localPath">Source file path or SAF URI.</param>
    /// <param name="remotePathInBucket">Optional object key; defaults to the file name.</param>
    /// <param name="upsert">If true, overwrites existing object.</param>
    /// <param name="onComplete">Invoked when the operation completes (success or failure).</param>
    /// <param name="onProgress">Invoked with upload progress [0..1].</param>
    public void UploadFile(string localPath, string remotePathInBucket = null, bool upsert = false, Action onComplete = null, Action<float> onProgress = null)
    {
        // For Android SAF paths, skip the File.Exists check
        bool isAndroidSAF = localPath.StartsWith("content://");
        if (!isAndroidSAF && !File.Exists(localPath))
        {
            Debug.LogError($"[Supabase] File not found → {localPath}");
            onComplete?.Invoke();
            return;
        }
        
        if (string.IsNullOrEmpty(remotePathInBucket))
        {
            remotePathInBucket = Path.GetFileName(localPath);
            if (string.IsNullOrEmpty(remotePathInBucket))
            {
                // Fallback for Android SAF or other paths
                Debug.LogWarning($"[Supabase] Could not extract filename from path: {localPath}");
                remotePathInBucket = "upload_" + System.DateTime.Now.Ticks + ".ply";
            }
        }
        
        StartCoroutine(CoSimpleUpload(localPath, remotePathInBucket, upsert, onComplete, onProgress));
    }

    /// <summary>
    /// Lists object keys in the current bucket, optionally filtered by a prefix.
    /// </summary>
    /// <param name="cb">Callback receiving the object names.</param>
    /// <param name="prefix">Optional prefix within the bucket.</param>
    public void ListFiles(Action<string[]> cb, string prefix = null)
        => StartCoroutine(CoListFiles(prefix, cb));

    /// <summary>
    /// Downloads an object from a private bucket using a signed URL to a local path or Android SAF URI.
    /// </summary>
    /// <param name="objectPath">Object key within the bucket.</param>
    /// <param name="saveAs">Destination path or SAF URI.</param>
    /// <param name="onComplete">Callback when finished.</param>
    public void DownloadSigned(string objectPath, string saveAs, Action onComplete = null)
        => StartCoroutine(CoDownloadSigned(objectPath, saveAs, onComplete));

    /// <summary>
    /// Deletes an object from the current bucket.
    /// </summary>
    /// <param name="objectPath">Object key to delete.</param>
    public void DeleteFile(string objectPath)
        => StartCoroutine(CoDelete(objectPath));

    /// <summary>
    /// Generates a time-limited signed URL for a private object.
    /// </summary>
    /// <param name="objectPath">Object key to sign.</param>
    /// <param name="cb">Callback receiving the signed URL (or empty if failed).</param>
    /// <param name="expires">Expiry in seconds.</param>
    public IEnumerator GenerateSignedUrl(string objectPath, Action<string> cb, int expires = 3600)
        => CoSignedUrl(objectPath, cb, expires);

    // ───────────────────────── Upload Coroutine ─────────────────────────
    private IEnumerator CoSimpleUpload(string localPath, string objectPath, bool upsert, Action onComplete, Action<float> onProgress)
    {
        var url = $"{projectUrl}/storage/v1/object/{bucketName}/{Uri.EscapeDataString(objectPath)}";
        if (upsert) url += "?upsert=true";

        UnityWebRequest req = null;

        try
        {
            if (localPath.StartsWith("content://"))
            {
                // SAF path: read raw bytes and upload from memory
                byte[] fileData = SimpleFileBrowser.FileBrowserHelpers.ReadBytesFromFile(localPath);
                req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT)
                {
                    uploadHandler = new UploadHandlerRaw(fileData),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                req.uploadHandler.contentType = "application/octet-stream";
            }
            else
            {
                // Local filesystem path: stream from disk to avoid large allocations
#if UNITY_2020_2_OR_NEWER
                req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT)
                {
                    uploadHandler = new UploadHandlerFile(localPath),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                req.uploadHandler.contentType = "application/octet-stream";
#else
                // Fallback for older Unity: read into memory
                byte[] fileData = File.ReadAllBytes(localPath);
                req = UnityWebRequest.Put(url, fileData);
#endif
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Supabase] Failed to prepare upload: {e.Message}");
            onComplete?.Invoke();
            yield break;
        }

        req.SetRequestHeader("apikey",        serviceKey);
        req.SetRequestHeader("Authorization", $"Bearer {serviceKey}");
        req.SetRequestHeader("Content-Type",  "application/octet-stream");

        if (verboseLogging) Debug.Log($"[Supabase] PUT {url}");

        var asyncOp = req.SendWebRequest();
        while (!asyncOp.isDone)
        {
            onProgress?.Invoke(req.uploadProgress);
            yield return null;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Supabase] Upload failed → HTTP {req.responseCode} | {req.error}\n{req.downloadHandler.text}");
            onComplete?.Invoke();
        }
        else
        {
            Debug.Log($"[Supabase] Upload OK → {objectPath}");
            onComplete?.Invoke();
        }
    }

    // ───────────────────────── List Coroutine ─────────────────────────
    private IEnumerator CoListFiles(string prefix, Action<string[]> cb)
    {
        var url = $"{projectUrl}/storage/v1/object/list/{bucketName}";
        if (!string.IsNullOrEmpty(prefix))
            url += $"?prefix={Uri.EscapeDataString(prefix)}";

        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("apikey",        serviceKey);
        req.SetRequestHeader("Authorization", $"Bearer {serviceKey}");
        if (verboseLogging) Debug.Log($"[Supabase] LIST {url}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Supabase] List failed → HTTP {req.responseCode} | {req.error}");
            cb?.Invoke(Array.Empty<string>());
            yield break;
        }

        var json = req.downloadHandler.text;
        var objs = JsonHelper.FromJsonArray<SupabaseObject>(json);
        var names = Array.ConvertAll(objs, o => o.name);
        cb?.Invoke(names);
    }

    // ───────────────────────── Download Coroutine (private bucket) ─────────────────────────
    private IEnumerator CoDownloadSigned(string objectPath, string saveAs, Action onComplete)
    {
        // 1) fetch the signed URL
        string signedUrl = null;
        yield return GenerateSignedUrl(objectPath, url => signedUrl = url);
        if (string.IsNullOrEmpty(signedUrl))
        {
            Debug.LogError($"[Supabase] Could not get signed URL for {objectPath}");
            yield break;
        }

        // 2) turn it into a full URL
        var url = signedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                  ? signedUrl
                  : $"{projectUrl}/storage/v1{signedUrl}";

        if (verboseLogging)
            Debug.Log($"[Supabase] GET {url} → {saveAs}");

        // 3) download into memory
        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();

        if (progressBar != null) progressBar.value = 0f;
        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            if (progressBar != null) progressBar.value = op.progress;
            yield return null;
        }
        if (progressBar != null) progressBar.value = 1f;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Supabase] Download failed → HTTP {req.responseCode} | {req.error}");
            yield break;
        }

        var data = req.downloadHandler.data;

        // 4) write it out
        try
        {
            if (saveAs.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                // Android SAF URI → use ContentResolver
#if UNITY_ANDROID && !UNITY_EDITOR
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", saveAs);

                var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var outputStream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);

                // write the bytes
                outputStream.Call("write", data);
                outputStream.Call("close");
#else
                Debug.LogError("[Supabase] SAF download requested on non-Android platform");
#endif
            }
            else
            {
                // normal file path
                File.WriteAllBytes(saveAs, data);
            }

            if (verboseLogging)
                Debug.Log($"[Supabase] Download OK → {saveAs}");
            onComplete?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Supabase] Error saving file → {e}");
        }
    }

    // ───────────────────────── Delete Coroutine ─────────────────────────
    private IEnumerator CoDelete(string objectPath)
    {
        var url = $"{projectUrl}/storage/v1/object/{bucketName}/{Uri.EscapeDataString(objectPath)}";
        using var req = UnityWebRequest.Delete(url);
        req.SetRequestHeader("apikey",        serviceKey);
        req.SetRequestHeader("Authorization", $"Bearer {serviceKey}");

        if (verboseLogging)
            Debug.Log($"[Supabase] DELETE {url}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogError($"[Supabase] Delete failed → HTTP {req.responseCode} | {req.error}\n{req.downloadHandler.text}");
        else
            Debug.Log($"[Supabase] Deleted → {objectPath}");
    }

    // ───────────────────────── Signed URL Coroutine ─────────────────────────
    private IEnumerator CoSignedUrl(string objectPath, Action<string> cb, int expires)
    {
        var url  = $"{projectUrl}/storage/v1/object/sign/{bucketName}/{Uri.EscapeDataString(objectPath)}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes($"{{\"expiresIn\":{expires}}}");

        if (verboseLogging)
            Debug.Log($"[Supabase] POST (sign) {url}");

        var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler   = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer()
        };
        req.SetRequestHeader("Content-Type",  "application/json");
        req.SetRequestHeader("apikey",        serviceKey);
        req.SetRequestHeader("Authorization", $"Bearer {serviceKey}");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var json = req.downloadHandler.text;
            if (verboseLogging) Debug.Log($"[Supabase] Sign OK → {json}");
            try
            {
                cb?.Invoke(JsonUtility.FromJson<SignedUrlResponse>(json).signedURL);
            }
            catch
            {
                Debug.LogWarning("[Supabase] Sign parsing failed");
                cb?.Invoke(string.Empty);
            }
        }
        else
        {
            Debug.LogError($"[Supabase] Sign failed → HTTP {req.responseCode} | {req.error}");
            cb?.Invoke(string.Empty);
        }
    }

    [Serializable]
    private struct SignedUrlResponse { public string signedURL; }
}
