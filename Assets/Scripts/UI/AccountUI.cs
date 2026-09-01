using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Core;
using Cloud2026.Services;

namespace Cloud2026.UI
{
    /// <summary>
    /// Controlador de UI para cuentas con usuario y contraseña: registro, inicio de sesión y
    /// vinculación de una sesión anónima existente.
    ///
    /// Convive con <see cref="AnonymousLoginUI"/>: aquel gestiona la visibilidad de los paneles
    /// de sesión abierta y cerrada, y este solo los controles de credenciales que cuelgan de ellos.
    ///
    /// No valida las reglas de usuario y contraseña en cliente: las comprueba el servidor de UGS
    /// y aquí se muestra el mensaje que devuelva. El cliente no es autoridad sobre qué credencial
    /// es aceptable, y duplicar las reglas aquí solo garantizaría que acaben desincronizadas.
    /// </summary>
    public class AccountUI : MonoBehaviour
    {
        [Header("Acceso con credenciales (panel de sesión cerrada)")]
        [Tooltip("Campo de nombre de usuario para registro e inicio de sesión.")]
        [SerializeField] private TMP_InputField usernameInput;

        [Tooltip("Campo de contraseña. Debe tener ContentType = Password en el inspector.")]
        [SerializeField] private TMP_InputField passwordInput;

        [Tooltip("Crea una cuenta nueva con las credenciales introducidas.")]
        [SerializeField] private Button signUpButton;

        [Tooltip("Inicia sesión en una cuenta ya existente.")]
        [SerializeField] private Button signInButton;

        [Tooltip("Abre el navegador para iniciar sesión con una cuenta de Unity (Unity Player Accounts).")]
        [SerializeField] private Button signInWithUnityButton;

        [Header("Vinculación (panel de sesión iniciada)")]
        [Tooltip("Contenedor visible solo mientras la sesión sea anónima.")]
        [SerializeField] private GameObject linkGroup;

        [SerializeField] private TMP_InputField linkUsernameInput;
        [SerializeField] private TMP_InputField linkPasswordInput;
        [SerializeField] private Button linkButton;

        [Tooltip("Vincula una cuenta de Unity a la sesión anónima en curso.")]
        [SerializeField] private Button linkWithUnityButton;

        [Tooltip("Muestra si la sesión es de invitado o a qué cuenta está vinculada.")]
        [SerializeField] private TextMeshProUGUI accountStateText;

        [Header("Feedback")]
        [Tooltip("Texto compartido para mensajes de estado y errores.")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Tooltip("Se activa mientras hay una operación en vuelo.")]
        [SerializeField] private GameObject loadingIndicator;

        private static readonly Color ColorOk = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color ColorError = new Color(1f, 0.35f, 0.35f);
        private static readonly Color ColorAviso = new Color(1f, 0.85f, 0.3f);

        private IAuthService _authService;
        private bool _isBusy;

        private void Start()
        {
            ConnectAuthService();
            SetupButtonListeners();
            UpdateUIState();
        }

        private void OnDestroy()
        {
            if (_authService != null)
            {
                _authService.OnSignedIn -= HandleSignedIn;
                _authService.OnSignedOut -= HandleSignedOut;
                _authService.OnAccountLinked -= HandleAccountLinked;
                _authService.OnSignInFailed -= HandleFailure;
            }

            if (signUpButton != null) signUpButton.onClick.RemoveListener(OnSignUpClicked);
            if (signInButton != null) signInButton.onClick.RemoveListener(OnSignInClicked);
            if (signInWithUnityButton != null) signInWithUnityButton.onClick.RemoveListener(OnSignInWithUnityClicked);
            if (linkButton != null) linkButton.onClick.RemoveListener(OnLinkClicked);
            if (linkWithUnityButton != null) linkWithUnityButton.onClick.RemoveListener(OnLinkWithUnityClicked);
        }

        private void ConnectAuthService()
        {
            if (GameBootstrap.Instance != null && GameBootstrap.Instance.AuthService != null)
            {
                _authService = GameBootstrap.Instance.AuthService;
            }
            else
            {
                _authService = FindFirstObjectByType<UGSAuthService>();
            }

            if (_authService == null)
            {
                SetStatus("No se encontró el servicio de autenticación.", ColorError);
                return;
            }

            _authService.OnSignedIn += HandleSignedIn;
            _authService.OnSignedOut += HandleSignedOut;
            _authService.OnAccountLinked += HandleAccountLinked;
            _authService.OnSignInFailed += HandleFailure;
        }

        private void SetupButtonListeners()
        {
            if (signUpButton != null) signUpButton.onClick.AddListener(OnSignUpClicked);
            if (signInButton != null) signInButton.onClick.AddListener(OnSignInClicked);
            if (signInWithUnityButton != null) signInWithUnityButton.onClick.AddListener(OnSignInWithUnityClicked);
            if (linkButton != null) linkButton.onClick.AddListener(OnLinkClicked);
            if (linkWithUnityButton != null) linkWithUnityButton.onClick.AddListener(OnLinkWithUnityClicked);
        }

        private async void OnSignUpClicked()
        {
            if (!TryReadCredentials(usernameInput, passwordInput, true, out string user, out string pass)) return;

            SetBusy(true);
            SetStatus("Creando cuenta...", Color.white);
            bool ok = await _authService.SignUpWithUsernamePasswordAsync(user, pass);
            SetBusy(false);

            if (ok)
            {
                SetStatus($"Cuenta {user} creada. Sesión iniciada.", ColorOk);
                ClearPasswords();
            }

            UpdateUIState();
        }

