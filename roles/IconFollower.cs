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
    
    private VRCPlayerApi targetPlayer;
    private VRCPlayerApi localPlayer;
    private Transform cachedTransform;
    private int visibilityCheckCounter = 0;
    private bool isActive = false;
    
    void Start()
    {
        cachedTransform = transform;
        localPlayer = Networking.LocalPlayer;
        
        if (iconSpriteRenderer == null)
            iconSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            
        if (iconSpriteRenderer != null)
        {
            iconSpriteRenderer.sortingOrder = 100;
            iconSpriteRenderer.enabled = false;
        }
        
        // Establecer escala fija
        cachedTransform.localScale = Vector3.one * iconScale;
    }
    
    void Update()
    {
        // Si no está activo o no hay jugador, salir
        if (!isActive || targetPlayer == null || !targetPlayer.IsValid()) 
        {
            if (isActive && targetPlayer != null && !targetPlayer.IsValid())
            {
                // El jugador ya no es válido, desactivar
                isActive = false;
                if (iconSpriteRenderer != null)
                    iconSpriteRenderer.enabled = false;
            }
            return;
        }
        
        // POSICIÓN INSTANTÁNEA - Sin ningún delay
        VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 targetPos = headData.position + (Vector3.up * heightOffset);
        
        // Solo actualizar si la posición es válida
        if (targetPos.magnitude > 0.1f)
        {
            cachedTransform.position = targetPos;
        }
        
        // ROTACIÓN INSTANTÁNEA - Billboard effect
        if (localPlayer == null)
            localPlayer = Networking.LocalPlayer;
            
        if (localPlayer != null)
        {
            VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 lookDir = localHeadData.position - cachedTransform.position;
            if (lookDir.magnitude > 0.01f)
            {
                cachedTransform.rotation = Quaternion.LookRotation(lookDir);
            }
        }
        
        // Visibilidad - Solo cada 15 frames para optimizar
        visibilityCheckCounter++;
        if (visibilityCheckCounter >= 15)
        {
            visibilityCheckCounter = 0;
            if (localPlayer != null && iconSpriteRenderer != null)
            {
                float distance = Vector3.Distance(localPlayer.GetPosition(), cachedTransform.position);
                iconSpriteRenderer.enabled = distance <= maxDistance && isActive;
            }
        }
    }
    
    public void SetupIcon(VRCPlayerApi player, Sprite roleSprite)
    {
        Debug.Log($"[IconFollower] SetupIcon llamado para {player.displayName}");
        
        targetPlayer = player;
        
        if (cachedTransform == null)
            cachedTransform = transform;
            
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
            }
            iconSpriteRenderer.sortingOrder = 100;
            iconSpriteRenderer.enabled = true;
        }
        
        // Establecer escala
        cachedTransform.localScale = Vector3.one * iconScale;
        
        // POSICIONAR INMEDIATAMENTE
        if (targetPlayer != null && targetPlayer.IsValid())
        {
            VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 initialPos = headData.position + (Vector3.up * heightOffset);
            
            // Si la posición es válida, usarla
            if (initialPos.magnitude > 0.1f)
            {
                cachedTransform.position = initialPos;
            }
            else
            {
                // Respaldo con posición del jugador
                cachedTransform.position = targetPlayer.GetPosition() + (Vector3.up * (heightOffset + 1.8f));
            }
            
            Debug.Log($"[IconFollower] Posición inicial establecida en {cachedTransform.position}");
        }
        
        // Activar el seguimiento
        isActive = true;
        
        Debug.Log($"[IconFollower] Icon activado para {player.displayName}");
    }
    
    public bool IsActive()
    {
        return isActive;
    }
    
    public VRCPlayerApi GetTargetPlayer()
    {
        return targetPlayer;
    }
}