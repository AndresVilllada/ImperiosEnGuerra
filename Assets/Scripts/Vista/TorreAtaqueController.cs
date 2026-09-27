using UnityEngine;
using ImperiosEnGuerra.Modelo;

namespace ImperiosEnGuerra.Vista
{
    // Se coloca en el prefab Edificio_Torre para que busque enemigos y ataque automáticamente
    public class TorreAtaqueController : MonoBehaviour
    {
        [Header("Configuración de Disparo")]
        [SerializeField] private float tiempoEntreDisparos = 2f; // Segundos que tarda la torre en volver a disparar
        private float cronometroAtaque;

        private EdificioView edificioView;

        private void Awake()
        {
            edificioView = GetComponent<EdificioView>();
        }

        private void Update()
        {
            if (edificioView == null || edificioView.EdificioModelo == null) return;

            // Verificamos que el edificio sea una Defensa (Torre), esté construido y esté vivo
            if (edificioView.EdificioModelo is Defensa torreModelo && torreModelo.EstaConstruido && !torreModelo.EstaDestruido)
            {
                cronometroAtaque += Time.deltaTime;

                // Si ya pasó el tiempo de recarga, busca a quién disparar
                if (cronometroAtaque >= tiempoEntreDisparos)
                {
                    AtacarObjetivoCercano(torreModelo);
                    cronometroAtaque = 0f; // Reinicia el cronómetro
                }
            }
        }

        private void AtacarObjetivoCercano(Defensa torreModelo)
        {
            // Buscamos todas las unidades activas en la escena que tengan el script UnidadView
            UnidadView[] todasLasUnidades = FindObjectsOfType<UnidadView>();

            UnidadView objetivoMasCercano = null;
            float menorDistancia = float.MaxValue;

            foreach (var unidadView in todasLasUnidades)
            {
                // Evitamos atacar a nuestras propias unidades (si tienes diferenciación de equipos)
                // Aquí calculamos la distancia en casillas (Manhattan o distancia de Unity)
                float distancia = Vector3.Distance(transform.position, unidadView.transform.position);

                // Verificamos si está dentro del rango de ataque de la torre (convertido a unidades de Unity)
                if (distancia <= torreModelo.RangoAtaque * 1.5f && distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    objetivoMasCercano = unidadView;
                }
            }

            // Si encontró un objetivo en rango, le inflige daño en el modelo
            if (objetivoMasCercano != null && objetivoMasCercano.UnidadModelo != null)
            {
                objetivoMasCercano.UnidadModelo.RecibirDanio(torreModelo.DanioAtaque);
                Debug.Log($"¡La torre disparó al enemigo e hizo {torreModelo.DanioAtaque} de daño!");

                // Opcional: Aquí podrías instanciar una flecha voladora hacia el objetivo
            }
        }
    }
}