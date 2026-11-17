using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using UnityEngine.UI;
using System.Linq;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public class FateViewerMain : MonoBehaviour
{
    public static FateViewerMain Instance;

    [Header("Online Data")]
    public string AAAPI = "https://api.atlasacademy.io/export/";
    public static string AAContent { get; set; } = "https://static.atlasacademy.io/JP/";
    public string Region = "JP";
    public string debugUrl = "this.is.a.test/website/modelViewer.html?id=303800";

    [Header("Selected Servant")]
    public List<FateServantContainer> SpawnedServants = new List<FateServantContainer>();
    public static FateServantContainer SelectedServant;

    [Header("General")]
    public string AnimationName;
    public BasicServant Servants;
    public BasicServant Enemies;

    [Header("Global")]
    public GameObject Cam;
    public float TimeScale = 1;
    public Slider TimeScaleSlider;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(Instance);
        }
        Instance = this;
    }

    private void Start()
    {
        Application.targetFrameRate = 30;
        Cam = Camera.main.gameObject;

        StartCoroutine(AtlasStart());
    }

    private IEnumerator AtlasStart()
    {
        Error.Log(Color.green, "Version: " + Application.version);
        string url = Application.absoluteURL;
        string fullId = "";
        string ascension = "1";

#if UNITY_EDITOR || PLATFORM_STANDALONE
        url = debugUrl;
#endif
        //Error.Log(Color.green, url);
        string[] parts = url.Split('?');
        foreach (var part in parts)
        {
            string partL = part.ToLower();
            if (partL.StartsWith("id="))
            {
                if (int.TryParse(partL.Split('=')[1], out int result))
                {
                    fullId = result.ToString();
                }
            }
            else if (partL.StartsWith("ascension="))
            {
                ascension = partL.Split('=')[1];
            }
            else if (partL.StartsWith("customurl="))
            {
                AAContent = part.Split('=')[1];
            }
            else if (partL.StartsWith("region="))
            {
                switch (partL.Split('=')[1])
                {
                    case "na":
                        Region = "NA"; break;
                    case "jp":
                        Region = "JP"; break;
                    default:
                        Error.Log(Color.yellow, $"Region {partL.Split('=')[1]} not available. Defaulting to JP");
                        Region = "JP";
                        break;
                }
            }
            else if (partL.StartsWith("animation="))
            {
                AnimationName = partL.Split('=')[1];
            }
            else if (partL.StartsWith("debug"))
            {
                Error.CanvasContent.SetActive(true);
                foreach (var parameter in url.Split('?'))
                {
                    if (parameter.StartsWith("http"))
                    {
                        continue;
                    }
                    else if (parameter.Split('=').Length <= 1)
                    {
                        Error.Log(Color.green, $"{parameter} is ON");
                    }
                    else
                    {
                        Error.Log(Color.green, $"{parameter.Split('=')[0]} is {parameter.Split('=')[1]}");
                    }
                }
            }
        }

        yield return LoadCharaList();
        yield return LoadEnemyList();
        //StartCoroutine(LoadStage("39510"));
        //yield break;
        if (string.IsNullOrEmpty(fullId) || fullId == "-1")
        {
            Error.Log(Color.yellow, "Loading random");
            int result = Servants.GetAllCostumes()[UnityEngine.Random.Range(0, Servants.GetAllCostumes().Count)].battleCharaId;
            StartCoroutine(FateModelBuilder.Instance.LoadServant(result.ToString()));
        }
        else
        {
            StartCoroutine(FateModelBuilder.Instance.LoadServant(fullId));
        }
    }

    public void ChangeAnimationTimeScale(float time)
    {
        TimeScale = time / 100;

        // Keep the original viewer's speed control working while containers manage
        // their own playback speed in the standalone runtime.
        foreach (var servant in GameObject.FindObjectsOfType<FateServantContainer>())
            servant.PlaybackSpeed = TimeScale;

        foreach (var np in GameObject.FindObjectsOfType<FateNPContainer>())
            np.PlaybackSpeed = TimeScale;
    }

    public void ChangeAnimationFrame(int value)
    {
        TimeScaleSlider.value = 0;
        foreach(var servant in GameObject.FindObjectsOfType<FateServantContainer>())
        {
            servant.CurrentFrame = mod(Mathf.RoundToInt(servant.CurrentFrame + value), servant.ClipFrameCount + 1);
        }
    }

    int mod(int x, int m)
    {
        return (x % m + m) % m;
    }

    public IEnumerator LoadCharaList()
    {
        string url = $"{AAAPI}JP/basic_servant_lang_en.json";
        if (Region == "NA")
        {
            url = $"{AAAPI}NA/basic_servant.json";
        }
        yield return FateAssetManager.DownloadText(url,
            (txt) =>
            {
                txt = $"{{\"Entries\": {txt} }}";
                Servants = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicServant>(txt);
                foreach (var servant in Servants.Entries)
                {
                    servant.costumes = servant.costume.Select(c => c.Value).ToList();
                    if (servant.costumes.FirstOrDefault(c => c.battleCharaId == servant.id) == null)
                        servant.costumes.Insert(0, new BasicServant.Costume()
                        {
                            battleCharaId = servant.id,
                            id = 0,
                            shortName = servant.name,
                        });
                }
                Servants.Entries = Servants.Entries.OrderBy(e => e.collectionNo).ToList();
                UIController.Instance.DownloadProgress.SetProgress("", 1);
            },
            (progress) =>
            {
                UIController.Instance.DownloadProgress.SetProgress("servant list", progress);
            }
        );

        if (Servants.Entries.Count > 0)
        {
            GameObject servantContainer = Resources.Load("ServantContainer") as GameObject;
            foreach (var servant in Servants.Entries)
            {
                var servant1 = servant;
                ServantUIContainer go = Instantiate(servantContainer, UIController.Instance.ServantList.content).GetComponent<ServantUIContainer>();
                go.Servant = servant1;
                go.FaceUrl = servant1.face;
                go.ID.text = servant1.id.ToString();
                go.Name.text = servant1.name.ToString();
                go.Button.onClick.AddListener(() => {
                    UIController.Instance.OpenServantCostumePanel(servant1);
                });
            }
        }
    }

    public IEnumerator LoadEnemyList()
    {
        string url = $"{AAAPI}JP/basic_svt_lang_en.json";
        Dictionary<string, List<BasicServant.Costume>> entries = new Dictionary<string, List<BasicServant.Costume>>();
        yield return FateAssetManager.DownloadText(url,
            (txt) =>
            {
                txt = $"{{\"Entries\": {txt} }}";
                Enemies = Newtonsoft.Json.JsonConvert.DeserializeObject<BasicServant>(txt);
                Enemies.Entries = Enemies.Entries.Where(e => e.type == "enemy").ToList();
                Enemies.Entries = Enemies.Entries.OrderBy(e => e.name).ToList();
                foreach (var enemy in Enemies.Entries)
                {
                    enemy.costumes = enemy.costume.Select(c => c.Value).ToList();
                    foreach (var costume in enemy.costumes)
                    {
                        var cos = new BasicServant.Costume()
                        {
                            name = costume.name,
                            shortName = costume.shortName,
                            id = costume.id,
                            battleCharaId = costume.battleCharaId
                        };
                        if (entries.ContainsKey(enemy.name))
                        {
                            if (!entries[enemy.name].Any(c => c.battleCharaId == costume.battleCharaId))
                                entries[enemy.name].Add(cos);
                        }
                        else
                        {
                            entries[enemy.name] = new List<BasicServant.Costume>() { cos };
                        }
                    }
                }
                UIController.Instance.DownloadProgress.SetProgress("", 1);
            },
            (progress) =>
            {
                UIController.Instance.DownloadProgress.SetProgress("enemy list", progress);
            }
        );

        if (entries.Count() > 0)
        {
            GameObject servantContainer = Resources.Load("ServantContainer") as GameObject;
            foreach (var enemy in entries)
            {
                var servant1 = enemy.Value;
                ServantUIContainer go = Instantiate(servantContainer, UIController.Instance.EnemiesList.content).GetComponent<ServantUIContainer>();
                //go.Servant = servant1[0];
                go.FaceUrl = $"https://static.atlasacademy.io/JP/Enemys/{servant1[0].battleCharaId}1.png";
                go.ID.text = servant1[0].battleCharaId.ToString();
                go.Name.text = enemy.Key;
                go.Button.onClick.AddListener(() => {
                    UIController.Instance.OpenEnemyCostumePanel(servant1);
                });
            }
        }
    }

    public void ToggleCameraMode()
    {
        if(Cam.GetComponent<CameraOrbit>().enabled == false)
        {
            Cam.GetComponent<CameraOrbit3D>().enabled = false;
            Cam.GetComponent<Camera>().orthographic = true;
            Cam.GetComponent<CameraOrbit>().enabled = true;
            FateModelBuilder.Instance.CenterCamera();
        }
        else
        {
            Cam.GetComponent<CameraOrbit>().enabled = false;
            Cam.GetComponent<Camera>().orthographic = false;
            Cam.GetComponent<CameraOrbit3D>().TargetCenter = Vector3.zero;
            Cam.GetComponent<CameraOrbit3D>().enabled = true;
        }
    }

    #region Unused/WIP
    //IEnumerator GalleryMode()
    //{
    //    foreach (var servant in Servants.Entries)
    //    {
    //        yield return FateModelBuilder.Instance.ReplaceServant(servant.id.ToString());
    //        do
    //        {
    //            yield return new WaitForEndOfFrame();
    //        }
    //        while (FateModelBuilder.Instance.LoadingInProgress);
    //        yield return new WaitForEndOfFrame();
    //        Screenshot.GrabFrame(2000, 2000);
    //        yield return new WaitForEndOfFrame();
    //    }
    //}

    //IEnumerator LoadStage(string id)
    //{
    //    AssetBundle assetBundle = AssetBundle.LoadFromMemory((Resources.Load(id) as TextAsset).bytes);
    //    List<GameObject> SpawnedGos = new List<GameObject>();
    //    foreach (string name in assetBundle.GetAllAssetNames())
    //    {
    //        //Error.Log(Color.green, name);
    //        object asset = assetBundle.LoadAsset(name);
    //        if (asset == null) { continue; }
    //        else if (asset.GetType() == typeof(GameObject))
    //        {
    //            if (name.EndsWith(".prefab"))
    //                SpawnedGos.Add(Instantiate(asset as GameObject));
    //        }
    //    }
    //    foreach (var go in SpawnedGos)
    //    {
    //        foreach (var rend in go.GetComponentsInChildren<Renderer>())
    //        {
    //            foreach (Material mat in rend.materials)
    //            {
    //                if (mat.name.ToLower().Contains("particle"))
    //                {
    //                    mat.shader = Shader.Find("Particles/Standard Unlit");
    //                    var tex = mat.mainTexture;
    //                    var color = mat.color;
    //                    mat.CopyPropertiesFromMaterial(FateModelBuilder.Instance.ParticleMaterial);
    //                    mat.mainTexture = tex;
    //                    mat.color = color;
    //                }
    //                else
    //                    mat.shader = Shader.Find("FateStgShader");
    //            }
    //        }
    //        foreach (var particles in go.GetComponentsInChildren<ParticleSystemRenderer>())
    //        {
    //            particles.sortingOrder = 1000;
    //        }
    //    }
    //    assetBundle.Unload(false);
    //    yield break;
    //}

    //public void LoadNP()
    //{
    //    var NP1 = AssetBundle.LoadFromMemory(((TextAsset)Resources.Load("NoblePhantasm/403800")).bytes);
    //    StartCoroutine(FateModelBuilder.Instance.BuildNP(NP1));
    //}

    //public static string GetDownloadFolder()
    //{
    //    string[] temp = (Application.persistentDataPath.Replace("Android", "")).Split(new string[] { "//" }, System.StringSplitOptions.None);

    //    return (temp[0] + "/Download");
    //}
    #endregion
}