using System;
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
/// Atlas metadata + original spriteModel AssetBundle downloader.
/// The API response and manifest are saved verbatim. Bundle and texture files are
/// downloaded unchanged; no unpacking, conversion or reserialization is performed.
/// Compatible with Unity 2020.3 (no external JSON package required).
/// </summary>
[DisallowMultipleComponent]
public sealed class FGOAtlasAssetDownloader : MonoBehaviour
{
    private sealed class ModelEntry
    {
        public string kind;       // ascension / costume
        public string key;        // API key, do not assume contiguous values
        public string manifestUrl;
        public string displayName;
    }

    private sealed class ManifestAsset
    {
        public string kind;       // bundle / texture
        public string url;
        public string suggestedName;
    }

    private readonly List<ModelEntry> modelEntries = new List<ModelEntry>();
    private readonly List<string> downloadedBundles = new List<string>();
    private Vector2 panelScroll;
    private Vector2 entriesScroll;
    private Vector2 downloadedScroll;
    private GUIStyle wrappedLabelStyle;
    private string region = "JP";
    private string servantId = "100100";
    private string downloadRoot = "";
    private string servantName = "";
    private string status = "输入角色 ID，获取 Atlas 资源目录。";
    private bool busy;
    private float progress;
    private string busyLabel = "";
    private string lastMetadataPath = "";
    private FGOCharacterWorkbench workbench;

    private const string ApiBase = "https://api.atlasacademy.io";
    private const string StaticBase = "https://static.atlasacademy.io";

    private void Awake()
    {
        workbench = GetComponent<FGOCharacterWorkbench>();
        downloadRoot = GetDefaultDownloadRoot();
        RefreshDownloadedBundleList();
    }

