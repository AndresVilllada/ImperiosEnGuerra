using System;
using UnityEngine;

namespace ImperiosEnGuerra.Vista
{
    // ---------------------------------------------------------------------
    // ENTRENAMIENTOCOOLDOWNVIEW: muestra en una esquina de la pantalla si el
    // jugador ya puede entrenar otra tropa o cuantos segundos faltan. Sin
    // esto, al presionar la tecla durante el cooldown "no pasaba nada" y el
    // jugador no sabia por que.
    //
    // Usa IMGUI (OnGUI): no necesita prefabs, Canvas ni asignar nada en el
    // Inspector; GameController lo agrega por codigo.
    //
    // Es Vista pura: NO decide nada del juego. El Modelo le dice si el
    // entrenamiento esta disponible (disponible) y cuantos segundos faltan
    // (segundosRestantes); aqui solo se elige el color y el formato del texto.
    // ---------------------------------------------------------------------
    public class EntrenamientoCooldownView : MonoBehaviour
    {
        private Func<bool> disponible;
        private Func<double> segundosRestantes;
        private GUIStyle estilo;

        public void Configurar(Func<bool> disponible, Func<double> segundosRestantes)
        {
            this.disponible = disponible;
            this.segundosRestantes = segundosRestantes;
        }

        private void OnGUI()
        {
            if (disponible == null || segundosRestantes == null) return;

            // El estilo se crea aqui (y no en Awake) porque GUI.skin solo
            // esta disponible dentro de OnGUI.
            if (estilo == null)
            {
                estilo = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold
                };
            }

            bool listo = disponible(); // la respuesta la da el Modelo

            estilo.normal.textColor = listo ? Color.green : Color.yellow;
            string texto = listo
                ? "Entrenar tropa: LISTO"
                : $"Entrenar tropa: {segundosRestantes():0.0} s";

            GUI.Label(new Rect(20, Screen.height - 50, 420, 40), texto, estilo);
        }
    }
}