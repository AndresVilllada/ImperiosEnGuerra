using System;
using UnityEngine;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // ---------------------------------------------------------------------
    // INPUTCONTROLLER: sistema de input del jugador humano (Grecia). Revisa
    // cada frame si hubo clic o tecla relevante y traduce la intencion del
    // jugador en la llamada correcta al Modelo.
    //
    // El Controlador NO decide si una accion es valida (eso lo valida el
    // Modelo, como siempre: costo, celda libre, rango de ataque...) — solo
    // transporta la orden.
    //
    // Es una clase normal (no MonoBehaviour): GameController la crea y
    // llama a ManejarInput() desde su Update().
    // ---------------------------------------------------------------------
    public class InputController
    {
        private readonly Mapa mapa;
        private readonly Jugador jugadorGrecia;

        // -------------------------------------------------------------
        // Guarda que hay seleccionado en este momento. Solo uno de los dos
        // puede estar activo a la vez — seleccionar algo nuevo limpia el
        // otro.
        // -------------------------------------------------------------
        private Edificio edificioSeleccionado;
        private Unidad unidadSeleccionada;

        public InputController(Mapa mapa, Jugador jugadorGrecia)
        {
            this.mapa = mapa;
            this.jugadorGrecia = jugadorGrecia;
        }

        // Se llama UNA vez por frame desde GameController.Update().
        public void ManejarInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                ManejarClicIzquierdo();
            }

            // Solo tiene sentido leer teclas de accion si hay un edificio
            // PROPIO seleccionado esperando que el jugador elija que hacer.
            if (edificioSeleccionado != null)
            {
                ManejarTeclasDeAccion();
            }
        }

        // Convierte la posicion del mouse en pantalla a coordenadas del
        // mundo, y revisa que collider 2D hay justo ahi (los edificios y las
        // unidades necesitan un Collider 2D en su Prefab para poder
        // seleccionarse con clic).
        private void ManejarClicIzquierdo()
        {
            Vector2 puntoMundo = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Collider2D colisionado = Physics2D.OverlapPoint(puntoMundo);

            if (colisionado == null)
            {
                // Clic en una celda vacia: si tenia una unidad propia
                // seleccionada, la mandamos a moverse hacia ahi.
                if (unidadSeleccionada != null)
                {
                    OrdenarMover(puntoMundo);
                }
                DeseleccionarTodo();
                return;
            }

            var edificioView = colisionado.GetComponent<EdificioView>();
            if (edificioView != null)
            {
                ManejarClicEnEdificio(edificioView.EdificioModelo);
                return;
            }

            var unidadView = colisionado.GetComponent<UnidadView>();
            if (unidadView != null)
            {
                ManejarClicEnUnidad(unidadView.UnidadModelo);
                return;
            }
        }

        // Revisa si un Edificio o Unidad le pertenece a Grecia, buscandolo
        // en sus propios diccionarios (mismo patron que usa JugadorIA para
        // identificar al rival).
        private bool EsDeGrecia(Edificio edificio) => jugadorGrecia.Edificios.ContainsKey(edificio.Id);
        private bool EsDeGrecia(Unidad unidad) => jugadorGrecia.Unidades.ContainsKey(unidad.Id);

        private void ManejarClicEnEdificio(Edificio edificio)
        {
            // Si ya tengo una tropa seleccionada y hago clic en un
            // edificio RIVAL, es una orden de ataque, no de seleccion.
            if (unidadSeleccionada is Tropa tropaSeleccionada && !EsDeGrecia(edificio))
            {
                OrdenarAtacar(tropaSeleccionada, edificio);
                return;
            }

            // Solo se puede seleccionar (para construir/entrenar) un
            // edificio PROPIO. Clic en edificio rival sin tropa armada:
            // no hace nada, solo deselecciona.
            if (!EsDeGrecia(edificio))
            {
                DeseleccionarTodo();
                return;
            }

            DeseleccionarTodo();
            edificioSeleccionado = edificio;

            // Aviso simple (queda en log_partida.txt) de que quedo
            // seleccionado y que teclas usar — no hay UI de botones todavia.
            if (edificio is TownCenter)
                jugadorGrecia.Eventos.Enqueue(new EventoJuego("Seleccion", "Centro Urbano seleccionado. Teclas: 1=Casa 2=Taller 3=Torre"));
            else if (edificio is Taller taller && taller.EstaConstruido)
                jugadorGrecia.Eventos.Enqueue(new EventoJuego("Seleccion", "Taller seleccionado. Teclas: 1=Espadachin 2=Piquero 3=Arquero"));
        }

        private void ManejarClicEnUnidad(Unidad unidad)
        {
            if (EsDeGrecia(unidad))
            {
                // Selecciono su propia unidad (aldeano o tropa).
                DeseleccionarTodo();
                unidadSeleccionada = unidad;
            }
            else if (unidadSeleccionada is Tropa tropaSeleccionada)
            {
                // Unidad rival + ya tenia una tropa propia seleccionada
                // -> orden de ataque.
                OrdenarAtacar(tropaSeleccionada, unidad);
            }
        }

        // Lee las teclas 1/2/3 segun que tipo de edificio propio este
        // seleccionado, y llama al metodo correspondiente del Modelo.
        private void ManejarTeclasDeAccion()
        {
            if (edificioSeleccionado is TownCenter)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) OrdenarConstruir(pos => new House(pos));
                else if (Input.GetKeyDown(KeyCode.Alpha2)) OrdenarConstruir(pos => new Taller(pos));
                else if (Input.GetKeyDown(KeyCode.Alpha3)) OrdenarConstruir(pos => new Defensa(pos));
            }
            else if (edificioSeleccionado is Taller tallerSeleccionado && tallerSeleccionado.EstaConstruido)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) OrdenarEntrenar(tallerSeleccionado, pos => new Espadachin(pos));
                else if (Input.GetKeyDown(KeyCode.Alpha2)) OrdenarEntrenar(tallerSeleccionado, pos => new Piquero(pos));
                else if (Input.GetKeyDown(KeyCode.Alpha3)) OrdenarEntrenar(tallerSeleccionado, pos => new Arquero(pos));
            }
        }

        // Construye el edificio elegido en la celda libre mas cercana al
        // edificio seleccionado (en vez de pedir un segundo clic de
        // posicion, para simplificar la interaccion). La busqueda de la
        // celda la hace el Modelo (Mapa.BuscarCeldaLibreCercana), y
        // Jugador.ConstruirEdificio ya valida costo y celda libre,
        // exactamente igual que con la IA.
        private void OrdenarConstruir(Func<Posicion, Edificio> fabricaEdificio)
        {
            var posicionLibre = mapa.BuscarCeldaLibreCercana(edificioSeleccionado.Posicion);
            jugadorGrecia.ConstruirEdificio(fabricaEdificio, posicionLibre);
            DeseleccionarTodo();
        }

        // Mismo patron que OrdenarConstruir, pero entrena UNA sola tropa
        // (no un lote de 5 como hace JugadorIA) porque aqui es una accion
        // puntual decidida por el jugador, no una decision automatica.
        private void OrdenarEntrenar(Taller taller, Func<Posicion, Tropa> fabricaTropa)
        {
            var posicionLibre = mapa.BuscarCeldaLibreCercana(taller.Posicion);
            jugadorGrecia.EntrenarLote(taller, fabricaTropa, posicionLibre, cantidad: 1);
            DeseleccionarTodo();
        }

        // Manda a la unidad seleccionada a moverse hacia la celda donde
        // se hizo clic. Unidad.MoverHacia ya hace todo el trabajo (hilo
        // propio, un paso a la vez, revisando colisiones en el Mapa).
        private void OrdenarMover(Vector2 puntoMundo)
        {
            if (unidadSeleccionada == null || !unidadSeleccionada.EstaViva) return;

            var destino = new Posicion(Mathf.FloorToInt(puntoMundo.x), Mathf.FloorToInt(puntoMundo.y));
            if (!mapa.EstaDentroDelMapa(destino)) return;

            unidadSeleccionada.MoverHacia(mapa, destino, jugadorGrecia.Eventos);
        }

        // Ordena a la tropa seleccionada atacar el objetivo (edificio o
        // unidad rival). Tropa.Atacar ya valida rango y vida por su cuenta.
        private void OrdenarAtacar(Tropa tropa, IObjetivoAtacable objetivo)
        {
            if (tropa.Atacar(objetivo))
            {
                jugadorGrecia.Eventos.Enqueue(new EventoJuego("Ataque", $"{tropa.Nombre} atacó al rival."));
            }
            else
            {
                jugadorGrecia.Eventos.Enqueue(new EventoJuego("Ataque", $"{tropa.Nombre} no pudo atacar (fuera de rango)."));
            }
            DeseleccionarTodo();
        }

        private void DeseleccionarTodo()
        {
            edificioSeleccionado = null;
            unidadSeleccionada = null;
        }
    }
}
