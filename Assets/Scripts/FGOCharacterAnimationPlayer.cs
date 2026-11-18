using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Standalone animation driver for exported FGO character prefabs.
/// It preserves FateViewer's frame-stepped AnimatorOverrideController playback
/// without requiring FateServantContainer or the original AssetBundle.
/// </summary>
public sealed class FGOCharacterAnimationPlayer : MonoBehaviour
{
    [Serializable]
    public sealed class ClipEntry
    {
        public string stateName;
        public AnimationClip clip;
    }

    [SerializeField] private Animator animator;
    [SerializeField] private RuntimeAnimatorController baseController;
    [SerializeField] private AnimationClip slotClip;
    [SerializeField] private string animatorStateName = "FGO_Clip";
    [SerializeField] private string initialAnimationName = "wait";
    [SerializeField] private List<ClipEntry> animations = new List<ClipEntry>();
    [SerializeField] private bool playOnAwake = true;
    [SerializeField] private float playbackSpeed = 1f;

    private AnimatorOverrideController runtimeController;
    private AnimationClip currentClip;
    private int frameCount;
    private float currentFrame;
    private bool animationReady;

    public string CurrentAnimationName { get; private set; }
    public float PlaybackSpeed
    {
        get { return playbackSpeed; }
        set { playbackSpeed = Mathf.Max(0f, value); }
    }

    public IReadOnlyList<ClipEntry> Animations { get { return animations; } }

    /// <summary>Called by the Editor exporter when it builds the standalone prefab.</summary>
    public void Configure(
        Animator targetAnimator,
        RuntimeAnimatorController controller,
        AnimationClip animationSlot,
        List<ClipEntry> clipEntries,
        string initialAnimation)
    {
        animator = targetAnimator;
        baseController = controller;
        slotClip = animationSlot;
        animations = clipEntries ?? new List<ClipEntry>();
        initialAnimationName = initialAnimation;
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null || baseController == null || slotClip == null)
        {
            Debug.LogError("[FGO Animation] Animator, base controller, or animation slot is missing.", this);
            enabled = false;
            return;
        }

        // Use a runtime instance so changing the selected clip never edits the exported asset.
        runtimeController = new AnimatorOverrideController(baseController);
        animator.runtimeAnimatorController = runtimeController;
        animator.speed = 0f;
        animator.applyRootMotion = false;

        if (playOnAwake && !string.IsNullOrEmpty(initialAnimationName))
            PlayAnimation(initialAnimationName);
    }

    private void Update()
    {
        if (!animationReady || animator == null || currentClip == null || frameCount <= 0)
            return;

        float normalizedTime = Mathf.Clamp01(currentFrame / frameCount);
        animator.Play(animatorStateName, 0, normalizedTime);

        currentFrame += playbackSpeed;
        currentFrame %= frameCount + 1;
    }

    /// <summary>Play a named FGO SimpleAnimation state.</summary>
    public bool PlayAnimation(string animationName)
    {
        if (runtimeController == null || slotClip == null || animator == null)
        {
            Debug.LogWarning("[FGO Animation] Player has not been initialized.", this);
            return false;
        }

        ClipEntry entry = animations.FirstOrDefault(item =>
            item != null && item.clip != null &&
            string.Equals(item.stateName, animationName, StringComparison.Ordinal));

        if (entry == null)
        {
            Debug.LogWarning("[FGO Animation] Animation state not found: " + animationName, this);
            return false;
        }

        animationReady = false;
        currentClip = entry.clip;
        frameCount = Mathf.Max(1, Mathf.RoundToInt(currentClip.length * currentClip.frameRate));
        currentFrame = 0f;
        CurrentAnimationName = entry.stateName;

        runtimeController[slotClip.name] = currentClip;
        animator.Play(animatorStateName, 0, 0f);
        animationReady = true;
        return true;
    }

    public void StopAnimation()
    {
        animationReady = false;
    }

    private void OnDestroy()
    {
        if (runtimeController == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeController);
        else
            DestroyImmediate(runtimeController);
    }
}
