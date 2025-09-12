using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class IconFollower : UdonSharpBehaviour
{
    [Header("Referencias")]
    [SerializeField] private SpriteRenderer iconSpriteRenderer;

    [Header("Configuración")]
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private float maxDistance = 50f;

    // Configuración fija
    private const float ICON_SCALE = 0.3f;
    private const float HEIGHT_OFFSET = 1f; // 1 metro sobre la cabeza

    private VRCPlayerApi targetPlayer;
    private VRCPlayerApi localPlayer;
    private Transform cachedTransform;
    private float nextUpdateTime;
    private const float UPDATE_INTERVAL = 0.1f;

    void Start()
    {
        cachedTransform = transform;
        localPlayer = Networking.LocalPlayer;

        if (iconSpriteRenderer == null)
            iconSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.enabled = false;
            iconSpriteRenderer.sortingOrder = 100;
        }

        // Establecer escala fija
        cachedTransform.localScale = Vector3.one * ICON_SCALE;
    }

    void Update()
    {
        if (!IsValidTarget() || Time.time < nextUpdateTime)
            return;

        nextUpdateTime = Time.time + UPDATE_INTERVAL;

        UpdatePosition();
        UpdateVisibility();
        UpdateRotation();
    }

    public void Initialize(VRCPlayerApi player, Sprite roleSprite)
    {
        targetPlayer = player;

        if (iconSpriteRenderer != null && roleSprite != null)
        {
            iconSpriteRenderer.sprite = roleSprite;
            iconSpriteRenderer.color = Color.white;
            iconSpriteRenderer.enabled = true;
        }
    }

    private void UpdatePosition()
    {
        VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 targetPosition = headData.position + (Vector3.up * HEIGHT_OFFSET);

        cachedTransform.position = Vector3.Lerp(
            cachedTransform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );
    }

    private void UpdateVisibility()
    {
        if (localPlayer == null || iconSpriteRenderer == null) return;

        float distance = Vector3.Distance(localPlayer.GetPosition(), cachedTransform.position);
        iconSpriteRenderer.enabled = distance <= maxDistance;
    }

    private void UpdateRotation()
    {
        if (localPlayer == null) return;

        // Billboard effect - siempre mira a la cámara
        VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 lookDirection = localHeadData.position - cachedTransform.position;

        if (lookDirection != Vector3.zero)
            cachedTransform.rotation = Quaternion.LookRotation(lookDirection);
    }

    private bool IsValidTarget()
    {
        return targetPlayer != null && targetPlayer.IsValid();
    }

    public VRCPlayerApi GetTargetPlayer()
    {
        return targetPlayer;
    }
}