    private void OnGUI()
    {
        float width = Mathf.Min(500f, Mathf.Max(330f, Screen.width - 470f));
        float x = Screen.width >= 960f ? 454f : 12f;
        float height = Mathf.Max(300f, Screen.height - 24f);
        Rect area = new Rect(x, 12f, width, height);

        if (wrappedLabelStyle == null)
        {
            wrappedLabelStyle = new GUIStyle(GUI.skin.label);
            wrappedLabelStyle.wordWrap = true;
        }

        GUILayout.BeginArea(area, "Atlas 下载到本地", GUI.skin.window);
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, true, GUILayout.Width(width - 12f), GUILayout.Height(height - 34f));
        GUILayout.Label("获取 API 元数据 → 选择灵基 / 灵衣 → 下载原始 Bundle 与纹理");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Region", GUILayout.Width(58));
        int selectedRegion = region == "NA" ? 1 : 0;
        selectedRegion = GUILayout.Toolbar(selectedRegion, new[] { "JP", "NA" });
        region = selectedRegion == 1 ? "NA" : "JP";
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Servant ID", GUILayout.Width(76));
        servantId = GUILayout.TextField(servantId);
        GUI.enabled = !busy;
        if (GUILayout.Button("获取目录", GUILayout.Width(88)))
            StartCoroutine(FetchServantMetadata());
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("本地保存目录", GUILayout.Width(76));
        downloadRoot = GUILayout.TextField(downloadRoot);
#if UNITY_EDITOR
        GUI.enabled = !busy;
        if (GUILayout.Button("浏览…", GUILayout.Width(66)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择 Atlas 资源保存目录", downloadRoot, "");
            if (!string.IsNullOrEmpty(selected)) downloadRoot = selected;
        }
        GUI.enabled = true;
#endif
        GUILayout.EndHorizontal();

        if (busy)
        {
            GUILayout.Space(4);
            GUILayout.Label(busyLabel, GUI.skin.box);
            Rect bar = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
            GUI.Box(bar, GUIContent.none);
            Rect fill = new Rect(bar.x + 2f, bar.y + 2f, Mathf.Max(0f, (bar.width - 4f) * Mathf.Clamp01(progress)), bar.height - 4f);
            Color old = GUI.color;
            GUI.color = new Color(0.22f, 0.68f, 0.38f, 0.95f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = old;
        }

        GUILayout.Label("状态：" + status, GUI.skin.box);
        if (!string.IsNullOrEmpty(servantName))
            GUILayout.Label("角色：" + servantName + "  /  " + region + "  /  " + servantId);

        if (modelEntries.Count > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("API 返回的模型资源（按实际数据列出）：");
            entriesScroll = GUILayout.BeginScrollView(entriesScroll, false, true, GUILayout.MinHeight(110f), GUILayout.MaxHeight(280f));
            foreach (ModelEntry entry in modelEntries)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.BeginVertical();
                GUILayout.Label(entry.displayName, GUI.skin.label);
                GUILayout.Label(entry.manifestUrl, wrappedLabelStyle, GUILayout.MaxWidth(width - 112f));
                GUILayout.EndVertical();
                GUI.enabled = !busy;
                if (GUILayout.Button("下载", GUILayout.Width(62f), GUILayout.Height(34f)))
                    StartCoroutine(DownloadModelEntry(entry));
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Label("尚未获取资源目录。部分角色可能没有 spriteModel 数据。", GUI.skin.box);
        }

        GUILayout.Space(6);
        GUILayout.Label("已下载的原始角色 Bundle：");
        if (downloadedBundles.Count == 0)
            GUILayout.Label("尚无已下载 Bundle。", GUI.skin.box);
        else
        {
            downloadedScroll = GUILayout.BeginScrollView(downloadedScroll, false, true, GUILayout.MinHeight(60f), GUILayout.MaxHeight(170f));
            for (int i = downloadedBundles.Count - 1; i >= 0; i--)
            {
                string path = downloadedBundles[i];
                if (!File.Exists(path)) continue;
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(Path.GetFileName(path), wrappedLabelStyle, GUILayout.MinWidth(90f), GUILayout.MaxWidth(width - 170f));
                GUI.enabled = !busy;
                if (GUILayout.Button("加载到角色工作台", GUILayout.Width(142f)))
                {
                    if (workbench == null) workbench = GetComponent<FGOCharacterWorkbench>();
                    if (workbench != null) workbench.LoadLocalBundleFile(path);
                    else status = "找不到 FGOCharacterWorkbench 组件。";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label(path, wrappedLabelStyle, GUILayout.MaxWidth(width - 24f));
            }
            GUILayout.EndScrollView();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private IEnumerator FetchServantMetadata()
    {
        if (busy) yield break;
        int parsedId;
        if (!int.TryParse(servantId, out parsedId) || parsedId <= 0)
        {
            status = "角色 ID 必须是正整数。";
            yield break;
        }
        if (string.IsNullOrWhiteSpace(downloadRoot))
        {
            status = "请指定本地保存目录。";
            yield break;
        }

        busy = true;
        progress = 0f;
        busyLabel = "正在获取 Atlas 角色数据…";
        status = "请求 " + ApiBase + "/nice/" + region + "/servant/" + parsedId;
        string url = ApiBase + "/nice/" + region + "/servant/" + parsedId;
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 45;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            status = "Atlas API 请求失败：" + request.error + "（HTTP " + request.responseCode + "）";
            request.Dispose();
            busy = false;
            yield break;
        }

        string json = request.downloadHandler.text;
        request.Dispose();
        object root;
        try { root = MiniJson.Deserialize(json); }
        catch (Exception ex)
        {
            status = "API JSON 解析失败：" + ex.Message;
            busy = false;
            yield break;
        }

        string regionFolder = Path.Combine(downloadRoot, region, parsedId.ToString());
        try
        {
            Directory.CreateDirectory(regionFolder);
            lastMetadataPath = Path.Combine(regionFolder, "servant_" + parsedId + ".json");
            File.WriteAllText(lastMetadataPath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            status = "API 数据已取得，但保存 JSON 失败：" + ex.Message;
            busy = false;
            yield break;
        }

        modelEntries.Clear();
        servantName = GetString(GetValue(root, "name"));
        object extraAssets = GetValue(root, "extraAssets");
        object spriteModel = GetValue(extraAssets, "spriteModel");
        AddModelEntries(GetValue(spriteModel, "ascension"), "ascension");
        AddModelEntries(GetValue(spriteModel, "costume"), "costume");

        modelEntries.Sort((a, b) =>
        {
            int kindCompare = string.Compare(a.kind, b.kind, StringComparison.Ordinal);
            if (kindCompare != 0) return kindCompare;
            return CompareKeys(a.key, b.key);
        });

        string countSummary = modelEntries.Count(e => e.kind == "ascension") + " 个灵基资源 / " +
                              modelEntries.Count(e => e.kind == "costume") + " 个灵衣资源";
        status = "角色数据已保存：" + lastMetadataPath + "。找到 " + countSummary + "。";
        progress = 1f;
        busy = false;
    }

    private void AddModelEntries(object mapObject, string kind)
    {
        IDictionary<string, object> map = mapObject as IDictionary<string, object>;
        if (map == null) return;
        foreach (KeyValuePair<string, object> pair in map)
        {
            List<string> urls = new List<string>();
            CollectManifestUrls(pair.Value, urls);
            string manifest = urls.FirstOrDefault(u => u.IndexOf("manifest.json", StringComparison.OrdinalIgnoreCase) >= 0);
            if (string.IsNullOrEmpty(manifest)) continue;
            manifest = NormalizeAtlasUrl(manifest);
            modelEntries.Add(new ModelEntry
            {
                kind = kind,
                key = pair.Key,
                manifestUrl = manifest,
                displayName = (kind == "ascension" ? "灵基 " : "灵衣 ") + pair.Key
            });
        }
    }

    private static void CollectManifestUrls(object node, List<string> output)
    {
        if (node == null) return;
        string text = node as string;
        if (text != null)
        {
            if (text.IndexOf("manifest.json", StringComparison.OrdinalIgnoreCase) >= 0)
                output.Add(text);
            return;
        }
        IDictionary<string, object> dictionary = node as IDictionary<string, object>;
        if (dictionary != null)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
                CollectManifestUrls(pair.Value, output);
            return;
        }
        IList<object> list = node as IList<object>;
        if (list != null)
            foreach (object item in list) CollectManifestUrls(item, output);
    }

    private IEnumerator DownloadModelEntry(ModelEntry entry)
    {
        if (busy) yield break;
        if (entry == null || string.IsNullOrEmpty(entry.manifestUrl))
        {
            status = "该资源没有有效 manifest URL。";
            yield break;
        }

        busy = true;
        progress = 0f;
        busyLabel = "正在读取 " + entry.displayName + " 的资源清单…";
        string outputFolder = Path.Combine(downloadRoot, region, servantId, entry.kind + "_" + MakeSafeFileName(entry.key));
        try { Directory.CreateDirectory(outputFolder); }
        catch (Exception ex)
        {
            status = "无法创建本地目录：" + ex.Message;
            busy = false;
            yield break;
        }

        string manifestPath = Path.Combine(outputFolder, "manifest.json");
        string manifestJson = null;
        using (UnityWebRequest manifestRequest = UnityWebRequest.Get(entry.manifestUrl))
        {
            manifestRequest.timeout = 45;
            yield return manifestRequest.SendWebRequest();
            if (manifestRequest.result != UnityWebRequest.Result.Success)
            {
                status = "Manifest 下载失败：" + manifestRequest.error + "（HTTP " + manifestRequest.responseCode + "）";
                busy = false;
                yield break;
            }
            manifestJson = manifestRequest.downloadHandler.text;
        }

        try { File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false)); }
        catch (Exception ex)
        {
            status = "Manifest 已获取，但写入失败：" + ex.Message;
            busy = false;
            yield break;
        }

        object manifestRoot;
        try { manifestRoot = MiniJson.Deserialize(manifestJson); }
        catch (Exception ex)
        {
            status = "Manifest 已保存，但 JSON 解析失败：" + ex.Message + "。清单保留在本地供检查。";
            busy = false;
            yield break;
        }

        List<ManifestAsset> assets = new List<ManifestAsset>();
        CollectManifestAssets(manifestRoot, "", entry.manifestUrl, assets);
        DeduplicateAssets(assets);
        List<ManifestAsset> bundles = assets.Where(a => a.kind == "bundle").ToList();
        List<ManifestAsset> textures = assets.Where(a => a.kind == "texture").ToList();

        if (bundles.Count == 0)
        {
            status = "Manifest 已保存，但没有识别到 Modified Unity3D Bundle 地址。请把该 manifest.json 发给我；不会猜测资源 URL。";
            busy = false;
            yield break;
        }

        int total = bundles.Count + textures.Count;
        int completed = 0;
        List<string> savedBundlePaths = new List<string>();
        foreach (ManifestAsset asset in bundles.Concat(textures))
        {
            string fileName = MakeSafeFileName(asset.suggestedName);
            if (string.IsNullOrEmpty(fileName))
                fileName = asset.kind == "bundle" ? "servant_model.unity3d" : "texture_" + completed.ToString("00") + ".png";
            string destination = GetNonClobberPath(outputFolder, fileName);
            busyLabel = "正在下载 " + asset.kind + "：" + fileName + "（" + (completed + 1) + "/" + total + "）";
            progress = total == 0 ? 0f : (float)completed / total;

            byte[] bytes;
            using (UnityWebRequest assetRequest = UnityWebRequest.Get(asset.url))
            {
                assetRequest.timeout = 120;
                yield return assetRequest.SendWebRequest();
                if (assetRequest.result != UnityWebRequest.Result.Success)
                {
                    status = "文件下载失败：" + asset.url + "\n" + assetRequest.error + "（HTTP " + assetRequest.responseCode + "）。Manifest 和已下载文件已保留。";
                    busy = false;
                    RefreshDownloadedBundleList();
                    yield break;
                }
                bytes = assetRequest.downloadHandler.data;
            }

            if (asset.kind == "bundle" && !HasUnityBundleHeader(bytes))
            {
                status = "下载内容不是可识别的 Unity AssetBundle（文件头校验失败）：" + asset.url + "。原始响应未保存为 Bundle。";
                busy = false;
                yield break;
            }

            try { File.WriteAllBytes(destination, bytes); }
            catch (Exception ex)
            {
                status = "下载成功，但写入本地文件失败：" + ex.Message;
                busy = false;
                yield break;
            }

            if (asset.kind == "bundle") savedBundlePaths.Add(destination);
            completed++;
            progress = (float)completed / Mathf.Max(1, total);
        }

        WriteDownloadRecord(outputFolder, entry, entry.manifestUrl, manifestPath, savedBundlePaths, assets);
        RefreshDownloadedBundleList();
        status = "下载完成：" + entry.displayName + "。Bundle " + savedBundlePaths.Count + " 个，纹理 " + textures.Count + " 个。原始文件已保存到：" + outputFolder;
        busy = false;
        progress = 1f;
    }

    private static void CollectManifestAssets(object node, string inheritedKind, string manifestUrl, List<ManifestAsset> output)
    {
        if (node == null) return;
        string directText = node as string;
        if (directText != null)
        {
            if (!string.IsNullOrEmpty(inheritedKind) && LooksLikeAssetPath(directText))
                AddManifestAsset(output, inheritedKind, directText, manifestUrl);
            return;
        }

        IDictionary<string, object> dictionary = node as IDictionary<string, object>;
        if (dictionary != null)
        {
            string localKind = inheritedKind;
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                string byKey = DetectKind(pair.Key);
                if (!string.IsNullOrEmpty(byKey)) localKind = byKey;
                string valueString = pair.Value as string;
                if (valueString != null && (pair.Key.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            pair.Key.IndexOf("kind", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    string byValue = DetectKind(valueString);
                    if (!string.IsNullOrEmpty(byValue)) localKind = byValue;
                }
            }

            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                string pairKind = DetectKind(pair.Key);
                if (string.IsNullOrEmpty(pairKind)) pairKind = localKind;
                string valueString = pair.Value as string;
                if (valueString != null)
                {
                    bool isTypeField = pair.Key.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        pair.Key.IndexOf("kind", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Atlas manifests may identify a Modified Unity3D bundle by type
                    // while providing a bare relative path such as "2501700".
                    // Accept that path only when the manifest explicitly marks it as a bundle.
                    bool isTypedBundlePath = pairKind == "bundle" &&
                                             pair.Key.Equals("path", StringComparison.OrdinalIgnoreCase);

                    if (!isTypeField && (LooksLikeAssetPath(valueString) || isTypedBundlePath))
                    {
                        string inferred = InferKind(valueString, pairKind);
                        if (!string.IsNullOrEmpty(inferred))
                            AddManifestAsset(output, inferred, valueString, manifestUrl);
                    }
                }
                else
                {
                    CollectManifestAssets(pair.Value, pairKind, manifestUrl, output);
                }
            }
            return;
        }

        IList<object> list = node as IList<object>;
        if (list != null)
            foreach (object item in list) CollectManifestAssets(item, inheritedKind, manifestUrl, output);
    }

    private static string DetectKind(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string lower = text.ToLowerInvariant();
        if (lower.Contains("modified unity3d") || lower.Contains("unity3d") || lower.Contains("assetbundle")) return "bundle";
        if (lower.Contains("texture2d") || lower.Contains("texture")) return "texture";
        return "";
    }

    private static string InferKind(string value, string context)
    {
        string lower = value.ToLowerInvariant().Split('?')[0];
        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".tga")) return "texture";
        if (lower.EndsWith(".unity3d") || lower.EndsWith(".bundle") || lower.EndsWith(".assetbundle")) return "bundle";
        return context;
    }

