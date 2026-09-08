using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using CloudSaveSdk = Unity.Services.CloudSave.CloudSaveService;

namespace Cloud2026.Services
{
    /// <summary>
    /// Wrapper de Unity Gaming Services Cloud Save para el perfil del jugador. Guarda y carga
    /// directamente desde el cliente, a diferencia de Economy/Cloud Code: esto no es autoridad
    /// sobre ningún valor de juego, es sólo persistencia del progreso que el propio jugador
    /// controla (nivel, XP, misiones, desbloqueos). Ver ICloudSaveService para el porqué.
    ///
    /// Guarda todo el perfil bajo UNA clave (<see cref="KeyProfile"/>) como un único JSON, en
    /// vez de una clave por campo: así el guardado es una sola llamada atómica desde el punto
    /// de vista del cliente, y el esquema completo se ve de un vistazo en un solo sitio.
    /// </summary>
    public class UGSCloudSaveService : MonoBehaviour, ICloudSaveService
    {
        /// <summary>Clave de Cloud Save Player Data bajo la que vive todo el perfil.</summary>
        public const string KeyProfile = "player_profile";

        /// <summary>
        /// Clave de PlayerPrefs donde se cachea el último JSON guardado o cargado con éxito.
        /// Sirve para que PlayerProfileSync.ResolveLoadOutcome pueda detectar, en la próxima
        /// carga, si un guardado anterior nunca llegó a confirmarse en el servidor (por
        /// ejemplo, porque el juego se cerró a medio guardar) y así avisar en vez de pisar
        /// progreso local sin que el jugador se entere.
        /// </summary>
        private const string LocalCacheKey = "cloud2026.player_profile.local_cache";

        public event Action<string> OnCallFailed;

        public bool IsReady =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        private bool _isCalling;

        public async Task<PlayerProfileLoadResult> LoadProfileAsync()
        {
            if (!IsReady)
            {
                const string msg = "Necesitas iniciar sesión antes de cargar tu perfil.";
                Debug.LogWarning($"[UGSCloudSaveService] {msg}");
                OnCallFailed?.Invoke(msg);
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }

            if (_isCalling)
            {
                Debug.LogWarning("[UGSCloudSaveService] Ya hay una llamada en curso; se ignora esta.");
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }

            _isCalling = true;

            try
            {
                var local = ReadLocalCache();

                Debug.Log($"[UGSCloudSaveService] Cargando '{KeyProfile}' desde Cloud Save...");
                var results = await CloudSaveSdk.Instance.Data.Player.LoadAsync(new HashSet<string> { KeyProfile });

                PlayerProfile cloud = null;
                if (results.TryGetValue(KeyProfile, out var item))
                {
                    cloud = ParseProfile(item.Value.GetAsString());
                }

                var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

                switch (outcome)
                {
                    case CloudSaveOutcome.NotFound:
                        Debug.Log("[UGSCloudSaveService] No hay perfil ni en la nube ni en la caché local. Primera vez.");
                        return new PlayerProfileLoadResult(null, outcome);

                    case CloudSaveOutcome.LocalIsNewer:
                        // No se toca la caché: ya contiene el intento que no llegó a la nube.
                        // Devolvemos ese perfil local para que la UI lo muestre y avise, en vez
                        // de descartarlo silenciosamente a favor de una copia más vieja.
                        Debug.LogWarning("[UGSCloudSaveService] La caché local es más reciente que la nube: " +
                                         "un guardado anterior no llegó a completarse.");
                        return new PlayerProfileLoadResult(local, outcome);

                    case CloudSaveOutcome.Loaded:
                        WriteLocalCache(JsonUtility.ToJson(cloud));
                        return new PlayerProfileLoadResult(cloud, outcome);

                    default:
                        // ResolveLoadOutcome nunca debería devolver Error: ese valor lo usa
                        // este servicio sólo para sus propios fallos de red o de sesión.
                        return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
                }
            }
            catch (CloudSaveRateLimitedException rateEx)
            {
                Report($"Demasiadas llamadas seguidas a Cloud Save. Reinténtalo en {rateEx.RetryAfter} s.", rateEx);
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }
            catch (CloudSaveValidationException valEx)
            {
                Report(TranslateValidationError(valEx), valEx);
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }
            catch (CloudSaveException csEx)
            {
                Report(TranslateCloudSaveError(csEx), csEx);
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }
            catch (RequestFailedException reqEx)
            {
                Report($"Error de conexión con UGS ({reqEx.ErrorCode}): {reqEx.Message}", reqEx);
                return new PlayerProfileLoadResult(null, CloudSaveOutcome.Error);
            }
            finally
            {
                _isCalling = false;
            }
        }

        public async Task<bool> SaveProfileAsync(PlayerProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[UGSCloudSaveService] SaveProfileAsync recibió un perfil nulo; no hay nada que guardar.");
                return false;
            }

            if (!IsReady)
            {
                const string msg = "Necesitas iniciar sesión antes de guardar tu perfil.";
                Debug.LogWarning($"[UGSCloudSaveService] {msg}");
                OnCallFailed?.Invoke(msg);
                return false;
            }

            if (_isCalling)
            {
                Debug.LogWarning("[UGSCloudSaveService] Ya hay una llamada en curso; se ignora esta.");
                return false;
            }

