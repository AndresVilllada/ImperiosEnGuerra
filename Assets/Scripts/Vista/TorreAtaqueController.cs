using UnityEngine;
using ImperiosEnGuerra.Modelo;

namespace ImperiosEnGuerra.Vista
{
    public class TorreAtaqueController : MonoBehaviour
    {
        [Header("Configuración de Disparo")]
        [SerializeField] private float tiempoEntreDisparos = 2f;
        private float cronometroAtaque;

        private EdificioView edificioView;

        private void Awake()
        {
            edificioView = GetComponent<EdificioView>();
        }

        private void Update()
        {
            if (edificioView == null || edificioView.EdificioModelo == null) return;

            if (edificioView.EdificioModelo is Defensa torreModelo && torreModelo.EstaConstruido && !torreModelo.EstaDestruido)
            {
                cronometroAtaque += Time.deltaTime;

                if (cronometroAtaque >= tiempoEntreDisparos)
                {
                    AtacarObjetivoCercano(torreModelo);
                    cronometroAtaque = 0f;
                }
            }
        }

        private void AtacarObjetivoCercano(Defensa torreModelo)
        {
            // Usamos la API limpia recomendada por Unity sin modos de ordenamiento obsoletos
            UnidadView[] todasLasUnidades = FindObjectsByType<UnidadView>(FindObjectsInactive.Exclude);

            UnidadView objetivoMasCercano = null;
            float menorDistancia = float.MaxValue;

            foreach (var unidadView in todasLasUnidades)
            {
                float distancia = Vector3.Distance(transform.position, unidadView.transform.position);

                if (distancia <= torreModelo.RangoAtaque * 1.5f && distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    objetivoMasCercano = unidadView;
                }
            }

            if (objetivoMasCercano != null && objetivoMasCercano.UnidadModelo != null)
            {
                objetivoMasCercano.UnidadModelo.RecibirDanio(torreModelo.DanioAtaque);
                Debug.Log($"¡La torre disparó al enemigo e hizo {torreModelo.DanioAtaque} de daño!");
            }
        }
    }
}