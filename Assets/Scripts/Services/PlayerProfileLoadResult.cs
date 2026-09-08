namespace Cloud2026.Services
{
    /// <summary>
    /// Resultado de pedir el perfil del jugador a Cloud Save: qué perfil hay (si lo hay) y
    /// qué pasó al decidir cuál usar. Ver <see cref="CloudSaveOutcome"/>.
    ///
    /// Clase en vez de tupla con nombres, para mantener el mismo estilo que <c>GreetingResult</c>
    /// y <c>MatchViewDto</c>: en este proyecto los resultados de un servicio son DTOs, no tuplas.
    /// </summary>
    public class PlayerProfileLoadResult
    {
        public PlayerProfile Profile { get; set; }
        public CloudSaveOutcome Outcome { get; set; }

        public PlayerProfileLoadResult(PlayerProfile profile, CloudSaveOutcome outcome)
        {
            Profile = profile;
            Outcome = outcome;
        }
    }
}