            _isCalling = true;

            try
            {
                PlayerProfileSync.Stamp(profile, DateTime.UtcNow);
                var json = JsonUtility.ToJson(profile);

                // Falla alto y claro antes de gastar una llamada de red si el perfil ha
                // crecido más de lo razonable para este ejercicio.
                PlayerProfileSync.EnsureFitsWithinSizeLimit(json);

                // Se cachea ANTES de llamar al servidor, no después de confirmar éxito: si el
                // juego se cierra a media escritura, la próxima carga tiene que poder detectar
                // que este intento nunca llegó a completarse (ver LocalCacheKey).
                WriteLocalCache(json);

                Debug.Log($"[UGSCloudSaveService] Guardando '{KeyProfile}' en Cloud Save ({json.Length} caracteres)...");

                await CloudSaveSdk.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
                {
                    { KeyProfile, json }
                });

                Debug.Log("[UGSCloudSaveService] Perfil guardado.");
                return true;
            }
            catch (PlayerProfileTooLargeException sizeEx)
            {
                var msg = $"El perfil pesa demasiado para guardarlo ({sizeEx.ActualSizeBytes} de " +
                          $"{sizeEx.MaxSizeBytes} bytes permitidos en este ejercicio).";
                Debug.LogError($"[UGSCloudSaveService] {msg}");
                OnCallFailed?.Invoke(msg);
                return false;
            }
            catch (CloudSaveRateLimitedException rateEx)
            {
                Report($"Demasiadas llamadas seguidas a Cloud Save. Reinténtalo en {rateEx.RetryAfter} s.", rateEx);
                return false;
            }
            catch (CloudSaveValidationException valEx)
            {
                Report(TranslateValidationError(valEx), valEx);
                return false;
            }
            catch (CloudSaveException csEx)
            {
                Report(TranslateCloudSaveError(csEx), csEx);
                return false;
            }
            catch (RequestFailedException reqEx)
            {
                Report($"Error de conexión con UGS ({reqEx.ErrorCode}): {reqEx.Message}", reqEx);
                return false;
            }
            finally
            {
                _isCalling = false;
            }
        }

        private static PlayerProfile ParseProfile(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<PlayerProfile>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UGSCloudSaveService] No se pudo interpretar el perfil recibido: {ex.Message}");
                return null;
            }
        }

        private static PlayerProfile ReadLocalCache()
        {
            var json = PlayerPrefs.GetString(LocalCacheKey, string.Empty);
            return ParseProfile(json);
        }

        private static void WriteLocalCache(string json)
        {
            PlayerPrefs.SetString(LocalCacheKey, json);
            PlayerPrefs.Save();
        }

        private void Report(string message, Exception exception)
        {
            Debug.LogError($"[UGSCloudSaveService] {message}\n{exception}");
            OnCallFailed?.Invoke(message);
        }

        /// <summary>Traduce el motivo del fallo a un mensaje que el jugador entienda.</summary>
        private static string TranslateCloudSaveError(CloudSaveException ex)
        {
            switch (ex.Reason)
            {
                case CloudSaveExceptionReason.NoInternetConnection:
                    return "Sin conexión a internet.";

                case CloudSaveExceptionReason.Unauthorized:
                    return "La sesión no tiene permiso para guardar o cargar datos.";

                case CloudSaveExceptionReason.KeyLimitExceeded:
                    return "Se ha superado el número máximo de claves permitidas en Cloud Save.";

                case CloudSaveExceptionReason.NotFound:
                    return "No se encontró el dato solicitado en Cloud Save.";

                case CloudSaveExceptionReason.TooManyRequests:
                    return "Demasiadas peticiones seguidas a Cloud Save. Espera unos segundos.";

                case CloudSaveExceptionReason.ServiceUnavailable:
                    return "Cloud Save no está disponible ahora mismo. Inténtalo más tarde.";

                case CloudSaveExceptionReason.Conflict:
                    return "Otra escritura llegó antes y este guardado entró en conflicto.";

                case CloudSaveExceptionReason.InvalidArgument:
                    return "Los datos enviados no son válidos para Cloud Save.";

                case CloudSaveExceptionReason.ProjectIdMissing:
                case CloudSaveExceptionReason.PlayerIdMissing:
                case CloudSaveExceptionReason.AccessTokenMissing:
                    return "Falta información de sesión para hablar con Cloud Save. Vuelve a iniciar sesión.";

                default:
                    return $"No se pudo completar la operación con Cloud Save: {ex.Reason}";
            }
        }

        /// <summary>
        /// CloudSaveValidationException trae el detalle de qué campo rechazó el servidor.
        /// Se muestra tal cual: son mensajes pensados para depurar, no traducidos, pero más
        /// útiles en clase que el mensaje genérico de TranslateCloudSaveError.
        /// </summary>
        private static string TranslateValidationError(CloudSaveValidationException ex)
        {
            if (ex.Details == null || ex.Details.Count == 0)
            {
                return TranslateCloudSaveError(ex);
            }

            var messages = ex.Details.SelectMany(d => d.Messages ?? new List<string>());
            return "Cloud Save rechazó el perfil: " + string.Join(" ", messages);
        }
    }
}
