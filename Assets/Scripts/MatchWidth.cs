using UnityEngine;

public class MatchWidth : MonoBehaviour
{
    public float sceneWidth = 1;
    public float sceneHeight = 1;

    UnityEngine.UI.CanvasScaler _canvas;

    void Start()
    {
        _canvas = GameObject.FindObjectOfType<UnityEngine.UI.CanvasScaler>();
    }

    // Adjust the camera's height so the desired scene width fits in view
    // even if the screen/window size changes dynamically.
    void Update()
    {
        if ((float)Screen.width / Screen.height > (float)sceneWidth / sceneHeight)
        {
            _canvas.matchWidthOrHeight = 1;
        }
        else
        {
            _canvas.matchWidthOrHeight = 0;
        }
    }

}