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

        // Recuerdan que GameObject visual le corresponde a cada Unidad/
        // Edificio del Modelo (por su Id), para no crear duplicados y para
        // poder actualizarlos frame a frame.
        private readonly Dictionary<Guid, GameObject> vistasUnidades = new Dictionary<Guid, GameObject>();
        private readonly Dictionary<Guid, GameObject> vistasEdificios = new Dictionary<Guid, GameObject>();

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
            ActualizarEdificiosDe(jugadorGrecia, jugadorIA, esDeIA: false);
            ActualizarEdificiosDe(jugadorIA, jugadorGrecia, esDeIA: true);

            ActualizarHUD();
        }

        // HUD: recursos actuales de Grecia (el jugador humano; el HUD no
        // muestra los recursos de la IA, solo los del jugador real).
        private void ActualizarHUD()
        {
            if (hud == null) return;

            hud.ActualizarOro(jugadorGrecia.Recursos[TipoRecurso.Oro]);
            hud.ActualizarMadera(jugadorGrecia.Recursos[TipoRecurso.Madera]);
            hud.ActualizarPiedra(jugadorGrecia.Recursos[TipoRecurso.Piedra]);
            hud.ActualizarMetal(jugadorGrecia.Recursos[TipoRecurso.Metal]);
        }

        // Crea la vista de cada Unidad nueva que aparezca en "jugador"
        // (aldeano recien entrenado, tropa recien entrenada), y actualiza
        // las que ya existen. Cuando el Modelo marca una Unidad como
        // destruida, UnidadView se encarga de destruirse sola (ver
        // UnidadView.ManejarMuerte), asi que aqui solo hace falta dejar de
        // instanciarla de nuevo si ya no tiene vista viva.
        private void ActualizarUnidadesDe(Jugador jugador, bool esDeIA)
        {
            foreach (var unidad in jugador.Unidades.Values)
            {
                if (!vistasUnidades.TryGetValue(unidad.Id, out var vistaGO) || vistaGO == null)
                {
                    if (unidad.EstaDestruido) continue; // nunca tuvo vista y ya murio, no crear nada

                    var prefab = elegirPrefabUnidad(unidad);
                    if (prefab == null) continue; // tipo de unidad sin prefab asignado todavia

                    var instancia = UnityEngine.Object.Instantiate(prefab, padreVistas);
                    instancia.GetComponent<UnidadView>().Inicializar(unidad, esDeIA);
                    vistasUnidades[unidad.Id] = instancia;
                }
                else
                {
                    vistaGO.GetComponent<UnidadView>().ActualizarVisual();
                }
            }
        }

        // Mismo patron que ActualizarUnidadesDe, pero para Edificios. Ademas,
        // cuando aparece una Defensa (Torre) nueva, le pedimos al propio
        // Jugador que arranque su hilo de disparo automatico (ver
        // Jugador.IniciarDefensaAutomatica en el Modelo) — el Controlador
        // solo dispara ese aviso, no decide nada del combate en si.
        private void ActualizarEdificiosDe(Jugador jugador, Jugador rival, bool esDeIA)
        {
            foreach (var edificio in jugador.Edificios.Values)
            {
                if (!vistasEdificios.TryGetValue(edificio.Id, out var vistaGO) || vistaGO == null)
                {
                    if (edificio.EstaDestruido) continue;

                    var prefab = elegirPrefabEdificio(edificio);
                    if (prefab == null) continue;

                    var instancia = UnityEngine.Object.Instantiate(prefab, padreVistas);
                    instancia.GetComponent<EdificioView>().Inicializar(edificio, esDeIA);
                    vistasEdificios[edificio.Id] = instancia;

                    if (edificio is Defensa torre)
                    {
                        jugador.IniciarDefensaAutomatica(torre, rival);
                    }
                }
                else
                {
                    vistaGO.GetComponent<EdificioView>().ActualizarVisual();
                }
            }
        }
    }
}
