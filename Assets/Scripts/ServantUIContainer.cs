using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ServantUIContainer : MonoBehaviour
{
    public BasicServant.BasicServantEntry Servant;
    public string FaceUrl;
    public Image Face;
    public TMPro.TextMeshProUGUI Name;
    public TMPro.TextMeshProUGUI ID;
    public Button Button;
    bool _done;

    private void OnEnable()
    {
        if (_done) return;
        StartCoroutine(LoadIcon());
    }

    private IEnumerator LoadIcon()
    {
        yield return new WaitForEndOfFrame();
        while (!GetComponent<RectTransform>().IsVisible())
        {
            yield return new WaitForEndOfFrame();
        }
        _done = true;
        if (!string.IsNullOrEmpty(FaceUrl))
        {
            Davinci.get().load(FaceUrl).into(Face).start();
        }
    }
}
