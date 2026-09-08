using System;
using System.Threading.Tasks;

namespace Cloud2026.Services
{
    /// <summary>
    /// Contrato para guardar y cargar el perfil del jugador en Cloud Save. Desacopla la UI y
    /// el gameplay del SDK, igual que ICloudCodeService con Cloud Code.
    ///
    /// Este servicio persiste progreso propio del jugador (nivel, XP, misiones completadas,
    /// desbloqueos de UI), no economía: no decide saldo, precio ni recompensa. Esta semana la
    /// lección es el mecanismo de guardado en sí (SaveAsync/LoadAsync, conflictos, límites de
    /// tamaño); la autoridad del servidor sobre el VALOR del progreso llega con Cloud Code +
    /// Economy en las semanas 6-8.
    /// </summary>
    public interface ICloudSaveService
    {
        /// <summary>Se dispara cuando una llamada falla, con un mensaje apto para la UI.</summary>
        event Action<string> OnCallFailed;

        /// <summary>
        /// True si los servicios están inicializados y hay sesión iniciada. Cloud Save Player
        /// Data exige un jugador autenticado: sin sesión no hay a quién guardarle nada.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Carga el perfil, comparando la copia de la nube contra la última copia guardada en
        /// caché local. Ver <see cref="CloudSaveOutcome"/> para qué significa cada resultado.
        /// </summary>
        Task<PlayerProfileLoadResult> LoadProfileAsync();

        /// <summary>
        /// Sella el perfil con la hora actual y lo sube a Cloud Save. Devuelve false si la
        /// llamada falla o si el perfil no cabe dentro del límite de tamaño de este ejercicio;
        /// el motivo va al log y a <see cref="OnCallFailed"/>.
        /// </summary>
        Task<bool> SaveProfileAsync(PlayerProfile profile);
    }
}
