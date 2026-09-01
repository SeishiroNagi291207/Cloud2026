using System;
using System.Threading.Tasks;

namespace Cloud2026.Services
{
    /// <summary>
    /// Contrato para el servicio de autenticación de UGS.
    /// Desacopla la lógica de UI y Gameplay de la implementación concreta del SDK.
    /// </summary>
    public interface IAuthService
    {
        event Action<string> OnSignedIn;
        event Action OnSignedOut;
        event Action<string> OnSignInFailed;

        /// <summary>
        /// Se dispara al vincular credenciales a la sesión en curso. Lleva el nombre de usuario.
        /// </summary>
        event Action<string> OnAccountLinked;

        bool IsInitialized { get; }
        bool IsSignedIn { get; }
        string PlayerId { get; }
        string PlayerName { get; }

        /// <summary>
        /// Nombre de usuario de la cuenta con credenciales, o cadena vacía si la sesión es anónima.
        /// </summary>
        string Username { get; }

        /// <summary>True si la sesión tiene una cuenta de Unity (Player Accounts) vinculada.</summary>
        bool IsUnityAccountLinked { get; }

        /// <summary>
        /// True si hay sesión iniciada pero sin ninguna identidad persistente vinculada
        /// (ni usuario/contraseña ni cuenta de Unity). Ese progreso se pierde al desinstalar
        /// el juego o cambiar de dispositivo.
        /// </summary>
        bool IsAnonymous { get; }

        Task InitializeAsync();
        Task<bool> SignInAnonymouslyAsync();

        /// <summary>Crea una cuenta nueva con usuario y contraseña, y deja la sesión iniciada.</summary>
        Task<bool> SignUpWithUsernamePasswordAsync(string username, string password);

        /// <summary>Inicia sesión en una cuenta existente de usuario y contraseña.</summary>
        Task<bool> SignInWithUsernamePasswordAsync(string username, string password);

        /// <summary>
        /// Vincula usuario y contraseña a la sesión anónima actual. Conserva el mismo PlayerId,
        /// así que el progreso del jugador sobrevive al cambio de dispositivo.
        /// </summary>
        Task<bool> LinkUsernamePasswordAsync(string username, string password);

        /// <summary>
        /// Abre el navegador del sistema para iniciar sesión con una cuenta de Unity
        /// (Unity Player Accounts) y completa el login en UGS con ese token.
        /// </summary>
        Task<bool> SignInWithUnityAsync();

        /// <summary>
        /// Vincula una cuenta de Unity a la sesión anónima en curso. Igual que
        /// <see cref="LinkUsernamePasswordAsync"/>, conserva el PlayerId.
        /// </summary>
        Task<bool> LinkWithUnityAsync();

        void SignOut(bool clearCredentials = false);
    }
}
