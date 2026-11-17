using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderDisplay : MonoBehaviour
{
    public void UpdateDisplay(float value)
    {
        this.GetComponent<Text>().text = value.ToString("F2");
    }

    public void UpdateDisplayInt(float value)
    {
        this.GetComponent<Text>().text = value.ToString();
    }
}