    private static bool LooksLikeAssetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.Trim();
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        string lower = v.ToLowerInvariant().Split('?')[0];
        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".tga") ||
            lower.EndsWith(".unity3d") || lower.EndsWith(".bundle") || lower.EndsWith(".assetbundle")) return true;
        return v.Contains("/") || v.Contains("\\");
    }

    private static void AddManifestAsset(List<ManifestAsset> output, string kind, string value, string manifestUrl)
    {
        string url = NormalizeRelativeUrl(value, manifestUrl);
        if (string.IsNullOrEmpty(url)) return;
        string suggested = GetUrlFileName(url);
        output.Add(new ManifestAsset { kind = kind, url = url, suggestedName = suggested });
    }

    private static void DeduplicateAssets(List<ManifestAsset> assets)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = assets.Count - 1; i >= 0; i--)
        {
            string key = assets[i].kind + "|" + assets[i].url;
            if (!seen.Add(key)) assets.RemoveAt(i);
        }
    }

    private static string NormalizeAtlasUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string text = value.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.StartsWith("//")) return "https:" + text;
        if (text.StartsWith("/")) return StaticBase + text;
        if (text.StartsWith("JP/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("NA/", StringComparison.OrdinalIgnoreCase)) return StaticBase + "/" + text;
        return StaticBase + "/" + text;
    }

    private static string NormalizeRelativeUrl(string value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string text = value.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.StartsWith("//")) return "https:" + text;
        try { return new Uri(new Uri(baseUrl), text).ToString(); }
        catch { return NormalizeAtlasUrl(text); }
    }

    private static string GetUrlFileName(string url)
    {
        try
        {
            Uri uri = new Uri(url);
            string name = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(name)) return Uri.UnescapeDataString(name);
        }
        catch { }
        return "";
    }

    private static string GetNonClobberPath(string folder, string fileName)
    {
        string candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, stem + "_" + suffix.ToString("00") + ext);
            suffix++;
        }
        return candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string result = value;
        foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return result.Replace('/', '_').Replace('\\', '_');
    }

    private static bool HasUnityBundleHeader(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 7) return false;
        string magic = Encoding.ASCII.GetString(bytes, 0, Math.Min(12, bytes.Length));
        return magic.StartsWith("UnityFS", StringComparison.Ordinal) ||
               magic.StartsWith("UnityRaw", StringComparison.Ordinal) ||
               magic.StartsWith("UnityWeb", StringComparison.Ordinal) ||
               magic.StartsWith("UnityArchive", StringComparison.Ordinal);
    }

    private void WriteDownloadRecord(string folder, ModelEntry entry, string manifestUrl, string manifestPath,
        List<string> bundlePaths, List<ManifestAsset> assets)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FGO Atlas SpriteModel Download Record");
            sb.AppendLine("Region: " + region);
            sb.AppendLine("Servant ID: " + servantId);
            sb.AppendLine("Kind: " + entry.kind);
            sb.AppendLine("Key: " + entry.key);
            sb.AppendLine("Manifest URL: " + manifestUrl);
            sb.AppendLine("Manifest File: " + manifestPath);
            sb.AppendLine("Bundles:");
            foreach (string path in bundlePaths) sb.AppendLine("  " + path);
            sb.AppendLine("Assets discovered: " + assets.Count);
            foreach (ManifestAsset asset in assets) sb.AppendLine("  [" + asset.kind + "] " + asset.url);
            File.WriteAllText(Path.Combine(folder, "download_record.txt"), sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Debug.LogWarning("Could not write Atlas download record: " + ex.Message, this); }
    }

    private void RefreshDownloadedBundleList()
    {
        downloadedBundles.Clear();
        try
        {
            if (string.IsNullOrEmpty(downloadRoot) || !Directory.Exists(downloadRoot)) return;
            string[] files = Directory.GetFiles(downloadRoot, "*", SearchOption.AllDirectories);
            foreach (string path in files)
            {
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (info.Length >= 7 && HasUnityBundleHeader(File.ReadAllBytes(path))) downloadedBundles.Add(path);
                }
                catch { }
            }
            downloadedBundles.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Debug.LogWarning("Refresh downloaded bundle list failed: " + ex.Message, this); }
    }

    private static string GetDefaultDownloadRoot()
    {
#if UNITY_EDITOR
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string workspaceRoot = Directory.GetParent(projectRoot).FullName;
            return Path.Combine(workspaceRoot, "Data", "Cache", "Atlas");
        }
        catch { }
