#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Exports the selected live FateServantContainer as a standalone character prefab.
/// The exported prefab uses FGOCharacterAnimationPlayer, not FateServantContainer.
/// </summary>
public static class FGOCharacterAssetExporter
{
    private const string AnimatorStateName = "FGO_Clip";
    private const string AnimationSlotName = "FGO_Clip_Slot";

    [MenuItem("FGO Local Runtime/Export Selected Character Assets")]
    private static void ExportSelectedCharacter()
    {
        FateServantContainer servant = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<FateServantContainer>()
            : null;

        if (servant == null || servant.BodyMain == null)
        {
            EditorUtility.DisplayDialog(
                "FGO Character Export",
                "请在 Hierarchy 中选择当前已加载角色的根对象（包含 FateServantContainer）。",
                "OK");
            return;
        }

        string selectedFolder = EditorUtility.SaveFolderPanel(
            "选择导出文件夹",
            Application.dataPath,
            "");

        if (string.IsNullOrEmpty(selectedFolder))
            return;

        string folder = selectedFolder.Replace("\\", "/");
        string assetsRoot = Application.dataPath.Replace("\\", "/");
        if (!folder.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
            !folder.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "FGO Character Export",
                "导出目录必须位于当前 Unity 工程的 Assets 文件夹内。",
                "OK");
            return;
        }

        string parentAssetFolder = "Assets" + folder.Substring(assetsRoot.Length);
        if (string.IsNullOrEmpty(parentAssetFolder))
            parentAssetFolder = "Assets";

        if (!AssetDatabase.IsValidFolder(parentAssetFolder))
        {
            EditorUtility.DisplayDialog(
                "FGO Character Export",
                "所选父目录尚未被 Unity 识别为有效的 Assets 文件夹。请在 Unity Project 窗口中先创建或刷新该父目录，再重新选择。",
                "OK");
            return;
        }

        // Always place one character export inside its own dedicated folder.
        // The user selects only an existing parent folder; Unity creates the
        // character folder so it is immediately registered in AssetDatabase.
        string characterFolderName = "FGO_Character_" + MakeSafeName(servant.name);
        string assetFolder = parentAssetFolder.TrimEnd('/') + "/" + characterFolderName;
        int suffix = 2;
        while (AssetDatabase.IsValidFolder(assetFolder))
        {
            assetFolder = parentAssetFolder.TrimEnd('/') + "/" +
                characterFolderName + "_" + suffix;
            suffix++;
        }

        string createdFolderGuid = AssetDatabase.CreateFolder(
            parentAssetFolder,
            Path.GetFileName(assetFolder));
        if (string.IsNullOrEmpty(createdFolderGuid))
        {
            EditorUtility.DisplayDialog(
                "FGO Character Export",
                "无法创建角色专属导出文件夹：" + assetFolder,
                "OK");
            return;
        }

        GameObject prefabSource = null;
        try
        {
            GameObject source = servant.BodyMain;
            LogMaterialReferenceDiagnostics(servant, source, "SOURCE_BEFORE_EXPORT");
            Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap =
                new Dictionary<UnityEngine.Object, UnityEngine.Object>();

            int meshCount = ExportMeshes(source, assetFolder, assetMap);
            int textureCount = ExportTextures(servant, source, assetFolder, assetMap);
            int materialCount = ExportMaterials(servant, source, assetFolder, assetMap);
            List<string> shaderDependencies = ExportShaderDependencies(assetFolder, assetMap);
            List<FGOCharacterAnimationPlayer.ClipEntry> exportedStates =
                ExportAnimationClips(servant.AnimationClips, assetFolder, assetMap);

            if (exportedStates.Count == 0)
                throw new InvalidOperationException("角色没有可导出的 AnimationClip。");

            Dictionary<UnityEngine.Object, UnityEngine.Object> hierarchyMap;
            prefabSource = BuildCleanExportHierarchy(source, out hierarchyMap);
            RemapSerializedAssetReferences(prefabSource, assetMap);
            RemapHierarchyAssets(prefabSource, assetMap);
            LogMaterialReferenceDiagnostics(servant, prefabSource, "EXPORT_AFTER_REMAP");

            Animator sourceAnimator = source.GetComponentInChildren<Animator>(true);
            Animator targetAnimator = prefabSource.GetComponentInChildren<Animator>(true);
            if (sourceAnimator == null || targetAnimator == null)
                throw new InvalidOperationException("角色层级中没有可用的 Animator。");

            if (sourceAnimator.avatar != null)
            {
                Avatar exportedAvatar = ExportObject(sourceAnimator.avatar, assetFolder, assetMap);
                targetAnimator.avatar = exportedAvatar;
            }

            AnimationClip slotClip;
            AnimatorController baseController =
                CreateStandaloneAnimatorController(assetFolder, out slotClip);

            targetAnimator.runtimeAnimatorController = baseController;
            targetAnimator.speed = 0f;
            targetAnimator.applyRootMotion = false;

            string initialAnimation = FindInitialAnimation(exportedStates);
            FGOCharacterAnimationPlayer player =
                prefabSource.AddComponent<FGOCharacterAnimationPlayer>();
            player.Configure(
                targetAnimator,
                baseController,
                slotClip,
                exportedStates,
                initialAnimation);
            EditorUtility.SetDirty(player);

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                assetFolder + "/" + MakeSafeName(source.name) + ".prefab");
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
            if (savedPrefab == null)
                throw new InvalidOperationException("Unity 未能保存导出 Prefab。");

