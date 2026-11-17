#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FGOCharacterWorkbenchSetup
{
    private const string ScenePath = "Assets/Scenes/FGO_LocalRuntime.unity";

    [MenuItem("FGO Local Runtime/Create Workbench Scene")]
    public static void CreateWorkbenchScene()
    {
        if (!EditorUtility.DisplayDialog(
            "Create FGO Local Runtime Scene",
            "将创建一个新的独立工作台场景，不修改原 FateViewer 场景。请先保存当前场景中的未保存修改。",
            "创建场景", "取消"))
            return;

        string sceneFolder = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(sceneFolder))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        AnimatorOverrideController controller = FindOverrideController();
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "缺少 AnimatorOverrideController",
                "项目中没有找到 AnimatorOverrideController。请确认原 FateViewer 的 Override Controller 资源仍在 Assets 中。",
                "OK");
            return;
        }

        Material particleMaterial = FindParticleMaterial();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject globals = new GameObject("FGO_RuntimeGlobals");
        FateViewerMain main = globals.AddComponent<FateViewerMain>();
        // Awake still establishes the singleton; disabling prevents the original Atlas/UI startup routine.
        main.enabled = false;
        main.TimeScale = 1f;

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 35f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
        cameraObject.transform.position = new Vector3(0f, 1.7f, -7f);
        cameraObject.transform.rotation = Quaternion.Euler(7f, 0f, 0f);
        cameraObject.AddComponent<AudioListener>();

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

        GameObject workbenchObject = new GameObject("FGO Character Workbench");
        FGOCharacterWorkbench workbench = workbenchObject.AddComponent<FGOCharacterWorkbench>();
        workbench.animatorController = controller;
        workbench.particleMaterial = particleMaterial;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = workbenchObject;
        EditorGUIUtility.PingObject(workbenchObject);

        EditorUtility.DisplayDialog(
            "FGO Local Runtime",
            "独立工作台场景已创建：\n" + ScenePath +
            "\n\n已绑定 AnimatorOverrideController：" + controller.name +
            "\nParticle Material：" + (particleMaterial != null ? particleMaterial.name : "未找到（可后续补充）") +
            "\n\n场景不使用 FateViewer 原网页 UI；保留 FateViewerMain 的禁用计时桥接，以兼容原 FateServantContainer 动画帧逻辑。",
            "OK");
    }

    private static AnimatorOverrideController FindOverrideController()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimatorOverrideController");
        AnimatorOverrideController fallback = null;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimatorOverrideController candidate = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (candidate == null) continue;
            if (fallback == null) fallback = candidate;
            if (candidate.name.IndexOf("Override Controller", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate;
        }
        return fallback;
    }

    private static Material FindParticleMaterial()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material ParticleAdditive");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
        }
        return null;
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] oldScenes = EditorBuildSettings.scenes;
        foreach (EditorBuildSettingsScene scene in oldScenes)
            if (scene.path == scenePath) return;

        EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[oldScenes.Length + 1];
        for (int i = 0; i < oldScenes.Length; i++) updated[i] = oldScenes[i];
        updated[updated.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = updated;
    }
}
#endif
