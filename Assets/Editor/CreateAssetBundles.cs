using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
    [MenuItem("Assets/Build selected as AssetBundle")]
    static void BuildAssetBundle()
    {
        string assetBundleDirectory = "Assets/AssetBundles/";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundle(Selection.activeObject, new UnityEngine.Object[] { Selection.activeObject }, assetBundleDirectory + Selection.activeObject.name,
                                        BuildAssetBundleOptions.UncompressedAssetBundle,
                                        EditorUserBuildSettings.activeBuildTarget);
    }

    [MenuItem("Assets/Build selected as AssetBundle", true)]
    static bool CheckAssetBundlePossible()
    {
        return Selection.activeObject != null;
    }
}