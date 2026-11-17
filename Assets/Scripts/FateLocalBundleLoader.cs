using System;
using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Additive local AssetBundle entry point for FateViewer.
/// Keeps the original AssetBundle format and delegates character creation to
/// the existing FateModelBuilder.BuildModel() implementation.
/// API loading remains available through FateModelBuilder.LoadServant().
/// </summary>
public sealed class FateLocalBundleLoader : MonoBehaviour
{
    private bool localLoadInProgress;

    public void LoadLocalServant(string bundleFilePath)
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("FateLocalBundleLoader: enter Play Mode before loading a local character bundle.");
            return;
        }

        if (localLoadInProgress)
        {
            Debug.LogWarning("FateLocalBundleLoader: a local bundle load is already in progress.");
            return;
        }

        if (string.IsNullOrEmpty(bundleFilePath))
        {
            Debug.LogError("FateLocalBundleLoader: bundle file path is empty.");
            return;
        }

        StartCoroutine(LoadLocalServantCoroutine(bundleFilePath));
    }

    private IEnumerator LoadLocalServantCoroutine(string bundleFilePath)
    {
        localLoadInProgress = true;
        AssetBundle loadedBundle = null;
        FateModelBuilder builder = FateModelBuilder.Instance;

        if (builder == null)
        {
            Debug.LogError("FateLocalBundleLoader: FateModelBuilder.Instance was not found. Open the original FateViewer scene.");
            localLoadInProgress = false;
            yield break;
        }

        if (builder.LoadingInProgress)
        {
            Debug.LogWarning("FateLocalBundleLoader: FateModelBuilder is already loading another character.");
            localLoadInProgress = false;
            yield break;
        }

        builder.LoadingInProgress = true;

        Debug.Log("FateViewer Local Mode: loading original AssetBundle from " + bundleFilePath);

        yield return FateAssetManager.LoadBundleFromFile(
            bundleFilePath,
            bundle => loadedBundle = bundle,
            progress => Debug.Log("FateViewer Local Mode: load progress " + progress.ToString("P0")));

        if (loadedBundle == null)
        {
            Debug.LogError("FateViewer Local Mode: failed to load AssetBundle: " + bundleFilePath);
            builder.LoadingInProgress = false;
            localLoadInProgress = false;
            yield break;
        }

        // Only replace the currently displayed servant after the new local bundle
        // has loaded successfully, so a wrong path does not remove the current model.
        if (FateViewerMain.SelectedServant != null)
        {
            FateViewerMain.SelectedServant.DeleteModel();
            FateViewerMain.SelectedServant = null;
        }

        Debug.Log("FateViewer Local Mode: AssetBundle loaded: " + loadedBundle.name + ". Passing original bundle to FateModelBuilder.BuildModel().");

        // BuildModel is the original FateViewer path for material setup, character
        // hierarchy construction, SimpleAnimation state extraction and Animator setup.
        // Its second argument is currently unused by the original implementation.
        yield return builder.BuildModel(loadedBundle, Path.GetDirectoryName(bundleFilePath));

        localLoadInProgress = false;
        Debug.Log("FateViewer Local Mode: character loading routine finished.");
    }
}

#if UNITY_EDITOR
public static class FateLocalBundleLoaderMenu
{
    private const string MenuPath = "Tools/FGO/Load Local Servant AssetBundle";

    [MenuItem(MenuPath)]
    private static void LoadLocalServant()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "FateViewer Local Mode",
                "请先打开 FateViewer 原始场景并进入 Play Mode。",
                "OK");
            return;
        }

        FateModelBuilder builder = FateModelBuilder.Instance;
        if (builder == null)
        {
            EditorUtility.DisplayDialog(
                "FateViewer Local Mode",
                "当前场景中没有 FateModelBuilder。请确认运行的是 FateViewer 原始场景。",
                "OK");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string initialDirectory = Path.Combine(projectRoot, "Data");
        if (!Directory.Exists(initialDirectory))
            initialDirectory = projectRoot;

        string bundlePath = EditorUtility.OpenFilePanel(
            "选择本地 FGO 角色 AssetBundle（原始文件，不需要扩展名）",
            initialDirectory,
            string.Empty);

        if (string.IsNullOrEmpty(bundlePath))
            return;

        FateLocalBundleLoader loader = builder.GetComponent<FateLocalBundleLoader>();
        if (loader == null)
            loader = builder.gameObject.AddComponent<FateLocalBundleLoader>();

        loader.LoadLocalServant(bundlePath);
    }
}
#endif
