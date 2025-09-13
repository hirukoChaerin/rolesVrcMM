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
    [SerializeField] private bool billboardEffect = true; // Siempre mirar a la cámara
    [SerializeField] private bool lockYAxis = true; // Mantener vertical

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

        // BILLBOARD EFFECT - Siempre de frente al jugador local
        if (billboardEffect)
        {
            if (localPlayer == null)
                localPlayer = Networking.LocalPlayer;

            if (localPlayer != null)
            {
                // Obtener la posición de la cámara del jugador local
                VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Quaternion playerHeadRotation = localHeadData.rotation;
                // El icono debe mirar hacia la cámara
                Vector3 lookDirection = cachedTransform.position - localHeadData.position;

                // Si lockYAxis está activado, mantener el icono vertical
                if (lockYAxis)
                {
                    lookDirection.y = 0;

                    Vector3 euler = playerHeadRotation.eulerAngles;
                    euler.x = 0; // Anula la inclinación adelante/atrás
                    euler.z = 0; // Anula la inclinación lateral
                    cachedTransform.rotation = Quaternion.Euler(euler);
                }

                // Solo rotar si hay una dirección válida
                if (lookDirection.magnitude > 0.01f)
                {
                    // Rotar para mirar hacia la cámara
                    cachedTransform.rotation = Quaternion.LookRotation(lookDirection);
                }
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

        // DEBUG: Verificar sprite
        Debug.LogError($"[DEBUG] SpriteRenderer found: {iconSpriteRenderer != null}");
        Debug.LogError($"[DEBUG] RoleSprite provided: {roleSprite != null}, Name: {(roleSprite != null ? roleSprite.name : "NULL")}");

        // Configurar sprite
        if (iconSpriteRenderer != null)
        {
            if (roleSprite != null)
            {
                if (targetPlayer == localPlayer)
                {
                    iconSpriteRenderer.flipX = true;
                }
                else
                {
                    iconSpriteRenderer.flipX = false;
                }
                iconSpriteRenderer.sprite = roleSprite;
                iconSpriteRenderer.color = Color.white;
                Debug.LogError($"[DEBUG] Sprite assigned: {iconSpriteRenderer.sprite.name}");
            }
            else
            {
                Debug.LogError("[DEBUG] WARNING: No roleSprite provided, using fallback color");
                iconSpriteRenderer.sprite = null;
                iconSpriteRenderer.color = Color.red; // Color rojo para debug
            }
            iconSpriteRenderer.sortingOrder = 100;
            iconSpriteRenderer.enabled = true;
            Debug.LogError($"[DEBUG] SpriteRenderer enabled: {iconSpriteRenderer.enabled}");
        }

        // Establecer escala
        cachedTransform.localScale = Vector3.one * iconScale;
        Debug.LogError($"[DEBUG] Scale set to: {cachedTransform.localScale}");

        // POSICIONAR INMEDIATAMENTE
        if (targetPlayer != null && targetPlayer.IsValid())
        {
            VRCPlayerApi.TrackingData headData = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 initialPos = headData.position + (Vector3.up * heightOffset);

            // Si la posición es válida, usarla
            if (initialPos.magnitude > 0.1f)
            {
                cachedTransform.position = initialPos;
                Debug.LogError($"[DEBUG] Position set to HEAD: {cachedTransform.position}");
            }
            else
            {
                // Respaldo con posición del jugador
                cachedTransform.position = targetPlayer.GetPosition() + (Vector3.up * (heightOffset + 1.8f));
                Debug.LogError($"[DEBUG] Position set to PLAYER: {cachedTransform.position}");
            }

            // ROTACIÓN INICIAL - Mirar hacia la cámara del jugador local
            if (billboardEffect && localPlayer != null)
            {
                VRCPlayerApi.TrackingData localHeadData = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Quaternion playerHeadRotation = localHeadData.rotation;

                Vector3 lookDirection = cachedTransform.position - localHeadData.position;

                if (lockYAxis)
                {
                    lookDirection.y = 0;

                    Vector3 euler = playerHeadRotation.eulerAngles;
                    euler.x = 0; // Anula la inclinación adelante/atrás
                    euler.z = 0; // Anula la inclinación lateral
                    cachedTransform.rotation = Quaternion.Euler(euler);
                }

                if (lookDirection.magnitude > 0.01f)
                {
                    cachedTransform.rotation = Quaternion.LookRotation(lookDirection);
                }
            }

            Debug.Log($"[IconFollower] Posición inicial establecida en {cachedTransform.position}");
        }

        // Activar el seguimiento
        isActive = true;

        // DEBUG FINAL
        /* Debug.LogError($"[DEBUG FINAL] Icon Setup Complete:");
        Debug.LogError($"  - GameObject: {gameObject.name}");
        Debug.LogError($"  - Position: {cachedTransform.position}");
        Debug.LogError($"  - Scale: {cachedTransform.localScale}");
        Debug.LogError($"  - Active: {isActive}");
        Debug.LogError($"  - Sprite: {iconSpriteRenderer.sprite.name}");
        Debug.LogError($"  - Sprite Enabled: {iconSpriteRenderer.enabled}");
        Debug.LogError($"  - Sprite Color: {iconSpriteRenderer.color}"); */

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