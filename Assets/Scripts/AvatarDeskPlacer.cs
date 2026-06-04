using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// Places an avatar prefab on a desk/table anchor detected by MRUK,
/// with configurable position offset and automatic facing toward the user.
/// 
/// Setup:
/// 1. Attach this script to any GameObject in your scene.
/// 2. Assign your avatar prefab and OVRCameraRig reference in the Inspector.
/// 3. Make sure MRUK is in your scene and Scene Support is enabled.
/// 4. Adjust offset and scale in the Inspector to your liking.
/// </summary>
public class AvatarDeskPlacer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The avatar prefab to spawn on the desk.")]
    public GameObject avatarPrefab;

    [Tooltip("Reference to the OVRCameraRig in your scene.")]
    public OVRCameraRig cameraRig;

    [Header("Desk Placement Settings")]
    [Tooltip("Offset from the desk anchor center (local space). X = left/right, Y = up (above desk surface), Z = forward/back.")]
    public Vector3 positionOffset = new Vector3(0f, 0f, 0f);

    [Tooltip("If true, the avatar will rotate to face the user's head on spawn.")]
    public bool faceUser = true;

    [Tooltip("If true, only rotates on the Y axis (keeps avatar upright).")]
    public bool constrainToYAxis = true;

    [Header("Scale")]
    [Tooltip("Scale of the spawned avatar. Use (1,1,1) for human-sized, smaller values like (0.2, 0.2, 0.2) for miniature.")]
    public Vector3 avatarScale = Vector3.one;

    [Header("Face Mesh Settings")]
    [Tooltip("Name of the mesh object containing facial blendshapes.")]
    public string faceMeshName = "F_HeadSlot";

    [Tooltip("Name of the blendshape used for lip sync mouth opening.")]
    public string mouthBlendShapeName = "phoneme_Ah";

    private GameObject spawnedAvatar;

    private void Start()
    {
        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
    }

    private void OnSceneLoaded()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No room found.");
            return;
        }

        MRUKAnchor deskAnchor = null;

        foreach (var anchor in room.Anchors)
        {
            Debug.Log($"  Anchor: {anchor.name}, Labels: {anchor.Label}");

            if (anchor.Label.HasFlag(MRUKAnchor.SceneLabels.TABLE))
            {
                deskAnchor = anchor;
                break;
            }
        }

        if (deskAnchor == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No TABLE anchor found in room. " +
                "Make sure you've scanned your desk in Quest Space Setup.");
            return;
        }

        SpawnOnAnchor(deskAnchor);
    }

    private void SpawnOnAnchor(MRUKAnchor anchor)
    {
        Vector3 anchorPosition = anchor.transform.position;
        Vector3 spawnPosition = anchorPosition + positionOffset;

        spawnedAvatar = Instantiate(avatarPrefab, spawnPosition, Quaternion.identity);
        spawnedAvatar.transform.localScale = avatarScale;

        if (faceUser && cameraRig != null)
        {
            FaceTarget(cameraRig.centerEyeAnchor.position);
        }
        else
        {
            spawnedAvatar.transform.rotation = anchor.transform.rotation;
        }

        // Set up head tracking
        var headLook = spawnedAvatar.AddComponent<HeadLookAt>();
        headLook.Target = cameraRig.centerEyeAnchor;
        headLook.weight = 0.4f;
        Debug.Log("AvatarDeskPlacer: Head tracking enabled.");

        // Set up voice
        SetupVoice();

        Debug.Log($"AvatarDeskPlacer: Avatar spawned at {spawnPosition} on anchor '{anchor.name}'");
    }

    /// <summary>
    /// Finds the face SkinnedMeshRenderer containing blendshapes.
    /// </summary>
    private SkinnedMeshRenderer FindFaceMesh()
    {
        // 1. Try direct child
        Transform directChild = spawnedAvatar.transform.Find(faceMeshName);
        if (directChild != null)
        {
            var renderer = directChild.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) return renderer;
        }

        // 2. Recursive search by name
        var allRenderers = spawnedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in allRenderers)
        {
            if (renderer.gameObject.name == faceMeshName)
            {
                return renderer;
            }
        }

        // 3. Last resort: find any mesh that has the mouth blendshape
        foreach (var renderer in allRenderers)
        {
            if (renderer.sharedMesh != null &&
                renderer.sharedMesh.GetBlendShapeIndex(mouthBlendShapeName) != -1)
            {
                Debug.LogWarning($"AvatarDeskPlacer: '{faceMeshName}' not found by name, " +
                    $"but found '{mouthBlendShapeName}' on '{renderer.gameObject.name}'. Using that.");
                return renderer;
            }
        }

        return null;
    }

    private void SetupVoice()
    {
        var voiceController = FindObjectOfType<CompanionVoiceController>();
        if (voiceController == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No CompanionVoiceController found in scene. Voice disabled.");
            return;
        }

        // Add AudioSource to avatar for spatial audio
        var audioSource = spawnedAvatar.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 0.5f;
        audioSource.maxDistance = 10f;
        audioSource.playOnAwake = false;

        // Find the face mesh with blendshapes for lip sync
        SkinnedMeshRenderer faceMesh = FindFaceMesh();
        int mouthIndex = -1;

        if (faceMesh != null && faceMesh.sharedMesh != null)
        {
            mouthIndex = faceMesh.sharedMesh.GetBlendShapeIndex(mouthBlendShapeName);

            if (mouthIndex != -1)
            {
                Debug.Log($"AvatarDeskPlacer: Found '{mouthBlendShapeName}' on {faceMesh.gameObject.name}, index {mouthIndex}.");
            }
            else
            {
                Debug.LogError($"AvatarDeskPlacer: No '{mouthBlendShapeName}' blendshape on {faceMesh.gameObject.name}!");
                Mesh mesh = faceMesh.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    Debug.Log($"  Available blendshape [{i}]: {mesh.GetBlendShapeName(i)}");
                }
            }
        }
        else
        {
            Debug.LogError("AvatarDeskPlacer: Could not find face mesh with blendshapes! " +
                $"Searched for '{faceMeshName}'. Check your avatar hierarchy.");
        }

        voiceController.Initialize(audioSource, faceMesh, mouthIndex >= 0 ? mouthIndex : 0);

        // Auto-connect to the voice API
        if (voiceController.backend == CompanionVoiceController.VoiceBackend.ElevenLabs)
        {
            var elConnection = voiceController.elevenLabsConnection;
            if (elConnection == null)
                elConnection = FindObjectOfType<ElevenLabsConnection>();

            if (elConnection != null)
            {
                voiceController.elevenLabsConnection = elConnection;
                elConnection.Connect();
                Debug.Log("AvatarDeskPlacer: Connecting to ElevenLabs...");
            }
            else
            {
                Debug.LogWarning("AvatarDeskPlacer: No ElevenLabsConnection found. Voice disabled.");
            }
        }
        else
        {
            var apiConnection = voiceController.openAIConnection;
            if (apiConnection == null)
                apiConnection = FindObjectOfType<RealtimeAPIConnection>();

            if (apiConnection != null)
            {
                voiceController.openAIConnection = apiConnection;
                apiConnection.Connect();
                Debug.Log("AvatarDeskPlacer: Connecting to OpenAI Realtime API...");
            }
            else
            {
                Debug.LogWarning("AvatarDeskPlacer: No RealtimeAPIConnection found. Voice disabled.");
            }
        }
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        if (spawnedAvatar == null) return;

        Vector3 direction = targetPosition - spawnedAvatar.transform.position;

        if (constrainToYAxis)
        {
            direction.y = 0f;
        }

        if (direction.sqrMagnitude > 0.001f)
        {
            spawnedAvatar.transform.rotation = Quaternion.LookRotation(direction.normalized);
        }
    }

    public void UpdateFacing()
    {
        if (spawnedAvatar != null && cameraRig != null)
        {
            FaceTarget(cameraRig.centerEyeAnchor.position);
        }
    }

    public void SetScale(Vector3 newScale)
    {
        avatarScale = newScale;
        if (spawnedAvatar != null)
        {
            spawnedAvatar.transform.localScale = avatarScale;
        }
    }

    public GameObject GetSpawnedAvatar()
    {
        return spawnedAvatar;
    }

    // =========================================================================
    //  Gaze & Head Tracking Control (for experimenter remote control)
    // =========================================================================

    /// <summary>
    /// Enable or disable gaze tracking (HeadLookAt).
    /// When disabled, head returns to animation control.
    /// </summary>
    public void SetGazeTracking(bool enabled)
    {
        if (spawnedAvatar == null) return;

        // Head tracking
        var headLook = spawnedAvatar.GetComponent<HeadLookAt>();
        if (headLook != null)
        {
            headLook.SetEnabled(enabled);
        }

        Debug.Log($"AvatarDeskPlacer: Gaze tracking {(enabled ? "enabled" : "disabled")}.");
    }
}