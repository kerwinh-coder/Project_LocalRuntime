using HSVPicker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public bool KnobsVisible = false;
    public static UIController Instance;

    public GameObject LoadedCharaButton;
    public Dropdown BackgroundDropdown;
    public Dropdown BodyAnimationDropdown;
    public UIDownloadProgress DownloadProgress;
    public Transform MeshesListContent;
    public ScrollRect ServantList;
    public ScrollRect EnemiesList;
    public ScrollRect ServantCostumeList;
    public ScrollRect EnemyCostumeList;
    public Slider CutoffSlider;
    public Button ReloadTextureButton;
    public TMPro.TMP_InputField SSWidth, SSHeight, GifWidth, GifHeight;
    public AspectRatioFitter SSVisual, GifVisual;

    public GameObject CameraSettingsPanel;

    public GameObject ColorPickerPanel;
    public GameObject ColorPicker;

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
        var pref = PlayerPrefs.GetString("SSWidth");
        if (string.IsNullOrEmpty(pref)) pref = "0";
        SSWidth.text = pref;
        SSWidth.onEndEdit.AddListener((str) => PlayerPrefs.SetString("SSWidth", str));
        pref = PlayerPrefs.GetString("SSHeight");
        if (string.IsNullOrEmpty(pref)) pref = "0";
        SSHeight.text = pref;
        SSHeight.onEndEdit.AddListener((str) => PlayerPrefs.SetString("SSHeight", str));
        pref = PlayerPrefs.GetString("GifWidth");
        if (string.IsNullOrEmpty(pref)) pref = "512";
        GifWidth.text = pref;
        GifWidth.onEndEdit.AddListener((str) => PlayerPrefs.SetString("GifWidth", str));
        pref = PlayerPrefs.GetString("GifHeight");
        if (string.IsNullOrEmpty(pref)) pref = "512";
        GifHeight.text = pref;
        GifHeight.onEndEdit.AddListener((str) => PlayerPrefs.SetString("GifHeight", str));

        BackgroundDropdown.onValueChanged.AddListener((value) => {
            OnBackgroundDropdownSelected(value);
        });
        BodyAnimationDropdown.onValueChanged.AddListener((value) => {
            OnBodyAnimationDropdownSelected(value);
        });
        ColorPicker.GetComponent<ColorPicker>().onValueChanged.AddListener((color) => SetBackgroundColor(color));

        List<Dropdown> dds = new List<Dropdown>()
        {
            BodyAnimationDropdown, BackgroundDropdown
        };
        foreach(Dropdown dd in dds)
        {
            foreach(Transform t in dd.transform)
            {
                if(t.name == "LeftButton")
                {
                    t.gameObject.GetComponent<Button>().onClick.AddListener(() => { KeyboardShortcuts.Instance.DropdownPrevious(dd); });
                }
                else if (t.name == "RightButton")
                {
                    t.gameObject.GetComponent<Button>().onClick.AddListener(() => { KeyboardShortcuts.Instance.DropdownNext(dd); });
                }
            }
        }
    }

    public void OpenServantCostumePanel(BasicServant.BasicServantEntry servant)
    {
        var costumeContainer = Resources.Load("CostumeContainer") as GameObject;
        var databaseContainer = Resources.Load("DatabaseContainer") as GameObject;
        for (int i = ServantCostumeList.content.childCount-1; i >= 0; i--)
        {
            Destroy(ServantCostumeList.content.GetChild(i).gameObject);
            Resources.UnloadUnusedAssets();
        }
        ServantCostumeList.gameObject.SetActive(true);

        GameObject go = Instantiate(databaseContainer, ServantCostumeList.content);
        go.transform.Find("Button").GetComponent<Button>().onClick.AddListener(() => {
            OpenUrl("https://apps.atlasacademy.io/db/JP/servant/" + servant.collectionNo);
        });

        foreach (var cos in servant.costumes)
        {
            go = Instantiate(costumeContainer, ServantCostumeList.content);

            if (cos.costumeCollectionNo == 0)
            {
                if(string.IsNullOrEmpty(cos.shortName))
                    cos.shortName = "Ascension " + cos.id;
                var finalId = 0;
                switch (cos.id)
                {
                    case 0: finalId = 0; break;
                    case 1: finalId = 1; break;
                    case 3: finalId = 2; break;
                }

                Davinci.get().load($"{FateViewerMain.AAContent}Faces/f_{servant.id}{finalId}.png").into(go.transform.Find("Face").GetComponent<Image>()).start();
            }
            else
                Davinci.get().load($"{FateViewerMain.AAContent}Faces/f_{cos.battleCharaId}0.png").into(go.transform.Find("Face").GetComponent<Image>()).start();

            go.transform.Find("Name").GetComponent<TMPro.TextMeshProUGUI>().text = !string.IsNullOrEmpty(cos.shortName)? cos.shortName : cos.name;
            go.transform.Find("Button").GetComponent<Button>().onClick.AddListener(() => {
                StartCoroutine(FateModelBuilder.Instance.ReplaceServant(cos.battleCharaId.ToString()));
            });
        }
    }

    public void OpenEnemyCostumePanel(List<BasicServant.Costume> enemy)
    {
        var costumeContainer = Resources.Load("CostumeContainer") as GameObject;
        var databaseContainer = Resources.Load("DatabaseContainer") as GameObject;
        for (int i = EnemyCostumeList.content.childCount - 1; i >= 0; i--)
        {
            Destroy(EnemyCostumeList.content.GetChild(i).gameObject);
            Resources.UnloadUnusedAssets();
        }
        EnemyCostumeList.gameObject.SetActive(true);

        foreach (var cos in enemy)
        {
            GameObject go = Instantiate(costumeContainer, EnemyCostumeList.content);

            Davinci.get().load($"{FateViewerMain.AAContent}Enemys/{cos.battleCharaId}{cos.id}.png").into(go.transform.Find("Face").GetComponent<Image>()).start();

            go.transform.Find("Name").GetComponent<TMPro.TextMeshProUGUI>().text = !string.IsNullOrEmpty(cos.battleCharaId.ToString()) ? cos.battleCharaId.ToString() : cos.name;
            go.transform.Find("Button").GetComponent<Button>().onClick.AddListener(() => {
                StartCoroutine(FateModelBuilder.Instance.ReplaceServant(cos.battleCharaId.ToString()));
            });
        }
    }

    public void OpenUrl(string url)
    {
        Application.OpenURL(url);
    }

    public void ReactivateTextureLoad()
    {
        ReloadTextureButton.interactable = true;
    }

    public void OrderBy(string sorter)
    {
        var servantList = new List<BasicServant.BasicServantEntry>();
        switch (sorter)
        {
            case "Date":
                servantList = FateViewerMain.Instance.Servants.Entries.OrderBy(s => s.collectionNo).ToList();
                break;
            case "ID":
                servantList = FateViewerMain.Instance.Servants.Entries.OrderBy(s => s.id).ToList();
                break;
            case "Name":
                servantList = FateViewerMain.Instance.Servants.Entries.OrderBy(s => s.name).ToList();
                break;
        }
        ServantUIContainer[] servants = ServantList.content.GetComponentsInChildren<ServantUIContainer>();
        for (int i = 0; i < servantList.Count; i++)
        {
            servants.First(s => s.Servant.id == servantList[i].id).transform.SetSiblingIndex(i);
        }
    }

    public void SetBackgroundColor(Color color)
    {
        Camera.main.backgroundColor = color;
    }

    #region Dropdown Value Callbacks
    public void OnBackgroundDropdownSelected(int index)
    {
        switch (index)
        {
            case 0:
                Camera.main.clearFlags = CameraClearFlags.Skybox;
                ColorPickerPanel.SetActive(false);
                //ToggleVisible(StageDropdown.gameObject, true, false);
                break;
            case 1:
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                ColorPickerPanel.SetActive(true);
                //ToggleVisible(StageDropdown.gameObject, true, false);
                break;
            case 2:
                Camera.main.clearFlags = CameraClearFlags.Nothing;
                ColorPickerPanel.SetActive(false);
                //ToggleVisible(StageDropdown.gameObject, true, false);
                break;
            default:
                break;
        }
    }
    public void OnBodyAnimationDropdownSelected(int index)
    {
        FateViewerMain.SelectedServant.PlayAnimation(BodyAnimationDropdown.options[index].text);
    }

    #endregion

    #region Dropdown Value Setters
    public void SetBodyAnimationDropdownData(List<string> animations)
    {
        BodyAnimationDropdown.ClearOptions();
        BodyAnimationDropdown.AddOptions(new List<string>() { });
        if (animations != null)
        {
            animations.Sort();
            BodyAnimationDropdown.AddOptions(animations);
        }
    }
    #endregion

    public void ToggleHandles(bool value)
    {
        KnobsVisible = value;
    }

    public void ToggleVisibleButton(GameObject go)
    {
        ToggleVisible(go);
    }

    public void ToggleFiltering()
    {
        if (FateViewerMain.SelectedServant == null) return;
        FateViewerMain.SelectedServant.ToggleTextureFiltering();
    }

    public void FlipCharacter()
    {
        if (FateViewerMain.SelectedServant == null) return;
        FateViewerMain.SelectedServant.transform.localScale = new Vector3(1, 1, -FateViewerMain.SelectedServant.transform.localScale.z);
    }

    public void ToggleVisible(GameObject go, bool force = false, bool forceValue = false)
    {
        if (force)
        {
            go.SetActive(forceValue);
        }
        else
        {
            go.SetActive(!go.activeSelf);
        }
    }
}
