using System;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Cloud2026.Services;

namespace Cloud2026.Tests
{
    /// <summary>
    /// Pruebas de <see cref="PlayerProfileSync"/>: clase estática y pura (sin UnityEngine ni SDK
    /// de UGS), así que se cubre entera en EditMode sin arrancar el motor ni tocar la red. Misma
    /// idea que las pruebas de MatchRules en CloudCode.Modules/TurnMatch: la lógica que decide
    /// vive separada de la entrada/salida que la rodea.
    /// </summary>
    public class PlayerProfileSyncTests
    {
        // ---------- Stamp ----------

        [Test]
        public void Stamp_SetsUpdatedAtUtc_ToInjectedTime_InParseableFormat()
        {
            var when = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);
            var profile = new PlayerProfile();

            PlayerProfileSync.Stamp(profile, when);

            bool parsed = DateTime.TryParse(
                profile.UpdatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result);

            Assert.IsTrue(parsed, "UpdatedAtUtc debe quedar en un formato que DateTime.TryParse entienda.");
            Assert.AreEqual(when, result, "La fecha guardada debe ser exactamente la que se inyectó, no DateTime.UtcNow.");
        }

        [Test]
        public void Stamp_WithNullProfile_DoesNotThrow()
        {
            // Stamp no confía en que quien llama ya haya comprobado null: es una salvaguarda,
            // no un caso que deba tirar la app abajo.
            Assert.DoesNotThrow(() => PlayerProfileSync.Stamp(null, DateTime.UtcNow));
        }

        // ---------- ResolveLoadOutcome: casos sin comparación de fechas ----------

        [Test]
        public void ResolveLoadOutcome_BothNull_ReturnsNotFound()
        {
            var outcome = PlayerProfileSync.ResolveLoadOutcome(null, null);
            Assert.AreEqual(CloudSaveOutcome.NotFound, outcome, "Sin perfil en ningún sitio, es la primera vez del jugador.");
        }

        [Test]
        public void ResolveLoadOutcome_CloudNullAndLocalPresent_ReturnsLocalIsNewer()
        {
            // Hay caché local pero nada en la nube: un guardado anterior nunca llegó a completarse.
            var local = new PlayerProfile { UpdatedAtUtc = "2026-01-01T00:00:00.0000000Z" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, null);

            Assert.AreEqual(CloudSaveOutcome.LocalIsNewer, outcome);
        }

        [Test]
        public void ResolveLoadOutcome_LocalNullAndCloudPresent_ReturnsLoaded()
        {
            // Sólo hay copia en la nube (por ejemplo, primer login en un dispositivo nuevo).
            var cloud = new PlayerProfile { UpdatedAtUtc = "2026-01-01T00:00:00.0000000Z" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(null, cloud);

            Assert.AreEqual(CloudSaveOutcome.Loaded, outcome);
        }

        // ---------- ResolveLoadOutcome: ambos presentes, comparación de fechas ----------

        [Test]
        public void ResolveLoadOutcome_LocalStrictlyNewerThanCloud_ReturnsLocalIsNewer()
        {
            var local = new PlayerProfile { UpdatedAtUtc = "2026-02-01T12:00:00.0000000Z" };
            var cloud = new PlayerProfile { UpdatedAtUtc = "2026-01-01T12:00:00.0000000Z" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.LocalIsNewer, outcome);
        }

        [Test]
        public void ResolveLoadOutcome_CloudNewerThanLocal_ReturnsLoaded()
        {
            var local = new PlayerProfile { UpdatedAtUtc = "2026-01-01T12:00:00.0000000Z" };
            var cloud = new PlayerProfile { UpdatedAtUtc = "2026-02-01T12:00:00.0000000Z" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.Loaded, outcome);
        }

        [Test]
        public void ResolveLoadOutcome_EqualTimestamps_ReturnsLoadedNotLocalIsNewer()
        {
            // El criterio real es "local ESTRICTAMENTE más reciente"; un empate no cuenta como
            // conflicto y no debe avisar al jugador de nada.
            const string sameInstant = "2026-01-01T12:00:00.0000000Z";
            var local = new PlayerProfile { UpdatedAtUtc = sameInstant };
            var cloud = new PlayerProfile { UpdatedAtUtc = sameInstant };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.Loaded, outcome);
        }

        // ---------- ResolveLoadOutcome: fechas vacías o corruptas ----------

