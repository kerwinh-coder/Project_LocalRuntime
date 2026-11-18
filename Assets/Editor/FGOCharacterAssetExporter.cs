#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exports the currently loaded FGO character as editable Unity assets.
/// Select the live FateServantContainer in the Hierarchy, then use the menu.
/// </summary>
public static class FGOCharacterAssetExporter
{
    [MenuItem("FGO Local Runtime/Export Selected Character Assets")]
    private static void ExportSelectedCharacter()
    {
        FateServantContainer servant = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<FateServantContainer>()
            : null;
        if (servant == null || servant.BodyMain == null)
        {
            EditorUtility.DisplayDialog("FGO Asset Export", "请在 Hierarchy 中选择当前已加载角色的根对象（包含 FateServantContainer）。", "OK");
            return;
        }

        string selectedFolder = EditorUtility.SaveFolderPanel("选择导出目录（必须位于当前 Unity 工程 Assets 内）",
            Application.dataPath, "FGO_Character_Export");
        if (string.IsNullOrEmpty(selectedFolder)) return;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        string folder = selectedFolder.Replace("\\", "/");
        string assetsRoot = Application.dataPath.Replace("\\", "/");
        if (!folder.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("FGO Asset Export", "导出目录必须位于当前工程的 Assets 文件夹内。", "OK");
            return;
        }

        string relativeFolder = "Assets" + folder.Substring(assetsRoot.Length);
        string characterFolder = relativeFolder + "/" + MakeSafeName(servant.name);
        if (AssetDatabase.IsValidFolder(characterFolder))
            characterFolder += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        CreateFolders(characterFolder);

        try
        {
            GameObject source = servant.BodyMain;
            Dictionary<UnityEngine.Object, UnityEngine.Object> remap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            int meshCount = ExportMeshes(source, characterFolder, remap);
            int textureCount = ExportTextures(servant.Textures, characterFolder, remap);
            int materialCount = ExportMaterials(servant.Materials, characterFolder, remap);
            int clipCount = ExportClips(servant.AnimationClips, characterFolder, remap);

            GameObject prefabSource = UnityEngine.Object.Instantiate(source);
            prefabSource.name = source.name;
            RemapHierarchyAssets(prefabSource, remap);

            Animator sourceAnimator = source.GetComponentInChildren<Animator>(true);
            Animator targetAnimator = prefabSource.GetComponentInChildren<Animator>(true);
            string controllerNote = "Animator Controller 未复制；请在导出报告中检查控制器依赖。";
            RuntimeAnimatorController baseController = servant.OverrideController != null
                ? servant.OverrideController.runtimeAnimatorController
                : (sourceAnimator != null ? sourceAnimator.runtimeAnimatorController : null);
            if (baseController != null)
            {
                string controllerPath = AssetDatabase.GetAssetPath(baseController);
                if (!string.IsNullOrEmpty(controllerPath) && controllerPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                {
                    string copiedControllerPath = AssetDatabase.GenerateUniqueAssetPath(characterFolder + "/" + Path.GetFileName(controllerPath));
                    if (AssetDatabase.CopyAsset(controllerPath, copiedControllerPath))
                    {
                        RuntimeAnimatorController copiedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(copiedControllerPath);
                        AnimatorOverrideController exportedOverride = new AnimatorOverrideController(copiedController);
                        List<KeyValuePair<AnimationClip, AnimationClip>> sourceOverrides =
                            new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        servant.OverrideController.GetOverrides(sourceOverrides);
                        List<KeyValuePair<AnimationClip, AnimationClip>> exportedOverrides =
                            new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        foreach (KeyValuePair<AnimationClip, AnimationClip> pair in sourceOverrides)
                        {
                            AnimationClip replacement = pair.Value;
                            if (replacement != null && remap.ContainsKey(replacement))
                                replacement = (AnimationClip)remap[replacement];
                            exportedOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, replacement));
                        }
                        exportedOverride.ApplyOverrides(exportedOverrides);
                        AssetDatabase.CreateAsset(exportedOverride, characterFolder + "/Character_AnimatorOverride.overrideController");
                        if (targetAnimator != null) targetAnimator.runtimeAnimatorController = exportedOverride;
                        controllerNote = "基础 Animator Controller 与当前 Override Controller 已复制；原始状态映射已尽可能重定向到导出 Clip。";
                    }
                }
            }

            string prefabPath = characterFolder + "/" + MakeSafeName(source.name) + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
            UnityEngine.Object.DestroyImmediate(prefabSource);

            string reportPath = characterFolder + "/ExportReport.txt";
            File.WriteAllText(reportPath,
                "FGO Character Asset Export\n" +
                "Character: " + servant.name + "\n" +
                "Source Bundle: " + servant.name + "\n" +
                "Prefab: " + prefabPath + "\n" +
                "Meshes: " + meshCount + "\nTextures: " + textureCount +
                "\nMaterials: " + materialCount + "\nAnimation Clips: " + clipCount +
                "\n" + controllerNote + "\n\n" +
                "依赖检查提示：材质 Shader、Animator Controller 的间接引用、Avatar、脚本及其他外部资源仍需在独立工程验证。此报告不代表依赖已完全自包含。");
            AssetDatabase.Refresh();
            UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("FGO Asset Export",
                "第一轮资产导出完成。\nPrefab：" + prefabPath +
                "\nMesh：" + meshCount + "；Texture：" + textureCount +
                "；Material：" + materialCount + "；AnimationClip：" + clipCount +
                "\n\n请先检查导出目录与 ExportReport.txt，再进行独立工程验证。",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("FGO Asset Export Failed", ex.Message, "OK");
        }
    }

