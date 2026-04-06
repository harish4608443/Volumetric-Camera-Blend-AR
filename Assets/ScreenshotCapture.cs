using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds a floating camera-icon button that saves a screenshot to the device gallery.
/// Attach to any active GameObject in the scene (e.g. AR Session Origin or AR Camera).
/// No scene setup needed — button and notification are created entirely at runtime.
/// </summary>
public class ScreenshotCapture : MonoBehaviour
{
    private Button screenshotButton;

    void Start()
    {
        CreateUI();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // UI construction
    // ───────────────────────────────────────────────────────────────────────────

    void CreateUI()
    {
        // Re-use an existing Canvas if one is present; otherwise create a new one.
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("ScreenshotCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // ── Screenshot button — bottom-right corner ──
        GameObject btnGO = new GameObject("ScreenshotButton");
        btnGO.transform.SetParent(canvas.transform, false);

        RectTransform btnRect = btnGO.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1f, 0f);
        btnRect.anchorMax = new Vector2(1f, 0f);
        btnRect.pivot     = new Vector2(1f, 0f);
        // Position: 20 px from right, 20 px from bottom — stays clear of the toggle button
        btnRect.anchoredPosition = new Vector2(-20f, 20f);
        btnRect.sizeDelta        = new Vector2(120f, 120f);

        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);

        screenshotButton = btnGO.AddComponent<Button>();
        screenshotButton.targetGraphic = btnImage;
        ColorBlock cb = screenshotButton.colors;
        cb.normalColor      = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        cb.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 0.90f);
        cb.pressedColor     = new Color(0.05f, 0.05f, 0.05f, 1.00f);
        screenshotButton.colors = cb;
        screenshotButton.onClick.AddListener(TakeScreenshot);

        // Camera icon label inside the button
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);
        RectTransform labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        Text labelText = labelGO.AddComponent<Text>();
        labelText.text      = "📷";
        labelText.fontSize  = 48;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color     = Color.white;
        labelText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Debug.Log("[Screenshot] UI created — camera button bottom-right");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Screenshot logic
    // ───────────────────────────────────────────────────────────────────────────

    void TakeScreenshot()
    {
        StartCoroutine(CaptureAfterRender());
    }

    IEnumerator CaptureAfterRender()
    {
        // Wait until end of frame so the current AR frame is fully composited
        yield return new WaitForEndOfFrame();

        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename  = "ARCapture_" + timestamp + ".png";

        // Grab screen pixels into a Texture2D
        Texture2D tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();
        byte[] pngBytes = tex.EncodeToPNG();
        Destroy(tex);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android — save directly into the gallery via MediaStore (works on Android 10+ / scoped storage).
        // No WRITE_EXTERNAL_STORAGE permission is required when writing through MediaStore.
        SaveToGalleryAndroid(pngBytes, filename);
#else
        // Editor / other platforms — write to project folder
        string editorPath = System.IO.Path.Combine(Application.dataPath, "..", filename);
        System.IO.File.WriteAllBytes(editorPath, pngBytes);
        ShowNotification("Saved: " + filename);
        Debug.Log("[Screenshot] Editor: saved to " + editorPath);
#endif
    }

    private void ShowNotification(string message)
    {
        // Notification UI removed — log only
        Debug.Log("[Screenshot] " + message);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Inserts the PNG bytes directly into the Android MediaStore so it appears in the gallery.
    /// Uses MediaStore.Images.Media (DCIM/ARCapture folder) — compatible with Android 10+ scoped storage.
    /// Falls back to MediaScanner on Android 9 and below.
    /// </summary>
    private void SaveToGalleryAndroid(byte[] pngBytes, string filename)
    {
        try
        {
            AndroidJavaClass versionClass = new AndroidJavaClass("android.os.Build$VERSION");
            int sdkInt = versionClass.GetStatic<int>("SDK_INT");

            AndroidJavaObject activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            if (sdkInt >= 29)
            {
                // Android 10+ (API 29+): use MediaStore API — no WRITE_EXTERNAL_STORAGE needed
                AndroidJavaObject contentResolver = activity.Call<AndroidJavaObject>("getContentResolver");

                AndroidJavaObject contentValues = new AndroidJavaObject("android.content.ContentValues");
                contentValues.Call("put", "_display_name", filename);   // MediaColumns.DISPLAY_NAME
                contentValues.Call("put", "mime_type", "image/png");      // MediaColumns.MIME_TYPE
                contentValues.Call("put", "relative_path", "DCIM/Screenshots"); // same folder the user already knows

                AndroidJavaClass mediaStoreClass = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                AndroidJavaObject externalUri = mediaStoreClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

                AndroidJavaObject uri = contentResolver.Call<AndroidJavaObject>("insert", externalUri, contentValues);
                if (uri != null)
                {
                    AndroidJavaObject outputStream = contentResolver.Call<AndroidJavaObject>("openOutputStream", uri);
                    outputStream.Call("write", pngBytes);
                    outputStream.Call("flush");
                    outputStream.Call("close");
                    ShowNotification("Saved to Screenshots!");
                    Debug.Log("[Screenshot] Saved to gallery: DCIM/Screenshots/" + filename);
                }
                else
                {
                    throw new System.Exception("MediaStore insert returned null URI");
                }
            }
            else
            {
                // Android 9 and below: write to DCIM folder, then trigger media scan
                string dcim = "/storage/emulated/0/DCIM/Screenshots";
                System.IO.Directory.CreateDirectory(dcim);
                string filePath = System.IO.Path.Combine(dcim, filename);
                System.IO.File.WriteAllBytes(filePath, pngBytes);

                // Tell the media scanner about the new file so it shows in gallery
                AndroidJavaClass scannerClass = new AndroidJavaClass("android.media.MediaScannerConnection");
                scannerClass.CallStatic("scanFile",
                    activity,
                    new string[] { filePath },
                    new string[] { "image/png" },
                    null);

                ShowNotification("Saved to Screenshots!");
                Debug.Log("[Screenshot] Saved to DCIM/Screenshots (legacy): " + filePath);
            }
        }
        catch (System.Exception e)
        {
            // Last-resort fallback: save to persistent data path
            string fallback = System.IO.Path.Combine(Application.persistentDataPath, filename);
            System.IO.File.WriteAllBytes(fallback, pngBytes);
            ShowNotification("Saved to App Folder");
            Debug.LogError("[Screenshot] Gallery save failed, wrote to: " + fallback + " | Error: " + e.Message);
        }
    }
#endif

}
