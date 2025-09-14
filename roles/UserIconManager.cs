using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UserIconManager : UdonSharpBehaviour
{
    [Header("Configuración")]
    [SerializeField] private VRCUrl rolesJsonUrl;
    [SerializeField] private GameObject iconPrefab;
    [SerializeField] private float updateInterval = 60f;
    
    [Header("Sprites de Roles")]
    [SerializeField] private Sprite[] roleSprites;
    [SerializeField] private string[] roleNames;
    
    [Header("Optimización")]
    [SerializeField] private int maxIconPool = 20; // Pool máximo de iconos activos
    [SerializeField] private bool useBatchedUpdates = true; // Actualizar jugadores en lotes
    [SerializeField] private int playersPerBatch = 10; // Jugadores por lote
    
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;
    
    // Datos de roles cargados del JSON
    private DataDictionary rolesData;
    private DataDictionary userRoleMap;
    
    // Gestión de iconos con pooling
    private DataDictionary activeIcons; // playerId -> GameObject
    private GameObject[] iconPool; // Pool de iconos reutilizables
    private int[] poolInUse; // Estado del pool (0=libre, 1=en uso)
    private int poolSize = 0;
    
    // Control de tiempo y batching
    private float nextUpdateTime;
    private float nextJsonUpdateTime;
    private const float JSON_UPDATE_INTERVAL = 300f;
    private int currentBatchIndex = 0;
    
    // Cache de jugadores
    private VRCPlayerApi[] playerCache;
    private int playerCount = 0;
    
    void Start()
    {
        InitializeSystem();
        LoadRolesFromJson();
        SendCustomEventDelayedSeconds(nameof(UpdatePlayerIcons), 2f);
    }
    
    private void InitializeSystem()
    {
        activeIcons = new DataDictionary();
        userRoleMap = new DataDictionary();
        
        // Inicializar pool
        iconPool = new GameObject[maxIconPool];
        poolInUse = new int[maxIconPool];
        playerCache = new VRCPlayerApi[80];
        
        // Validaciones
        if (iconPrefab == null)
        {
            Debug.LogError("[UserIconManager] Icon Prefab no asignado!");
            return;
        }
        
        if (roleSprites == null || roleSprites.Length == 0)
        {
            Debug.LogError("[UserIconManager] No hay sprites de roles asignados!");
            return;
        }
        
        if (roleNames == null || roleNames.Length == 0)
        {
            Debug.LogError("[UserIconManager] No hay nombres de roles configurados!");
            return;
        }
    }
    
    void Update()
    {
        float currentTime = Time.time;
        
        // Actualización periódica de iconos
        if (currentTime >= nextUpdateTime)
        {
            if (useBatchedUpdates)
            {
                UpdatePlayerIconsBatched();
            }
            else
            {
                UpdatePlayerIcons();
            }
            nextUpdateTime = currentTime + updateInterval;
        }
        
        // Actualización del JSON
        if (currentTime >= nextJsonUpdateTime)
        {
            LoadRolesFromJson();
            nextJsonUpdateTime = currentTime + JSON_UPDATE_INTERVAL;
        }
    }
    
    // OPTIMIZACIÓN: Actualizar jugadores en lotes para distribuir la carga
    private void UpdatePlayerIconsBatched()
    {
        if (rolesData == null) return;
        
        // Obtener jugadores actuales
        playerCache = VRCPlayerApi.GetPlayers(playerCache);
        playerCount = 0;
        
        // Contar jugadores válidos
        foreach (VRCPlayerApi player in playerCache)
        {
            if (player != null && player.IsValid())
                playerCount++;
            else
                break;
        }
        
        if (playerCount == 0) return;
        
        // Procesar un lote de jugadores
        int startIndex = currentBatchIndex;
        int endIndex = Mathf.Min(currentBatchIndex + playersPerBatch, playerCount);
        
        for (int i = startIndex; i < endIndex; i++)
        {
            if (playerCache[i] != null && playerCache[i].IsValid())
            {
                UpdatePlayerIcon(playerCache[i]);
            }
        }
        
        // Avanzar al siguiente lote
        currentBatchIndex += playersPerBatch;
        if (currentBatchIndex >= playerCount)
        {
            currentBatchIndex = 0;
            CleanupUnusedIcons();
        }
    }
    
    public void LoadRolesFromJson()
    {
        if (rolesJsonUrl != null)
        {
            VRCStringDownloader.LoadUrl(rolesJsonUrl, (IUdonEventReceiver)this);
            DebugLog("Cargando datos de roles desde JSON...");
        }
    }
    
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken jsonResult))
        {
            DebugLog("Error al parsear JSON");
            return;
        }
        
        if (jsonResult.TokenType != TokenType.DataDictionary)
            return;
            
        DataDictionary jsonDict = jsonResult.DataDictionary;
        if (!jsonDict.TryGetValue("roles", out DataToken rolesToken))
            return;
            
        rolesData = rolesToken.DataDictionary;
        ProcessRolesData();
        
        DebugLog($"Roles cargados: {rolesData.Count}");
        UpdatePlayerIcons();
    }
    
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        DebugLog($"Error cargando JSON: {result.Error}");
    }
    
    private void ProcessRolesData()
    {
        userRoleMap.Clear();
        
        if (rolesData == null) return;
        
        DataList roleKeys = rolesData.GetKeys();
        
        for (int i = 0; i < roleKeys.Count; i++)
        {
            string roleName = roleKeys[i].String;
            DataToken roleData = rolesData[roleName];
            
            if (roleData.TokenType != TokenType.DataDictionary)
                continue;
                
            DataDictionary roleDict = roleData.DataDictionary;
            
            if (roleDict.TryGetValue("users", out DataToken usersToken) && 
                usersToken.TokenType == TokenType.DataList)
            {
                DataList users = usersToken.DataList;
                int priority = GetRolePriority(roleDict);
                
                for (int j = 0; j < users.Count; j++)
                {
                    string username = users[j].String;
                    
                    if (!userRoleMap.ContainsKey(username) || 
                        priority > GetUserRolePriority(username))
                    {
                        userRoleMap[username] = roleName;
                    }
                }
            }
        }
        
        DebugLog($"Usuarios procesados: {userRoleMap.Count}");
    }
    
    private int GetRolePriority(DataDictionary roleDict)
    {
        if (roleDict.TryGetValue("priority", out DataToken priorityToken))
            return (int)priorityToken.Number;
        return 0;
    }
    
    private int GetUserRolePriority(string username)
    {
        if (!userRoleMap.TryGetValue(username, out DataToken roleToken))
            return 0;
            
        string roleName = roleToken.String;
        if (!rolesData.TryGetValue(roleName, out DataToken roleData))
            return 0;
            
        if (roleData.TokenType == TokenType.DataDictionary)
            return GetRolePriority(roleData.DataDictionary);
            
        return 0;
    }
    
    public void UpdatePlayerIcons()
    {
        if (rolesData == null) return;
        
        playerCache = VRCPlayerApi.GetPlayers(playerCache);
        DataList currentPlayerIds = new DataList();
        
        foreach (VRCPlayerApi player in playerCache)
        {
            if (player == null || !player.IsValid()) continue;
            
            string playerId = player.playerId.ToString();
            currentPlayerIds.Add(playerId);
            UpdatePlayerIcon(player);
        }
        
        CleanupDisconnectedPlayers(currentPlayerIds);
    }
    
    private void UpdatePlayerIcon(VRCPlayerApi player)
    {
        string playerId = player.playerId.ToString();
        string playerName = player.displayName;
        
        string roleName = null;
        if (userRoleMap.TryGetValue(playerName, out DataToken roleToken))
            roleName = roleToken.String;
        
        // Si no tiene rol, eliminar icono si existe
        if (string.IsNullOrEmpty(roleName))
        {
            RemovePlayerIcon(playerId);
            return;
        }
        
        // Si ya tiene icono, verificar que sigue siendo válido
        if (activeIcons.ContainsKey(playerId))
        {
            // Verificar que el GameObject sigue existiendo
            DataToken iconToken = activeIcons[playerId];
            GameObject icon = (GameObject)iconToken.Reference;
            if (icon == null)
            {
                activeIcons.Remove(playerId);
            }
            else
            {
                return; // Ya tiene icono válido
            }
        }
        
        // Crear o reutilizar icono del pool
        CreateOrReuseIcon(player, roleName);
    }
    
    private void CreateOrReuseIcon(VRCPlayerApi player, string roleName)
    {
        if (player == null || !player.IsValid()) return;
        
        Sprite roleSprite = GetRoleSprite(roleName);
        if (roleSprite == null)
        {
            DebugLog($"No se encontró sprite para el rol: {roleName}");
            return;
        }
        
        GameObject icon = GetIconFromPool();
        
        if (icon == null)
        {
            // Si no hay iconos disponibles en el pool y no hemos alcanzado el máximo
            if (poolSize < maxIconPool)
            {
                icon = CreateNewIcon();
            }
            else
            {
                DebugLog("Pool de iconos lleno, no se puede crear más");
                return;
            }
        }
        
        // Configurar el icono
        icon.name = $"Icon_{player.displayName}_{roleName}";
        IconFollower follower = icon.GetComponent<IconFollower>();
        
        if (follower != null)
        {
            follower.SetupIcon(player, roleSprite);
            icon.SetActive(true);
        }
        else
        {
            DebugLog("No se encontró componente IconFollower");
            return;
        }
        
        // Registrar icono activo
        activeIcons[player.playerId.ToString()] = icon;
        DebugLog($"Icono asignado para {player.displayName} ({roleName})");
    }
    
    private GameObject GetIconFromPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            if (poolInUse[i] == 0 && iconPool[i] != null)
            {
                poolInUse[i] = 1;
                return iconPool[i];
            }
        }
        return null;
    }
    
    private GameObject CreateNewIcon()
    {
        if (iconPrefab == null || poolSize >= maxIconPool) return null;
        
        GameObject newIcon = Instantiate(iconPrefab);
        if (newIcon != null)
        {
            iconPool[poolSize] = newIcon;
            poolInUse[poolSize] = 1;
            poolSize++;
            newIcon.SetActive(false); // Empezar desactivado
            return newIcon;
        }
        
        return null;
    }
    
    private void ReturnIconToPool(GameObject icon)
    {
        if (icon == null) return;
        
        // Limpiar el icono
        IconFollower follower = icon.GetComponent<IconFollower>();
        if (follower != null)
        {
            follower.CleanupIcon();
        }
        
        // Desactivar y marcar como disponible
        icon.SetActive(false);
        
        for (int i = 0; i < poolSize; i++)
        {
            if (iconPool[i] == icon)
            {
                poolInUse[i] = 0;
                break;
            }
        }
    }
    
    private Sprite GetRoleSprite(string roleName)
    {
        for (int i = 0; i < roleNames.Length && i < roleSprites.Length; i++)
        {
            if (roleNames[i] == roleName && roleSprites[i] != null)
                return roleSprites[i];
        }
        return null;
    }
    
    private void RemovePlayerIcon(string playerId)
    {
        if (!activeIcons.TryGetValue(playerId, out DataToken iconToken))
            return;
            
        GameObject icon = (GameObject)iconToken.Reference;
        if (icon != null)
        {
            ReturnIconToPool(icon);
        }
        
        activeIcons.Remove(playerId);
    }
    
    private void CleanupDisconnectedPlayers(DataList currentPlayerIds)
    {
        DataList activeKeys = activeIcons.GetKeys();
        
        for (int i = 0; i < activeKeys.Count; i++)
        {
            string playerId = activeKeys[i].String;
            bool found = false;
            
            for (int j = 0; j < currentPlayerIds.Count; j++)
            {
                if (currentPlayerIds[j].String == playerId)
                {
                    found = true;
                    break;
                }
            }
            
            if (!found)
                RemovePlayerIcon(playerId);
        }
    }
    
    private void CleanupUnusedIcons()
    {
        DataList activeKeys = activeIcons.GetKeys();
        
        for (int i = 0; i < activeKeys.Count; i++)
        {
            DataToken iconToken = activeIcons[activeKeys[i]];
            GameObject icon = (GameObject)iconToken.Reference;
            
            if (icon == null || !icon.activeSelf)
            {
                activeIcons.Remove(activeKeys[i]);
            }
        }
    }
    
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        SendCustomEventDelayedSeconds(nameof(UpdateSinglePlayer), 2f);
        lastJoinedPlayer = player;
        DebugLog($"Jugador unido: {player.displayName}");
    }
    
    private VRCPlayerApi lastJoinedPlayer;
    
    public void UpdateSinglePlayer()
    {
        if (lastJoinedPlayer != null && lastJoinedPlayer.IsValid())
        {
            UpdatePlayerIcon(lastJoinedPlayer);
        }
    }
    
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        RemovePlayerIcon(player.playerId.ToString());
        DebugLog($"Jugador salió: {player.displayName}");
    }
    
    private void DebugLog(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[UserIconManager] {message}");
    }
}