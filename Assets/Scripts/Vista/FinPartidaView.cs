using UnityEngine;

namespace ImperiosEnGuerra.Vista
{
    public class FinPartidaView : MonoBehaviour
    {
        [Header("Paneles de Fin de Juego")]
        [SerializeField] private GameObject panelVictoria;
        [SerializeField] private GameObject panelDerrota;

        // Llama a este método desde tu Controlador principal cuando el jugador gane
        public void MostrarVictoria()
        {
            panelVictoria.SetActive(true);
            // Opcional: Pausar el juego para que no se sigan moviendo las unidades
            Time.timeScale = 0f;
        }

        // Llama a este método cuando la IA destruya el centro urbano del jugador
        public void MostrarDerrota()
        {
            panelDerrota.SetActive(true);
            Time.timeScale = 0f;
        }

        // Si usas Time.timeScale = 0f, recuerda restaurarlo a 1 al salir para que
        // el menú y la próxima partida no se queden congelados.
        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }
    }
}