            string reportPath = assetFolder + "/ExportReport.txt";
            string stateReport = string.Join("\n", exportedStates.Select(entry =>
                entry.stateName + " -> " + entry.clip.name +
                " (" + entry.clip.length.ToString("0.000") + "s, " +
                entry.clip.frameRate.ToString("0.##") + " fps)"));

            File.WriteAllText(
                Path.GetFullPath(reportPath),
                "FGO Standalone Character Export\n" +
                "Character: " + servant.name + "\n" +
                "Source: FateViewer live character instance\n" +
                "Prefab: " + prefabPath + "\n" +
                "Meshes: " + meshCount + "\n" +
                "Textures: " + textureCount + "\n" +
                "Materials: " + materialCount + "\n" +
                "Shader Dependencies:\n" + string.Join("\n", shaderDependencies.Select(item => "  " + item)) + "\n" +
                "Animation States: " + exportedStates.Count + "\n" +
                "Unique Animation Clips: " + exportedStates.Select(entry => entry.clip).Distinct().Count() + "\n" +
                "Animation Driver: FGOCharacterAnimationPlayer\n" +
                "Animator State: " + AnimatorStateName + "\n" +
                "Animator Slot: " + AnimationSlotName + "\n" +
                "Initial Animation: " + initialAnimation + "\n" +
                "Avatar: " + (sourceAnimator.avatar != null ? "Copied" : "Source Animator has no Avatar") + "\n\n" +
                "Animation States:\n" + stateReport + "\n\n" +
                "Dependency note: Meshes, textures, materials, supported FateViewer shaders, clips and a minimal Animator Controller " +
                "are exported. Unity built-in shaders remain engine dependencies. Any external component dependencies " +
                "must still be checked in a clean Unity project; this report does not claim complete dependency closure.\n");

