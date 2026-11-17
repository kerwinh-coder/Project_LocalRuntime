using System;
using System.Collections;
using uGIF;
using UnityEngine;

public class Screenshot : MonoBehaviour
{
    public static Screenshot Instance;
    bool recording = false;

    private void Awake()
    {
        Instance = this;
    }

    public static void ScreenshotDefault()
    {
        var image = GrabFrame(int.Parse(UIController.Instance.SSWidth.text), int.Parse(UIController.Instance.SSHeight.text));

        string fileName = string.Format("FateViewer_{0}", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
        byte[] pngShot = ImageConversion.EncodeToPNG(image);
        WebGLDownload.DownloadFile(pngShot, fileName, "png");

        Destroy(image);
    }

    public static Texture2D GrabFrame(int width, int height, bool gifBackground = false)
    {
        var dimensions = GetResolution(width, height);
        width = dimensions.x;
        height = dimensions.y;

        Camera cam = Camera.main;
        int oldMask = cam.cullingMask;
        var bak_cam_clearFlags = cam.clearFlags;
        cam.cullingMask = ~LayerMask.GetMask("UI");

        if (gifBackground)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0, 0, 0, 0);
        }
        else
        {
            cam.clearFlags = CameraClearFlags.Depth;
        }

        var tex_color = new Texture2D(width, height, TextureFormat.ARGB32, false);
        var tex_alpha = new Texture2D(width, height, TextureFormat.ARGB32, false);
        var render_texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var grab_area = new Rect(0, 0, width, height);

        RenderTexture.active = render_texture;
        cam.targetTexture = render_texture;

        cam.Render();
        tex_color.ReadPixels(grab_area, 0, 0);
        tex_color.Apply();

        foreach(var servant in GameObject.FindObjectsOfType<FateServantContainer>())
        {
            servant.SwitchShaderToScreenshot(true);
        }

        cam.Render();
        tex_alpha.ReadPixels(grab_area, 0, 0);
        tex_alpha.Apply();

        var alpha = tex_alpha.GetPixels();
        var color = tex_color.GetPixels();
        for (int i = 0; i < color.Length; i++)
        {
            color[i].a = alpha[i].a;
        }

        tex_color.SetPixels(color);
        Destroy(tex_alpha);

        foreach (var servant in GameObject.FindObjectsOfType<FateServantContainer>())
        {
            servant.SwitchShaderToScreenshot(false);
        }

        cam.clearFlags = bak_cam_clearFlags;
        cam.cullingMask = oldMask;
        cam.targetTexture = null;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(render_texture);

        return tex_color;
    }

    public static Vector2Int GetResolution(int width, int height)
    {
        if (width == 0 && height == 0)
        {
            width = Screen.width;
            height = Screen.height;
        }
        else if (width == 0)
        {
            float ratio = (float)Screen.width / (float)Screen.height;
            width = Mathf.RoundToInt((float)height * ratio);
        }
        else if (height == 0)
        {
            float ratio = (float)Screen.height / (float)Screen.width;
            height = Mathf.RoundToInt((float)width * ratio);
        }
        return new Vector2Int(width, height);
    }

    public void StartAnimRecord()
    {
        if (recording) return;
        recording = true;
        StartCoroutine(RecordAnim());
    }

    public IEnumerator RecordAnim()
    {
        Error.Log(Color.yellow, "Recording! Don't touch anything!");
        FateViewerMain.SelectedServant.CurrentFrame = 0;

        StartCoroutine(CaptureToGIFCustom.Instance.Encode());
        while (FateViewerMain.SelectedServant.CurrentFrame <= FateViewerMain.SelectedServant.ClipFrameCount)
        {
            yield return new WaitForEndOfFrame();
            var tex = GrabFrame(int.Parse(UIController.Instance.GifWidth.text), int.Parse(UIController.Instance.GifHeight.text), true);
            CaptureToGIFCustom.Instance.Frames.Add(new Image(tex));
            Destroy(tex);
            //Debug.Log(FateViewerMain.SelectedServant.CurrentFrame);
            if (FateViewerMain.SelectedServant.CurrentFrame == FateViewerMain.SelectedServant.ClipFrameCount) break;
        }
        CaptureToGIFCustom.Instance.stop = true;
        recording = false;
    }
}