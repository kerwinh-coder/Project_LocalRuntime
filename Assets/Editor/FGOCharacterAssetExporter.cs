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

        string selectedFolder = EditorUtility.SaveFolderPanel("选择导出文件夹（全部导出文件直接保存到此文件夹，不会新建子文件夹）",
            Application.dataPath, "");
        if (string.IsNullOrEmpty(selectedFolder)) return;

        string folder = selectedFolder.Replace("\\", "/");
        string assetsRoot = Application.dataPath.Replace("\\", "/");
        if (!folder.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !folder.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("FGO Asset Export", "导出目录必须位于当前工程的 Assets 文件夹内。", "OK");
            return;
        }

        string characterFolder = "Assets" + folder.Substring(assetsRoot.Length);
        if (!AssetDatabase.IsValidFolder(characterFolder))
        {
            EditorUtility.DisplayDialog("FGO Asset Export", "所选目录不是有效的 Unity Assets 文件夹。", "OK");
            return;
        }

        try
        {
            GameObject source = servant.BodyMain;
            Dictionary<UnityEngine.Object, UnityEngine.Object> remap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            int meshCount = ExportMeshes(source, characterFolder, remap);
            int textureCount = ExportTextures(servant.Textures, characterFolder, remap);
            int materialCount = ExportMaterials(servant.Materials, characterFolder, remap);
            int clipCount = ExportClips(servant.AnimationClips, characterFolder, remap);

            Dictionary<UnityEngine.Object, UnityEngine.Object> hierarchyMap;
            GameObject prefabSource = BuildCleanExportHierarchy(source, out hierarchyMap);
            RemapHierarchyAssets(prefabSource, remap);

            Animator sourceAnimator = source.GetComponentInChildren<Animator>(true);
            Animator targetAnimator = prefabSource.GetComponentInChildren<Animator>(true);
            int avatarCount = 0;
            string avatarNote = "Avatar 未找到。";
            if (sourceAnimator != null && sourceAnimator.avatar != null)
            {
                try
                {
                    ExportObject(sourceAnimator.avatar, characterFolder, remap);
                    Avatar exportedAvatar = remap[sourceAnimator.avatar] as Avatar;
                    if (targetAnimator != null && exportedAvatar != null)
                        targetAnimator.avatar = exportedAvatar;
                    avatarCount = exportedAvatar != null ? 1 : 0;
                    avatarNote = exportedAvatar != null
                        ? "Avatar 已复制并重新绑定到导出 Prefab。"
                        : "Avatar 复制后无法读取导出对象。";
                }
                catch (Exception avatarException)
                {
                    avatarNote = "Avatar 导出失败：" + avatarException.Message;
                    Debug.LogWarning("[FGO Export] " + avatarNote);
                }
            }
            string controllerNote = "Animator Controller 未复制；请在导出报告中检查控制器依赖。";
            RuntimeAnimatorController baseController = servant.OverrideController != null
                ? servant.OverrideController.runtimeAnimatorController
                : (sourceAnimator != null ? sourceAnimator.runtimeAnimatorController : null);
            if (baseController != null)
            {
                string controllerPath = AssetDatabase.GetAssetPath(baseController);
                if (!string.IsNullOrEmpty(controllerPath) && controllerPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                {
                    string copiedControllerPath = characterFolder + "/" + Path.GetFileName(controllerPath);
                    if (AssetDatabase.LoadMainAssetAtPath(copiedControllerPath) != null)
                        AssetDatabase.DeleteAsset(copiedControllerPath);
                    if (AssetDatabase.CopyAsset(controllerPath, copiedControllerPath))
                    {
                        RuntimeAnimatorController copiedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(copiedControllerPath);
                        AnimatorOverrideController exportedOverride = new AnimatorOverrideController(copiedController);
                        List<KeyValuePair<AnimationClip, AnimationClip>> exportedOverrides =
                            new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        exportedOverride.GetOverrides(exportedOverrides);
                        List<KeyValuePair<AnimationClip, AnimationClip>> sourceOverrides =
                            new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        if (servant.OverrideController != null)
                            servant.OverrideController.GetOverrides(sourceOverrides);
                        for (int i = 0; i < exportedOverrides.Count; i++)
                        {
                            AnimationClip sourceReplacement = sourceOverrides
                                .FirstOrDefault(pair => pair.Key != null && exportedOverrides[i].Key != null &&
                                    pair.Key.name == exportedOverrides[i].Key.name).Value;
                            AnimationClip replacement = sourceReplacement != null && remap.ContainsKey(sourceReplacement)
                                ? (AnimationClip)remap[sourceReplacement]
                                : sourceReplacement;
                            exportedOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(exportedOverrides[i].Key, replacement);
                        }
                        exportedOverride.ApplyOverrides(exportedOverrides);
                        string overridePath = characterFolder + "/Character_AnimatorOverride.overrideController";
                        if (AssetDatabase.LoadMainAssetAtPath(overridePath) != null)
                            AssetDatabase.DeleteAsset(overridePath);
                        AssetDatabase.CreateAsset(exportedOverride, overridePath);
                        if (targetAnimator != null) targetAnimator.runtimeAnimatorController = exportedOverride;
                        controllerNote = "基础 Animator Controller 与当前 Override Controller 已复制；原始状态映射已尽可能重定向到导出 Clip。";
                    }
                }
            }

            string prefabPath = characterFolder + "/" + MakeSafeName(source.name) + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
            UnityEngine.Object.DestroyImmediate(prefabSource);

            string stateReport = string.Join("\n", servant.AnimationClips
                .Where(state => state != null && state.clip != null)
                .Select(state => state.name + " -> " + state.clip.name));
            string reportPath = characterFolder + "/ExportReport.txt";
            File.WriteAllText(reportPath,
                "FGO Character Asset Export\n" +
                "Character: " + servant.name + "\n" +
                "Source Bundle: " + servant.name + "\n" +
                "Prefab: " + prefabPath + "\n" +
                "Meshes: " + meshCount + "\nTextures: " + textureCount +
                "\nMaterials: " + materialCount + "\nAnimation Clips: " + clipCount +
                "\nAvatars: " + avatarCount + "\n" + avatarNote +
                "\n" + controllerNote + "\n\n" +
                "Animation States:\n" + stateReport + "\n\n" +
                "依赖检查提示：材质 Shader、Animator Controller 的间接引用、Avatar、脚本及其他外部资源仍需在独立工程验证。此报告不代表依赖已完全自包含。");
            AssetDatabase.Refresh();
            UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("FGO Asset Export",
                "第一轮资产导出完成。\nPrefab：" + prefabPath +
                "\nMesh：" + meshCount + "；Texture：" + textureCount +
                "；Material：" + materialCount + "；AnimationClip：" + clipCount +
                "；Avatar：" + avatarCount +
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
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            throw new InvalidOperationException("导出目标文件夹无效：" + (folder ?? "<null>"));

        T copy;
        if (source is Texture2D)
            copy = (T)(UnityEngine.Object)CopyTextureReadable((Texture2D)(UnityEngine.Object)source);
        else
            copy = UnityEngine.Object.Instantiate(source);
        copy.name = string.IsNullOrEmpty(source.name) ? source.GetType().Name : source.name;
        string extension = source is AnimationClip ? ".anim" : source is Material ? ".mat" : ".asset";
        string basePath = folder.TrimEnd('/') + "/" + MakeSafeName(copy.name) + extension;
        string path = basePath;
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            AssetDatabase.DeleteAsset(path);
        if (string.IsNullOrEmpty(path))
        {
            UnityEngine.Object.DestroyImmediate(copy);
            throw new InvalidOperationException("Unity 无法生成资产路径。目标文件夹：" + folder + "；原始对象：" + source.GetType().Name + " / " + source.name);
        }

        try
        {
            AssetDatabase.CreateAsset(copy, path);
            remap.Add(source, copy);
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(copy);
            throw;
        }
    }

    // AssetBundles often contain GPU-only textures with Read/Write disabled.
    // Blit through a temporary RenderTexture so they can be exported as readable assets.
    private static Texture2D CopyTextureReadable(Texture2D source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.name = source.name;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            copy.Apply(false, false);
            return copy;
        }
        catch
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    // Rebuild the hierarchy instead of cloning it. This deliberately avoids carrying
    // invalid serialized script entries (including non-MonoBehaviour script records)
    // from AssetBundle instances into the exported Prefab.
    private static GameObject BuildCleanExportHierarchy(
        GameObject sourceRoot,
        out Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        Dictionary<Transform, Transform> transformMap = new Dictionary<Transform, Transform>();
        Dictionary<Component, Component> componentMap = new Dictionary<Component, Component>();

        GameObject targetRoot = new GameObject(sourceRoot.name);
        objectMap[sourceRoot] = targetRoot;
        CopyTransformValues(sourceRoot.transform, targetRoot.transform);
        objectMap[sourceRoot.transform] = targetRoot.transform;
        transformMap[sourceRoot.transform] = targetRoot.transform;

        CopyChildrenRecursive(sourceRoot.transform, targetRoot.transform, objectMap, transformMap, componentMap);
        RemapSerializedReferences(objectMap);

        return targetRoot;
    }

    private static void CopyChildrenRecursive(
        Transform sourceParent,
        Transform targetParent,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap,
        Dictionary<Transform, Transform> transformMap,
        Dictionary<Component, Component> componentMap)
    {
        foreach (Transform sourceChild in sourceParent)
        {
            GameObject targetObject = new GameObject(sourceChild.gameObject.name);
            Transform targetTransform = targetObject.transform;
            targetTransform.SetParent(targetParent, false);
            CopyTransformValues(sourceChild, targetTransform);
            objectMap[sourceChild.gameObject] = targetObject;
            objectMap[sourceChild] = targetTransform;
            transformMap[sourceChild] = targetTransform;

            CopyChildrenRecursive(sourceChild, targetTransform, objectMap, transformMap, componentMap);
        }

        GameObject targetGameObject = targetParent.gameObject;
        GameObject sourceGameObject = sourceParent.gameObject;
        foreach (Component sourceComponent in sourceGameObject.GetComponents<Component>())
        {
            // Null entries are intentionally skipped: these are where Unity exposes
            // unsupported/missing serialized script records on some AssetBundle objects.
            if (sourceComponent == null) continue;
            Type type = sourceComponent.GetType();
            if (type == typeof(Transform) || type == typeof(SimpleAnimation)) continue;
            if (type.Name == "BattleServantDeadComponent") continue;

            try
            {
                Component targetComponent = targetGameObject.AddComponent(type);
                if (targetComponent == null)
                {
                    Debug.LogWarning("[FGO Export] Could not add component: " + type.FullName +
                        " on " + GetHierarchyPath(sourceParent));
                    continue;
                }
                EditorUtility.CopySerialized(sourceComponent, targetComponent);
                objectMap[sourceComponent] = targetComponent;
                componentMap[sourceComponent] = targetComponent;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FGO Export] Skipped component " + type.FullName +
                    " on " + GetHierarchyPath(sourceParent) + ": " + exception.Message);
            }
        }
    }

    private static void CopyTransformValues(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
        target.localEulerAngles = source.localEulerAngles;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static void RemapSerializedReferences(Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        foreach (UnityEngine.Object targetObject in objectMap.Values.Distinct())
        {
            Component component = targetObject as Component;
            if (component == null) continue;

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.GetIterator();
            bool changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                UnityEngine.Object reference = property.objectReferenceValue;
                if (reference != null && objectMap.TryGetValue(reference, out UnityEngine.Object replacement))
                {
                    property.objectReferenceValue = replacement;
                    changed = true;
                }
            }
            if (changed) serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
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
