using System.Collections.Generic;
using UnityEngine;
using ImperiosEnGuerra.Modelo;

namespace ImperiosEnGuerra.Vista
{
    // Sincroniza los depositos de Recurso del Mapa (Modelo) con GameObjects
    // visibles en la escena. Solo LEE el Modelo, nunca lo modifica -- cumple
    // la regla de que la Vista nunca debe alterar el estado del juego.
    public class MapaView : MonoBehaviour
    {
        [SerializeField] private GameObject prefabRecursoOro;
        [SerializeField] private GameObject prefabRecursoMadera;
        [SerializeField] private GameObject prefabRecursoPiedra;
        [SerializeField] private GameObject prefabRecursoMetal;

        // Recuerda que GameObject visual representa a cada Recurso del
        // Modelo, para poder actualizarlo/destruirlo despues sin tener que
        // recorrer y recrear todo desde cero en cada llamada.
        private readonly Dictionary<Recurso, GameObject> vistasDeRecursos = new Dictionary<Recurso, GameObject>();

        // Llamar esto cuando el Controlador termine de inicializar el Mapa
        // con sus depositos de recursos colocados (una vez al arrancar la
        // partida), y de nuevo cada vez que aparezca un deposito nuevo.
        public void RenderizarRecursos(Mapa mapa)
        {
            foreach (var par in mapa.RecursosEnMapa)
            {
                Posicion posicion = par.Key;
                Recurso recurso = par.Value;

                if (vistasDeRecursos.ContainsKey(recurso)) continue; // ya tiene su vista, no duplicar

                GameObject prefab = ElegirPrefab(recurso.Tipo);
                if (prefab == null) continue;

                // +0.5 en X e Y porque el Tile Anchor del Tilemap esta en
                // 0.5,0.5 (centro de celda) -- esto alinea el sprite del
                // recurso exactamente en el centro visual de su celda.
                Vector3 posicionMundo = new Vector3(posicion.X + 0.5f, posicion.Y + 0.5f, 0f);
                GameObject instancia = Instantiate(prefab, posicionMundo, Quaternion.identity, transform);

                vistasDeRecursos[recurso] = instancia;
            }
        }

        // Llamar esto seguido (por ejemplo, cuando el Controlador procesa un
        // EventoJuego de tipo "Recoleccion") para que los depositos que ya
        // se agotaron en el Modelo desaparezcan tambien de la pantalla.
        public void ActualizarRecursos(Mapa mapa)
        {
            RenderizarRecursos(mapa); // por si aparecieron depositos nuevos

            var agotados = new List<Recurso>();
            foreach (var par in vistasDeRecursos)
            {
                if (par.Key.EstaAgotado())
                {
                    Destroy(par.Value);
                    agotados.Add(par.Key);
                }
            }

            foreach (var recurso in agotados)
            {
                vistasDeRecursos.Remove(recurso);
            }
        }

        private GameObject ElegirPrefab(TipoRecurso tipo)
        {
            switch (tipo)
            {
                case TipoRecurso.Oro: return prefabRecursoOro;
                case TipoRecurso.Madera: return prefabRecursoMadera;
                case TipoRecurso.Piedra: return prefabRecursoPiedra;
                case TipoRecurso.Metal: return prefabRecursoMetal;
                default: return null;
            }
        }
    }
}