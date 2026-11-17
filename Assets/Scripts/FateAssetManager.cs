using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class FateAssetManager : MonoBehaviour
{
    public static FateAssetManager Instance;

    void Awake()
    {
        Instance = this;
    }

    public static IEnumerator DownloadTexture(string url, System.Action<Texture2D> callback)
    {
        Texture2D outTex = null;
        Debug.Log("Downloading Texture " + url);
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(www.error);
        }
        else
        {
            outTex = ((DownloadHandlerTexture)www.downloadHandler).texture;
            outTex.wrapMode = TextureWrapMode.Clamp;
        }
        callback(outTex);
    }

    public static IEnumerator DownloadText(string url, System.Action<string> callback, System.Action<float> onProgress = null)
    {
        UnityWebRequest www = UnityWebRequest.Get(url);
        var request = www.SendWebRequest();

        while (!request.isDone)
        {
            onProgress?.Invoke(request.progress);
            yield return 0;
        }

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(url);
            Debug.Log(www.error);
            callback(null);
        }
        else
        {
            var str = www.downloadHandler.text;
            callback(str);
        }
    }

    // Original API-backed loader. Kept unchanged so API mode remains available.
    public static IEnumerator DownloadBundle(string url, System.Action<AssetBundle> callback, System.Action<float> onProgress = null)
    {
        Error.Log(Color.green, $"Downloading bundle from {url}");
        UnityWebRequest www = UnityWebRequest.Get(url);

        var request = www.SendWebRequest();

        while (!request.isDone)
        {
            onProgress?.Invoke(request.progress);
            yield return 0;
        }

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(url);
            Debug.Log(www.error);
            callback(null);
        }
        else
        {
            var bytes = www.downloadHandler.data;
            callback(AssetBundle.LoadFromMemory(bytes));
        }
    }

    // Additive local-file loader. It loads the original AssetBundle file directly;
    // no unpacking, reserialization, conversion, or AssetDatabase import is performed.
    public static IEnumerator LoadBundleFromFile(string filePath, System.Action<AssetBundle> callback, System.Action<float> onProgress = null)
    {
        if (callback == null)
        {
            Debug.LogError("LoadBundleFromFile requires a non-null callback.");
            yield break;
        }

        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogError("Local AssetBundle path is empty.");
            callback(null);
            yield break;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Invalid local AssetBundle path: " + filePath + "\n" + ex);
            callback(null);
            yield break;
        }

        if (!File.Exists(fullPath))
        {
            Debug.LogError("Local AssetBundle file does not exist: " + fullPath);
            callback(null);
            yield break;
        }

        onProgress?.Invoke(0f);
        Debug.Log("Loading local AssetBundle from file: " + fullPath);

        AssetBundleCreateRequest request;
        try
        {
            request = AssetBundle.LoadFromFileAsync(fullPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("AssetBundle.LoadFromFileAsync failed: " + ex);
            callback(null);
            yield break;
        }

        if (request == null)
        {
            Debug.LogError("AssetBundle.LoadFromFileAsync returned a null request: " + fullPath);
            callback(null);
            yield break;
        }

        while (!request.isDone)
        {
            onProgress?.Invoke(request.progress);
            yield return null;
        }

        AssetBundle bundle = request.assetBundle;
        if (bundle == null)
            Debug.LogError("Unity could not load the local AssetBundle: " + fullPath);
        else
            Debug.Log("Local AssetBundle loaded successfully: " + bundle.name);

        onProgress?.Invoke(1f);
        callback(bundle);
    }
}
