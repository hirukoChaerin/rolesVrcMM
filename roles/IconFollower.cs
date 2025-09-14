using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class IconFollower : UdonSharpBehaviour
{
    [Header("Referencias")]
    [SerializeField] private SpriteRenderer iconSpriteRenderer;

    [Header("Configuración")]
    [SerializeField] private float maxDistance = 50f;
    [SerializeField] private float iconScale = 0.3f;
    [SerializeField] private float heightOffset = 1f;
    [SerializeField] private bool lockYAxis = true;
    
    // Optimización
    [Header("Optimización")]
    [SerializeField] private float distanceCheckInterval = 0.5f; // Revisar distancia cada 0.5 segundos
    [SerializeField] private float rotationUpdateInterval = 0.033f; // ~30 FPS para rotación

    private VRCPlayerApi targetPlayer;
    private VRCPlayerApi localPlayer;
    private Transform cachedTransform;
    private GameObject cachedGameObject;
    
    // Control de tiempo para optimización
    private float nextDistanceCheck = 0f;
    private float nextRotationUpdate = 0f;
    private float currentDistance = 0f;
    private bool isWithinRange = false;
    
    // Estado
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

        // Establecer escala
        cachedTransform.localScale = Vector3.one * iconScale;
    }

    void Update()
    {
        // Validación rápida
        if (!isInitialized || targetPlayer == null || !targetPlayer.IsValid())
        {
            if (isInitialized && (targetPlayer == null || !targetPlayer.IsValid()))
            {
                // Jugador desconectado - desactivar GameObject completo
                DisableIcon();
            }
            return;
        }

        // OPTIMIZACIÓN: Solo actualizar posición si estamos dentro del rango
        if (isWithinRange)
        {
            // Actualizar posición cada frame (necesario para seguimiento suave)
            UpdatePosition();
            
            // Actualizar rotación con menor frecuencia
            if (Time.time >= nextRotationUpdate)
            {
                UpdateRotation();
                nextRotationUpdate = Time.time + rotationUpdateInterval;
            }
        }

        // Revisar distancia con menor frecuencia
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

        // Calcular distancia
        currentDistance = Vector3.Distance(localPlayer.GetPosition(), GetTargetPlayerPosition());
        bool shouldBeActive = currentDistance <= maxDistance;

        // Si el estado cambió, actualizar
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
        // Activar solo el SpriteRenderer, no todo el GameObject
        // Esto permite que Update siga funcionando para detectar cuando volver a activar
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.enabled = true;
        }
        
        // Actualizar posición inmediatamente al activar
        UpdatePosition();
        UpdateRotation();
    }

    private void DisableIcon()
    {
        // Solo desactivar el renderer, no el GameObject
        // Esto es más eficiente que desactivar todo el GameObject
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.enabled = false;
        }
    }

    public void SetupIcon(VRCPlayerApi player, Sprite roleSprite)
    {
        Debug.Log($"[IconFollower] SetupIcon llamado para {player.displayName}");

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

                // Auto-flip basado en si es el jugador local
                iconSpriteRenderer.flipX = (targetPlayer == localPlayer);
            }
            else
            {
                // Fallback para debug
                iconSpriteRenderer.sprite = null;
                iconSpriteRenderer.color = Color.red;
            }
            
            iconSpriteRenderer.sortingOrder = 100;
        }

        // Establecer escala
        cachedTransform.localScale = Vector3.one * iconScale;

        // Posicionar inmediatamente
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
            Debug.Log($"[IconFollower] Posición inicial establecida en {cachedTransform.position}");
        }

        // Marcar como inicializado
        isInitialized = true;
        
        // Hacer check inicial de distancia
        CheckDistanceAndToggle();
        
        Debug.Log($"[IconFollower] Icon activado para {player.displayName}");
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