using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Error : MonoBehaviour
{
    public static GameObject ErrorText;
    public static GameObject CanvasContent;

    private void Awake()
    {
        ErrorText = Resources.Load("ErrorText") as GameObject;
        CanvasContent = GameObject.Find("Canvas/ErrorScrollView/Viewport/Content");
    }

    public static void Log(Color color, string message, float time = 5)
    {
        Log(message, color, time);
    }

    public static void Log(string message, Color color, float time = 5)
    {
        Debug.Log(message);
        Text text = Instantiate(ErrorText, CanvasContent.transform).GetComponent<Text>();
        text.text = message;
        text.color = color;
        Destroy(text.gameObject, CanvasContent.transform.childCount + 1 * time);
    }
}