            AssetDatabase.ImportAsset(reportPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "FGO Character Export",
                "独立角色 Prefab 已生成。\n\n" +
                prefabPath + "\n\n" +
                "动画状态：" + exportedStates.Count + "\n" +
                "动画驱动：FGOCharacterAnimationPlayer\n" +
                "初始动作：" + initialAnimation,
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "FGO Character Export Failed",
                exception.Message,
                "OK");
        }
        finally
        {
            if (prefabSource != null)
                UnityEngine.Object.DestroyImmediate(prefabSource);
        }
    }

    private static void LogSingleMaterialReferenceDiagnostics(Material material, string stage)
    {
        if (material == null) return;

        string textureDetails = "";
        foreach (string propertyName in material.GetTexturePropertyNames())
        {
            Texture texture = material.GetTexture(propertyName);
            textureDetails += " [" + propertyName + "=" +
                (texture != null
                    ? texture.name + "#" + texture.GetInstanceID()
                    : "NULL") + "]";
        }

        Debug.Log("[FGO MaterialDiag][" + stage + "] Material=" +
            material.name + " id=" + material.GetInstanceID() +
            " shader=" + (material.shader != null ? material.shader.name : "NULL") +
            textureDetails, material);
    }

    // Temporary diagnostics: compare live source references with exported asset references.
    private static void LogMaterialReferenceDiagnostics(
        FateServantContainer servant,
        GameObject root,
        string stage)
    {
        HashSet<Material> materials = new HashSet<Material>();
        if (servant != null)
            foreach (Material material in servant.Materials)
                if (material != null) materials.Add(material);

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] rendererMaterials = renderer.sharedMaterials;
            foreach (Material material in rendererMaterials)
                if (material != null) materials.Add(material);

            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                Material material = rendererMaterials[i];
                if (material == null) continue;
                bool alsoInServantList = servant != null && servant.Materials.Contains(material);
                Debug.Log("[FGO MaterialDiag][" + stage + "] Renderer=" +
                    GetHierarchyPath(renderer.transform) + " slot=" + i +
                    " material=" + material.name + " materialId=" + material.GetInstanceID() +
                    " inServantMaterials=" + alsoInServantList, renderer);
            }
        }

        foreach (Material material in materials)
        {
            string textureDetails = "";
            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(propertyName);
                textureDetails += " [" + propertyName + "=" +
                    (texture != null
                        ? texture.name + "#" + texture.GetInstanceID()
                        : "NULL") + "]";
            }

            Debug.Log("[FGO MaterialDiag][" + stage + "] Material=" +
                material.name + " id=" + material.GetInstanceID() +
                " shader=" + (material.shader != null ? material.shader.name : "NULL") +
                textureDetails, material);
        }
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

    private static List<string> ExportShaderDependencies(
        string assetFolder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        List<string> dependencies = new List<string>();
        HashSet<Shader> processedShaders = new HashSet<Shader>();
        string shaderFolder = assetFolder + "/Shaders";
        bool shaderFolderCreated = false;

        foreach (KeyValuePair<UnityEngine.Object, UnityEngine.Object> pair in assetMap)
        {
            Material sourceMaterial = pair.Key as Material;
            Material exportedMaterial = pair.Value as Material;
            if (sourceMaterial == null || exportedMaterial == null || sourceMaterial.shader == null)
                continue;

            Shader sourceShader = sourceMaterial.shader;
            if (!processedShaders.Add(sourceShader))
                continue;

            string shaderName = sourceShader.name;
            if (shaderName != "FateShader3Compatible" &&
                shaderName != "FateScreenshotShaderCompatible")
            {
                string externalPath = AssetDatabase.GetAssetPath(sourceShader);
                if (!string.IsNullOrEmpty(externalPath))
                {
                    throw new InvalidOperationException(
                        "发现尚未纳入独立导出的项目 Shader：" + shaderName +
                        "（" + externalPath + "）。为避免生成不完整资产，本次导出已停止。");
                }

                dependencies.Add(shaderName + " (Unity built-in)");
                continue;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceShader);
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(Path.GetFullPath(sourcePath)))
            {
                throw new InvalidOperationException(
                    "无法定位 FateViewer Shader 源文件：" + shaderName);
            }

            if (!shaderFolderCreated)
            {
                string guid = AssetDatabase.CreateFolder(assetFolder, "Shaders");
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException("无法创建 Shader 导出目录：" + shaderFolder);
                shaderFolderCreated = true;
            }

            string exportedShaderName = "FGO/Standalone/" + shaderName;
            string destinationPath = shaderFolder + "/" + shaderName + ".shader";
            string sourceText = File.ReadAllText(Path.GetFullPath(sourcePath));
            string sourceDeclaration = "Shader \"" + shaderName + "\"";
            string exportedDeclaration = "Shader \"" + exportedShaderName + "\"";
            if (!sourceText.Contains(sourceDeclaration))
            {
                throw new InvalidOperationException(
                    "Shader 源文件中未找到预期声明：" + sourceDeclaration);
            }

            string exportedText = sourceText.Replace(sourceDeclaration, exportedDeclaration);
            File.WriteAllText(Path.GetFullPath(destinationPath), exportedText);
            AssetDatabase.ImportAsset(
                destinationPath,
                ImportAssetOptions.ForceSynchronousImport);

            Shader exportedShader = AssetDatabase.LoadAssetAtPath<Shader>(destinationPath);
            if (exportedShader == null)
            {
                throw new InvalidOperationException(
                    "导出的 Shader 无法被 Unity 导入：" + destinationPath);
            }

            foreach (KeyValuePair<UnityEngine.Object, UnityEngine.Object> materialPair in assetMap)
            {
                Material source = materialPair.Key as Material;
                Material target = materialPair.Value as Material;
                if (source != null && target != null && source.shader == sourceShader)
                    target.shader = exportedShader;
            }

            dependencies.Add(
                exportedShaderName + " <- " + sourcePath);
        }

        return dependencies;
    }

    private static int ExportMeshes(
        GameObject root,
        string folder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        HashSet<Mesh> meshes = new HashSet<Mesh>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh != null) meshes.Add(renderer.sharedMesh);

        foreach (Mesh mesh in meshes)
            ExportObject(mesh, folder, assetMap);

        return meshes.Count;
    }

    private static int ExportTextures(
        FateServantContainer servant,
        GameObject root,
        string folder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        HashSet<Texture2D> textures = new HashSet<Texture2D>(
            servant.Textures.Where(texture => texture != null));

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string propertyName in material.GetTexturePropertyNames())
                {
                    Texture2D texture = material.GetTexture(propertyName) as Texture2D;
                    if (texture != null) textures.Add(texture);
                }
            }
        }

        foreach (Texture2D texture in textures)
            ExportObject(texture, folder, assetMap);

        return textures.Count;
    }

    private static int ExportMaterials(
        FateServantContainer servant,
        GameObject root,
        string folder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        HashSet<Material> materials = new HashSet<Material>(
            servant.Materials.Where(material => material != null));

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) materials.Add(material);

        foreach (Material material in materials)
            ExportObject(material, folder, assetMap);

        return materials.Count;
    }

    private static List<FGOCharacterAnimationPlayer.ClipEntry> ExportAnimationClips(
        IEnumerable<SimpleAnimation.State> states,
        string folder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        List<FGOCharacterAnimationPlayer.ClipEntry> result =
            new List<FGOCharacterAnimationPlayer.ClipEntry>();

        foreach (SimpleAnimation.State state in states)
        {
            if (state == null || state.clip == null || string.IsNullOrEmpty(state.name))
                continue;

            AnimationClip exportedClip = ExportObject(state.clip, folder, assetMap);
            result.Add(new FGOCharacterAnimationPlayer.ClipEntry
            {
                stateName = state.name,
                clip = exportedClip
            });
        }

        return result;
    }

    private static string FindInitialAnimation(
        List<FGOCharacterAnimationPlayer.ClipEntry> states)
    {
        FGOCharacterAnimationPlayer.ClipEntry initial = states.FirstOrDefault(entry =>
            string.Equals(entry.stateName, "Default", StringComparison.OrdinalIgnoreCase));
        if (initial == null)
            initial = states.FirstOrDefault(entry =>
                string.Equals(entry.stateName, "wait", StringComparison.OrdinalIgnoreCase));
        if (initial == null)
            initial = states.FirstOrDefault();

        if (initial == null || initial.clip == null)
            throw new InvalidOperationException("找不到有效的初始动画状态。");

        return initial.stateName;
    }

    private static AnimatorController CreateStandaloneAnimatorController(
        string folder,
        out AnimationClip slotClip)
    {
        string slotPath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/" + AnimationSlotName + ".anim");
        slotClip = new AnimationClip();
        slotClip.name = AnimationSlotName;
        AssetDatabase.CreateAsset(slotClip, slotPath);

        string controllerPath = AssetDatabase.GenerateUniqueAssetPath(
            folder + "/FGO_Standalone.controller");
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        ChildAnimatorState[] existingStates = stateMachine.states;
        foreach (ChildAnimatorState childState in existingStates)
            if (childState.state != null)
                stateMachine.RemoveState(childState.state);

        AnimatorState animationState = stateMachine.AddState(AnimatorStateName);
        animationState.motion = slotClip;
        stateMachine.defaultState = animationState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static T ExportObject<T>(
        T source,
        string folder,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
        where T : UnityEngine.Object
    {
        if (source == null) return null;

        UnityEngine.Object existing;
        if (assetMap.TryGetValue(source, out existing))
            return existing as T;

        if (source is Material)
            LogSingleMaterialReferenceDiagnostics((Material)(UnityEngine.Object)source, "EXPORT_OBJECT_SOURCE");

        T copy;
        if (source is Texture2D)
            copy = (T)(UnityEngine.Object)CopyTextureReadable((Texture2D)(UnityEngine.Object)source);
        else
            copy = UnityEngine.Object.Instantiate(source);

        copy.name = string.IsNullOrEmpty(source.name) ? source.GetType().Name : source.name;

        if (source is Material)
            LogSingleMaterialReferenceDiagnostics((Material)(UnityEngine.Object)copy, "EXPORT_OBJECT_INSTANTIATED");

        string extension = source is AnimationClip ? ".anim" :
            source is Material ? ".mat" :
            source is Mesh ? ".asset" :
            source is Avatar ? ".asset" : ".asset";
        string path = AssetDatabase.GenerateUniqueAssetPath(
            folder.TrimEnd('/') + "/" + MakeSafeName(copy.name) + extension);

        try
        {
            AssetDatabase.CreateAsset(copy, path);
            assetMap.Add(source, copy);
            if (copy is Material)
                LogSingleMaterialReferenceDiagnostics((Material)(UnityEngine.Object)copy, "EXPORT_OBJECT_ASSET_CREATED");
            return copy;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(copy);
            throw;
        }
    }

    private static Texture2D CopyTextureReadable(Texture2D source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = null;

        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.name = source.name;
            copy.filterMode = source.filterMode;
            copy.wrapMode = source.wrapMode;
            copy.anisoLevel = source.anisoLevel;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            copy.Apply(false, false);
            LogTextureCopyDiagnostics(source, copy);
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


    private static void LogTextureCopyDiagnostics(Texture2D source, Texture2D copy)
    {
        if (source == null || copy == null) return;

        Color32[] pixels = copy.GetPixels32();
        byte minAlpha = 255;
        byte maxAlpha = 0;
        int transparentPixels = 0;
        int opaquePixels = 0;
        int partialAlphaPixels = 0;

        foreach (Color32 pixel in pixels)
        {
            if (pixel.a < minAlpha) minAlpha = pixel.a;
            if (pixel.a > maxAlpha) maxAlpha = pixel.a;
            if (pixel.a == 0) transparentPixels++;
            else if (pixel.a == 255) opaquePixels++;
            else partialAlphaPixels++;
        }

        Debug.Log(
            "[FGO Export][TextureCopy] " + source.name +
            " source=" + source.width + "x" + source.height +
            " format=" + source.format +
            " mipmaps=" + source.mipmapCount +
            " copy=" + copy.width + "x" + copy.height +
            " format=" + copy.format +
            " alphaMin=" + minAlpha +
            " alphaMax=" + maxAlpha +
            " transparent=" + transparentPixels +
            " partial=" + partialAlphaPixels +
            " opaque=" + opaquePixels);
    }

    private static GameObject BuildCleanExportHierarchy(
        GameObject sourceRoot,
        out Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        GameObject targetRoot = new GameObject(sourceRoot.name);
        objectMap[sourceRoot] = targetRoot;
        objectMap[sourceRoot.transform] = targetRoot.transform;
        CopyTransformValues(sourceRoot.transform, targetRoot.transform);

        CopyChildrenRecursive(sourceRoot.transform, targetRoot.transform, objectMap);
        RemapSerializedReferences(objectMap);
        return targetRoot;
    }

    private static void CopyChildrenRecursive(
        Transform sourceParent,
        Transform targetParent,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
    {
        foreach (Transform sourceChild in sourceParent)
        {
            GameObject targetObject = new GameObject(sourceChild.gameObject.name);
            Transform targetTransform = targetObject.transform;
            targetTransform.SetParent(targetParent, false);
            CopyTransformValues(sourceChild, targetTransform);
            objectMap[sourceChild.gameObject] = targetObject;
            objectMap[sourceChild] = targetTransform;
            CopyChildrenRecursive(sourceChild, targetTransform, objectMap);
        }

        GameObject sourceObject = sourceParent.gameObject;
        GameObject targetObjectForComponents = targetParent.gameObject;
        foreach (Component sourceComponent in sourceObject.GetComponents<Component>())
        {
            if (sourceComponent == null) continue;

            Type componentType = sourceComponent.GetType();
            if (componentType == typeof(Transform) || componentType == typeof(SimpleAnimation))
                continue;
            if (componentType.Name == "BattleServantDeadComponent")
                continue;

            try
            {
                Component targetComponent = targetObjectForComponents.AddComponent(componentType);
                if (targetComponent == null)
                    continue;

                EditorUtility.CopySerialized(sourceComponent, targetComponent);
                objectMap[sourceComponent] = targetComponent;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[FGO Export] Skipped component " + componentType.FullName +
                    " on " + GetHierarchyPath(sourceParent) + ": " + exception.Message);
            }
        }
    }

    private static void CopyTransformValues(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
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

    private static void RemapSerializedReferences(
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap)
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
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object reference = property.objectReferenceValue;
                UnityEngine.Object replacement;
                if (reference != null && objectMap.TryGetValue(reference, out replacement))
                {
                    property.objectReferenceValue = replacement;
                    changed = true;
                }
            }

            if (changed)
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void RemapSerializedAssetReferences(
        GameObject root,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        int remappedReferenceCount = 0;

        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.GetIterator();
            bool changed = false;

            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object sourceReference = property.objectReferenceValue;
                UnityEngine.Object exportedReference;
                if (sourceReference == null ||
                    !assetMap.TryGetValue(sourceReference, out exportedReference))
                    continue;

                property.objectReferenceValue = exportedReference;
                changed = true;
                remappedReferenceCount++;

                Debug.Log(
                    "[FGO Export][SerializedAssetRemap] component=" +
                    GetHierarchyPath(component.transform) +
                    " type=" + component.GetType().Name +
                    " property=" + property.propertyPath +
                    " source=" + sourceReference.name +
                    " exported=" + exportedReference.name,
                    component);
            }

            if (changed)
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log(
            "[FGO Export][SerializedAssetRemap] completed references=" +
            remappedReferenceCount,
            root);
    }

    private static void RemapHierarchyAssets(
        GameObject root,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            UnityEngine.Object replacement;
            if (assetMap.TryGetValue(filter.sharedMesh, out replacement))
                filter.sharedMesh = replacement as Mesh;
        }

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh != null)
            {
                UnityEngine.Object replacement;
                if (assetMap.TryGetValue(renderer.sharedMesh, out replacement))
                    renderer.sharedMesh = replacement as Mesh;
            }
        }

        foreach (Material material in assetMap.Values.OfType<Material>())
        {
            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(propertyName);
                UnityEngine.Object replacement;
                bool mapped = texture != null && assetMap.TryGetValue(texture, out replacement);
                Debug.Log(
                    "[FGO RemapDiag][MaterialTexture] material=" + material.name +
                    " property=" + propertyName +
                    " before=" + (texture != null ? texture.name + "#" + texture.GetInstanceID() : "NULL") +
                    " mapped=" + mapped +
                    (mapped ? " after=" + replacement.name + "#" + replacement.GetInstanceID() : ""));
                if (mapped)
                    material.SetTexture(propertyName, replacement as Texture);
            }
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material sourceMaterial = materials[i];
                UnityEngine.Object replacement;
                bool mapped = sourceMaterial != null &&
                    assetMap.TryGetValue(sourceMaterial, out replacement);
                Debug.Log(
                    "[FGO RemapDiag][RendererMaterial] renderer=" + GetHierarchyPath(renderer.transform) +
                    " slot=" + i +
                    " before=" + (sourceMaterial != null ? sourceMaterial.name + "#" + sourceMaterial.GetInstanceID() : "NULL") +
                    " mapped=" + mapped +
                    (mapped ? " after=" + replacement.name + "#" + replacement.GetInstanceID() : ""));
                if (mapped)
                    materials[i] = replacement as Material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static string MakeSafeName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return string.IsNullOrEmpty(value)
            ? "FGO_Character"
            : value.Replace('/', '_').Replace('\\', '_');
    }
}
#endif
