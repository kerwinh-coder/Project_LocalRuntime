﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Minimal standalone FGO character workbench.
/// Reuses the original FateServantContainer / SimpleAnimation / AnimatorOverrideController
/// runtime, but does not use FateViewer's browser UI or FateModelBuilder's UI-bound BuildModel.
/// Original AssetBundles are loaded unchanged from disk or the Atlas static CDN.
/// </summary>
public sealed class FGOCharacterWorkbench : MonoBehaviour
{
    [Serializable]
    private sealed class LocalAssetEntry
    {
        public string fileName;
        public string fullPath;
        public string relativePath;
        public long fileSize;
    }

    [Serializable]
    private sealed class LocalAssetIndex
    {
        public string sourceFolder;
        public string generatedAt;
        public List<LocalAssetEntry> entries = new List<LocalAssetEntry>();
    }

    public AnimatorOverrideController animatorController;
    public Material particleMaterial;

    [Header("Atlas source")]
    [SerializeField] private string atlasRegion = "JP";
    [SerializeField] private string assetBaseUrl = "https://static.atlasacademy.io/JP/";

    private enum SourceMode { LocalFile, Atlas }
    private SourceMode sourceMode = SourceMode.LocalFile;
    private string resourceReference = "";
    private string status = "Ready. Choose a local AssetBundle or enter an Atlas battleCharaId / URL.";
    private bool loading;
    private Vector2 animationScroll;
    private Vector2 localCatalogScroll;
    private Vector2 workbenchScroll;
    private string localRootFolder = "";
    private bool scanningLocalAssets;
    private bool cancelLocalScanRequested;
    private int scanFilesInspected;
    private int scanBundlesFound;
    private int scanDirectoriesVisited;
    private int scanReadErrors;
    private float scanStartedAt;
    private string localCatalogSearch = "";
    private string localCatalogStatus = "尚未扫描本地资源目录。";
    private readonly List<LocalAssetEntry> localBundles = new List<LocalAssetEntry>();
    private const string LocalIndexFileName = "FGO_LocalAssetIndex.json";
    private const string WorkbenchVersion = "0.3.2.4-preview1";
    private string loadedBundleName = "";
    private FateServantContainer currentServant;
    private float playbackSpeed = 1f;
    private int selectedAscension = 1;
    private readonly Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();

    private void Awake()
    {
        Application.targetFrameRate = 30;
#if UNITY_EDITOR
        localRootFolder = GetInitialDataFolder();
#endif
        LoadSavedLocalIndex();

        // Keep the Atlas downloader panel paired with this workbench.
        // The downloader must live on the same GameObject because it calls
        // GetComponent<FGOCharacterWorkbench>() to hand downloaded Bundles back.
        if (GetComponent<FGOAtlasAssetDownloader>() == null)
            gameObject.AddComponent<FGOAtlasAssetDownloader>();
    }

