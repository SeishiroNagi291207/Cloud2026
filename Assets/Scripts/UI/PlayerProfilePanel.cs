using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Core;
using Cloud2026.Services;

namespace Cloud2026.UI
{
    /// <summary>
    /// Demo de Cloud Save: guarda y carga el perfil del jugador (nivel, XP, misiones
    /// completadas, desbloqueos) directamente desde el cliente.
    ///
    /// Esto es distinto de Economy/Cloud Code: aquí no hay ningún valor de juego en disputa
    /// (saldo, precio, recompensa). "Simular progreso" suma una cantidad fija de XP y marca
    /// una misión y un desbloqueo de prueba a mano, como haría cualquier sistema de gameplay
    /// real antes de pedirle a Cloud Save que lo recuerde. El mecanismo de guardado es la
    /// lección de esta semana; la autoridad del servidor sobre el VALOR de ese progreso llega
    /// con Cloud Code + Economy en las semanas 6-8.
    /// </summary>
    public class PlayerProfilePanel : MonoBehaviour
    {
        private const int SimulatedExperienceGain = 25;
        private const string TestMissionId = "tutorial_mission_01";
        private const string TestUnlockId = "skin_test";

        [Header("Perfil")]
        [Tooltip("Muestra nivel, XP, misiones completadas y desbloqueos del perfil actual en memoria.")]
        [SerializeField] private TextMeshProUGUI profileText;

        [Header("Acciones")]
        [Tooltip("Suma XP y una misión/desbloqueo de prueba al perfil en memoria, y lo guarda en Cloud Save.")]
        [SerializeField] private Button simulateProgressButton;

        [Tooltip("Carga el perfil desde Cloud Save, comparándolo contra la última copia guardada localmente.")]
        [SerializeField] private Button loadFromCloudButton;

        [Header("Feedback")]
        [Tooltip("Texto compartido para mensajes de estado, avisos y errores.")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Tooltip("Se activa mientras hay una operación en vuelo.")]
        [SerializeField] private GameObject loadingIndicator;

        [Header("Navegación (flujo unificado)")]
        [Tooltip("Root del Menú principal externo (p.ej. AnonymousLoginUI) a reactivar al volver.")]
        [SerializeField] private GameObject mainMenuRoot;

        [Tooltip("GameObject a desactivar al volver al menú. Si se deja vacío, se desactiva este mismo objeto.")]
        [SerializeField] private GameObject perfilRoot;

        [Tooltip("Botón para volver al Menú principal.")]
        [SerializeField] private Button backToMenuButton;

        private static readonly Color ColorOk = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color ColorError = new Color(1f, 0.35f, 0.35f);
        private static readonly Color ColorAviso = new Color(1f, 0.85f, 0.3f);

        private ICloudSaveService _cloudSaveService;
        private PlayerProfile _profile;
        private bool _isBusy;

        private void Start()
        {
            FindService();
            WireButtons();
            RenderProfile();
        }

        private void OnDestroy()
        {
            if (_cloudSaveService != null)
            {
                _cloudSaveService.OnCallFailed -= HandleCallFailed;
            }

            if (simulateProgressButton != null) simulateProgressButton.onClick.RemoveListener(OnSimulateProgressClicked);
            if (loadFromCloudButton != null) loadFromCloudButton.onClick.RemoveListener(OnLoadFromCloudClicked);
            if (backToMenuButton != null) backToMenuButton.onClick.RemoveListener(OnBackToMenuClicked);
        }

        private void FindService()
        {
            if (GameBootstrap.Instance != null)
            {
                _cloudSaveService = GameBootstrap.Instance.CloudSaveService;
            }

            _cloudSaveService ??= FindFirstObjectByType<UGSCloudSaveService>();

            if (_cloudSaveService != null)
            {
                _cloudSaveService.OnCallFailed += HandleCallFailed;
            }
            else
            {
                SetText(statusText, "No se encontró el servicio de Cloud Save.", ColorError);
            }
        }

        private void WireButtons()
        {
            if (simulateProgressButton != null) simulateProgressButton.onClick.AddListener(OnSimulateProgressClicked);
            if (loadFromCloudButton != null) loadFromCloudButton.onClick.AddListener(OnLoadFromCloudClicked);
            if (backToMenuButton != null) backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
        }

