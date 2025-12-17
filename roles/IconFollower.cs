using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class IconFollower : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer iconSpriteRenderer;

    [Header("Configuration")]
    [SerializeField] private float maxDistance = 50f;
    [SerializeField] private float iconScale = 0.3f;
    [SerializeField] private float heightOffset = 1f;
    [SerializeField] private bool lockYAxis = true;
    
    // Optimization
    [Header("Optimization")]
    [SerializeField] private float distanceCheckInterval = 0.5f; // Check distance every 0.5 seconds
    [SerializeField] private float rotationUpdateInterval = 0.033f; // ~30 FPS for rotation

    private VRCPlayerApi targetPlayer;
    private VRCPlayerApi localPlayer;
    private Transform cachedTransform;
    private GameObject cachedGameObject;
    
    // Time control for optimization
    private float nextDistanceCheck = 0f;
    private float nextRotationUpdate = 0f;
    private float currentDistance = 0f;
    private bool isWithinRange = false;
    
    private bool isInitialized = false;

    void Start()
    {
        cachedTransform = transform;
        cachedGameObject = gameObject;
        localPlayer = Networking.LocalPlayer;

        if (iconSpriteRenderer == null)
            iconSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.sortingOrder = 100;
        }

        // Establish scale
        cachedTransform.localScale = Vector3.one * iconScale;
    }

    void Update()
    {
        if (!isInitialized || targetPlayer == null || !targetPlayer.IsValid())
        {
            if (isInitialized && (targetPlayer == null || !targetPlayer.IsValid()))
            {
                // Player disconnected - disable the entire GameObject
                DisableIcon();
            }
            return;
        }

        // OPTIMIZATION: Only update position if we are within range
        if (isWithinRange)
        {
            // Update position every frame (necessary for smooth tracking)
            UpdatePosition();
            
            // Update rotation less frequently
            if (Time.time >= nextRotationUpdate)
            {
                UpdateRotation();
                nextRotationUpdate = Time.time + rotationUpdateInterval;
            }
        }

        // Check distance less frequently
        if (Time.time >= nextDistanceCheck)
        {
            CheckDistanceAndToggle();
            nextDistanceCheck = Time.time + distanceCheckInterval;
        }
    }

    private void UpdatePosition()
    {
        Vector3 targetPos = GetTargetPlayerPosition();
        if (targetPos.magnitude > 0.1f)
        {
            cachedTransform.position = targetPos;
        }
    }

    private void UpdateRotation()
    {
        if (localPlayer != null && localPlayer.IsValid())
        {
            VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Quaternion playerHeadRotation = localHeadData.rotation;
            Vector3 lookDirection = cachedTransform.position - localHeadData.position;

            if (lockYAxis)
            {
                lookDirection.y = 0;
                Vector3 euler = playerHeadRotation.eulerAngles;
                euler.x = 0;
                euler.z = 0;
                cachedTransform.rotation = Quaternion.Euler(euler);
            }
            else if (lookDirection.magnitude > 0.01f)
            {
                cachedTransform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
    }

    private void CheckDistanceAndToggle()
    {
        if (localPlayer == null || !localPlayer.IsValid())
        {
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;
        }

        // Calculate distance
        currentDistance = Vector3.Distance(localPlayer.GetPosition(), GetTargetPlayerPosition());
        bool shouldBeActive = currentDistance <= maxDistance;

        // If the status changed, update
        if (shouldBeActive != isWithinRange)
        {
            isWithinRange = shouldBeActive;
            
            if (isWithinRange)
            {
                EnableIcon();
            }
            else
            {
                DisableIcon();
            }
        }
    }

    private void EnableIcon()
    {
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.enabled = true;
        }
        
        // Update position immediately upon activation
        UpdatePosition();
        UpdateRotation();
    }

    private void DisableIcon()
    {
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.enabled = false;
        }
    }

    public void SetupIcon(VRCPlayerApi player, Sprite roleSprite)
    {
        Debug.Log($"[IconFollower] SetupIcon -> player: {player.displayName}");

        targetPlayer = player;
        
        if (cachedTransform == null)
            cachedTransform = transform;
        
        if (cachedGameObject == null)
            cachedGameObject = gameObject;

        if (localPlayer == null)
            localPlayer = Networking.LocalPlayer;

        if (iconSpriteRenderer == null)
            iconSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // Configurar sprite
        if (iconSpriteRenderer != null)
        {
            if (roleSprite != null)
            {
                iconSpriteRenderer.sprite = roleSprite;
                iconSpriteRenderer.color = Color.white;

                // Auto-flip based on whether it is the local player
                iconSpriteRenderer.flipX = (targetPlayer == localPlayer);
            }
            else
            {
                // Fallback for debug
                iconSpriteRenderer.sprite = null;
                iconSpriteRenderer.color = Color.red;
            }
            
            iconSpriteRenderer.sortingOrder = 100;
        }

        // Set scale
        cachedTransform.localScale = Vector3.one * iconScale;

        // Position immediately
        if (targetPlayer != null && targetPlayer.IsValid())
        {
            Vector3 initialPos = GetTargetPlayerPosition();
            if (initialPos.magnitude > 0.1f)
            {
                cachedTransform.position = initialPos;
            }
            else
            {
                cachedTransform.position = targetPlayer.GetPosition() + (Vector3.up * (heightOffset + 1.8f));
            }

            UpdateRotation();
            Debug.Log($"[IconFollower] Initial position established in{cachedTransform.position}");
        }

        isInitialized = true;
        
        CheckDistanceAndToggle();
        
        Debug.Log($"[IconFollower] Icon activated for {player.displayName}");
    }

    private Vector3 GetTargetPlayerPosition()
    {
        if (targetPlayer == null || !targetPlayer.IsValid())
            return Vector3.zero;
            
        VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        return headData.position + (Vector3.up * heightOffset);
    }

    public void CleanupIcon()
    {
        isInitialized = false;
        targetPlayer = null;
        DisableIcon();
    }

    public bool IsActive()
    {
        return isInitialized && isWithinRange;
    }

    public VRCPlayerApi GetTargetPlayer()
    {
        return targetPlayer;
    }
    
    public float GetCurrentDistance()
    {
        return currentDistance;
    }
}