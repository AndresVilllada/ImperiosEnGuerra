using System;
using System.Collections.Generic;
using UnityEngine;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // ---------------------------------------------------------------------
    // SINCRONIZACIONVISTACONTROLLER: cada frame, revisa lo que dice el
    // Modelo (unidades, edificios, recursos de los dos jugadores) y se lo
    // "pasa" a la Vista: crea el GameObject de lo que sea nuevo, actualiza
    // lo que ya existe, y refresca el HUD. NO decide nada de negocio.
    //
    // Es una clase normal (no MonoBehaviour): GameController la crea y la
    // llama desde su Update(). Los Prefabs siguen asignandose en el
    // Inspector de GameController; aqui solo se recibe la "traduccion"
    // Modelo -> Prefab como funciones (ver constructor).
    //
    // OPTIMIZACION: esto corre en el hilo principal en CADA frame, asi que
    // su costo crece con la cantidad de unidades. Por eso:
    //   - se guarda la VISTA ya tipada (UnidadView/EdificioView) y
    //     GetComponent se llama una sola vez, al crearla (antes se llamaba
    //     por cada unidad, en cada frame);
    //   - se recorre el ConcurrentDictionary directamente (foreach sobre el
    //     diccionario) en vez de pedir .Values, que crea una copia nueva de
    //     la coleccion en cada llamada;
    //   - el HUD solo se toca cuando un recurso CAMBIA de valor.
    // ---------------------------------------------------------------------
    public class SincronizacionVistaController
    {
        private readonly MapaView mapaView;
        private readonly HUDController hud;
        private readonly Transform padreVistas; // bajo quien se instancian los GameObjects (el de GameController)
        private readonly Func<Unidad, GameObject> elegirPrefabUnidad;
        private readonly Func<Edificio, GameObject> elegirPrefabEdificio;

        private readonly Mapa mapa;
        private readonly Jugador jugadorGrecia;
        private readonly Jugador jugadorIA;

        // Recuerdan que vista le corresponde a cada Unidad/Edificio del
        // Modelo (por su Id), para no crear duplicados y para poder
        // actualizarlas frame a frame. Se guarda el componente View (no el
        // GameObject) para no tener que buscarlo con GetComponent cada frame.
        private readonly Dictionary<Guid, UnidadView> vistasUnidades = new Dictionary<Guid, UnidadView>();
        private readonly Dictionary<Guid, EdificioView> vistasEdificios = new Dictionary<Guid, EdificioView>();

        // Ultimos valores mostrados en el HUD (-1 = todavia no se mostro
        // nada), para actualizarlo solo cuando algo cambia.
        private int ultimoOro = -1;
        private int ultimaMadera = -1;
        private int ultimaPiedra = -1;
        private int ultimoMetal = -1;

        public SincronizacionVistaController(
            MapaView mapaView,
            HUDController hud,
            Transform padreVistas,
            Func<Unidad, GameObject> elegirPrefabUnidad,
            Func<Edificio, GameObject> elegirPrefabEdificio,
            Mapa mapa,
            Jugador jugadorGrecia,
            Jugador jugadorIA)
        {
            this.mapaView = mapaView;
            this.hud = hud;
            this.padreVistas = padreVistas;
            this.elegirPrefabUnidad = elegirPrefabUnidad;
            this.elegirPrefabEdificio = elegirPrefabEdificio;
            this.mapa = mapa;
            this.jugadorGrecia = jugadorGrecia;
            this.jugadorIA = jugadorIA;
        }

        // Se llama UNA vez por frame desde GameController.Update().
        public void Sincronizar()
        {
            // Cada frame, revisamos si algun recurso se agoto (para que
            // MapaView destruya su GameObject visual). RenderizarRecursos
            // ya esta incluido dentro de ActualizarRecursos, asi que esto
            // tambien capta depositos nuevos si en algun momento se agregan
            // en caliente durante la partida.
            mapaView.ActualizarRecursos(mapa);

            // Unidades y edificios de AMBOS jugadores. "esDeIA" le dice a la
            // Vista si debe teñir el sprite de rojo (rival) o dejarlo normal
            // (Grecia) — logica que ya trae hecha UnidadView/EdificioView.
            ActualizarUnidadesDe(jugadorGrecia, esDeIA: false);
            ActualizarUnidadesDe(jugadorIA, esDeIA: true);
            ActualizarEdificiosDe(jugadorGrecia, esDeIA: false);
            ActualizarEdificiosDe(jugadorIA, esDeIA: true);

            ActualizarHUD();
        }

        // HUD: recursos actuales de Grecia (el jugador humano; el HUD no
        // muestra los recursos de la IA, solo los del jugador real). Solo se
        // actualiza el texto de un recurso cuando su valor cambio desde la
        // ultima vez (cambiar un texto de TextMeshPro fuerza regenerar su
        // malla, y antes se hacia 4 veces por frame aunque nada cambiara).
        private void ActualizarHUD()
        {
            if (hud == null) return;

            int oro = jugadorGrecia.Recursos[TipoRecurso.Oro];
            if (oro != ultimoOro) { hud.ActualizarOro(oro); ultimoOro = oro; }

            int madera = jugadorGrecia.Recursos[TipoRecurso.Madera];
            if (madera != ultimaMadera) { hud.ActualizarMadera(madera); ultimaMadera = madera; }

            int piedra = jugadorGrecia.Recursos[TipoRecurso.Piedra];
            if (piedra != ultimaPiedra) { hud.ActualizarPiedra(piedra); ultimaPiedra = piedra; }

            int metal = jugadorGrecia.Recursos[TipoRecurso.Metal];
            if (metal != ultimoMetal) { hud.ActualizarMetal(metal); ultimoMetal = metal; }
        }

        // Crea la vista de cada Unidad nueva que aparezca en "jugador"
        // (aldeano recien entrenado, tropa recien entrenada), y actualiza
        // las que ya existen. Cuando el Modelo marca una Unidad como
        // destruida, UnidadView se encarga de destruirse sola (ver
        // UnidadView.ManejarMuerte), asi que aqui solo hace falta dejar de
        // instanciarla de nuevo si ya no tiene vista viva.
        private void ActualizarUnidadesDe(Jugador jugador, bool esDeIA)
        {
            // foreach directo sobre el ConcurrentDictionary: es seguro aunque
            // otro hilo agregue unidades al mismo tiempo (el enumerador es
            // "debilmente consistente") y no copia la coleccion.
            foreach (var par in jugador.Unidades)
            {
                var unidad = par.Value;

                // "vista == null" cubre las dos situaciones: nunca tuvo vista,
                // o ya se destruyo (Unity sobrecarga == para objetos destruidos).
                if (!vistasUnidades.TryGetValue(unidad.Id, out var vista) || vista == null)
                {
                    if (unidad.EstaDestruido) continue; // nunca tuvo vista y ya murio, no crear nada

                    var prefab = elegirPrefabUnidad(unidad);
                    if (prefab == null) continue; // tipo de unidad sin prefab asignado todavia

                    var instancia = UnityEngine.Object.Instantiate(prefab, padreVistas);
                    var nuevaVista = instancia.GetComponent<UnidadView>(); // UNA sola vez, al crearla
                    nuevaVista.Inicializar(unidad, esDeIA);
                    vistasUnidades[unidad.Id] = nuevaVista;
                }
                else
                {
                    vista.ActualizarVisual();
                }
            }
        }

        // Mismo patron que ActualizarUnidadesDe, pero para Edificios. Aqui
        // solo se crea/actualiza la VISTA: el disparo automatico de las
        // Torres ya no depende de este metodo (antes se arrancaba aqui, al
        // aparecer la vista; ahora lo arranca el Modelo al construirlas).
        private void ActualizarEdificiosDe(Jugador jugador, bool esDeIA)
        {
            foreach (var par in jugador.Edificios)
            {
                var edificio = par.Value;

                if (!vistasEdificios.TryGetValue(edificio.Id, out var vista) || vista == null)
                {
                    if (edificio.EstaDestruido) continue;

                    var prefab = elegirPrefabEdificio(edificio);
                    if (prefab == null) continue;

                    var instancia = UnityEngine.Object.Instantiate(prefab, padreVistas);
                    var nuevaVista = instancia.GetComponent<EdificioView>();
                    nuevaVista.Inicializar(edificio, esDeIA);
                    vistasEdificios[edificio.Id] = nuevaVista;
                }
                else
                {
                    vista.ActualizarVisual();
                }
            }
        }
    }
}
