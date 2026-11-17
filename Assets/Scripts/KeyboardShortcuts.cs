using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardShortcuts : MonoBehaviour
{
    public static KeyboardShortcuts Instance;
    public List<char> KeyStrokes = new List<char>();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(Instance);
        }
        Instance = this;
    }

    public void DropdownPrevious(Dropdown dropdown)
    {
        dropdown.value = dropdown.value - 1 < 0 ? dropdown.options.Count - 1 : dropdown.value - 1;
    }

    public void DropdownNext(Dropdown dropdown)
    {
        dropdown.value = dropdown.value + 1 >= dropdown.options.Count ? 0 : dropdown.value + 1;
    }

    private void Update()
    {
        //Time
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            FateViewerMain.Instance.ChangeAnimationFrame(-1);
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            FateViewerMain.Instance.ChangeAnimationFrame(1);
        }
    }
}
