using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using Cloud2026.Core;
using Cloud2026.Services;
using Cloud2026.UI;

namespace Cloud2026.EditorTools
{
    /// <summary>
    /// Genera la escena que unifica login y Partida en un único flujo.
    ///
    /// Reutiliza AnonymousLoginUI + AccountUI para no duplicar el login que ya trae
    /// TurnMatchPanel: aquí solo se instancian sus pantallas Lobby y Match. PartidaRoot
    /// arranca inactivo y se activa a través del gameplayRoot de AnonymousLoginUI, así
    /// que "Jugar" es simplemente el botón que ya sabía mostrar un gameplayRoot.
    ///
    /// SampleScene.unity y TurnMatch.unity no se tocan: siguen siendo el PoC de cada
    /// pieza por separado.
    /// </summary>
    public static class MainFlowSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ScenePath = ScenesFolder + "/MainFlow.unity";
        private const float PanelWidth = 860f;

        [MenuItem("Cloud2026/Crear escena Flujo principal")]
        public static void CreateMainFlowScene()
        {
            // En batch mode (regenerar por CLI) EditorUtility.DisplayDialog no puede
            // mostrar nada y Unity registra un warning; hay que saltarse la
            // confirmación en ese caso, no interpretarla como "cancelar".
            if (System.IO.File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Sobrescribir escena",
                    "Ya existe " + ScenePath + ". ¿Quieres reemplazarla?",
                    "Reemplazar", "Cancelar"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            BuildBootstrap();
            var canvas = UiFactory.CreateCanvas();
            BuildEventSystem();
            BuildUi(canvas.transform);

            if (!AssetDatabase.IsValidFolder(ScenesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();

            Debug.Log("[MainFlowSceneBuilder] Escena creada en " + ScenePath +
                      ". Despliega el módulo TurnMatch desde la ventana de Deployment antes de darle a Play.");
        }

        /// <summary>
        /// El login es explícito a propósito: el flujo que se enseña empieza en la
        /// pantalla de Login, no con una sesión ya abierta.
        /// </summary>
        private static void BuildBootstrap()
        {
            var go = new GameObject("GameBootstrap",
                typeof(UGSAuthService),
                typeof(UGSCloudCodeService),
                typeof(UGSTurnMatchService),
                typeof(GameBootstrap));

            var serialized = new SerializedObject(go.GetComponent<GameBootstrap>());
            UiFactory.Wire(serialized, "authService", go.GetComponent<UGSAuthService>());
            UiFactory.Wire(serialized, "cloudCodeService", go.GetComponent<UGSCloudCodeService>());
            UiFactory.Wire(serialized, "turnMatchService", go.GetComponent<UGSTurnMatchService>());
            serialized.FindProperty("autoLoginAnonymous").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static void BuildUi(Transform canvasTransform)
        {
            var resources = UiFactory.Resources();

            // La Partida se construye primero para poder pasar su GameObject como
            // gameplayRoot del login; se desactiva al final, cuando ya no hace
            // falta que esté activa para que SerializedObject pueda escribir en ella.
            var partidaRoot = BuildPartidaRoot(canvasTransform, resources);
            var loginRoot = BuildLoginRoot(canvasTransform, resources, partidaRoot);

            var turnMatchPanel = partidaRoot.GetComponent<TurnMatchPanel>();
            var partidaSerialized = new SerializedObject(turnMatchPanel);
            UiFactory.Wire(partidaSerialized, "mainMenuRoot", loginRoot);
            UiFactory.Wire(partidaSerialized, "partidaRoot", partidaRoot);
            partidaSerialized.ApplyModifiedPropertiesWithoutUndo();

            partidaRoot.SetActive(false);
        }

        // --- Login + Menú principal -----------------------------------------

        private static GameObject BuildLoginRoot(
            Transform parent, TMP_DefaultControls.Resources resources, GameObject partidaRoot)
        {
            var root = UiFactory.CreatePanel(parent, "LoginRoot", PanelWidth, withBackground: true);

            UiFactory.CreateText(root.transform, resources, "Title", "Cloud2026", 40f, 56f, Color.white);

            var loggedOut = BuildLoggedOutPanel(root.transform, resources,
                out var loginAnonymousButton, out var usernameInput, out var passwordInput,
                out var signUpButton, out var signInButton, out var signInWithUnityButton);

            var loggedIn = BuildLoggedInPanel(root.transform, resources,
                out var playerIdText, out var accountStateText, out var linkGroup,
                out var linkUsernameInput, out var linkPasswordInput, out var linkButton, out var linkWithUnityButton,
                out var playButton, out var signOutButton, out var newGuestButton);

            // Compartidos entre AnonymousLoginUI y AccountUI: una sola línea de
            // estado y un solo spinner para toda el área de login, no una por
            // componente. Viven fuera de los dos paneles para no desaparecer al
            // cambiar entre ellos.
            var statusText = UiFactory.CreateText(root.transform, resources, "StatusText", "", 22f, 40f, Color.white);
            var loadingIndicator = BuildLoadingIndicator(root.transform);

            var anonymousLogin = root.AddComponent<AnonymousLoginUI>();
            var anonymousSerialized = new SerializedObject(anonymousLogin);
            UiFactory.Wire(anonymousSerialized, "loggedOutPanel", loggedOut);
            UiFactory.Wire(anonymousSerialized, "loggedInPanel", loggedIn);
            UiFactory.Wire(anonymousSerialized, "loginAnonymousButton", loginAnonymousButton);
            UiFactory.Wire(anonymousSerialized, "signOutButton", signOutButton);
            UiFactory.Wire(anonymousSerialized, "newGuestButton", newGuestButton);
            UiFactory.Wire(anonymousSerialized, "playButton", playButton);
            UiFactory.Wire(anonymousSerialized, "statusText", statusText);
            UiFactory.Wire(anonymousSerialized, "playerIdText", playerIdText);
            UiFactory.Wire(anonymousSerialized, "loadingIndicator", loadingIndicator);
            UiFactory.Wire(anonymousSerialized, "gameplayRoot", partidaRoot);
            anonymousSerialized.FindProperty("hideOnPlay").boolValue = true;
            anonymousSerialized.ApplyModifiedPropertiesWithoutUndo();

            var account = root.AddComponent<AccountUI>();
            var accountSerialized = new SerializedObject(account);
            UiFactory.Wire(accountSerialized, "usernameInput", usernameInput);
            UiFactory.Wire(accountSerialized, "passwordInput", passwordInput);
            UiFactory.Wire(accountSerialized, "signUpButton", signUpButton);
            UiFactory.Wire(accountSerialized, "signInButton", signInButton);
            UiFactory.Wire(accountSerialized, "linkGroup", linkGroup);
            UiFactory.Wire(accountSerialized, "linkUsernameInput", linkUsernameInput);
            UiFactory.Wire(accountSerialized, "linkPasswordInput", linkPasswordInput);
            UiFactory.Wire(accountSerialized, "linkButton", linkButton);
            UiFactory.Wire(accountSerialized, "linkWithUnityButton", linkWithUnityButton);
            UiFactory.Wire(accountSerialized, "accountStateText", accountStateText);
            UiFactory.Wire(accountSerialized, "statusText", statusText);
            UiFactory.Wire(accountSerialized, "loadingIndicator", loadingIndicator);
            UiFactory.Wire(accountSerialized, "signInWithUnityButton", signInWithUnityButton);
            accountSerialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject BuildLoggedOutPanel(
            Transform parent, TMP_DefaultControls.Resources resources,
            out UnityEngine.UI.Button loginAnonymousButton,
            out TMP_InputField usernameInput, out TMP_InputField passwordInput,
            out UnityEngine.UI.Button signUpButton, out UnityEngine.UI.Button signInButton,
            out UnityEngine.UI.Button signInWithUnityButton)
        {
            var panel = UiFactory.CreatePanel(parent, "LoggedOutPanel", PanelWidth - 40f, withBackground: false);

            loginAnonymousButton = UiFactory.CreateButton(panel.transform, resources,
                "LoginAnonymousButton", "Entrar como invitado");

            UiFactory.CreateText(panel.transform, resources, "SeparatorText",
                "— o accede con tu cuenta —", 20f, 34f, UiFactory.SubtleText);

            usernameInput = UiFactory.CreateInputField(panel.transform, resources, "UsernameInput", "Usuario");
            passwordInput = UiFactory.CreateInputField(panel.transform, resources, "PasswordInput", "Contraseña");
            passwordInput.contentType = TMP_InputField.ContentType.Password;

            signUpButton = UiFactory.CreateButton(panel.transform, resources, "SignUpButton", "Crear cuenta");
            signInButton = UiFactory.CreateButton(panel.transform, resources, "SignInButton", "Iniciar sesión");

            UiFactory.CreateText(panel.transform, resources, "SeparatorUnityText",
                "— o con tu cuenta de Unity —", 20f, 34f, UiFactory.SubtleText);

            signInWithUnityButton = UiFactory.CreateButton(panel.transform, resources,
                "SignInWithUnityButton", "Iniciar sesión con Unity");

            return panel;
        }

        private static GameObject BuildLoggedInPanel(
            Transform parent, TMP_DefaultControls.Resources resources,
            out TextMeshProUGUI playerIdText, out TextMeshProUGUI accountStateText,
            out GameObject linkGroup, out TMP_InputField linkUsernameInput,
            out TMP_InputField linkPasswordInput, out UnityEngine.UI.Button linkButton,
            out UnityEngine.UI.Button linkWithUnityButton,
            out UnityEngine.UI.Button playButton, out UnityEngine.UI.Button signOutButton,
            out UnityEngine.UI.Button newGuestButton)
        {
            var panel = UiFactory.CreatePanel(parent, "LoggedInPanel", PanelWidth - 40f, withBackground: false);

            UiFactory.CreateText(panel.transform, resources, "MenuTitle", "Menú principal", 30f, 44f, Color.white);
            playerIdText = UiFactory.CreateText(panel.transform, resources, "PlayerIdText", "", 20f, 32f, UiFactory.SubtleText);
            accountStateText = UiFactory.CreateText(panel.transform, resources, "AccountStateText", "", 22f, 40f, Color.white);

            linkGroup = BuildLinkGroup(panel.transform, resources, out linkUsernameInput, out linkPasswordInput, out linkButton, out linkWithUnityButton);

            playButton = UiFactory.CreateButton(panel.transform, resources, "PlayButton", "Jugar partida", 72f, 30f);
            signOutButton = UiFactory.CreateButton(panel.transform, resources, "SignOutButton", "Cerrar sesión", 48f, 20f);
            newGuestButton = UiFactory.CreateButton(panel.transform, resources, "NewGuestButton", "Nuevo invitado", 48f, 20f);

            return panel;
        }

        private static GameObject BuildLinkGroup(
            Transform parent, TMP_DefaultControls.Resources resources,
            out TMP_InputField linkUsernameInput, out TMP_InputField linkPasswordInput,
            out UnityEngine.UI.Button linkButton, out UnityEngine.UI.Button linkWithUnityButton)
        {
            var group = UiFactory.CreatePanel(parent, "LinkGroup", PanelWidth - 80f, withBackground: false);

            UiFactory.CreateText(group.transform, resources, "LinkTitle",
                "Sesión de invitado: vincula una cuenta para no perder el progreso.", 18f, 46f, UiFactory.SubtleText);

            linkUsernameInput = UiFactory.CreateInputField(group.transform, resources, "LinkUsernameInput", "Usuario");
            linkPasswordInput = UiFactory.CreateInputField(group.transform, resources, "LinkPasswordInput", "Contraseña");
            linkPasswordInput.contentType = TMP_InputField.ContentType.Password;

            linkButton = UiFactory.CreateButton(group.transform, resources, "LinkButton", "Vincular cuenta");
            linkWithUnityButton = UiFactory.CreateButton(group.transform, resources, "LinkWithUnityButton", "Vincular con Unity");

            return group;
        }

        private static GameObject BuildLoadingIndicator(Transform parent)
        {
            var go = new GameObject("LoadingIndicator", typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(1f, 1f, 1f, 0.6f);

            UiFactory.SetPreferredHeight(go, 28f);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 28f);

            go.SetActive(false);
            return go;
        }

        // --- Partida (Lobby + Match, sin login propio) -----------------------

        private static GameObject BuildPartidaRoot(Transform parent, TMP_DefaultControls.Resources resources)
        {
            var root = UiFactory.CreatePanel(parent, "PartidaRoot", PanelWidth, withBackground: true);
            var panel = root.AddComponent<TurnMatchPanel>();

            UiFactory.CreateText(root.transform, resources, "Title",
                "Partida por turnos · idempotencia", 38f, 54f, Color.white);

            var lobby = BuildLobbyPanel(root.transform, resources,
                out var createButton, out var joinInput, out var joinButton,
                out var lobbyStatus, out var backButton);
            var match = BuildMatchPanel(root.transform, resources,
                out var codeText, out var turnLabel, out var historyText, out var outcomeText,
                out var passButton, out var resendButton, out var leaveButton);

            var serialized = new SerializedObject(panel);

            // loginPanel / guestLoginButton / loginStatusText se dejan sin asignar
            // a propósito: el login ya lo resolvió AnonymousLoginUI antes de que
            // este root se active, y TurnMatchPanel tolera esos campos en null.
            UiFactory.Wire(serialized, "lobbyPanel", lobby);
            UiFactory.Wire(serialized, "createMatchButton", createButton);
            UiFactory.Wire(serialized, "joinCodeInput", joinInput);
            UiFactory.Wire(serialized, "joinMatchButton", joinButton);
            UiFactory.Wire(serialized, "lobbyStatusText", lobbyStatus);
            UiFactory.Wire(serialized, "backToMenuButton", backButton);

            UiFactory.Wire(serialized, "matchPanel", match);
            UiFactory.Wire(serialized, "matchCodeText", codeText);
            UiFactory.Wire(serialized, "turnText", turnLabel);
            UiFactory.Wire(serialized, "historyText", historyText);
            UiFactory.Wire(serialized, "outcomeText", outcomeText);
            UiFactory.Wire(serialized, "passTurnButton", passButton);
            UiFactory.Wire(serialized, "resendTurnButton", resendButton);
            UiFactory.Wire(serialized, "leaveMatchButton", leaveButton);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject BuildLobbyPanel(
            Transform parent, TMP_DefaultControls.Resources resources,
            out UnityEngine.UI.Button createButton, out TMP_InputField joinInput,
            out UnityEngine.UI.Button joinButton, out TextMeshProUGUI status,
            out UnityEngine.UI.Button backButton)
        {
            var panel = UiFactory.CreatePanel(parent, "LobbyPanel", PanelWidth, withBackground: false);

            createButton = UiFactory.CreateButton(panel.transform, resources, "CreateMatchButton", "Crear partida");

            UiFactory.CreateText(panel.transform, resources, "Separator",
                "— o únete a la de otra persona —", 20f, 34f, UiFactory.SubtleText);

            joinInput = UiFactory.CreateInputField(panel.transform, resources, "JoinCodeInput", "Código de 4 letras");
            joinButton = UiFactory.CreateButton(panel.transform, resources, "JoinMatchButton", "Unirse");
            status = UiFactory.CreateText(panel.transform, resources, "LobbyStatusText", "", 22f, 40f, Color.white);

            backButton = UiFactory.CreateButton(panel.transform, resources, "BackToMenuButton", "Volver al menú", 48f, 20f);

            return panel;
        }

        private static GameObject BuildMatchPanel(
            Transform parent, TMP_DefaultControls.Resources resources,
            out TextMeshProUGUI codeText, out TextMeshProUGUI turnLabel,
            out TextMeshProUGUI historyText, out TextMeshProUGUI outcomeText,
            out UnityEngine.UI.Button passButton, out UnityEngine.UI.Button resendButton,
            out UnityEngine.UI.Button leaveButton)
        {
            var panel = UiFactory.CreatePanel(parent, "MatchPanel", PanelWidth, withBackground: false);

            codeText = UiFactory.CreateText(panel.transform, resources, "MatchCodeText", "", 30f, 44f, Color.white);
            turnLabel = UiFactory.CreateText(panel.transform, resources, "TurnText", "", 26f, 44f, Color.white);
            historyText = UiFactory.CreateText(panel.transform, resources, "HistoryText", "", 20f, 130f, UiFactory.SubtleText);

            passButton = UiFactory.CreateButton(panel.transform, resources, "PassTurnButton", "Pasar turno");

            resendButton = UiFactory.CreateButton(panel.transform, resources, "ResendTurnButton",
                "Reenviar la misma petición (simula un reintento)", 56f, 20f);

            outcomeText = UiFactory.CreateText(panel.transform, resources, "OutcomeText", "", 21f, 80f, Color.white);

            leaveButton = UiFactory.CreateButton(panel.transform, resources, "LeaveMatchButton", "Salir", 48f, 20f);

            return panel;
        }

        private static void AddSceneToBuildSettings()
        {
            var current = EditorBuildSettings.scenes;

            foreach (var entry in current)
            {
                if (entry.path == ScenePath) return;
            }

            var updated = new EditorBuildSettingsScene[current.Length + 1];
            current.CopyTo(updated, 0);
            updated[current.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