    private static int ExportMeshes(GameObject root, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
    {
        int count = 0;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null) { ExportObject(filter.sharedMesh, folder, remap); count++; }
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh != null) { ExportObject(renderer.sharedMesh, folder, remap); count++; }
        return count;
    }

    private static int ExportTextures(IEnumerable<Texture2D> textures, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
    {
        int count = 0;
        foreach (Texture2D texture in textures)
            if (texture != null) { ExportObject(texture, folder, remap); count++; }
        foreach (Renderer renderer in Selection.activeGameObject.GetComponent<FateServantContainer>().BodyMain.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null)
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture2D texture = material.GetTexture(property) as Texture2D;
                        if (texture != null && !remap.ContainsKey(texture)) { ExportObject(texture, folder, remap); count++; }
                    }
        return count;
    }

    private static int ExportMaterials(IEnumerable<Material> materials, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
    {
        HashSet<Material> all = new HashSet<Material>(materials.Where(m => m != null));
        FateServantContainer servant = Selection.activeGameObject.GetComponent<FateServantContainer>();
        foreach (Renderer renderer in servant.BodyMain.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) all.Add(material);
        foreach (Material material in all) ExportObject(material, folder, remap);
        return all.Count;
    }

    private static int ExportClips(IEnumerable<SimpleAnimation.State> states, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
    {
        HashSet<AnimationClip> clips = new HashSet<AnimationClip>();
        foreach (SimpleAnimation.State state in states)
            if (state != null && state.clip != null) clips.Add(state.clip);
        foreach (AnimationClip clip in clips) ExportObject(clip, folder, remap);
        return clips.Count;
    }

    private static void ExportObject<T>(T source, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
        where T : UnityEngine.Object
    {
        if (source == null || remap.ContainsKey(source)) return;
        T copy = UnityEngine.Object.Instantiate(source);
        copy.name = source.name;
        string extension = source is AnimationClip ? ".anim" : source is Material ? ".mat" : ".asset";
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MakeSafeName(source.name) + extension);
        AssetDatabase.CreateAsset(copy, path);
        remap.Add(source, copy);
    }

    private static void RemapHierarchyAssets(GameObject root, Dictionary<UnityEngine.Object, UnityEngine.Object> remap)
    {
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null && remap.ContainsKey(filter.sharedMesh))
                filter.sharedMesh = (Mesh)remap[filter.sharedMesh];
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh != null && remap.ContainsKey(renderer.sharedMesh))
                renderer.sharedMesh = (Mesh)remap[renderer.sharedMesh];
        foreach (Material material in remap.Values.OfType<Material>())
        {
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture != null && remap.ContainsKey(texture))
                    material.SetTexture(property, (Texture)remap[texture]);
            }
        }
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && remap.ContainsKey(materials[i]))
                    materials[i] = (Material)remap[materials[i]];
            renderer.sharedMaterials = materials;
        }
    }

    private static void CreateFolders(string path)
    {
        string current = "Assets";
        foreach (string part in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }

    private static string MakeSafeName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrEmpty(value) ? "FGO_Character" : value.Replace('/', '_').Replace('\\', '_');
    }
}
#endif
