using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;

namespace Cloud2026.Services
{
    /// <summary>
    /// Wrapper autoritativo para Unity Gaming Services Authentication.
    /// Encapsula llamadas al SDK, gestión de eventos y captura tipada de excepciones.
    /// </summary>
    public class UGSAuthService : MonoBehaviour, IAuthService
    {
        public event Action<string> OnSignedIn;
        public event Action OnSignedOut;
        public event Action<string> OnSignInFailed;

        /// <summary>Se dispara al vincular credenciales a la sesión en curso.</summary>
        public event Action<string> OnAccountLinked;

        [Header("Configuración")]
        [Tooltip("Si es true, intenta inicializar Unity Services automáticamente en Awake.")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("Perfil de autenticación a utilizar (opcional, útil para pruebas multi-jugador locales).")]
        [SerializeField] private string profileName = "";

        [Tooltip("Entorno de UGS contra el que se inicializa. Debe existir en el Dashboard del proyecto.")]
        [SerializeField] private string environmentName = "production";

        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public bool IsSignedIn => IsInitialized && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;
        public string PlayerName => IsSignedIn ? AuthenticationService.Instance.PlayerName : string.Empty;

        /// <summary>
        /// Nombre de usuario de la cuenta con credenciales. Vacío si la sesión es anónima o si
        /// solo tiene una cuenta de Unity vinculada: el SDK devuelve null en PlayerInfo.Username
        /// mientras no haya credenciales de usuario/contraseña.
        /// </summary>
        public string Username =>
            IsSignedIn ? AuthenticationService.Instance.PlayerInfo?.Username ?? string.Empty : string.Empty;

        /// <summary>
        /// PlayerInfo.GetUnityId() solo devuelve algo cuando la sesión tiene una identidad de
        /// Unity Player Accounts vinculada (por SignInWithUnityAsync o LinkWithUnityAsync).
        /// </summary>
        public bool IsUnityAccountLinked =>
            IsSignedIn && !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerInfo?.GetUnityId());

        /// <summary>
        /// Sesión iniciada pero sin ninguna identidad persistente vinculada: ese progreso se
        /// pierde al desinstalar. Antes de sumar Unity Player Accounts esto solo miraba
        /// Username, así que un jugador logueado solo con su cuenta de Unity (sin
        /// usuario/contraseña) se veía como anónimo y le salía el aviso de vincular cuenta
        /// aunque ya tuviera una identidad real.
        /// </summary>
        public bool IsAnonymous => IsSignedIn && string.IsNullOrEmpty(Username) && !IsUnityAccountLinked;

        private Task _initializationTask;
        private bool _isSigningIn = false;

