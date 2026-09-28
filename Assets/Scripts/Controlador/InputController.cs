using UnityEngine;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // ---------------------------------------------------------------------
    // INPUTCONTROLLER: sistema de input del jugador humano (Grecia). Revisa
    // cada frame si hubo clic o tecla relevante y traduce la intencion del
    // jugador en UNA llamada al Modelo.
    //
    // El Controlador NO tiene logica de negocio: no crea edificios ni
    // tropas, no elige celdas, no valida costos, rangos ni limites del mapa,
    // y no fabrica eventos del juego. Solo hace tres cosas:
    //   1. convertir el clic del mouse en "que hay ahi" (Collider2D -> View),
    //   2. recordar que esta seleccionado (estado de la interfaz),
    //   3. traducir teclas/clics a ordenes del Modelo ("ConstruirCerca",
    //      "EntrenarTropaCerca", "OrdenarMovimiento", "OrdenarAtaque").
    // Todo lo demas (si esta permitido, donde, cuanto cuesta) lo decide el
    // Modelo, con las mismas reglas que usa la IA.
    //
    // Es una clase normal (no MonoBehaviour): GameController la crea y
    // llama a ManejarInput() desde su Update().
    // ---------------------------------------------------------------------
    public class InputController
    {
        private readonly Jugador jugadorGrecia;

        // -------------------------------------------------------------
        // Guarda que hay seleccionado en este momento. Solo uno de los dos
        // puede estar activo a la vez — seleccionar algo nuevo limpia el
        // otro. Es estado de la INTERFAZ, no del juego.
        // -------------------------------------------------------------
        private Edificio edificioSeleccionado;
        private Unidad unidadSeleccionada;

        public InputController(Jugador jugadorGrecia)
        {
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
            // seleccionado esperando que el jugador elija que hacer.
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

            if (colisionado != null)
            {
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

            // No hay ningun edificio ni unidad ahi: es una celda "sin nada
            // seleccionable" (vacia, o con un deposito de recurso, que aunque
            // tenga collider no es seleccionable). Si tenia una unidad
            // seleccionada, se le ordena ir hacia ahi: el Modelo decide que
            // significa (caminar hasta la celda, o, si es un aldeano y ahi hay
            // un deposito, ir a recolectarlo).
            if (unidadSeleccionada != null)
            {
                OrdenarMover(puntoMundo);
            }
            DeseleccionarTodo();
        }

        private void ManejarClicEnEdificio(Edificio edificio)
        {
            bool esPropio = jugadorGrecia.EsPropio(edificio); // la respuesta la da el Modelo

            // Si ya tengo una tropa seleccionada y hago clic en un
            // edificio RIVAL, es una orden de ataque, no de seleccion.
            if (unidadSeleccionada is Tropa tropaSeleccionada && !esPropio)
            {
                jugadorGrecia.OrdenarAtaque(tropaSeleccionada, edificio);
                return;
            }

            // Solo se puede seleccionar (para construir/entrenar) un
            // edificio PROPIO. Clic en edificio rival sin tropa armada:
            // no hace nada, solo deselecciona.
            if (!esPropio)
            {
                DeseleccionarTodo();
                return;
            }

            DeseleccionarTodo();
            edificioSeleccionado = edificio;
        }

        private void ManejarClicEnUnidad(Unidad unidad)
        {
            if (jugadorGrecia.EsPropio(unidad))
            {
                // Selecciono mi propia unidad (aldeano o tropa).
                DeseleccionarTodo();
                unidadSeleccionada = unidad;
            }
            else if (unidadSeleccionada is Tropa tropaSeleccionada)
            {
                // Unidad rival + ya tenia una tropa seleccionada -> orden de
                // ataque.
                jugadorGrecia.OrdenarAtaque(tropaSeleccionada, unidad);
            }
        }

        // Asigna las teclas 1/2/3 segun el tipo de edificio seleccionado
        // (esa asignacion de teclas es lo unico que sabe el Controlador) y
        // le pide al Modelo la accion. Si todavia no se puede (Taller sin
        // terminar, cooldown, falta de recursos), el Modelo rechaza la orden
        // y deja constancia en el log.
        private void ManejarTeclasDeAccion()
        {
            if (edificioSeleccionado is TownCenter)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) jugadorGrecia.ConstruirCerca(edificioSeleccionado, TipoEdificio.Casa);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) jugadorGrecia.ConstruirCerca(edificioSeleccionado, TipoEdificio.Taller);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) jugadorGrecia.ConstruirCerca(edificioSeleccionado, TipoEdificio.Torre);
            }
            else if (edificioSeleccionado is Taller tallerSeleccionado)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) jugadorGrecia.EntrenarTropaCerca(tallerSeleccionado, TipoTropa.Espadachin);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) jugadorGrecia.EntrenarTropaCerca(tallerSeleccionado, TipoTropa.Piquero);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) jugadorGrecia.EntrenarTropaCerca(tallerSeleccionado, TipoTropa.Arquero);
            }
            // El edificio SIGUE seleccionado despues de ordenar: se pueden
            // dar varias ordenes seguidas sin volver a hacer clic.
        }

        // Convierte el punto del mundo donde se hizo clic en una celda de la
        // matriz (Unity usa coordenadas continuas; el Modelo, celdas
        // enteras) y le pide al Modelo que mueva la unidad. El Modelo valida
        // que la unidad este viva y que la celda este dentro del mapa.
        private void OrdenarMover(Vector2 puntoMundo)
        {
            var destino = new Posicion(Mathf.FloorToInt(puntoMundo.x), Mathf.FloorToInt(puntoMundo.y));
            jugadorGrecia.OrdenarMovimiento(unidadSeleccionada, destino);
        }

        private void DeseleccionarTodo()
        {
            edificioSeleccionado = null;
            unidadSeleccionada = null;
        }
    }
}