        [Test]
        public void ResolveLoadOutcome_LocalTimestampEmpty_TreatsLocalAsOldest_ReturnsLoaded()
        {
            // Fecha local vacía cuenta como "lo más vieja posible": no debe ganarle por
            // accidente a una copia de la nube con fecha válida.
            var local = new PlayerProfile { UpdatedAtUtc = string.Empty };
            var cloud = new PlayerProfile { UpdatedAtUtc = "2026-01-01T12:00:00.0000000Z" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.Loaded, outcome);
        }

        [Test]
        public void ResolveLoadOutcome_CloudTimestampCorrupted_TreatsCloudAsOldest_ReturnsLocalIsNewer()
        {
            // Fecha de la nube corrupta/no parseable: se trata igual de vieja que "nunca
            // ocurrió", así que una copia local con fecha válida gana.
            var local = new PlayerProfile { UpdatedAtUtc = "2026-01-01T12:00:00.0000000Z" };
            var cloud = new PlayerProfile { UpdatedAtUtc = "esto-no-es-una-fecha" };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.LocalIsNewer, outcome);
        }

        [Test]
        public void ResolveLoadOutcome_BothTimestampsInvalid_ReturnsLoadedNotLocalIsNewer()
        {
            // Ambas fechas caen a DateTime.MinValue: son iguales entre sí, así que no es
            // "estrictamente más reciente" y el resultado es Loaded, no LocalIsNewer.
            var local = new PlayerProfile { UpdatedAtUtc = null };
            var cloud = new PlayerProfile { UpdatedAtUtc = "   " };

            var outcome = PlayerProfileSync.ResolveLoadOutcome(local, cloud);

            Assert.AreEqual(CloudSaveOutcome.Loaded, outcome);
        }

        // ---------- GetByteSize / FitsWithinSizeLimit / EnsureFitsWithinSizeLimit ----------

        [Test]
        public void GetByteSize_NullOrEmptyJson_ReturnsZero()
        {
            Assert.AreEqual(0, PlayerProfileSync.GetByteSize(null));
            Assert.AreEqual(0, PlayerProfileSync.GetByteSize(string.Empty));
        }

        [Test]
        public void GetByteSize_CountsUtf8Bytes_NotCharCount()
        {
            // 'ñ' ocupa 2 bytes en UTF-8: el tamaño real que viaja por red no es Length.
            const string withAccentedChar = "niño";
            int expectedBytes = Encoding.UTF8.GetByteCount(withAccentedChar);

            Assert.AreEqual(expectedBytes, PlayerProfileSync.GetByteSize(withAccentedChar));
            Assert.AreNotEqual(withAccentedChar.Length, PlayerProfileSync.GetByteSize(withAccentedChar),
                "Para este caso, bytes UTF-8 y longitud de string deben diferir.");
        }

        [Test]
        public void FitsWithinSizeLimit_SmallJson_ReturnsTrue()
        {
            const string smallJson = "{\"Level\":1,\"Experience\":0}";
            Assert.IsTrue(PlayerProfileSync.FitsWithinSizeLimit(smallJson));
        }

        [Test]
        public void FitsWithinSizeLimit_JsonOverLimit_ReturnsFalse()
        {
            string oversizedJson = BuildJsonLargerThanLimit();
            Assert.IsFalse(PlayerProfileSync.FitsWithinSizeLimit(oversizedJson));
        }

        [Test]
        public void EnsureFitsWithinSizeLimit_SmallJson_DoesNotThrow()
        {
            const string smallJson = "{\"Level\":1,\"Experience\":0}";
            Assert.DoesNotThrow(() => PlayerProfileSync.EnsureFitsWithinSizeLimit(smallJson));
        }

        [Test]
        public void EnsureFitsWithinSizeLimit_JsonOverLimit_ThrowsWithCoherentSizes()
        {
            string oversizedJson = BuildJsonLargerThanLimit();
            int expectedSize = PlayerProfileSync.GetByteSize(oversizedJson);

            var ex = Assert.Throws<PlayerProfileTooLargeException>(
                () => PlayerProfileSync.EnsureFitsWithinSizeLimit(oversizedJson));

            Assert.AreEqual(expectedSize, ex.ActualSizeBytes);
            Assert.AreEqual(PlayerProfileSync.MaxJsonSizeBytes, ex.MaxSizeBytes);
            Assert.Greater(ex.ActualSizeBytes, ex.MaxSizeBytes, "El tamaño real reportado debe superar el límite para justificar la excepción.");
        }

        /// <summary>Un JSON (no necesita ser válido de verdad: GetByteSize sólo cuenta bytes) mayor que MaxJsonSizeBytes.</summary>
        private static string BuildJsonLargerThanLimit()
        {
            return "{\"padding\":\"" + new string('a', PlayerProfileSync.MaxJsonSizeBytes + 1024) + "\"}";
        }
    }
}