        /// <summary>Cierra el panel de Perfil y devuelve el control al Menú principal externo.</summary>
        private void OnBackToMenuClicked()
        {
            if (_isBusy) return;

            if (mainMenuRoot != null)
            {
                mainMenuRoot.SetActive(true);
            }

            if (perfilRoot != null)
            {
                perfilRoot.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // --- Acciones --------------------------------------------------------

        private async void OnSimulateProgressClicked()
        {
            if (_isBusy || _cloudSaveService == null) return;

            if (!_cloudSaveService.IsReady)
            {
                SetText(statusText, "Necesitas iniciar sesión antes de guardar tu perfil.", ColorAviso);
                return;
            }

            // Progreso de ejemplo para demostrar el guardado, no una recompensa calculada:
            // no hay ninguna autoridad de servidor decidiendo nada aquí todavía.
            _profile ??= new PlayerProfile();
            _profile.Experience += SimulatedExperienceGain;

            if (!_profile.CompletedMissionIds.Contains(TestMissionId))
            {
                _profile.CompletedMissionIds.Add(TestMissionId);
            }

            if (!_profile.UnlockedIds.Contains(TestUnlockId))
            {
                _profile.UnlockedIds.Add(TestUnlockId);
            }

            SetBusy(true);
            SetText(statusText, "Guardando perfil en Cloud Save...", Color.white);

            var ok = await _cloudSaveService.SaveProfileAsync(_profile);

            SetBusy(false);
            RenderProfile();

            if (ok)
            {
                SetText(statusText, "Perfil guardado en la nube.", ColorOk);
            }
        }

        private async void OnLoadFromCloudClicked()
        {
            if (_isBusy || _cloudSaveService == null) return;

            if (!_cloudSaveService.IsReady)
            {
                SetText(statusText, "Necesitas iniciar sesión antes de cargar tu perfil.", ColorAviso);
                return;
            }

            SetBusy(true);
            SetText(statusText, "Cargando perfil desde Cloud Save...", Color.white);

            var result = await _cloudSaveService.LoadProfileAsync();

            SetBusy(false);
            ApplyLoadResult(result);
        }

        private void ApplyLoadResult(PlayerProfileLoadResult result)
        {
            if (result == null) return;

            switch (result.Outcome)
            {
                case CloudSaveOutcome.NotFound:
                    _profile = new PlayerProfile();
                    SetText(statusText, "Todavía no hay perfil guardado. Empezando de cero.", Color.white);
                    break;

                case CloudSaveOutcome.LocalIsNewer:
                    // No se pisa nada en silencio: había progreso local que nunca llegó a la
                    // nube (por ejemplo, el juego se cerró a medio guardar). Se muestra ese
                    // progreso local y se avisa; guardarlo de nuevo lo sube a la nube.
                    _profile = result.Profile;
                    SetText(statusText,
                        "Aviso: tienes progreso local más reciente que el de la nube (un guardado " +
                        "anterior no llegó a completarse). Se muestra tu copia local; pulsa " +
                        "'Simular progreso y guardar' para subirla.",
                        ColorAviso);
                    break;

                case CloudSaveOutcome.Loaded:
                    _profile = result.Profile;
                    SetText(statusText, "Perfil cargado desde la nube.", ColorOk);
                    break;

                default:
                    // Error: el mensaje real ya llegó por HandleCallFailed.
                    break;
            }

            RenderProfile();
        }

        private void HandleCallFailed(string message)
        {
            SetBusy(false);
            SetText(statusText, message, ColorError);
        }

        // --- Pintado -----------------------------------------------------------

        private void RenderProfile()
        {
            if (profileText == null) return;

            if (_profile == null)
            {
                profileText.text = "Sin perfil cargado todavía.";
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Nivel {_profile.Level} · XP {_profile.Experience}");
            builder.AppendLine($"Misiones completadas: {DescribeList(_profile.CompletedMissionIds)}");
            builder.AppendLine($"Desbloqueos: {DescribeList(_profile.UnlockedIds)}");
            builder.Append("Última actualización: ");
            builder.Append(string.IsNullOrEmpty(_profile.UpdatedAtUtc) ? "todavía no guardado" : _profile.UpdatedAtUtc);

            profileText.text = builder.ToString();
        }

        private static string DescribeList(List<string> items)
        {
            return items == null || items.Count == 0 ? "ninguna" : string.Join(", ", items);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;

            if (loadingIndicator != null) loadingIndicator.SetActive(busy);
            if (simulateProgressButton != null) simulateProgressButton.interactable = !busy;
            if (loadFromCloudButton != null) loadFromCloudButton.interactable = !busy;
        }

        private static void SetText(TextMeshProUGUI label, string message, Color color)
        {
            if (label == null) return;

            label.text = message;
            label.color = color;
        }
    }
}