#endif
        return Path.Combine(Application.persistentDataPath, "FGO_Data", "Cache", "Atlas");
    }

    private static int CompareKeys(string a, string b)
    {
        int ai, bi;
        if (int.TryParse(a, out ai) && int.TryParse(b, out bi)) return ai.CompareTo(bi);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static object GetValue(object node, string key)
    {
        IDictionary<string, object> map = node as IDictionary<string, object>;
        if (map == null) return null;
        object value;
        if (map.TryGetValue(key, out value)) return value;
        foreach (KeyValuePair<string, object> pair in map)
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static string GetString(object value)
    {
        return value == null ? "" : value.ToString();
    }

    /// <summary>Small JSON parser returning dictionaries/lists/primitives.</summary>
    private static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            Parser parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.End) throw new FormatException("JSON has trailing characters at position " + parser.Position);
            return value;
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;
            public Parser(string source) { json = source; }
            public bool End { get { return index >= json.Length; } }
            public int Position { get { return index; } }
            public void SkipWhitespace() { while (!End && char.IsWhiteSpace(json[index])) index++; }

            public object ParseValue()
            {
                SkipWhitespace();
                if (End) throw new FormatException("Unexpected end of JSON.");
                char c = json[index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { ExpectLiteral("true"); return true; }
                if (c == 'f') { ExpectLiteral("false"); return false; }
                if (c == 'n') { ExpectLiteral("null"); return null; }
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                index++;
                SkipWhitespace();
                if (!End && json[index] == '}') { index++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (End || json[index] != '"') throw new FormatException("Expected object key at position " + index);
                    string key = ParseString();
                    SkipWhitespace();
                    if (End || json[index] != ':') throw new FormatException("Expected ':' at position " + index);
                    index++;
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (End) throw new FormatException("Unterminated object.");
                    char c = json[index++];
                    if (c == '}') break;
                    if (c != ',') throw new FormatException("Expected ',' or '}' at position " + (index - 1));
                }
                return result;
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                index++;
                SkipWhitespace();
                if (!End && json[index] == ']') { index++; return result; }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (End) throw new FormatException("Unterminated array.");
                    char c = json[index++];
                    if (c == ']') break;
                    if (c != ',') throw new FormatException("Expected ',' or ']' at position " + (index - 1));
                }
                return result;
            }

            private string ParseString()
            {
                index++;
                StringBuilder sb = new StringBuilder();
                while (!End)
                {
                    char c = json[index++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw new FormatException("Unterminated escape sequence.");
                    char e = json[index++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length) throw new FormatException("Incomplete unicode escape.");
                            string hex = json.Substring(index, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            index += 4;
                            break;
                        default: throw new FormatException("Invalid escape sequence: \\" + e);
                    }
                }
                throw new FormatException("Unterminated string.");
            }

            private object ParseNumber()
            {
                int start = index;
                if (!End && json[index] == '-') index++;
                while (!End && char.IsDigit(json[index])) index++;
                if (!End && json[index] == '.') { index++; while (!End && char.IsDigit(json[index])) index++; }
                if (!End && (json[index] == 'e' || json[index] == 'E'))
                {
                    index++;
                    if (!End && (json[index] == '+' || json[index] == '-')) index++;
                    while (!End && char.IsDigit(json[index])) index++;
                }
                if (start == index) throw new FormatException("Invalid value at position " + index);
                double number;
                if (!double.TryParse(json.Substring(start, index - start), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out number))
                    throw new FormatException("Invalid number at position " + start);
                return number;
            }

            private void ExpectLiteral(string literal)
            {
                if (index + literal.Length > json.Length || string.Compare(json, index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    throw new FormatException("Invalid literal at position " + index);
                index += literal.Length;
            }
        }
    }
}