        private void Awake()
        {
            if (initializeOnAwake)
            {
                _ = InitializeAsync();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        /// <summary>
        /// Inicializa los servicios centrales de Unity (UnityServices.InitializeAsync).
        /// </summary>
        public Task InitializeAsync()
        {
            if (IsInitialized)
            {
                return Task.CompletedTask;
            }

            // Devolvemos la MISMA tarea en vuelo en lugar de retornar de inmediato: así,
            // quien haga await sobre una segunda llamada espera a la inicialización real
            // y no continúa creyendo que los servicios ya están listos.
            if (_initializationTask != null)
            {
                return _initializationTask;
            }

            _initializationTask = InitializeInternalAsync();
            return _initializationTask;
        }

        private async Task InitializeInternalAsync()
        {
            try
            {
                var options = new InitializationOptions();

                if (!string.IsNullOrWhiteSpace(environmentName))
                {
                    options.SetEnvironmentName(environmentName);
                }

                if (!string.IsNullOrWhiteSpace(profileName))
                {
                    options.SetProfile(profileName);
                }

                await UnityServices.InitializeAsync(options);
                Debug.Log($"[UGSAuthService] Unity Services inicializado correctamente. Entorno: '{environmentName}'.");

                SubscribeToEvents();
            }
            catch (ServicesInitializationException initEx)
            {
                Debug.LogError($"[UGSAuthService] Error al inicializar Unity Services (Servicio no disponible): {initEx.Message}");
                OnSignInFailed?.Invoke($"Error de inicialización: {initEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UGSAuthService] Excepción inesperada durante InitializeAsync: {ex.Message}");
                OnSignInFailed?.Invoke($"Error inesperado: {ex.Message}");
            }
            finally
            {
                // Si falló, limpiamos la tarea para permitir un reintento posterior.
                if (!IsInitialized)
                {
                    _initializationTask = null;
                }
            }
        }

        /// <summary>
        /// Realiza el inicio de sesión anónimo contra los servidores de UGS.
        /// </summary>
        public async Task<bool> SignInAnonymouslyAsync()
        {
            if (_isSigningIn)
            {
                Debug.LogWarning("[UGSAuthService] Ya hay un intento de inicio de sesión en progreso.");
                return false;
            }

            if (!IsInitialized)
            {
                Debug.Log("[UGSAuthService] Servicios no inicializados. Inicializando antes del login...");
                await InitializeAsync();
                if (!IsInitialized)
                {
                    Debug.LogError("[UGSAuthService] No se pudo inicializar UGS para realizar el login.");
                    return false;
                }
            }

            if (IsSignedIn)
            {
                Debug.Log($"[UGSAuthService] Ya hay una sesión activa para el PlayerId: {PlayerId}");
                OnSignedIn?.Invoke(PlayerId);
                return true;
            }

            _isSigningIn = true;

            try
            {
                Debug.Log("[UGSAuthService] Iniciando login anónimo en UGS...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                
                string playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log($"[UGSAuthService] ¡Login anónimo exitoso! PlayerId: {playerId}");
                return true;
            }
            catch (AuthenticationException authEx)
            {
                // Errores específicos de autenticación (ej. sesión inválida, credenciales revocadas)
                string errorMsg = $"Error de Autenticación ({authEx.ErrorCode}): {authEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (RequestFailedException reqEx)
            {
                // Errores de red o de solicitud al servidor
                string errorMsg = $"Error de Conexión/Servidor ({reqEx.ErrorCode}): {reqEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error inesperado durante el login anónimo: {ex.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            finally
            {
                _isSigningIn = false;
            }
        }

        /// <summary>
        /// Cierra la sesión activa en UGS.
        /// </summary>
        /// <param name="clearCredentials">Si es true, borra el token de sesión almacenado en el cliente para crear un nuevo usuario anónimo en el próximo login.</param>
        public void SignOut(bool clearCredentials = false)
        {
            if (!IsSignedIn)
            {
                Debug.LogWarning("[UGSAuthService] No hay ninguna sesión activa para cerrar.");
                return;
            }

            try
            {
                if (clearCredentials)
                {
                    AuthenticationService.Instance.ClearSessionToken();
                    Debug.Log("[UGSAuthService] Token de sesión borrado. El próximo inicio de sesión generará un nuevo PlayerId.");
                }

                AuthenticationService.Instance.SignOut();

                if (PlayerAccountService.Instance.IsSignedIn)
                {
                    PlayerAccountService.Instance.SignOut();
                }

                Debug.Log("[UGSAuthService] Sesión cerrada correctamente.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UGSAuthService] Error al cerrar sesión: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea una cuenta nueva con usuario y contraseña y deja la sesión iniciada.
        /// </summary>
        public Task<bool> SignUpWithUsernamePasswordAsync(string username, string password)
        {
            return RunCredentialOperationAsync(
                "registro",
                () => AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password),
                username,
                isLink: false);
        }

        /// <summary>
        /// Inicia sesión en una cuenta existente de usuario y contraseña.
        /// </summary>
        public Task<bool> SignInWithUsernamePasswordAsync(string username, string password)
        {
            return RunCredentialOperationAsync(
                "inicio de sesión",
                () => AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password),
                username,
                isLink: false);
        }

        /// <summary>
        /// Vincula usuario y contraseña a la sesión anónima en curso. El PlayerId no cambia,
        /// así que el progreso del jugador sobrevive al cambio de dispositivo.
        /// </summary>
        public Task<bool> LinkUsernamePasswordAsync(string username, string password)
        {
            if (!IsSignedIn)
            {
                const string msg = "No hay sesión activa que vincular. Entra como invitado primero.";
                Debug.LogWarning($"[UGSAuthService] {msg}");
                OnSignInFailed?.Invoke(msg);
                return Task.FromResult(false);
            }

            return RunCredentialOperationAsync(
                "vinculación",
                () => AuthenticationService.Instance.AddUsernamePasswordAsync(username, password),
                username,
                isLink: true);
        }

        /// <summary>
        /// Abre el navegador del sistema para iniciar sesión con una cuenta de Unity y, con el
        /// token que devuelve, completa el login en UGS. El evento SignedIn de
        /// AuthenticationService (ya suscrito en SubscribeToEvents) dispara OnSignedIn solo con
        /// esto: no hace falta escuchar también los eventos de PlayerAccountService.
        /// </summary>
        public async Task<bool> SignInWithUnityAsync()
        {
            if (_isSigningIn)
            {
                Debug.LogWarning("[UGSAuthService] Ya hay una operación de cuenta en curso; se ignora el login con Unity.");
                return false;
            }

            if (!IsInitialized)
            {
                await InitializeAsync();
                if (!IsInitialized)
                {
                    OnSignInFailed?.Invoke("No se pudo contactar con Unity Gaming Services.");
                    return false;
                }
            }

            _isSigningIn = true;

            try
            {
                if (!PlayerAccountService.Instance.IsSignedIn)
                {
                    Debug.Log("[UGSAuthService] Abriendo el navegador para iniciar sesión con una cuenta de Unity...");
                    await PlayerAccountService.Instance.StartSignInAsync();
                }

                await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                Debug.Log($"[UGSAuthService] Login con cuenta de Unity correcto. PlayerId: {PlayerId}");
                return true;
            }
            catch (PlayerAccountsException paEx)
            {
                string errorMsg = $"No se pudo iniciar sesión con Unity: {paEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (AuthenticationException authEx)
            {
                string errorMsg = TranslateAuthError(authEx, "login con Unity");
                Debug.LogError($"[UGSAuthService] {errorMsg} (ErrorCode {authEx.ErrorCode}): {authEx.Message}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (RequestFailedException reqEx)
            {
                string errorMsg = $"Error de conexión durante el login con Unity ({reqEx.ErrorCode}): {reqEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            finally
            {
                _isSigningIn = false;
            }
        }

        /// <summary>
        /// Vincula una cuenta de Unity a la sesión anónima en curso. El PlayerId no cambia,
        /// igual que al vincular usuario y contraseña.
        /// </summary>
        public async Task<bool> LinkWithUnityAsync()
        {
            if (!IsSignedIn)
            {
                const string msg = "No hay sesión activa que vincular. Entra como invitado primero.";
                Debug.LogWarning($"[UGSAuthService] {msg}");
                OnSignInFailed?.Invoke(msg);
                return false;
            }

            if (_isSigningIn)
            {
                Debug.LogWarning("[UGSAuthService] Hay otra operación de cuenta en curso; se ignora la vinculación con Unity.");
                return false;
            }

            _isSigningIn = true;

            try
            {
                if (!PlayerAccountService.Instance.IsSignedIn)
                {
                    Debug.Log("[UGSAuthService] Abriendo el navegador para vincular una cuenta de Unity...");
                    await PlayerAccountService.Instance.StartSignInAsync();
                }

                await AuthenticationService.Instance.LinkWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                Debug.Log($"[UGSAuthService] Cuenta de Unity vinculada. PlayerId conservado: {PlayerId}");
                OnAccountLinked?.Invoke("tu cuenta de Unity");
                return true;
            }
            catch (AuthenticationException authEx) when (authEx.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                const string msg = "Esa cuenta de Unity ya está vinculada a otro jugador.";
                Debug.LogError($"[UGSAuthService] {msg} (ErrorCode {authEx.ErrorCode}): {authEx.Message}");
                OnSignInFailed?.Invoke(msg);
                return false;
            }
            catch (PlayerAccountsException paEx)
            {
                string errorMsg = $"No se pudo vincular la cuenta de Unity: {paEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (AuthenticationException authEx)
            {
                string errorMsg = TranslateAuthError(authEx, "vinculación con Unity");
                Debug.LogError($"[UGSAuthService] {errorMsg} (ErrorCode {authEx.ErrorCode}): {authEx.Message}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (RequestFailedException reqEx)
            {
                string errorMsg = $"Error de conexión durante la vinculación con Unity ({reqEx.ErrorCode}): {reqEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            finally
            {
                _isSigningIn = false;
            }
        }

        /// <summary>
        /// Tronco común de las tres operaciones con credenciales: evita solapamientos, garantiza
        /// la inicialización, ejecuta la llamada al SDK y traduce los fallos a mensajes de UI.
        /// </summary>
        private async Task<bool> RunCredentialOperationAsync(
            string operationName,
            Func<Task> operation,
            string username,
            bool isLink)
        {
            if (_isSigningIn)
            {
                Debug.LogWarning($"[UGSAuthService] Hay otra operación de cuenta en curso; se ignora el {operationName}.");
                return false;
            }

            if (!IsInitialized)
            {
                await InitializeAsync();
                if (!IsInitialized)
                {
                    OnSignInFailed?.Invoke("No se pudo contactar con Unity Gaming Services.");
                    return false;
                }
            }

            _isSigningIn = true;

            try
            {
                Debug.Log($"[UGSAuthService] Iniciando {operationName} para el usuario '{username}'...");
                await operation();

                if (isLink)
                {
                    Debug.Log($"[UGSAuthService] Cuenta vinculada como '{username}'. PlayerId conservado: {PlayerId}");
                    OnAccountLinked?.Invoke(username);
                }
                else
                {
                    Debug.Log($"[UGSAuthService] {operationName} correcto. PlayerId: {PlayerId}");
                }

                return true;
            }
            catch (AuthenticationException authEx)
            {
                string errorMsg = TranslateAuthError(authEx, operationName);
                Debug.LogError($"[UGSAuthService] {errorMsg} (ErrorCode {authEx.ErrorCode}): {authEx.Message}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            catch (RequestFailedException reqEx)
            {
                string errorMsg = $"Error de conexión durante el {operationName} ({reqEx.ErrorCode}): {reqEx.Message}";
                Debug.LogError($"[UGSAuthService] {errorMsg}");
                OnSignInFailed?.Invoke(errorMsg);
                return false;
            }
            finally
            {
                _isSigningIn = false;
            }
        }

        /// <summary>
        /// Traduce los códigos de AuthenticationErrorCodes a mensajes que el jugador entienda.
        /// </summary>
        private static string TranslateAuthError(AuthenticationException ex, string operationName)
        {
            if (ex.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
            {
                return "Usuario o contraseña no válidos. La contraseña necesita entre 8 y 30 caracteres, " +
                       "con al menos una mayúscula, una minúscula, un número y un símbolo.";
            }

            if (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                return "Ese nombre de usuario ya está en uso por otra cuenta.";
            }

            if (ex.ErrorCode == AuthenticationErrorCodes.AccountLinkLimitExceeded)
            {
                return "Esta cuenta ya tiene credenciales vinculadas.";
            }

            if (ex.ErrorCode == AuthenticationErrorCodes.ClientInvalidUserState)
            {
                return "La sesión no permite esta operación ahora mismo. Cierra sesión e inténtalo de nuevo.";
            }

            if (ex.ErrorCode == AuthenticationErrorCodes.BannedUser)
            {
                return "Esta cuenta está suspendida.";
            }

            return $"No se pudo completar el {operationName}: {ex.Message}";
        }

        private void SubscribeToEvents()
        {
            if (!IsInitialized) return;

            AuthenticationService.Instance.SignedIn += HandleSignedIn;
            AuthenticationService.Instance.SignedOut += HandleSignedOut;
            AuthenticationService.Instance.SignInFailed += HandleSignInFailed;
            AuthenticationService.Instance.Expired += HandleSessionExpired;
        }

        private void UnsubscribeFromEvents()
        {
            if (!IsInitialized) return;

            AuthenticationService.Instance.SignedIn -= HandleSignedIn;
            AuthenticationService.Instance.SignedOut -= HandleSignedOut;
            AuthenticationService.Instance.SignInFailed -= HandleSignInFailed;
            AuthenticationService.Instance.Expired -= HandleSessionExpired;
        }

        private void HandleSignedIn()
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"[UGSAuthService] Evento SignedIn recibido. PlayerId: {playerId}");
            OnSignedIn?.Invoke(playerId);
        }

        private void HandleSignedOut()
        {
            Debug.Log("[UGSAuthService] Evento SignedOut recibido.");
            OnSignedOut?.Invoke();
        }

        private void HandleSignInFailed(RequestFailedException exception)
        {
            string errorMsg = $"Fallo en login ({exception.ErrorCode}): {exception.Message}";
            Debug.LogError($"[UGSAuthService] Evento SignInFailed recibido: {errorMsg}");
            OnSignInFailed?.Invoke(errorMsg);
        }

        private void HandleSessionExpired()
        {
            Debug.LogWarning("[UGSAuthService] La sesión de autenticación ha expirado.");
            OnSignedOut?.Invoke();
        }
    }
}
