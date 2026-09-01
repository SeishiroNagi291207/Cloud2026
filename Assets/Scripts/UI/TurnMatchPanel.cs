using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Core;
using Cloud2026.Services;

namespace Cloud2026.UI
{
    /// <summary>
    /// Flujo completo del PoC: Login -> Play -> Partida.
    ///
    /// Todo lo que se ve aquí lo decide el servidor. Esta clase no sabe de quién es
    /// el turno ni cuántos turnos van: lee <see cref="MatchViewDto"/> y lo pinta.
    /// La única lógica propia es cuándo refrescar y qué botón habilitar.
    /// </summary>
    public class TurnMatchPanel : MonoBehaviour
    {
        private enum Screen
        {
            Login,
            Lobby,
            Match
        }

        [Header("Login")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private Button guestLoginButton;
        [SerializeField] private TextMeshProUGUI loginStatusText;

        [Header("Play")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private Button createMatchButton;
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button joinMatchButton;
        [SerializeField] private TextMeshProUGUI lobbyStatusText;

        [Header("Partida")]
        [SerializeField] private GameObject matchPanel;
        [SerializeField] private TextMeshProUGUI matchCodeText;
        [SerializeField] private TextMeshProUGUI turnText;
        [SerializeField] private TextMeshProUGUI historyText;
        [SerializeField] private TextMeshProUGUI outcomeText;
        [SerializeField] private Button passTurnButton;
        [SerializeField] private Button resendTurnButton;
        [SerializeField] private Button leaveMatchButton;

        [Header("Sondeo")]
        [Tooltip("Cada cuántos segundos se pregunta al servidor si el rival ya ha jugado.")]
        [SerializeField] private float pollIntervalSeconds = 2f;

        [Header("Navegación (flujo unificado)")]
        [Tooltip("Root del Menú principal externo (p.ej. AnonymousLoginUI) a reactivar al volver.")]
        [SerializeField] private GameObject mainMenuRoot;

        [Tooltip("GameObject a desactivar al volver al menú. Si se deja vacío, se desactiva este mismo objeto.")]
        [SerializeField] private GameObject partidaRoot;

        [Tooltip("Botón visible solo en Lobby para volver al Menú principal.")]
        [SerializeField] private Button backToMenuButton;

        private IAuthService _authService;
        private ITurnMatchService _matchService;

        private Screen _screen = Screen.Login;
        private MatchViewDto _view;
        private bool _isBusy;
        private float _pollTimer;

        private void Start()
        {
            FindServices();
            WireButtons();
            GoTo(Screen.Login);
        }

        private void OnDestroy()
        {
            if (_matchService != null)
            {
                _matchService.OnCallFailed -= HandleCallFailed;
            }

            if (guestLoginButton != null) guestLoginButton.onClick.RemoveListener(OnGuestLoginClicked);
            if (createMatchButton != null) createMatchButton.onClick.RemoveListener(OnCreateMatchClicked);
            if (joinMatchButton != null) joinMatchButton.onClick.RemoveListener(OnJoinMatchClicked);
            if (passTurnButton != null) passTurnButton.onClick.RemoveListener(OnPassTurnClicked);
            if (resendTurnButton != null) resendTurnButton.onClick.RemoveListener(OnResendTurnClicked);
            if (leaveMatchButton != null) leaveMatchButton.onClick.RemoveListener(OnLeaveMatchClicked);
            if (backToMenuButton != null) backToMenuButton.onClick.RemoveListener(OnBackToMenuClicked);
        }

        private void Update()
        {
            // El login es asíncrono: en cuanto haya sesión, pasamos a la pantalla
            // de juego. Comprobar el estado es más simple que encadenar eventos.
            if (_screen == Screen.Login && _matchService != null && _matchService.IsReady)
            {
                GoTo(Screen.Lobby);
                return;
            }

            if (_screen != Screen.Match || _isBusy) return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = pollIntervalSeconds;
                RefreshMatch();
            }
        }

        private void FindServices()
        {
            if (GameBootstrap.Instance != null)
            {
                _authService = GameBootstrap.Instance.AuthService;
                _matchService = GameBootstrap.Instance.TurnMatchService;
            }

            _authService ??= FindFirstObjectByType<UGSAuthService>();
            _matchService ??= FindFirstObjectByType<UGSTurnMatchService>();

            if (_matchService != null)
            {
                _matchService.OnCallFailed += HandleCallFailed;
            }
        }

        private void WireButtons()
        {
            if (guestLoginButton != null) guestLoginButton.onClick.AddListener(OnGuestLoginClicked);
            if (createMatchButton != null) createMatchButton.onClick.AddListener(OnCreateMatchClicked);
            if (joinMatchButton != null) joinMatchButton.onClick.AddListener(OnJoinMatchClicked);
            if (passTurnButton != null) passTurnButton.onClick.AddListener(OnPassTurnClicked);
            if (resendTurnButton != null) resendTurnButton.onClick.AddListener(OnResendTurnClicked);
            if (leaveMatchButton != null) leaveMatchButton.onClick.AddListener(OnLeaveMatchClicked);
            if (backToMenuButton != null) backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
        }

        // --- Login ---------------------------------------------------------

        private async void OnGuestLoginClicked()
        {
            if (_isBusy || _authService == null) return;

            SetBusy(true);
            SetText(loginStatusText, "Entrando como invitado...", Color.white);

            var ok = await _authService.SignInAnonymouslyAsync();

            SetBusy(false);
            SetText(loginStatusText,
                ok ? "Sesión iniciada." : "No se pudo iniciar sesión.",
                ok ? Color.white : Color.red);
        }

        // --- Play ----------------------------------------------------------

        private async void OnCreateMatchClicked()
        {
            if (_isBusy || _matchService == null) return;

            SetBusy(true);
            SetText(lobbyStatusText, "Creando partida...", Color.white);

            var view = await _matchService.CreateMatchAsync();

            SetBusy(false);
            if (view == null) return;

            _view = view;
            GoTo(Screen.Match);
        }

        private async void OnJoinMatchClicked()
        {
            if (_isBusy || _matchService == null) return;

            var code = joinCodeInput != null ? joinCodeInput.text : string.Empty;

            SetBusy(true);
            SetText(lobbyStatusText, $"Uniéndote a {code.ToUpperInvariant()}...", Color.white);

            var view = await _matchService.JoinMatchAsync(code);

            SetBusy(false);
            if (view == null) return;

            _view = view;
            GoTo(Screen.Match);
        }

        // --- Partida -------------------------------------------------------

        private async void OnPassTurnClicked()
        {
            if (_isBusy || _matchService == null || _view == null) return;

            SetBusy(true);

            // Le decimos al servidor sobre qué turno creemos estar jugando. Si ya
            // ha avanzado, preferimos que nos rechace a que aplique la jugada
            // sobre un estado que el jugador no llegó a ver.
            var view = await _matchService.SubmitTurnAsync(_view.TurnNumber);

            SetBusy(false);
            ApplyResult(view);
        }

        private async void OnResendTurnClicked()
        {
            if (_isBusy || _matchService == null) return;

            SetBusy(true);
            var view = await _matchService.ResendLastTurnAsync();
            SetBusy(false);

            ApplyResult(view);
        }

        private void OnLeaveMatchClicked()
        {
            if (_isBusy || _matchService == null) return;

            _matchService.LeaveMatch();
            _view = null;
            GoTo(Screen.Lobby);
        }

        /// <summary>
        /// Cierra la Partida y devuelve el control al Menú principal externo. Solo
        /// tiene sentido desde Lobby: en Match hay que salir de la partida primero.
        /// </summary>
        private void OnBackToMenuClicked()
        {
            if (_isBusy) return;

            if (mainMenuRoot != null)
            {
                mainMenuRoot.SetActive(true);
            }

            if (partidaRoot != null)
            {
                partidaRoot.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private async void RefreshMatch()
        {
            if (_matchService == null) return;

            var view = await _matchService.RefreshAsync();
            if (view == null) return;

            _view = view;
            RenderMatch();
        }

        private void ApplyResult(MatchViewDto view)
        {
            if (view == null) return;

            _view = view;
            RenderMatch();
            SetText(outcomeText, DescribeOutcome(view), OutcomeColor(view.Outcome));
        }

        // --- Pintado -------------------------------------------------------

        private void GoTo(Screen screen)
        {
            _screen = screen;
            _pollTimer = 0f;

            if (loginPanel != null) loginPanel.SetActive(screen == Screen.Login);
            if (lobbyPanel != null) lobbyPanel.SetActive(screen == Screen.Lobby);
            if (matchPanel != null) matchPanel.SetActive(screen == Screen.Match);

            if (screen == Screen.Match)
            {
                RenderMatch();
                SetText(outcomeText, _view != null ? DescribeOutcome(_view) : string.Empty,
                    _view != null ? OutcomeColor(_view.Outcome) : Color.white);
            }
        }

        private void RenderMatch()
        {
            if (_view == null) return;

            SetText(matchCodeText, $"Código:  <b>{_view.MatchCode}</b>", Color.white);

            if (_view.Status == MatchStatusValues.WaitingForGuest)
            {
                SetText(turnText, "Esperando a que alguien se una con ese código...", Color.yellow);
            }
            else
            {
                SetText(turnText,
                    _view.IsYourTurn
                        ? $"Turno {_view.TurnNumber} · <b>te toca a ti</b>"
                        : $"Turno {_view.TurnNumber} · le toca a {Short(_view.OpponentPlayerId)}",
                    _view.IsYourTurn ? new Color(0.4f, 1f, 0.5f) : Color.white);
            }

            SetText(historyText, BuildHistory(), new Color(0.7f, 0.75f, 0.85f));

            if (passTurnButton != null)
            {
                passTurnButton.interactable = !_isBusy && _view.Status == MatchStatusValues.Playing && _view.IsYourTurn;
            }

            if (resendTurnButton != null)
            {
                resendTurnButton.interactable = !_isBusy && _view.Status == MatchStatusValues.Playing;
            }
        }

        private string BuildHistory()
        {
            if (_view.History == null || _view.History.Count == 0)
            {
                return "Sin jugadas todavía.";
            }

            var builder = new StringBuilder();
            foreach (var record in _view.History)
            {
                var quien = record.PlayerId == _view.YourPlayerId ? "tú" : Short(record.PlayerId);
                builder.AppendLine($"Turno {record.TurnNumber} · {quien}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string DescribeOutcome(MatchViewDto view)
        {
            switch (view.Outcome)
            {
                case MatchOutcome.Applied:
                    return "Jugada aplicada. La partida avanzó un turno.";

                case MatchOutcome.Replayed:
                    return "REPETIDA: el servidor reconoció esta petición y NO volvió a aplicarla. " +
                           view.Message;

                case MatchOutcome.Conflict:
                case MatchOutcome.Stale:
                case MatchOutcome.NotYourTurn:
                case MatchOutcome.NotStarted:
                    return view.Message;

                default:
                    return string.Empty;
            }
        }

        private static Color OutcomeColor(string outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.Applied: return new Color(0.4f, 1f, 0.5f);
                case MatchOutcome.Replayed: return new Color(1f, 0.85f, 0.3f);
                case MatchOutcome.Conflict:
                case MatchOutcome.Stale: return new Color(1f, 0.6f, 0.3f);
                case MatchOutcome.NotYourTurn:
                case MatchOutcome.NotStarted: return Color.white;
                default: return Color.white;
            }
        }

        /// <summary>Los PlayerId son largos; en pantalla basta con el principio.</summary>
        private static string Short(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "nadie";
            return playerId.Length <= 8 ? playerId : playerId.Substring(0, 8);
        }

        private void HandleCallFailed(string message)
        {
            var target = _screen switch
            {
                Screen.Login => loginStatusText,
                Screen.Lobby => lobbyStatusText,
                _ => outcomeText
            };

            SetText(target, message, Color.red);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;

            if (guestLoginButton != null) guestLoginButton.interactable = !busy;
            if (createMatchButton != null) createMatchButton.interactable = !busy;
            if (joinMatchButton != null) joinMatchButton.interactable = !busy;
            if (leaveMatchButton != null) leaveMatchButton.interactable = !busy;
            if (backToMenuButton != null) backToMenuButton.interactable = !busy;

            if (busy)
            {
                if (passTurnButton != null) passTurnButton.interactable = false;
                if (resendTurnButton != null) resendTurnButton.interactable = false;
            }
            else if (_screen == Screen.Match)
            {
                RenderMatch();
            }
        }

        private static void SetText(TextMeshProUGUI label, string message, Color color)
        {
            if (label == null) return;

            label.text = message;
            label.color = color;
        }
    }
}
