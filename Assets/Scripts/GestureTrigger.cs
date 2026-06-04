using UnityEngine;

/// <summary>
/// Plays a single random upper-body gesture each time the AI starts speaking.
/// The gesture plays once and returns to idle — no looping.
///
/// Uses an Int parameter only (no Trigger) to avoid Unity's known issue
/// where triggers are consumed by the first evaluated transition even when
/// other conditions don't match.
///
/// Setup:
/// 1. Attach to the avatar GameObject (same one with Animator).
/// 2. Animator and VoiceController are auto-detected if not assigned.
/// 3. Requires an Animator with:
///    - Int parameter "GestureIndex" (default value: -1)
///    - A gesture layer (index 1) named "GestureLayer" with:
///      · Avatar Mask for upper body
///      · A default state named "GestureIdle" (set as default)
///      · Gesture states connected from GestureIdle
///
/// Animator transitions:
///   GestureIdle → Gesture1: GestureIndex Equals 0  (Has Exit Time ❌, Duration 0.25)
///   GestureIdle → Gesture2: GestureIndex Equals 1  (Has Exit Time ❌, Duration 0.25)
///   GestureIdle → Gesture3: GestureIndex Equals 2  (Has Exit Time ❌, Duration 0.25)
///   Gesture1 → GestureIdle: no condition  (Has Exit Time ✅, Exit Time 1.0, Duration 0.3)
///   Gesture2 → GestureIdle: no condition  (Has Exit Time ✅, Exit Time 1.0, Duration 0.3)
///   Gesture3 → GestureIdle: no condition  (Has Exit Time ✅, Exit Time 1.0, Duration 0.3)
///
/// Each gesture animation should have Loop Time UNCHECKED (plays once).
/// </summary>
public class GestureTrigger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-detected on this GameObject if not assigned.")]
    public Animator animator;

    [Tooltip("Auto-detected via FindObjectOfType if not assigned.")]
    public CompanionVoiceController voiceController;

    [Header("Settings")]
    [Tooltip("Number of gesture states in the Animator (Gesture1, Gesture2, ...).")]
    public int gestureCount = 3;

    [Tooltip("Animator layer index for the gesture layer. 0 = Base, 1 = first added layer.")]
    public int gestureLayerIndex = 1;

    // Cached parameter hash
    private static readonly int GestureIndex = Animator.StringToHash("GestureIndex");

    // -1 means "no gesture requested" — won't match any transition
    private const int NO_GESTURE = -1;

    // Edge detection
    private bool wasSpeaking;

    // Tracks whether we're waiting for the Animator to leave GestureIdle
    private bool waitingForTransition;
    private int waitStartFrame;
    private const int MAX_WAIT_FRAMES = 10; // Safety timeout

    // Avoid picking the same gesture twice in a row
    private int lastGestureIndex = -1;

    private void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (voiceController == null)
            voiceController = FindObjectOfType<CompanionVoiceController>();

        if (animator == null)
        {
            Debug.LogError("GestureTrigger: No Animator found. Disabling.");
            enabled = false;
            return;
        }

        if (voiceController == null)
        {
            Debug.LogWarning("GestureTrigger: No CompanionVoiceController found. " +
                             "Gestures will not trigger automatically.");
        }

        // Ensure GestureIndex starts at -1
        animator.SetInteger(GestureIndex, NO_GESTURE);
    }

    private void Update()
    {
        if (animator == null) return;

        // After setting GestureIndex, wait for the Animator to actually leave GestureIdle,
        // then reset the index to -1 so it doesn't re-trigger when the gesture finishes.
        if (waitingForTransition)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(gestureLayerIndex);
            bool leftIdle = !stateInfo.IsName("GestureIdle");
            bool timedOut = (Time.frameCount - waitStartFrame) > MAX_WAIT_FRAMES;

            Debug.Log($"[Gesture] leftIdle={leftIdle}, timedOut={timedOut}, frame={Time.frameCount - waitStartFrame}");

            if (leftIdle || timedOut)
            {
                animator.SetInteger(GestureIndex, NO_GESTURE);
                waitingForTransition = false;

                if (timedOut && !leftIdle)
                {
                    Debug.LogWarning("GestureTrigger: Transition timed out — Animator did not leave GestureIdle. " +
                                     "Check Animator transition conditions.");
                }
            }
        }

        bool speaking = voiceController != null && voiceController.IsAISpeaking;

        // Only trigger once when AI starts speaking, and not while waiting for a previous gesture
        if (speaking && !wasSpeaking && !waitingForTransition)
        {
            int index = PickRandomGesture();
            animator.SetInteger(GestureIndex, index);
            waitingForTransition = true;
            waitStartFrame = Time.frameCount;
            Debug.Log($"GestureTrigger: Playing gesture {index}");
        }

        wasSpeaking = speaking;
    }

    /// <summary>
    /// Pick a random gesture index, avoiding the same one twice in a row.
    /// </summary>
    private int PickRandomGesture()
    {
        if (gestureCount <= 1)
            return 0;

        int next;
        do
        {
            next = Random.Range(0, gestureCount);
        } while (next == lastGestureIndex);

        lastGestureIndex = next;
        return next;
    }
}