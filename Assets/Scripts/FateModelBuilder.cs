using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class FateModelBuilder : MonoBehaviour
{
    public Material ParticleMaterial;
    public static FateModelBuilder Instance;
    public bool LoadingInProgress = false;

    public AnimatorOverrideController AnimatorController;

    private void Awake()
    {
        Instance = this;
    }

    public IEnumerator ReplaceServant(string id)
    {
        if (LoadingInProgress)
        {
            Error.Log(Color.red, "Loading already in progress!");
            yield break;
        }
        if (FateViewerMain.SelectedServant != null)
        {
            FateViewerMain.SelectedServant.DeleteModel();
        }
        yield return LoadServant(id);
    }

    public IEnumerator LoadServant(string id)
    {
        if (LoadingInProgress) yield break;
        LoadingInProgress = true;

        var Servants = FateViewerMain.Instance.Servants;
        int idNum = int.Parse(id);
        var urlBase = $"{FateViewerMain.AAContent}Servants/{idNum}";

        var costume = Servants.GetAllCostumes().FirstOrDefault(s => s.battleCharaId == idNum);
        if (costume != null)
        {
            var servant = Servants.Entries.First(s => s.costumes.Contains(costume));

            var lcButt = UIController.Instance.LoadedCharaButton;
            lcButt.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = servant.name + " - AA database";
            lcButt.GetComponentInChildren<Button>().onClick.RemoveAllListeners();
            lcButt.GetComponentInChildren<Button>().onClick.AddListener(() => UIController.Instance.OpenUrl("https://apps.atlasacademy.io/db/JP/servant/" + servant.collectionNo));
        }

        AssetBundle assetBundle = null;
        string url = $"{urlBase}/{idNum}";

        yield return FateAssetManager.DownloadBundle(url, 
            (cb) =>
            {
                assetBundle = cb;
                UIController.Instance.DownloadProgress.SetProgress($"", 1);
            },
            (progress) =>
            {
                UIController.Instance.DownloadProgress.SetProgress($"Servants/{idNum}", progress);
            }
        );

        if (assetBundle == null)
        {
            Error.Log(Color.red, "Loading assetbundle failed!");
            LoadingInProgress = false;
            yield break;
        }

        StartCoroutine(BuildModel(assetBundle, urlBase));
    }

    public void ReplaceTexture()
    {
        StartCoroutine(ReplaceTexture1());
    }

    private IEnumerator ReplaceTexture1()
    {
        if (!FateViewerMain.SelectedServant)
        {
            Error.Log(Color.red, "No servant loaded!");
            UIController.Instance.ReactivateTextureLoad();
            yield break;
        }
        var uploader = WebGLUpDownExamples._webGLUpload;
        var newMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        //var targetMaterial = Materials.FirstOrDefault(m=>m.mainTexture.name )
        uploader.UploadTexture(WebGLUpload.ImageFormat.png, 0, true, newMat);
        do
        {
            yield return new WaitForEndOfFrame();
        }
        while (!uploader.TextureReady);
        uploader.TextureReady = false;

        Debug.Log($"file {newMat.mainTexture.name} loaded");
        foreach (var mat in FateViewerMain.SelectedServant.Materials)
        {
            if (mat.mainTexture.name == newMat.mainTexture.name)
            {
                mat.mainTexture = newMat.mainTexture;
            }
        }
        Destroy(newMat);
        UIController.Instance.ReactivateTextureLoad();
    }

    public IEnumerator BuildModel(AssetBundle assetBundle, string modelUrl)
    {
        if (assetBundle == null)
        {
            Error.Log(Color.red, "Asset load fail");
            Debug.LogError("Asset load fail");
            LoadingInProgress = false;
            assetBundle.Unload(true);
            yield break;
        }

        FateServantContainer servant = new GameObject().AddComponent<FateServantContainer>();
        servant.name = assetBundle.name;

        foreach (var asset in assetBundle.LoadAllAssets())
        {
            if (asset == null) { continue; }
            switch (asset)
            {
                case GameObject go:
                    {
                        if (go.name == "chr")
                        {
                            servant.BodyMain = go;
                        }
                        break;
                    }
                case Texture2D tex:
                    {
                        servant.Textures.Add(tex);
                        break;
                    }
                case Material mat:
                    {
                        servant.Materials.Add(mat);
                        if(mat.name == "9945400")
                        {
                            mat.mainTexture.wrapMode = TextureWrapMode.Repeat;
                            mat.mainTextureOffset = new Vector2(0.2f, 0.78f);
                            mat.mainTextureScale = new Vector2(10000, 10000);
                        }
                        break;
                    }
                case TextAsset text:
                case AnimationClip anim:
                default:
                    //Error.Log(Color.yellow, asset.GetType().ToString());
                    //Debug.LogWarning(asset.GetType());
                    break;
            }
        }

        UIController.Instance.CutoffSlider?.onValueChanged.RemoveAllListeners();
        foreach (Material mat in servant.Materials)
        {
            if (mat.shader.name.Contains("Particle"))
            {
                mat.shader = Shader.Find("Particles/Standard Unlit");
                var tex = mat.mainTexture;
                var color = mat.color;
                mat.CopyPropertiesFromMaterial(ParticleMaterial);
                mat.mainTexture = tex;
                mat.color = color;
            }
            else
            {
                mat.shader = Shader.Find("FateShader3Compatible");
                if (mat.mainTexture != null)
                {
                    var texture = servant.Textures.FirstOrDefault(t => t.name == mat.mainTexture.name);
                    if (texture != null)
                    {
                        mat.SetTexture("_MainTex", texture);
                    }
                    else
                    {
                        Error.Log(Color.red, $"Texture {mat.mainTexture.name} not found");
                    }
                    var alpha = servant.Textures.FirstOrDefault(t => t.name == mat.mainTexture.name + "a" || t.name == mat.mainTexture.name + "_alpha");
                    if (alpha)
                    {
                        mat.SetTexture("_Alpha", alpha);
                    }
                }
                Material temp = mat;
                mat.SetFloat("_Cutoff", 0.91f);
                UIController.Instance.CutoffSlider?.onValueChanged.AddListener((value) => { temp.SetFloat("_Cutoff", value / 100);});
            }
        }

        if(servant.BodyMain == null)
        {
            Error.Log(Color.red, "Servant body was not loaded");
            assetBundle.Unload(true);
            LoadingInProgress = false;
            yield break;
        }

        servant.BodyMain = Instantiate(servant.BodyMain, servant.transform);
        servant.BodyMain.transform.eulerAngles = Vector3.zero;

        var root = servant.BodyMain.transform.Find("joint_all_Base");
        if (root != null)
        {
            Debug.Log("Not null");
            root.localPosition = Vector3.zero;
        }

        servant.InstantiatedObjects.Add(servant.BodyMain);
        var simpleAnimation = servant.GetComponentInChildren<SimpleAnimation>();
        simpleAnimation.enabled = false;
        servant.AnimationClips.AddRange(simpleAnimation.GetStates());

        servant.Animator = servant.BodyMain.GetComponentInChildren<Animator>();
        if (servant.Animator)
        {
            servant.OverrideController = new AnimatorOverrideController(AnimatorController);
            servant.OverrideController["clip"] = null;
            servant.Animator.speed = 0;
            servant.Animator.runtimeAnimatorController = servant.OverrideController;
            servant.Animator.Play(null);
        }

        GameObject MeshToggle = Resources.Load("MeshToggle") as GameObject;
        var meshes = servant.Meshes = servant.BodyMain.GetComponentsInChildren<Renderer>().ToList();
        for (int i = 0; i < meshes.Count; i++)
        {
            int meshId = i;
            GameObject go = Instantiate(MeshToggle, UIController.Instance.MeshesListContent);
            servant.InstantiatedObjects.Add(go);
            go.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = meshes[i].name;
            if (meshes[i].enabled)
            {
                string[] splitName = meshes[i].name.Split('_');
                if (splitName.Length >= 3)
                {
                    //Debug.LogWarning(meshes[i].name);
                    string partName = splitName[0];
                    var sameParts = meshes.Where(r => r.name.Split('_').Length >= 3).Where(r => r.name.Split('_')[0] == partName);
                    var bestPart = sameParts.OrderBy(r => r.name.Split('_')[2]).Reverse().Take(1);
                    //Debug.LogWarning(sameParts.Count());
                    foreach (var part in sameParts.Except(bestPart))
                    {
                        part.enabled = false;
                    }
                }
            }
            go.GetComponentInChildren<Toggle>().isOn = meshes[i].enabled;
            go.GetComponentInChildren<Toggle>().onValueChanged.AddListener((value) => meshes[meshId].enabled = value);
        }
        FateViewerMain.Instance.SpawnedServants.Add(servant);
        FateViewerMain.SelectedServant = servant;
        UIController.Instance.SetBodyAnimationDropdownData(servant.AnimationClips.Select(a => a.name).ToList());
        var animId = UIController.Instance.BodyAnimationDropdown.options.IndexOf(UIController.Instance.BodyAnimationDropdown.options.FirstOrDefault(a => a.text == "Default"));
        UIController.Instance.BodyAnimationDropdown.SetValueWithoutNotify(animId);
        servant.PlayAnimation("Default");
        assetBundle.Unload(false);
        CenterCamera();
        LoadingInProgress = false;
    }


    public IEnumerator BuildNP(AssetBundle assetBundle)
    {
        if (assetBundle == null)
        {
            Error.Log(Color.red, "Asset load fail");
            Debug.LogError("Asset load fail");
            LoadingInProgress = false;
            assetBundle.Unload(true);
            yield break;
        }

        FateServantContainer servant = new GameObject().AddComponent<FateServantContainer>();
        servant.name = assetBundle.name;

        foreach (var asset in assetBundle.LoadAllAssets())
        {
            if (asset == null) { continue; }
            switch (asset)
            {
                case GameObject go:
                    {
                        if (asset.name.StartsWith("chr_"))
                        {
                            servant.BodyMain = go;
                        }
                        else if (asset.name.StartsWith("model_"))
                        {
                            Instantiate(asset, servant.transform);
                        }
                        break;
                    }
                case Texture2D tex:
                    {
                        servant.Textures.Add(tex);
                        break;
                    }
                case Material mat:
                    {
                        servant.Materials.Add(mat);
                        break;
                    }
                case TextAsset tex:
                case AnimationClip anim:
                default:
                    {
                        Error.Log(Color.yellow, asset.GetType().ToString());
                        Debug.LogWarning(asset.GetType());
                        break;
                    }
            }
        }

        UIController.Instance.CutoffSlider?.onValueChanged.RemoveAllListeners();
        foreach (Material mat in servant.Materials)
        {
            if (mat.shader.name.Contains("Particle"))
            {
                mat.shader = Shader.Find("Particles/Standard Unlit");
                var tex = mat.mainTexture;
                var color = mat.color;
                mat.CopyPropertiesFromMaterial(ParticleMaterial);
                mat.mainTexture = tex;
                mat.color = color;
            }
            else
            {
                mat.shader = Shader.Find("FateShader3Compatible");
                if (mat.mainTexture != null)
                {
                    var texture = servant.Textures.FirstOrDefault(t => t.name == mat.mainTexture.name);
                    if (texture != null)
                    {
                        mat.SetTexture("_MainTex", texture);
                    }
                    else
                    {
                        Error.Log(Color.red, $"Texture {mat.mainTexture.name} not found");
                    }
                    var alpha = servant.Textures.FirstOrDefault(t => t.name == mat.mainTexture.name + "a" || t.name == mat.mainTexture.name + "_alpha");
                    if (alpha)
                    {
                        mat.SetTexture("_Alpha", alpha);
                    }
                }
                Material temp = mat;
                mat.SetFloat("_Cutoff", 0.91f);
                UIController.Instance.CutoffSlider?.onValueChanged.AddListener((value) => { temp.SetFloat("_Cutoff", value / 100); });
            }

        }

        if (servant.BodyMain == null)
        {
            Error.Log(Color.red, "Servant body was not loaded");
            assetBundle.Unload(true);
            LoadingInProgress = false;
            yield break;
        }

        servant.BodyMain = Instantiate(servant.BodyMain, servant.transform);
        servant.InstantiatedObjects.Add(servant.BodyMain);
        servant.BodyMain.SetActive(true);
        var simpleAnimation = servant.GetComponentInChildren<SimpleAnimation>();
        simpleAnimation.enabled = false;
        servant.AnimationClips.AddRange(simpleAnimation.GetStates());

        servant.Animator = servant.BodyMain.GetComponentInChildren<Animator>();
        if (servant.Animator)
        {
            servant.OverrideController = new AnimatorOverrideController(AnimatorController);
            servant.OverrideController["clip"] = null;
            servant.Animator.speed = 0;
            servant.Animator.runtimeAnimatorController = servant.OverrideController;
            servant.Animator.Play(null);
        }

        //GameObject MeshToggle = Resources.Load("MeshToggle") as GameObject;
        //var meshes = servant.Meshes = servant.BodyMain.GetComponentsInChildren<Renderer>().ToList();
        //for (int i = 0; i < meshes.Count; i++)
        //{
        //    int meshId = i;
        //    GameObject go = Instantiate(MeshToggle, UIController.Instance.MeshesListContent);
        //    servant.InstantiatedObjects.Add(go);
        //    go.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = meshes[i].name;
        //    if (meshes[i].enabled)
        //    {
        //        string[] splitName = meshes[i].name.Split('_');
        //        if (splitName.Length >= 3)
        //        {
        //            string partName = splitName[0];
        //            var sameParts = meshes.Where(r => r.name.Split('_').Length >= 3).Where(r => r.name.Split('_')[0] == partName);
        //            var bestPart = sameParts.OrderBy(r => r.name.Split('_')[2]).Reverse().Take(1);
        //            foreach (var part in sameParts.Except(bestPart))
        //            {
        //                part.enabled = false;
        //            }
        //        }
        //    }
        //    go.GetComponentInChildren<Toggle>().isOn = meshes[i].enabled;
        //    go.GetComponentInChildren<Toggle>().onValueChanged.AddListener((value) => meshes[meshId].enabled = value);
        //}

        FateViewerMain.Instance.SpawnedServants.Add(servant);
        FateViewerMain.SelectedServant = servant;
        UIController.Instance.SetBodyAnimationDropdownData(servant.AnimationClips.Select(a => a.name).ToList());
        var animId = 0;
        UIController.Instance.BodyAnimationDropdown.SetValueWithoutNotify(animId);
        servant.PlayAnimation("Default");
        assetBundle.Unload(false);
        CenterCamera();
        LoadingInProgress = false;
    }

    public void CenterCamera()
    {
        /*
        foreach (Transform t in MainBone)
        {
            AnimBounds.Encapsulate(new Bounds(t.transform.position, Vector3.one * 1.5f));
        }
        Bounds AnimBounds = CharaModel.GetComponentInChildren<SkinnedMeshRenderer>().bounds; //CharaModel.GetComponentInChildren<SkinnedMeshRenderer>().localBounds;
        foreach (var renderer in CharaModel.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            AnimBounds.Encapsulate(renderer.bounds);
        }
        Camera.main.transform.position = new Vector3(-5, AnimBounds.center.y, 0);
        Camera.main.transform.eulerAngles = new Vector3(0, 90, 0);
        var orbit = GameObject.FindObjectOfType<CameraOrbit>();
        var max = Mathf.Max(AnimBounds.size.x, AnimBounds.size.y, AnimBounds.size.z);
        orbit.camZoom = max / 2;
        orbit.camZoomMax = max > 15 ? max : 15;
        orbit.camZoomMin = 0.2f;
        */

        var orbit = GameObject.FindObjectOfType<CameraOrbit>();
        orbit.camZoom = 1.5f;
        orbit.camZoomMax = 500;
        orbit.camZoomMin = 0.2f;
        Camera.main.transform.eulerAngles = new Vector3(0, 90, 0);
        Camera.main.transform.position = new Vector3(-5, 1, 0);
    }

    //public IEnumerator CreateRuntimeMask()
    //{
    //    //GameObject activeGameObject = CharaModel;
    //    //AvatarMask avatarMask = new AvatarMask();
    //    //avatarMask.AddTransformPath(activeGameObject.transform);

    //    //var layers = OriginalController.layers;
    //    //layers[1].avatarMask = avatarMask;
    //    //OriginalController.layers = layers;
    //    //Animator.runtimeAnimatorController = OriginalController;

    //    //for (int i = 0; i < avatarMask.transformCount; i++)
    //    //{
    //    //    bool value = avatarMask.GetTransformPath(i).Contains("joint_eye") || avatarMask.GetTransformPath(i).Contains("joint_mouth");
    //    //    avatarMask.SetTransformActive(i, value);
    //    //}

    //    //OverrideController = new AnimatorOverrideController(Animator.runtimeAnimatorController);
    //    //Animator.runtimeAnimatorController = OverrideController;

    //    yield return PlayAnimation("Default");
    //    yield return PlayAnimationLayer2("Default");
    //}
}
