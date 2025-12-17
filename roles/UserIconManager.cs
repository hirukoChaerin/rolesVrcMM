using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UserIconManager : UdonSharpBehaviour
{
    [Header("Configuration")]
    [SerializeField] private VRCUrl rolesJsonUrl;
    [SerializeField] private GameObject iconPrefab;
    [SerializeField] private float updateInterval = 60f;
    
    [Header("Roles")]
    [SerializeField] private Sprite[] roleSprites;
    [SerializeField] private string[] roleNames;
    
    [Header("Optimization")]
    [SerializeField] private int maxIconPool = 20; // Maximum active icon pool
    [SerializeField] private bool useBatchedUpdates = true; // Update players in batches
    [SerializeField] private int playersPerBatch = 10; // Players per batch
    
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;
    
    // Role data loaded from JSON
    private DataDictionary rolesData;
    private DataDictionary userRoleMap;
    
    // Icon management with pooling
    private DataDictionary activeIcons; // playerId -> GameObject
    private GameObject[] iconPool; // Reusable icon pool
    private int[] poolInUse; // Pool state (0=free, 1=in use)
    private int poolSize = 0;
    
    // Timing and batching control
    private float nextUpdateTime;
    private float nextJsonUpdateTime;
    private const float JSON_UPDATE_INTERVAL = 300f;
    private int currentBatchIndex = 0;
    
    // Player cache
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
    
    // OPTIMIZATION: Update players in batches to distribute the load
    private void UpdatePlayerIconsBatched()
    {
        if (rolesData == null) return;
        
        // Get current players
        playerCache = VRCPlayerApi.GetPlayers(playerCache);
        playerCount = 0;
        
        // Count valid players
        foreach (VRCPlayerApi player in playerCache)
        {
            if (player != null && player.IsValid())
                playerCount++;
            else
                break;
        }
        
        if (playerCount == 0) return;
        
        // Process a batch of players
        int startIndex = currentBatchIndex;
        int endIndex = Mathf.Min(currentBatchIndex + playersPerBatch, playerCount);
        
        for (int i = startIndex; i < endIndex; i++)
        {
            if (playerCache[i] != null && playerCache[i].IsValid())
            {
                UpdatePlayerIcon(playerCache[i]);
            }
        }
        
        // Advance to the next batch
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
            DebugLog("Loading role data from JSON...");
        }
    }
    
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        if (!VRCJson.TryDeserializeFromJson(result.Result, out DataToken jsonResult))
        {
            DebugLog("Error parsing JSON");
            return;
        }
        
        if (jsonResult.TokenType != TokenType.DataDictionary)
            return;
            
        DataDictionary jsonDict = jsonResult.DataDictionary;
        if (!jsonDict.TryGetValue("roles", out DataToken rolesToken))
            return;
            
        rolesData = rolesToken.DataDictionary;
        ProcessRolesData();
        
        DebugLog($"Roles loaded: {rolesData.Count}");
        UpdatePlayerIcons();
    }
    
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        DebugLog($"Error loading JSON: {result.Error}");
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
        
        DebugLog($"Users processed: {userRoleMap.Count}");
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
        
        // If it doesn't have a role, remove the icon if it exists.
        if (string.IsNullOrEmpty(roleName))
        {
            RemovePlayerIcon(playerId);
            return;
        }
        
        // If it already has an icon, verify that it is still valid.
        if (activeIcons.ContainsKey(playerId))
        {
            // Verify that the GameObject still exists
            DataToken iconToken = activeIcons[playerId];
            GameObject icon = (GameObject)iconToken.Reference;
            if (icon == null)
            {
                activeIcons.Remove(playerId);
            }
            else
            {
                return;
            }
        }
        
        // Create or reuse pool icon
        CreateOrReuseIcon(player, roleName);
    }
    
    private void CreateOrReuseIcon(VRCPlayerApi player, string roleName)
    {
        if (player == null || !player.IsValid()) return;
        
        Sprite roleSprite = GetRoleSprite(roleName);
        if (roleSprite == null)
        {
            DebugLog($"No sprite found for role: {roleName}");
            return;
        }
        
        GameObject icon = GetIconFromPool();
        
        if (icon == null)
        {
            // If there are no icons available in the pool and we haven't reached the maximum.
            if (poolSize < maxIconPool)
            {
                icon = CreateNewIcon();
            }
            else
            {
                DebugLog("Icon pool is full, no more can be created");
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
            DebugLog("IconFollower component not found");
            return;
        }
        
        // Registrar icono activo
        activeIcons[player.playerId.ToString()] = icon;
        DebugLog($"Icon assigned to {player.displayName} ({roleName})");
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
            newIcon.SetActive(false);
            return newIcon;
        }
        
        return null;
    }
    
    private void ReturnIconToPool(GameObject icon)
    {
        if (icon == null) return;
        
        // Clear the icon
        IconFollower follower = icon.GetComponent<IconFollower>();
        if (follower != null)
        {
            follower.CleanupIcon();
        }
        
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
        DebugLog($"Player joined: {player.displayName}");
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
        DebugLog($"Player left: {player.displayName}");
    }
    
    private void DebugLog(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[UserIconManager] {message}");
    }
}