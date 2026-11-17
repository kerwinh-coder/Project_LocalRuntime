using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FateServantContainer : MonoBehaviour
{
    public GameObject BodyMain;
    public List<SimpleAnimation.State> AnimationClips = new List<SimpleAnimation.State>();
    public List<Texture2D> Textures = new List<Texture2D>();
    public List<Material> Materials = new List<Material>();
    public List<Renderer> Meshes = new List<Renderer>();
    public Animator Animator;
    public AnimatorOverrideController OverrideController;

    public float CurrentFrame = 0;
    public float CurrentFrame2 = 0;
    private AnimationClip _currentClip;
    public int ClipFrameCount = 0;
    public int ClipFrameCount2 = 0;
    private bool _animationReady;
    private bool _animationReady2;

    [Header("Garbage")]
    public List<GameObject> InstantiatedObjects = new List<GameObject>();

    private void Update()
    {
        //if (Input.GetKeyDown(KeyCode.F))
        //{
        //    StartCoroutine(CreateRuntimeMask());
        //}
        if (_animationReady)// && _animationReady2)
        {
            Animate();
        }
    }

    private void Animate()
    {
        //Debug.Log($"Frame: {CurrentFrame}/{ClipFrameCount}");
        //Debug.Log($"Frame: {CurrentFrame2}/{ClipFrameCount2}");
        Animator.Play("clip", 0, Mathf.Round(CurrentFrame) / ClipFrameCount);
        CurrentFrame += FateViewerMain.Instance.TimeScale;
        CurrentFrame = (CurrentFrame) % (ClipFrameCount + 1);

        //Animator.Play("clip2", 1, Mathf.Round(CurrentFrame2) / ClipFrameCount2);
        //CurrentFrame2 += FateViewerMain.Instance.TimeScale;
        //CurrentFrame2 = Mathf.Clamp(CurrentFrame2, 0, ClipFrameCount2);
    }

    public void PlayAnimation(string animationName)
    {
        _animationReady = false;
        AnimationClip bbb = AnimationClips.FirstOrDefault(c => c.name == animationName).clip;
        OverrideController["clip"] = bbb;
        _currentClip = OverrideController["clip"];
        CurrentFrame = 0;
        ClipFrameCount = Mathf.RoundToInt(_currentClip.length * _currentClip.frameRate);
        Debug.Log("----now playing----");
        Debug.Log(_currentClip.name);
        Debug.Log($"Framerate: {_currentClip.frameRate}");
        Debug.Log($"Length: {_currentClip.length}");
        Debug.Log($"Frame Count: {_currentClip.frameRate * _currentClip.length}({ClipFrameCount})");
        Animator?.Play("clip", 0, 0);
        _animationReady = true;
    }

    public void PlayAnimationLayer2(string animationName)
    {
        _animationReady2 = false;
        AnimationClip bbb = AnimationClips.FirstOrDefault(c => c.name == animationName).clip;
        OverrideController["clip2"] = bbb;
        _currentClip = OverrideController["clip2"];
        CurrentFrame2 = 0;
        ClipFrameCount2 = Mathf.RoundToInt(_currentClip.length * _currentClip.frameRate);
        Debug.Log("----now playing----");
        Debug.Log(_currentClip.name);
        Debug.Log($"Framerate: {_currentClip.frameRate}");
        Debug.Log($"Length: {_currentClip.length}");
        Debug.Log($"Frame Count: {_currentClip.frameRate * _currentClip.length}({ClipFrameCount2})");
        Animator?.Play("clip2", 1, 0);
        _animationReady2 = true;
    }

    #region affect things

    public void ToggleTextureFiltering()
    {
        if (Textures.Count <= 0) return;
        FilterMode filter = Textures[0].filterMode;
        foreach (var tex in Textures)
        {
            tex.filterMode = filter == FilterMode.Point ? FilterMode.Bilinear : FilterMode.Point;
        }
    }

    public void SwitchShaderToScreenshot(bool value)
    {
        foreach (var mat in Materials)
        {
            if (value)
            {
                if (mat.shader.name == "FateShader3Compatible")
                {
                    mat.shader = Shader.Find("FateScreenshotShaderCompatible");
                }
            }
            else
            {
                if (mat.shader.name == "FateScreenshotShaderCompatible")
                {
                    mat.shader = Shader.Find("FateShader3Compatible");
                }
            }
        }
    }
    #endregion

    public void DeleteModel()
    {
        _animationReady = false;

        foreach (var rend in BodyMain.GetComponentsInChildren<Renderer>())
        {
            for (int i = rend.materials.Length - 1; i >= 0; i--)
            {
                Destroy(rend.materials[i]);
            }
        }
        for (int i = Textures.Count - 1; i >= 0; i--)
        {
            DestroyImmediate(Textures[i], true);
        }
        for (int i = InstantiatedObjects.Count - 1; i >= 0; i--)
        {
            if(InstantiatedObjects[i] != null)
            {
                Destroy(InstantiatedObjects[i]);
            }
        }

        Destroy(this.gameObject, 0.5f);

        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }
}
