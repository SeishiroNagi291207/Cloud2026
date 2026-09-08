using System;
using System.Collections.Generic;

namespace Cloud2026.Services
{
    /// <summary>
    /// Estado persistente del perfil del jugador: lo que Cloud Save guarda y carga.
    ///
    /// Va con [Serializable] y sólo campos públicos de tipos simples porque lo serializa
    /// JsonUtility (ToJson/FromJson), no Newtonsoft: nada de propiedades con getters, nada
    /// de colecciones anidadas raras. List&lt;string&gt; simple es justo lo que JsonUtility
    /// sabe manejar bien.
    ///
    /// Este perfil es progreso propio del jugador (nivel, XP, misiones, desbloqueos de UI),
    /// no economía. No guarda saldo ni nada que tenga que decidir un servidor: eso llega
    /// con Cloud Code + Economy en las semanas 6-8.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        /// <summary>
        /// Versión del esquema de este perfil. Existe para el día en que se añada un campo
        /// nuevo: un perfil viejo guardado en la nube llegará sin él, JsonUtility lo dejará
        /// en su valor por defecto en vez de fallar, y SchemaVersion es lo que permite
        /// detectar esa situación y decidir si hace falta migrar el dato antes de usarlo.
        /// De momento sólo existe el esquema 1.
        /// </summary>
        public int SchemaVersion = 1;

        public int Level = 1;

        public int Experience = 0;

        public List<string> CompletedMissionIds = new List<string>();

        public List<string> UnlockedIds = new List<string>();

        /// <summary>
        /// Hora UTC de la última modificación de este perfil, en ISO-8601. La escribe
        /// PlayerProfileSync.Stamp, nunca a mano: es el dato que decide qué copia gana al
        /// comparar el perfil local contra el de la nube.
        /// </summary>
        public string UpdatedAtUtc = string.Empty;
    }
}
