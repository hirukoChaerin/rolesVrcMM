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
    
    // Control de inicialización
    private bool isInitialized = false;
    private bool needsInitialPosition = false;
    private Sprite pendingSprite;
    
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
        if (cachedTransform != null)
            cachedTransform.localScale = Vector3.one * ICON_SCALE;
            
        // Si tenemos un sprite pendiente, aplicarlo
        if (pendingSprite != null && iconSpriteRenderer != null)
        {
            iconSpriteRenderer.sprite = pendingSprite;
            iconSpriteRenderer.color = Color.white;
            pendingSprite = null;
        }
        
        isInitialized = true;
    }
    
    void Update()
    {
        if (!IsValidTarget()) 
            return;
        
        // Si necesitamos establecer la posición inicial y ya estamos inicializados
        if (needsInitialPosition && isInitialized)
        {
            SetInitialPosition();
            needsInitialPosition = false;
        }
        
        if (Time.time < nextUpdateTime)
            return;
            
        nextUpdateTime = Time.time + UPDATE_INTERVAL;
        
        UpdatePosition();
        UpdateVisibility();
        UpdateRotation();
    }
    
    public void Initialize(VRCPlayerApi player, Sprite roleSprite)
    {
        targetPlayer = player;
        pendingSprite = roleSprite;
        
        // Si ya estamos inicializados (Start ya se ejecutó)
        if (isInitialized)
        {
            ApplySprite();
            SetInitialPosition();
        }
        else
        {
            // Marcar que necesitamos establecer la posición inicial cuando estemos listos
            needsInitialPosition = true;
        }
    }
    
    private void ApplySprite()
    {
        if (iconSpriteRenderer == null)
            iconSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.sortingOrder = 100;
            
            if (pendingSprite != null)
            {
                iconSpriteRenderer.sprite = pendingSprite;
                iconSpriteRenderer.color = Color.white;
                pendingSprite = null;
            }
            
            iconSpriteRenderer.enabled = true;
        }
    }
    
    private void SetInitialPosition()
    {
        if (cachedTransform == null)
            cachedTransform = transform;
            
        if (targetPlayer != null && targetPlayer.IsValid() && cachedTransform != null)
        {
            // Intentar obtener la posición de la cabeza
            VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 initialPosition = headData.position;
            
            // Si la posición de la cabeza es válida
            if (initialPosition.magnitude > 0.1f)
            {
                initialPosition += Vector3.up * HEIGHT_OFFSET;
            }
            else
            {
                // Usar la posición del jugador como respaldo
                initialPosition = targetPlayer.GetPosition() + Vector3.up * (HEIGHT_OFFSET + 1.8f);
            }
            
            // Solo establecer la posición si es válida
            if (initialPosition.magnitude > 0.1f)
            {
                cachedTransform.position = initialPosition;
            }
            
            // Aplicar el sprite y habilitar
            ApplySprite();
        }
    }
    
    private void UpdatePosition()
    {
        if (!IsValidTarget() || cachedTransform == null) return;
        
        VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 targetPosition = headData.position + (Vector3.up * HEIGHT_OFFSET);
        
        // Solo actualizar si la posición es válida
        if (targetPosition.magnitude > 0.1f)
        {
            cachedTransform.position = Vector3.Lerp(
                cachedTransform.position,
                targetPosition,
                smoothSpeed * Time.deltaTime
            );
        }
    }
    
    private void UpdateVisibility()
    {
        if (localPlayer == null)
            localPlayer = Networking.LocalPlayer;
            
        if (localPlayer == null || iconSpriteRenderer == null || !iconSpriteRenderer.enabled) 
            return;
        
        float distance = Vector3.Distance(localPlayer.GetPosition(), cachedTransform.position);
        bool shouldBeVisible = distance <= maxDistance;
        
        if (iconSpriteRenderer.enabled != shouldBeVisible)
            iconSpriteRenderer.enabled = shouldBeVisible;
    }
    
    private void UpdateRotation()
    {
        if (localPlayer == null)
            localPlayer = Networking.LocalPlayer;
            
        if (localPlayer == null || cachedTransform == null) return;
        
        // Billboard effect - siempre mira a la cámara
        VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 lookDirection = localHeadData.position - cachedTransform.position;
        
        if (lookDirection.magnitude > 0.01f)
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