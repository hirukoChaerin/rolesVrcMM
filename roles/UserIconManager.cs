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

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;

    // Datos de roles cargados del JSON
    private DataDictionary rolesData;
    private DataDictionary userRoleMap; // username -> role

    // Iconos activos: playerId -> GameObject
    private DataDictionary activeIcons;

    // Control de tiempo
    private float nextUpdateTime;
    private float nextJsonUpdateTime;
    private const float JSON_UPDATE_INTERVAL = 300f; // 5 minutos

    void Start()
    {
        activeIcons = new DataDictionary();
        userRoleMap = new DataDictionary();

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

        LoadRolesFromJson();
        SendCustomEventDelayedSeconds(nameof(UpdatePlayerIcons), 2f);
    }

    void Update()
    {
        float currentTime = Time.time;

        // Actualización periódica de iconos
        if (currentTime >= nextUpdateTime)
        {
            UpdatePlayerIcons();
            nextUpdateTime = currentTime + updateInterval;
        }

        // Actualización del JSON
        if (currentTime >= nextJsonUpdateTime)
        {
            LoadRolesFromJson();
            nextJsonUpdateTime = currentTime + JSON_UPDATE_INTERVAL;
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

            // Procesar usuarios del rol
            if (roleDict.TryGetValue("users", out DataToken usersToken) &&
                usersToken.TokenType == TokenType.DataList)
            {
                DataList users = usersToken.DataList;
                int priority = GetRolePriority(roleDict);

                for (int j = 0; j < users.Count; j++)
                {
                    string username = users[j].String;

                    // Solo asignar si no tiene rol o el nuevo tiene mayor prioridad
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

        VRCPlayerApi[] players = new VRCPlayerApi[80];
        players = VRCPlayerApi.GetPlayers(players);

        // Crear set de jugadores actuales
        DataList currentPlayerIds = new DataList();

        foreach (VRCPlayerApi player in players)
        {
            if (player == null || !player.IsValid()) continue;

            string playerId = player.playerId.ToString();
            currentPlayerIds.Add(playerId);

            UpdatePlayerIcon(player);
        }

        // Limpiar iconos de jugadores desconectados
        CleanupDisconnectedPlayers(currentPlayerIds);
    }

    private void UpdatePlayerIcon(VRCPlayerApi player)
    {
        string playerId = player.playerId.ToString();
        string playerName = player.displayName;

        // Obtener rol del jugador
        string roleName = null;
        if (userRoleMap.TryGetValue(playerName, out DataToken roleToken))
            roleName = roleToken.String;

        // Si no tiene rol, eliminar icono si existe
        if (string.IsNullOrEmpty(roleName))
        {
            RemovePlayerIcon(playerId);
            return;
        }

        // Si ya tiene icono, no hacer nada
        if (activeIcons.ContainsKey(playerId))
            return;

        // Crear nuevo icono
        CreateIconForPlayer(player, roleName);
    }

    private void CreateIconForPlayer(VRCPlayerApi player, string roleName)
    {
        if (iconPrefab == null) return;

        // Obtener sprite del rol
        Sprite roleSprite = GetRoleSprite(roleName);
        if (roleSprite == null)
        {
            DebugLog($"No se encontró sprite para el rol: {roleName}");
            return;
        }

        // Crear icono
        GameObject icon = Instantiate(iconPrefab);
        icon.name = $"Icon_{player.displayName}_{roleName}";

        // Configurar el icono
        IconFollower follower = icon.GetComponent<IconFollower>();
        if (follower != null)
        {
            follower.Initialize(player, roleSprite);
        }

        // Registrar icono activo
        activeIcons[player.playerId.ToString()] = icon;

        DebugLog($"Icono creado para {player.displayName} ({roleName})");
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
            Destroy(icon);

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

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        SendCustomEventDelayedSeconds(nameof(UpdatePlayerIcons), 2f);
        DebugLog($"Jugador unido: {player.displayName}");
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
