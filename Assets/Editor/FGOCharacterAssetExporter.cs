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
            List<string> componentAudit;
            prefabSource = BuildCleanExportHierarchy(source, out hierarchyMap, out componentAudit);
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

            // Audit the persisted prefab asset, not only the temporary scene hierarchy.
            Animator savedPrefabAnimator = savedPrefab.GetComponentInChildren<Animator>(true);
            if (savedPrefabAnimator == null)
                throw new InvalidOperationException("保存后的 Prefab 中找不到 Animator。");

            string reportPath = assetFolder + "/ExportReport.txt";
            string stateReport = string.Join("\n", exportedStates.Select(entry =>
                entry.stateName + " -> " + entry.clip.name +
                " (" + entry.clip.length.ToString("0.000") + "s, " +
                entry.clip.frameRate.ToString("0.##") + " fps)"));

            string animationEventReport = BuildAnimationEventReport(exportedStates, assetMap);
            string animationCurveReport = BuildAnimationCurveBindingReport(exportedStates, savedPrefab.transform);
            string animatorSettingsReport = BuildAnimatorSettingsReport(sourceAnimator, savedPrefabAnimator);

            int copiedComponentCount = componentAudit.Count(item => item.Contains(" | COPIED"));
            int excludedComponentCount = componentAudit.Count(item => item.Contains(" | INTENTIONALLY EXCLUDED"));
            int failedComponentCount = componentAudit.Count(item => item.Contains(" | FAILED:"));
            int missingScriptCount = componentAudit.Count(item => item.Contains(" | MISSING SCRIPT COMPONENT"));
            string componentAuditSummary =
                "  Copied: " + copiedComponentCount + "\n" +
                "  Intentionally excluded: " + excludedComponentCount + "\n" +
                "  Copy failures: " + failedComponentCount + "\n" +
                "  Missing scripts: " + missingScriptCount + "\n";

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
                "Component Copy Audit Summary:\n" + componentAuditSummary +
                "Component Copy Audit Details:\n" + string.Join("\n", componentAudit.Select(item => "  " + item)) + "\n" +
                "Animator State: " + AnimatorStateName + "\n" +
                "Animator Slot: " + AnimationSlotName + "\n" +
                "Initial Animation: " + initialAnimation + "\n" +
                "Animator Settings Audit:\n" + animatorSettingsReport + "\n\n" +
                "Avatar: " + (sourceAnimator.avatar != null ? "Copied" : "Source Animator has no Avatar") + "\n\n" +
                "Animation Curve Binding Audit:\n" + animationCurveReport + "\n\n" +
                "Animation Event Audit:\n" + animationEventReport + "\n\n" +
                "Animation States:\n" + stateReport + "\n\n" +
                "Dependency note: Meshes, textures, materials, supported FateViewer shaders, clips and a minimal Animator Controller " +
                "are exported. Unity built-in shaders remain engine dependencies. Any external component dependencies " +
                "must still be checked in a clean Unity project; this report does not claim complete dependency closure. " +
                "AnimationEvent object references are listed in the audit but are not automatically exported or remapped.\n");

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

    private static string BuildAnimatorSettingsReport(Animator source, Animator exported)
    {
        if (source == null || exported == null)
            return "Source or exported Animator is missing.";

        Avatar sourceAvatar = source.avatar;
        Avatar exportedAvatar = exported.avatar;

        return
            "Source Animator: " + GetHierarchyPath(source.transform) + "\n" +
            "Exported Animator: " + GetHierarchyPath(exported.transform) + "\n" +
            "Enabled: source=" + source.enabled + ", exported=" + exported.enabled + "\n" +
            "Update Mode: source=" + source.updateMode + ", exported=" + exported.updateMode + "\n" +
            "Culling Mode: source=" + source.cullingMode + ", exported=" + exported.cullingMode + "\n" +
            "Apply Root Motion: source=" + source.applyRootMotion + ", exported=" + exported.applyRootMotion + "\n" +
            "Speed: source=" + source.speed + ", exported=" + exported.speed + "\n" +
            "Avatar: source=" + (sourceAvatar != null ? sourceAvatar.name : "None") +
            ", exported=" + (exportedAvatar != null ? exportedAvatar.name : "None") + "\n" +
            "Avatar Valid: source=" + (sourceAvatar != null && sourceAvatar.isValid) +
            ", exported=" + (exportedAvatar != null && exportedAvatar.isValid) + "\n" +
            "Avatar Human: source=" + (sourceAvatar != null && sourceAvatar.isHuman) +
            ", exported=" + (exportedAvatar != null && exportedAvatar.isHuman);
    }

    private static string BuildAnimationCurveBindingReport(
        IEnumerable<FGOCharacterAnimationPlayer.ClipEntry> states,
        Transform exportedRoot)
    {
        List<string> lines = new List<string>();
        int totalFloatBindings = 0;
        int totalObjectBindings = 0;
        int missingPathCount = 0;

        foreach (FGOCharacterAnimationPlayer.ClipEntry entry in states)
        {
            if (entry == null || entry.clip == null)
                continue;

            EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(entry.clip);
            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(entry.clip);
            totalFloatBindings += floatBindings.Length;
            totalObjectBindings += objectBindings.Length;

            lines.Add(entry.stateName + " / " + entry.clip.name +
                " | float curves=" + floatBindings.Length +
                " | object-reference curves=" + objectBindings.Length);

            foreach (EditorCurveBinding binding in floatBindings)
            {
                if (HasAnimationBindingPath(exportedRoot, binding.path))
                    continue;

                missingPathCount++;
                lines.Add("  MISSING FLOAT PATH: " + (string.IsNullOrEmpty(binding.path) ? "<root>" : binding.path) +
                    " | property=" + binding.propertyName + " | type=" + binding.type);
            }

            foreach (EditorCurveBinding binding in objectBindings)
            {
                if (HasAnimationBindingPath(exportedRoot, binding.path))
                    continue;

                missingPathCount++;
                lines.Add("  MISSING OBJECT PATH: " + (string.IsNullOrEmpty(binding.path) ? "<root>" : binding.path) +
                    " | property=" + binding.propertyName + " | type=" + binding.type);
            }
        }

        lines.Insert(0,
            "Total float curves: " + totalFloatBindings +
            " | total object-reference curves: " + totalObjectBindings +
            " | missing hierarchy paths: " + missingPathCount);

        return lines.Count == 1 ? lines[0] + "\nNo animation states to audit." : string.Join("\n", lines);
    }

    private static bool HasAnimationBindingPath(Transform root, string path)
    {
        if (root == null)
            return false;

        if (string.IsNullOrEmpty(path))
            return true;

        return root.Find(path) != null;
    }


    }

    private static string BuildAnimationEventReport(
        IEnumerable<FGOCharacterAnimationPlayer.ClipEntry> states,
        Dictionary<UnityEngine.Object, UnityEngine.Object> assetMap)
    {
        List<string> lines = new List<string>();
        foreach (FGOCharacterAnimationPlayer.ClipEntry entry in states)
        {
            if (entry == null || entry.clip == null)
                continue;

            AnimationEvent[] events = entry.clip.events;
            if (events == null || events.Length == 0)
            {
                lines.Add(entry.stateName + " / " + entry.clip.name + ": No events");
                continue;
            }

            foreach (AnimationEvent animationEvent in events)
            {
                UnityEngine.Object reference = animationEvent.objectReferenceParameter;
                string referenceStatus = reference == null
                    ? "None"
                    : (assetMap.ContainsKey(reference)
                        ? "Source has an exported copy, but event reference remapping is not yet implemented"
                        : "UNMAPPED EXTERNAL REFERENCE");

                lines.Add(
                    entry.stateName + " / " + entry.clip.name +
                    " | time=" + animationEvent.time.ToString("0.000") +
                    " | function=" + animationEvent.functionName +
                    " | string=" + animationEvent.stringParameter +
                    " | object=" + (reference != null
                        ? reference.name + " (" + reference.GetType().Name + ")"
                        : "None") +
                    " | reference status=" + referenceStatus);
            }
        }

        return lines.Count == 0 ? "No animation states to audit." : string.Join("\n", lines);
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
        out Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap,
        out List<string> componentAudit)
    {
        objectMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        componentAudit = new List<string>();
        GameObject targetRoot = new GameObject(sourceRoot.name);
        objectMap[sourceRoot] = targetRoot;
        objectMap[sourceRoot.transform] = targetRoot.transform;
        CopyTransformValues(sourceRoot.transform, targetRoot.transform);

        CopyChildrenRecursive(sourceRoot.transform, targetRoot.transform, objectMap, componentAudit);
        RemapSerializedReferences(objectMap);
        return targetRoot;
    }

    private static void CopyChildrenRecursive(
        Transform sourceParent,
        Transform targetParent,
        Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap,
        List<string> componentAudit)
    {
        foreach (Transform sourceChild in sourceParent)
        {
            GameObject targetObject = new GameObject(sourceChild.gameObject.name);
            Transform targetTransform = targetObject.transform;
            targetTransform.SetParent(targetParent, false);
            CopyTransformValues(sourceChild, targetTransform);
            objectMap[sourceChild.gameObject] = targetObject;
            objectMap[sourceChild] = targetTransform;
            CopyChildrenRecursive(sourceChild, targetTransform, objectMap, componentAudit);
        }

        GameObject sourceObject = sourceParent.gameObject;
        GameObject targetObjectForComponents = targetParent.gameObject;
        foreach (Component sourceComponent in sourceObject.GetComponents<Component>())
        {
            if (sourceComponent == null)
            {
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | MISSING SCRIPT COMPONENT");
                continue;
            }

            Type componentType = sourceComponent.GetType();
            if (componentType == typeof(Transform))
            {
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | Transform | COPIED AS HIERARCHY");
                continue;
            }
            if (componentType == typeof(SimpleAnimation))
            {
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | SimpleAnimation | INTENTIONALLY EXCLUDED (clips exported separately)");
                continue;
            }
            if (componentType.Name == "BattleServantDeadComponent")
            {
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | " + componentType.FullName + " | INTENTIONALLY EXCLUDED");
                continue;
            }

            try
            {
                Component targetComponent = targetObjectForComponents.AddComponent(componentType);
                if (targetComponent == null)
                    continue;

                EditorUtility.CopySerialized(sourceComponent, targetComponent);
                objectMap[sourceComponent] = targetComponent;
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | " + componentType.FullName + " | COPIED");
            }
            catch (Exception exception)
            {
                componentAudit.Add(GetHierarchyPath(sourceParent) + " | " + componentType.FullName +
                    " | FAILED: " + exception.Message);
                Debug.LogWarning(
                    "[FGO Export] Failed to copy component " + componentType.FullName +
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
