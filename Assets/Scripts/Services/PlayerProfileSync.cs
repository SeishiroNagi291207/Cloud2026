using System;
using System.Globalization;
using System.Text;

namespace Cloud2026.Services
{
    /// <summary>
    /// Qué pasó al pedir el perfil del jugador. Lo decide <see cref="PlayerProfileSync.ResolveLoadOutcome"/>
    /// (salvo <see cref="Error"/>, que sólo usa el servicio cuando la llamada de red falla).
    /// </summary>
    public enum CloudSaveOutcome
    {
        /// <summary>Se cargó un perfil (de la nube, o el único que había si el otro faltaba).</summary>
        Loaded,

        /// <summary>No hay perfil ni en la nube ni en caché local. Primera vez del jugador.</summary>
        NotFound,

        /// <summary>
        /// La copia local es más reciente que la de la nube. Normalmente significa que un
        /// guardado anterior nunca llegó a completarse (el juego se cerró a medio guardar,
        /// se perdió la conexión a mitad de la llamada...). No se debe pisar en silencio.
        /// </summary>
        LocalIsNewer,

        /// <summary>La llamada a Cloud Save falló. No es un resultado de ResolveLoadOutcome.</summary>
        Error
    }

    /// <summary>
    /// Se lanza cuando el JSON de un <see cref="PlayerProfile"/> supera el límite que ESTE
    /// EJERCICIO se impone a sí mismo antes de escribirlo. Ver <see cref="PlayerProfileSync.MaxJsonSizeBytes"/>
    /// para la diferencia entre este límite y el límite real de Cloud Save.
    /// </summary>
    public class PlayerProfileTooLargeException : Exception
    {
        public int ActualSizeBytes { get; }
        public int MaxSizeBytes { get; }

        public PlayerProfileTooLargeException(int actualSizeBytes, int maxSizeBytes)
            : base($"El perfil ocupa {actualSizeBytes} bytes; el límite de este ejercicio es {maxSizeBytes} bytes.")
        {
            ActualSizeBytes = actualSizeBytes;
            MaxSizeBytes = maxSizeBytes;
        }
    }

    /// <summary>
    /// Reglas puras sobre un <see cref="PlayerProfile"/>: qué copia gana al comparar local
    /// contra nube, y si un JSON cabe dentro del límite de tamaño. Nada de UnityEngine ni del
    /// SDK de UGS aquí dentro, para que se pueda testear en EditMode sin arrancar el motor —
    /// la misma idea que MatchRules en CloudCode.Modules/TurnMatch: la lógica que decide vive
    /// separada de la entrada/salida que la rodea.
    ///
    /// Por la misma razón, nada de DateTime.UtcNow aquí dentro: la hora "actual" siempre llega
    /// como parámetro, para que un test pueda fijarla y el resultado sea el mismo cada vez que
    /// se ejecute.
    /// </summary>
    public static class PlayerProfileSync
    {
        /// <summary>
        /// Límite conservador que ESTE EJERCICIO se impone a sí mismo antes de escribir una
        /// sola clave de Cloud Save. NO es el límite real documentado del servicio.
        ///
        /// Lo confirmado en la documentación oficial (docs.unity.com/ugs/en-us/manual/cloud-save
        /// /manual/concepts/player-data, sección "Limits"): Player Data admite hasta 2000
        /// claves por Access Class y un TOTAL de 5 MiB por Access Class repartido entre todas
        /// esas claves ("a player can have 2000 keys of 2.5 KB each, or 1 key that is 5 MiB").
        /// No hay, en esa documentación, un tope distinto para una única clave aparte de estar
        /// acotado por ese total de 5 MiB.
        ///
        /// Vigilar 32 KiB por escritura aquí no es ese límite real: es una alarma temprana
        /// para este ejercicio, pensada para detectar un perfil que ha crecido de más (por
        /// ejemplo, listas de misiones que nunca se limpian) mucho antes de acercarse al
        /// límite real de la cuenta completa del jugador.
        /// </summary>
        public const int MaxJsonSizeBytes = 32 * 1024;