        private async void OnSignInClicked()
        {
            if (!TryReadCredentials(usernameInput, passwordInput, false, out string user, out string pass)) return;

            SetBusy(true);
            SetStatus("Iniciando sesión...", Color.white);
            bool ok = await _authService.SignInWithUsernamePasswordAsync(user, pass);
            SetBusy(false);

            if (ok)
            {
                SetStatus($"Bienvenido de nuevo, {user}.", ColorOk);
                ClearPasswords();
            }

            UpdateUIState();
        }

        private async void OnSignInWithUnityClicked()
        {
            if (_isBusy || _authService == null) return;

            SetBusy(true);
            SetStatus("Abriendo el navegador para iniciar sesión con Unity...", Color.white);
            bool ok = await _authService.SignInWithUnityAsync();
            SetBusy(false);

            if (ok)
            {
                SetStatus("Sesión iniciada con tu cuenta de Unity.", ColorOk);
            }

            UpdateUIState();
        }

        private async void OnLinkWithUnityClicked()
        {
            if (_isBusy || _authService == null) return;

            SetBusy(true);
            SetStatus("Abriendo el navegador para vincular tu cuenta de Unity...", Color.white);
            await _authService.LinkWithUnityAsync();
            SetBusy(false);

            UpdateUIState();
        }

        private async void OnLinkClicked()
        {
            if (!TryReadCredentials(linkUsernameInput, linkPasswordInput, true, out string user, out string pass)) return;

            SetBusy(true);
            SetStatus("Vinculando cuenta...", Color.white);
            bool ok = await _authService.LinkUsernamePasswordAsync(user, pass);
            SetBusy(false);

            if (ok)
            {
                ClearPasswords();
            }

            UpdateUIState();
        }

        /// <summary>
        /// Lee los campos y los pasa por <see cref="CredentialValidator"/> antes de gastar una
        /// llamada de red. El servidor sigue siendo la autoridad: esto solo adelanta el mensaje.
        /// </summary>
        /// <param name="checkPasswordRules">
        /// True al crear o vincular una cuenta. False al iniciar sesión: ahí la contraseña ya
        /// existe y comprobar su composición impediría entrar a quien la creó con otras reglas.
        /// </param>
        private bool TryReadCredentials(TMP_InputField userField, TMP_InputField passField,
            bool checkPasswordRules, out string username, out string password)
        {
            username = userField != null ? userField.text.Trim() : string.Empty;
            password = passField != null ? passField.text : string.Empty;

            if (_isBusy || _authService == null)
            {
                return false;
            }

            CredentialCheck check = CredentialValidator.Validate(username, password, checkPasswordRules);
            if (!check.IsValid)
            {
                SetStatus(check.Error, ColorAviso);
                return false;
            }

            return true;
        }

        private void HandleSignedIn(string playerId)
        {
            UpdateUIState();
        }

        private void HandleSignedOut()
        {
            UpdateUIState();
        }

        private void HandleAccountLinked(string username)
        {
            SetStatus($"Cuenta vinculada como {username}. Tu progreso ya no depende de este dispositivo.", ColorOk);
            UpdateUIState();
        }

        private void HandleFailure(string errorMessage)
        {
            SetBusy(false);
            SetStatus(errorMessage, ColorError);
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            bool isAnonymous = _authService != null && _authService.IsAnonymous;

            if (linkGroup != null)
            {
                linkGroup.SetActive(isAnonymous);
            }

            if (accountStateText != null)
            {
                if (_authService == null || !_authService.IsSignedIn)
                {
                    accountStateText.text = string.Empty;
                }
                else if (isAnonymous)
                {
                    accountStateText.text = "Sesión de invitado: vincula una cuenta para no perder el progreso.";
                    accountStateText.color = ColorAviso;
                }
                else if (!string.IsNullOrEmpty(_authService.Username))
                {
                    accountStateText.text = $"Cuenta: <color=#FFE600>{_authService.Username}</color>";
                    accountStateText.color = Color.white;
                }
                else
                {
                    // No anónimo pero sin Username: la identidad vinculada es una cuenta de
                    // Unity, no usuario/contraseña.
                    accountStateText.text = "Cuenta: <color=#FFE600>cuenta de Unity</color>";
                    accountStateText.color = Color.white;
                }
            }

            UpdateInteractable();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(busy);
            }

            UpdateInteractable();
        }

        private void UpdateInteractable()
        {
            bool canInteract = !_isBusy;

            if (signUpButton != null) signUpButton.interactable = canInteract;
            if (signInButton != null) signInButton.interactable = canInteract;
            if (signInWithUnityButton != null) signInWithUnityButton.interactable = canInteract;
            if (linkButton != null) linkButton.interactable = canInteract;
            if (linkWithUnityButton != null) linkWithUnityButton.interactable = canInteract;
            if (usernameInput != null) usernameInput.interactable = canInteract;
            if (passwordInput != null) passwordInput.interactable = canInteract;
            if (linkUsernameInput != null) linkUsernameInput.interactable = canInteract;
            if (linkPasswordInput != null) linkPasswordInput.interactable = canInteract;
        }

        private void ClearPasswords()
        {
            if (passwordInput != null) passwordInput.text = string.Empty;
            if (linkPasswordInput != null) linkPasswordInput.text = string.Empty;
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }
        }
    }
}