    private void OnGUI()
    {
        float panelWidth = Mathf.Min(430f, Screen.width - 24f);
        float panelHeight = Mathf.Max(300f, Screen.height - 24f);
        GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, panelHeight), "FGO Character Workbench", GUI.skin.window);
        workbenchScroll = GUILayout.BeginScrollView(workbenchScroll, GUILayout.Width(panelWidth - 12f), GUILayout.Height(panelHeight - 30f));
        GUILayout.Label("v" + WorkbenchVersion + " · 原始 AssetBundle · 本地 / Atlas · 原生动作播放");

        GUILayout.BeginHorizontal();
        GUI.enabled = !loading && !scanningLocalAssets;
        if (GUILayout.Toggle(sourceMode == SourceMode.LocalFile, "本地文件", GUI.skin.button))
            sourceMode = SourceMode.LocalFile;
        if (GUILayout.Toggle(sourceMode == SourceMode.Atlas, "Atlas 在线", GUI.skin.button))
            sourceMode = SourceMode.Atlas;
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (sourceMode == SourceMode.LocalFile)
        {
            GUILayout.Label("本地资源库（按 Unity Bundle 文件头识别，不依赖扩展名）：");
            GUILayout.BeginHorizontal();
            GUI.enabled = !scanningLocalAssets;
            localRootFolder = GUILayout.TextField(localRootFolder);
#if UNITY_EDITOR
            GUI.enabled = !loading && !scanningLocalAssets;
            if (GUILayout.Button("选择目录…", GUILayout.Width(94)))
            {
                string folder = EditorUtility.OpenFolderPanel("选择 FGO 本地资源根目录", GetInitialDataFolder(), "");
                if (!string.IsNullOrEmpty(folder)) localRootFolder = folder;
            }
#endif
            GUI.enabled = !loading && !scanningLocalAssets && !string.IsNullOrWhiteSpace(localRootFolder);
            if (GUILayout.Button(scanningLocalAssets ? "扫描中…" : "扫描", GUILayout.Width(72)))
                StartCoroutine(ScanLocalAssetFolderRoutine());
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(localCatalogStatus, GUI.skin.box);
            if (scanningLocalAssets)
            {
                DrawLocalScanProgress();
                GUILayout.BeginHorizontal();
                GUILayout.Label("已检查 " + scanFilesInspected + " 个文件  |  已识别 " + scanBundlesFound +
                    " 个 Bundle  |  已遍历 " + scanDirectoriesVisited + " 个目录", GUI.skin.label);
                GUI.enabled = true;
                if (GUILayout.Button("取消", GUILayout.Width(54)))
                    cancelLocalScanRequested = true;
                GUILayout.EndHorizontal();
            }

            if (localBundles.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("筛选", GUILayout.Width(38));
                localCatalogSearch = GUILayout.TextField(localCatalogSearch);
                GUILayout.EndHorizontal();
                localCatalogScroll = GUILayout.BeginScrollView(localCatalogScroll, GUILayout.MinHeight(76), GUILayout.MaxHeight(150));
                int shown = 0;
                foreach (LocalAssetEntry entry in localBundles)
                {
                    if (entry == null) continue;
                    string searchable = (entry.fileName + " " + entry.relativePath).ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(localCatalogSearch) &&
                        searchable.IndexOf(localCatalogSearch.Trim().ToLowerInvariant(), StringComparison.Ordinal) < 0)
                        continue;
                    if (shown >= 100) break;
                    GUILayout.BeginHorizontal("box");
                    GUILayout.BeginVertical();
                    GUILayout.Label(entry.fileName, GUI.skin.label);
                    GUILayout.Label(entry.relativePath + "  ·  " + FormatFileSize(entry.fileSize), GUI.skin.label);
                    GUILayout.EndVertical();
                    GUI.enabled = !loading && !scanningLocalAssets;
                    if (GUILayout.Button("选用", GUILayout.Width(54)))
                        resourceReference = entry.fullPath;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    shown++;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Label("当前 Bundle 文件路径：");
            GUILayout.BeginHorizontal();
            resourceReference = GUILayout.TextField(resourceReference);
#if UNITY_EDITOR
            GUI.enabled = !loading && !scanningLocalAssets;
            if (GUILayout.Button("浏览文件…", GUILayout.Width(94)))
            {
                string path = EditorUtility.OpenFilePanel("选择 FGO 原始 AssetBundle", GetInitialDataFolder(), "");
                if (!string.IsNullOrEmpty(path)) resourceReference = path;
            }
            GUI.enabled = true;
#endif
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("输入 battleCharaId（如 100100 / 100130），或完整 AssetBundle URL：");
            resourceReference = GUILayout.TextField(resourceReference);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Region", GUILayout.Width(52));
            int regionIndex = atlasRegion == "NA" ? 1 : 0;
            regionIndex = GUILayout.Toolbar(regionIndex, new[] { "JP", "NA" });
            atlasRegion = regionIndex == 1 ? "NA" : "JP";
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("资源根地址", GUILayout.Width(76));
            assetBaseUrl = GUILayout.TextField(assetBaseUrl);
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUI.enabled = !loading && !scanningLocalAssets && !string.IsNullOrWhiteSpace(resourceReference);
        if (GUILayout.Button(currentServant == null ? "加载角色" : "加载 / 替换角色或灵衣", GUILayout.Height(30)))
            StartCoroutine(LoadCharacterFromSelectedSource());
        GUI.enabled = true;
        if (loading) GUILayout.Label("正在加载…");
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("状态：" + status, GUI.skin.box);

        if (currentServant != null)
        {
            GUILayout.Space(8);
            GUILayout.Label("当前角色：" + loadedBundleName);
            DrawAscensionControls();
            DrawAnimationControls();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawLocalScanProgress()
    {
        Rect bar = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
        GUI.Box(bar, GUIContent.none);
        float segmentWidth = Mathf.Max(28f, bar.width * 0.22f);
        float travel = Mathf.Max(0f, bar.width - segmentWidth - 4f);
        float phase = Mathf.PingPong((Time.realtimeSinceStartup - scanStartedAt) * 0.7f, 1f);
        Color previous = GUI.color;
        GUI.color = new Color(0.25f, 0.72f, 0.42f, 0.9f);
        GUI.DrawTexture(new Rect(bar.x + 2f + travel * phase, bar.y + 2f, segmentWidth, bar.height - 4f), Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(bar, "正在分批扫描，工作台保持响应…", GUI.skin.label);
    }

    private void DrawAscensionControls()
    {
        GUILayout.Space(6);
        GUILayout.Label("灵基切换（按原始 Renderer 名称中的 level 标记匹配）：");
        GUILayout.BeginHorizontal();
        for (int level = 1; level <= 3; level++)
        {
            GUI.enabled = !loading;
            if (GUILayout.Button("灵基 " + level + (selectedAscension == level ? " ✓" : ""), GUI.skin.button))
                ApplyAscension(level);
            GUI.enabled = true;
        }
        if (GUILayout.Button("恢复默认", GUILayout.Width(90)))
            RestoreRendererDefaults();
        GUILayout.EndHorizontal();
        GUILayout.Label("灵衣切换：选择另一个灵衣的原始 Bundle / battleCharaId，再按上方“加载 / 替换”。");
    }

    // Keep these native states in the original AnimationClips collection,
    // but omit eye-only toggles from the user-facing action selector.
    private static bool IsHiddenAnimationFromSelection(string animationName)
    {
        return string.Equals(animationName, "eye_open", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(animationName, "eye_close", StringComparison.OrdinalIgnoreCase);
    }

    private void DrawAnimationControls()
    {
        if (currentServant == null) return;
        GUILayout.Space(8);
        GUILayout.Label("动作切换（原始 SimpleAnimation 状态）：");
        GUILayout.BeginHorizontal();
        GUILayout.Label("播放速度", GUILayout.Width(70));
        playbackSpeed = GUILayout.HorizontalSlider(playbackSpeed, 0f, 3f);
        GUILayout.Label(playbackSpeed.ToString("0.00") + "x", GUILayout.Width(48));
        GUILayout.EndHorizontal();
        currentServant.PlaybackSpeed = playbackSpeed;

        animationScroll = GUILayout.BeginScrollView(animationScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(260));
        foreach (SimpleAnimation.State state in currentServant.AnimationClips)
        {
            if (state == null || state.clip == null || string.IsNullOrEmpty(state.name)) continue;
            if (IsHiddenAnimationFromSelection(state.name)) continue;

            if (GUILayout.Button(state.name + "   " + state.clip.length.ToString("0.00") + "s"))
            {
                try
                {
                    currentServant.PlayAnimation(state.name);
                    status = "正在播放原始动作：" + state.name;
                    currentServant.PlaybackSpeed = playbackSpeed;
                }
                catch (Exception ex)
                {
                    status = "动作切换失败：" + ex.Message;
                    Debug.LogException(ex, this);
                }
            }
        }
        GUILayout.EndScrollView();
    }

    /// <summary>
    /// Public bridge used by FGOAtlasAssetDownloader after an original Bundle has
    /// been downloaded to disk. Keep this method public so the downloader can hand
    /// the untouched local file back to the same character-loading pipeline.
    /// </summary>
    public void LoadLocalBundleFile(string path)
    {
        if (loading)
        {
            status = "当前角色正在加载，请等待完成后再加载下载的 Bundle。";
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = "下载的 Bundle 文件不存在：" + path;
            return;
        }

        sourceMode = SourceMode.LocalFile;
        resourceReference = path;
        StartCoroutine(LoadCharacterFromSelectedSource());
    }

    private IEnumerator LoadCharacterFromSelectedSource()
    {
        if (loading) yield break;
        loading = true;
        status = "正在读取资源…";
        AssetBundle bundle = null;

        if (sourceMode == SourceMode.LocalFile)
        {
            string path = resourceReference.Trim().Trim('"');
            if (!File.Exists(path))
            {
                status = "找不到本地文件：" + path;
                loading = false;
                yield break;
            }

            AssetBundleCreateRequest fileRequest = null;
            Exception fileLoadException = null;
            try { fileRequest = AssetBundle.LoadFromFileAsync(path); }
            catch (Exception ex) { fileLoadException = ex; }
            if (fileLoadException != null || fileRequest == null)
            {
                status = "本地 AssetBundle 加载请求创建失败：" + (fileLoadException != null ? fileLoadException.Message : "Unity 返回空请求");
                loading = false;
                yield break;
            }
            yield return fileRequest;
            bundle = fileRequest.assetBundle;
        }
        else
        {
            string url = null;
            Exception resolveException = null;
            try { url = ResolveAtlasBundleUrl(resourceReference.Trim()); }
            catch (Exception ex) { resolveException = ex; }
            if (resolveException != null)
            {
                status = resolveException.Message;
                loading = false;
                yield break;
            }
            UnityWebRequest request = UnityWebRequest.Get(url);
            status = "正在从 Atlas 获取：" + url;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                status = "远程下载失败：" + request.error;
                Debug.LogError(status + " | " + url);
                request.Dispose();
                loading = false;
                yield break;
            }

            byte[] bytes = request.downloadHandler.data;
            request.Dispose();
            AssetBundleCreateRequest memoryRequest = null;
            Exception memoryLoadException = null;
            try { memoryRequest = AssetBundle.LoadFromMemoryAsync(bytes); }
            catch (Exception ex) { memoryLoadException = ex; }
            if (memoryLoadException != null || memoryRequest == null)
            {
                status = "远程数据已下载，但 AssetBundle 解码请求创建失败：" + (memoryLoadException != null ? memoryLoadException.Message : "Unity 返回空请求");
                loading = false;
                yield break;
            }
            yield return memoryRequest;
            bundle = memoryRequest.assetBundle;
        }

        if (bundle == null)
        {
            status = "Unity 无法加载该 AssetBundle。请确认选择的是原始 Bundle 文件，而不是 JSON 或导出资产。";
            loading = false;
            yield break;
        }

        FateServantContainer newServant = null;
        string newBundleName = bundle.name;
        Exception buildException = null;
        status = "Bundle 已载入，正在按 FateViewer 原生逻辑构建角色…";
        try { newServant = BuildNativeCharacter(bundle); }
        catch (Exception ex) { buildException = ex; }
        if (buildException != null || newServant == null)
        {
            if (newServant != null) Destroy(newServant.gameObject);
            bundle.Unload(false);
            status = "角色构建失败：" + (buildException != null ? buildException.Message : "没有生成角色实例");
            if (buildException != null) Debug.LogException(buildException, this);
            loading = false;
            yield break;
        }

        // Keep instantiated objects and referenced textures/materials alive.
        bundle.Unload(false);

        FateServantContainer previous = currentServant;
        currentServant = newServant;
        newServant.PlaybackSpeed = playbackSpeed;
        loadedBundleName = newBundleName;
        CaptureRendererDefaults();
        selectedAscension = 0;
        ApplyAscension(1);

        if (previous != null)
        {
            if (previous.OverrideController != null)
                Destroy(previous.OverrideController);
            previous.DeleteModel();
        }

        FrameCameraToCharacter(newServant);
        status = "角色加载成功。原始动画状态：" + newServant.AnimationClips.Count + "；可以切换灵基和动作。";
        loading = false;
    }

    private FateServantContainer BuildNativeCharacter(AssetBundle bundle)
    {
        if (animatorController == null)
            throw new InvalidOperationException("Workbench 尚未绑定原始 AnimatorOverrideController。请运行 Setup Scene 菜单重新生成场景。");

        FateServantContainer servant = new GameObject("FGO_" + bundle.name).AddComponent<FateServantContainer>();
        servant.name = bundle.name;
        try
        {
            GameObject sourceBody = null;
            UnityEngine.Object[] assets = bundle.LoadAllAssets();
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null) continue;
                GameObject go = asset as GameObject;
                if (go != null && string.Equals(go.name, "chr", StringComparison.OrdinalIgnoreCase))
                {
                    sourceBody = go;
                    continue;
                }
                Texture2D texture = asset as Texture2D;
                if (texture != null)
                {
                    if (!servant.Textures.Contains(texture)) servant.Textures.Add(texture);
                    continue;
                }
                Material material = asset as Material;
                if (material != null && !servant.Materials.Contains(material))
                    servant.Materials.Add(material);
            }

            if (sourceBody == null)
                throw new InvalidOperationException("Bundle 内没有找到名为 chr 的角色对象。Bundle=" + bundle.name);

            ApplyNativeMaterialSetup(servant);

            servant.BodyMain = Instantiate(sourceBody, servant.transform);
            servant.BodyMain.name = sourceBody.name;
            servant.BodyMain.transform.eulerAngles = Vector3.zero;
            Transform skeletonBase = servant.BodyMain.transform.Find("joint_all_Base");
            if (skeletonBase != null)
                skeletonBase.localPosition = Vector3.zero;
            servant.InstantiatedObjects.Add(servant.BodyMain);

            SimpleAnimation simpleAnimation = servant.GetComponentInChildren<SimpleAnimation>(true);
            if (simpleAnimation == null)
                throw new InvalidOperationException("chr 层级中没有 SimpleAnimation，无法读取原始动画状态。");
            servant.AnimationClips.AddRange(simpleAnimation.GetStates());
            simpleAnimation.enabled = false;

            servant.Animator = servant.BodyMain.GetComponentInChildren<Animator>(true);
            if (servant.Animator == null)
                throw new InvalidOperationException("chr 层级中没有 Animator。");

            servant.OverrideController = new AnimatorOverrideController(animatorController);
            servant.OverrideController["clip"] = null;
            servant.Animator.speed = 0f;
            servant.Animator.applyRootMotion = false;
            servant.Animator.runtimeAnimatorController = servant.OverrideController;
            servant.Animator.Play("clip", 0, 0f);

            servant.Meshes.AddRange(servant.BodyMain.GetComponentsInChildren<Renderer>(true));
            if (servant.AnimationClips.Count == 0)
                throw new InvalidOperationException("SimpleAnimation 没有返回任何原始动画状态。");

            string defaultState = FindDefaultAnimation(servant.AnimationClips);
            servant.PlayAnimation(defaultState);
            return servant;
        }
        catch
        {
            Destroy(servant.gameObject);
            throw;
        }
    }

    private void ApplyNativeMaterialSetup(FateServantContainer servant)
    {
        Shader characterShader = Shader.Find("FateShader3Compatible");
        Shader particleShader = Shader.Find("Particles/Standard Unlit");

        foreach (Material material in servant.Materials)
        {
            if (material == null || material.shader == null) continue;
            if (material.shader.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Particle shaders do not all expose the same color property.
                // Mobile/Particles/Additive commonly uses _TintColor rather than _Color,
                // so Material.color would emit an error. Read only a property that exists.
                Texture oldTexture = material.HasProperty("_MainTex")
                    ? material.GetTexture("_MainTex")
                    : material.mainTexture;

                Color oldColor = Color.white;
                bool hasSourceColor = false;
                if (material.HasProperty("_Color"))
                {
                    oldColor = material.GetColor("_Color");
                    hasSourceColor = true;
                }
                else if (material.HasProperty("_TintColor"))
                {
                    oldColor = material.GetColor("_TintColor");
                    hasSourceColor = true;
                }

                if (particleMaterial != null)
                    material.CopyPropertiesFromMaterial(particleMaterial);
                if (particleShader != null)
                    material.shader = particleShader;

                if (oldTexture != null && material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", oldTexture);

                // Map the source tint to whichever color property the replacement shader supports.
                if (hasSourceColor)
                {
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", oldColor);
                    else if (material.HasProperty("_TintColor"))
                        material.SetColor("_TintColor", oldColor);
                }
                continue;
            }

            // Capture source texture references before changing shaders. The replacement
            // shader may expose different defaults, so do not read these values afterward.
            Texture oldMain = material.HasProperty("_MainTex")
                ? material.GetTexture("_MainTex")
                : material.mainTexture;
            Texture oldAlpha = material.HasProperty("_Alpha")
                ? material.GetTexture("_Alpha")
                : null;

            if (characterShader != null)
                material.shader = characterShader;

            if (material.HasProperty("_MainTex") && oldMain != null)
                material.SetTexture("_MainTex", oldMain);

            if (material.HasProperty("_Alpha"))
            {
                // Keep an explicitly assigned source alpha texture when available.
                // Otherwise use FateViewer's conventional sibling texture naming.
                Texture alpha = oldAlpha;
                if (alpha == null && oldMain != null)
                {
                    alpha = servant.Textures.FirstOrDefault(t => t != null &&
                        (t.name == oldMain.name + "a" || t.name == oldMain.name + "_alpha"));
                }

                if (alpha != null)
                    material.SetTexture("_Alpha", alpha);
            }

            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", 0.91f);
        }
    }

    private static string FindDefaultAnimation(List<SimpleAnimation.State> states)
    {
        SimpleAnimation.State state = states.FirstOrDefault(s => s != null && s.clip != null &&
            string.Equals(s.name, "Default", StringComparison.OrdinalIgnoreCase));
        if (state == null)
            state = states.FirstOrDefault(s => s != null && s.clip != null &&
                string.Equals(s.name, "wait", StringComparison.OrdinalIgnoreCase));
        if (state == null)
            state = states.FirstOrDefault(s => s != null && s.clip != null);
        if (state == null)
            throw new InvalidOperationException("没有可播放的原始 AnimationClip 状态。");
        return state.name;
    }

    private void CaptureRendererDefaults()
    {
        originalRendererStates.Clear();
        if (currentServant == null) return;
        foreach (Renderer renderer in currentServant.Meshes)
            if (renderer != null) originalRendererStates[renderer] = renderer.enabled;
    }

    private void RestoreRendererDefaults()
    {
        foreach (KeyValuePair<Renderer, bool> pair in originalRendererStates)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        status = "已恢复该 Bundle 的原始 Renderer 显示状态。";
    }

    private void ApplyAscension(int level)
    {
        if (currentServant == null) return;
        int matched = 0;
        foreach (Renderer renderer in currentServant.Meshes)
        {
            if (renderer == null) continue;
            HashSet<int> levels;
            if (!TryReadLevelTags(renderer.name, out levels)) continue;
            renderer.enabled = levels.Contains(level);
            matched++;
        }
        if (matched == 0)
        {
            RestoreRendererDefaults();
            status = "该角色没有可识别的 level_1/2/3 Renderer 标记，已保留 Bundle 原始显示状态。";
            return;
        }
        selectedAscension = level;
        status = "已切换至灵基 " + level + "（按 Bundle 中的 level 标记显示 Renderer）。";
    }

    private static bool TryReadLevelTags(string rendererName, out HashSet<int> levels)
    {
        levels = new HashSet<int>();
        if (string.IsNullOrEmpty(rendererName)) return false;
        int marker = rendererName.IndexOf("_level_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;
        string suffix = rendererName.Substring(marker + "_level_".Length);
        string[] parts = suffix.Split('_');
        foreach (string part in parts)
        {
            int level;
            if (!int.TryParse(part, out level)) break;
            levels.Add(level);
        }
        return levels.Count > 0;
    }


    private void FrameCameraToCharacter(FateServantContainer servant)
    {
        if (servant == null || servant.BodyMain == null) return;
        Camera camera = Camera.main;
        if (camera == null) return;

        Renderer[] renderers = servant.BodyMain.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = new Bounds(servant.transform.position, Vector3.one);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!hasBounds) return;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.2f;
        Vector3 target = bounds.center;
        camera.transform.position = target + new Vector3(0f, radius * 0.04f, -distance);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
    }

    private IEnumerator ScanLocalAssetFolderRoutine()
    {
        if (scanningLocalAssets) yield break;
        if (string.IsNullOrWhiteSpace(localRootFolder) || !Directory.Exists(localRootFolder))
        {
            localCatalogStatus = "本地目录不存在，请重新选择 Data 或资源文件夹。";
            yield break;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(localRootFolder);
        }
        catch (Exception ex)
        {
            localCatalogStatus = "无法识别所选目录路径：" + ex.Message;
            yield break;
        }

        // Keep the previous index visible until a complete scan succeeds.
        // Directory.GetFiles(..., AllDirectories) previously blocked the Unity main
        // thread for large folders, making the entire OnGUI workbench appear frozen.
        scanningLocalAssets = true;
        cancelLocalScanRequested = false;
        scanFilesInspected = 0;
        scanBundlesFound = 0;
        scanDirectoriesVisited = 0;
        scanReadErrors = 0;
        scanStartedAt = Time.realtimeSinceStartup;
        localCatalogStatus = "正在扫描：" + localRootFolder + "（现有索引会保留到扫描完成）";

        List<LocalAssetEntry> discovered = new List<LocalAssetEntry>();
        Queue<string> pendingDirectories = new Queue<string>();
        pendingDirectories.Enqueue(normalizedRoot);
        int workSinceYield = 0;
        float lastYieldTime = Time.realtimeSinceStartup;

        while (pendingDirectories.Count > 0 && !cancelLocalScanRequested)
        {
            string currentDirectory = pendingDirectories.Dequeue();
            scanDirectoriesVisited++;

            IEnumerator<string> fileEnumerator = null;
            try
            {
                fileEnumerator = Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly).GetEnumerator();
            }
            catch (Exception ex)
            {
                scanReadErrors++;
                Debug.LogWarning("[FGO Local Index] 无法读取目录文件 " + currentDirectory + "：" + ex.Message, this);
            }

            if (fileEnumerator != null)
            {
                bool hasFile = true;
                while (hasFile && !cancelLocalScanRequested)
                {
                    string filePath = null;
                    try
                    {
                        hasFile = fileEnumerator.MoveNext();
                        if (hasFile) filePath = fileEnumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        hasFile = false;
                        scanReadErrors++;
                        Debug.LogWarning("[FGO Local Index] 文件枚举中断 " + currentDirectory + "：" + ex.Message, this);
                    }
                    if (!hasFile) break;

                    scanFilesInspected++;
                    try
                    {
                        FileInfo info = new FileInfo(filePath);
                        if (info.Exists && info.Length >= 8 && HasUnityBundleHeader(filePath))
                        {
                            string root = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string full = Path.GetFullPath(filePath);
                            string relative = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            discovered.Add(new LocalAssetEntry
                            {
                                fileName = info.Name,
                                fullPath = full,
                                relativePath = relative,
                                fileSize = info.Length
                            });
                            scanBundlesFound++;
                        }
                    }
                    catch (Exception ex)
                    {
                        scanReadErrors++;
                        Debug.LogWarning("[FGO Local Index] 跳过无法读取的文件 " + filePath + "：" + ex.Message, this);
                    }

                    workSinceYield++;
                    if (workSinceYield >= 48 || Time.realtimeSinceStartup - lastYieldTime >= 0.035f)
                    {
                        localCatalogStatus = "正在扫描本地资源… 当前已识别 " + scanBundlesFound + " 个 Bundle。";
                        workSinceYield = 0;
                        lastYieldTime = Time.realtimeSinceStartup;
                        yield return null;
                    }
                }
                try { fileEnumerator.Dispose(); } catch { }
            }

            if (cancelLocalScanRequested) break;

            IEnumerator<string> directoryEnumerator = null;
            try
            {
                directoryEnumerator = Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly).GetEnumerator();
            }
            catch (Exception ex)
            {
                scanReadErrors++;
                Debug.LogWarning("[FGO Local Index] 无法读取子目录 " + currentDirectory + "：" + ex.Message, this);
            }

            if (directoryEnumerator != null)
            {
                bool hasDirectory = true;
                while (hasDirectory && !cancelLocalScanRequested)
                {
                    string childDirectory = null;
                    try
                    {
                        hasDirectory = directoryEnumerator.MoveNext();
                        if (hasDirectory) childDirectory = directoryEnumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        hasDirectory = false;
                        scanReadErrors++;
                        Debug.LogWarning("[FGO Local Index] 子目录枚举中断 " + currentDirectory + "：" + ex.Message, this);
                    }
                    if (!hasDirectory) break;

                    try
                    {
                        FileAttributes attributes = File.GetAttributes(childDirectory);
                        // Avoid junction/symlink loops and do not leave the selected root.
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                            pendingDirectories.Enqueue(childDirectory);
                    }
                    catch (Exception ex)
                    {
                        scanReadErrors++;
                        Debug.LogWarning("[FGO Local Index] 跳过子目录 " + childDirectory + "：" + ex.Message, this);
                    }

                    workSinceYield++;
                    if (workSinceYield >= 48 || Time.realtimeSinceStartup - lastYieldTime >= 0.035f)
                    {
                        localCatalogStatus = "正在遍历目录… 当前已识别 " + scanBundlesFound + " 个 Bundle。";
                        workSinceYield = 0;
                        lastYieldTime = Time.realtimeSinceStartup;
                        yield return null;
                    }
                }
                try { directoryEnumerator.Dispose(); } catch { }
            }

            if (workSinceYield >= 12)
            {
                workSinceYield = 0;
                lastYieldTime = Time.realtimeSinceStartup;
                yield return null;
            }
        }

        if (cancelLocalScanRequested)
        {
            localCatalogStatus = "扫描已取消。保留上一次索引：" + localBundles.Count + " 个 Bundle。";
            scanningLocalAssets = false;
            yield break;
        }

        discovered.Sort((a, b) => string.Compare(a.relativePath, b.relativePath, StringComparison.OrdinalIgnoreCase));
        localBundles.Clear();
        localBundles.AddRange(discovered);
        string saveError;
        bool saved = SaveLocalAssetIndex(out saveError);
        float elapsed = Time.realtimeSinceStartup - scanStartedAt;
        localCatalogStatus = "扫描完成：检查 " + scanFilesInspected + " 个文件，识别到 " + localBundles.Count +
            " 个 Unity AssetBundle；遍历 " + scanDirectoriesVisited + " 个目录；读取异常 " + scanReadErrors +
            "；耗时 " + elapsed.ToString("0.0") + " 秒。" +
            (saved ? "索引已保存。" : "索引保存失败：" + saveError);
        status = localCatalogStatus;
        scanningLocalAssets = false;
    }

    private static bool HasUnityBundleHeader(string filePath)
    {
        try
        {
            byte[] header = new byte[12];
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = stream.Read(header, 0, header.Length);
                if (read < 7) return false;
                string magic = Encoding.ASCII.GetString(header, 0, read);
                return magic.StartsWith("UnityFS", StringComparison.Ordinal) ||
                       magic.StartsWith("UnityRaw", StringComparison.Ordinal) ||
                       magic.StartsWith("UnityWeb", StringComparison.Ordinal) ||
                       magic.StartsWith("UnityArchive", StringComparison.Ordinal);
            }
        }
        catch
        {
            return false;
        }
    }

    private bool SaveLocalAssetIndex(out string error)
    {
        error = null;
        try
        {
            LocalAssetIndex index = new LocalAssetIndex
            {
                sourceFolder = localRootFolder,
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                entries = new List<LocalAssetEntry>(localBundles)
            };
            string json = JsonUtility.ToJson(index, true);
            string path = Path.Combine(Application.persistentDataPath, LocalIndexFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.LogWarning("[FGO Local Index] 保存索引失败：" + ex.Message, this);
            return false;
        }
    }

    private void LoadSavedLocalIndex()
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, LocalIndexFileName);
            if (!File.Exists(path)) return;
            LocalAssetIndex index = JsonUtility.FromJson<LocalAssetIndex>(File.ReadAllText(path));
            if (index == null || index.entries == null) return;
            localBundles.Clear();
            foreach (LocalAssetEntry entry in index.entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.fullPath) && File.Exists(entry.fullPath))
                    localBundles.Add(entry);
            }
            if (!string.IsNullOrEmpty(index.sourceFolder)) localRootFolder = index.sourceFolder;
            localCatalogStatus = "已载入上次本地索引：" + localBundles.Count + " 个 Bundle（" + index.generatedAt + "）。如资源有变化，请重新扫描。";
        }
        catch (Exception ex)
        {
            localCatalogStatus = "读取本地索引失败：" + ex.Message;
            Debug.LogWarning(localCatalogStatus, this);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        if (bytes >= 1024L)
            return (bytes / 1024f).ToString("0.0") + " KB";
        return bytes + " B";
    }

    private string ResolveAtlasBundleUrl(string input)
    {
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input;

        int battleCharaId;
        if (!int.TryParse(input, out battleCharaId) || battleCharaId <= 0)
            throw new InvalidOperationException("Atlas 模式请输入数字 battleCharaId，或完整 AssetBundle URL。");

        string baseUrl = assetBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            baseUrl.IndexOf("static.atlasacademy.io", StringComparison.OrdinalIgnoreCase) >= 0)
            baseUrl = "https://static.atlasacademy.io/" + atlasRegion + "/";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return baseUrl + "Servants/" + battleCharaId + "/" + battleCharaId;
    }

    private static string GetInitialDataFolder()
    {
#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string dataFolder = Path.Combine(projectRoot, "..", "Data");
        if (Directory.Exists(dataFolder)) return dataFolder;
        string projectData = Path.Combine(projectRoot, "Data");
        if (Directory.Exists(projectData)) return projectData;
#endif
        return Application.persistentDataPath;
    }
}