        /// <summary>Formato ISO-8601 de ida y vuelta que usa .NET para "o". Mismo formato que usan los módulos de Cloud Code (ver MatchRules.cs).</summary>
        private const string RoundtripFormat = "o";

        /// <summary>
        /// Fija <see cref="PlayerProfile.UpdatedAtUtc"/> a <paramref name="nowUtc"/>. La hora
        /// no la mira esta clase por su cuenta: la trae quien llama, para que el resultado sea
        /// determinista en los tests.
        /// </summary>
        public static void Stamp(PlayerProfile profile, DateTime nowUtc)
        {
            if (profile == null) return;

            profile.UpdatedAtUtc = nowUtc.ToString(RoundtripFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Decide qué copia del perfil usar al cargar.
        ///
        ///   cloud == null &amp;&amp; local == null -&gt; NotFound: no hay nada en ningún sitio.
        ///   cloud == null &amp;&amp; local != null -&gt; LocalIsNewer: el guardado a la nube nunca llegó.
        ///   local == null                        -&gt; Loaded: sólo hay copia en la nube.
        ///   ambos existen                        -&gt; se comparan las fechas.
        /// </summary>
        public static CloudSaveOutcome ResolveLoadOutcome(PlayerProfile local, PlayerProfile cloud)
        {
            if (cloud == null && local == null)
            {
                return CloudSaveOutcome.NotFound;
            }

            if (cloud == null)
            {
                return CloudSaveOutcome.LocalIsNewer;
            }

            if (local == null)
            {
                return CloudSaveOutcome.Loaded;
            }

            var localTime = ParseOrOldestPossible(local.UpdatedAtUtc);
            var cloudTime = ParseOrOldestPossible(cloud.UpdatedAtUtc);

            return localTime > cloudTime ? CloudSaveOutcome.LocalIsNewer : CloudSaveOutcome.Loaded;
        }

        /// <summary>Bytes que ocuparía este JSON codificado en UTF-8, que es como viaja por red.</summary>
        public static int GetByteSize(string json)
        {
            return string.IsNullOrEmpty(json) ? 0 : Encoding.UTF8.GetByteCount(json);
        }

        /// <summary>True si el JSON cabe dentro de <see cref="MaxJsonSizeBytes"/>.</summary>
        public static bool FitsWithinSizeLimit(string json)
        {
            return GetByteSize(json) <= MaxJsonSizeBytes;
        }

        /// <summary>
        /// Igual que <see cref="FitsWithinSizeLimit"/>, pero lanzando <see cref="PlayerProfileTooLargeException"/>
        /// si no cabe, para el sitio de llamada que prefiere fallar alto y claro antes de gastar
        /// una llamada de red en vez de comprobar un booleano.
        /// </summary>
        public static void EnsureFitsWithinSizeLimit(string json)
        {
            var size = GetByteSize(json);
            if (size > MaxJsonSizeBytes)
            {
                throw new PlayerProfileTooLargeException(size, MaxJsonSizeBytes);
            }
        }

        /// <summary>
        /// Una fecha ausente o corrupta cuenta como "lo más vieja posible": así, ante la duda,
        /// nunca gana por accidente a una copia con fecha válida.
        /// </summary>
        private static DateTime ParseOrOldestPossible(string isoUtc)
        {
            if (string.IsNullOrWhiteSpace(isoUtc))
            {
                return DateTime.MinValue;
            }

            // RoundtripKind no se puede combinar con AdjustToUniversal: .NET lo rechaza con
            // ArgumentException sin mirar siquiera el string. Stamp() siempre escribe con "o"
            // a partir de un DateTime de Kind=Utc, así que el string ya termina en "Z" y
            // RoundtripKind solo basta para recuperarlo como UTC.
            var parsed = DateTime.TryParse(
                isoUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result);

            return parsed ? result : DateTime.MinValue;
        }
    }
}
