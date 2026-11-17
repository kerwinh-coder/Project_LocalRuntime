using UnityEngine;
using UnityEngine.UI;

public class UIDownloadProgress : MonoBehaviour
{
    public TMPro.TMP_Text ProgressLabel;
    public Slider ProgressSlider;

    public void SetProgress(string itemName, float progress)
    {
        if(progress == 1)
        {
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(true);
            ProgressLabel.text = "Downloading: " + itemName;
            ProgressSlider.value = progress;
        }
    }
}
