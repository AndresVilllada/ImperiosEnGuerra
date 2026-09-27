using UnityEngine;
using System.Threading;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // Orquesta toda la partida: crea el Modelo (Mapa, Jugadores, Partida),
    // coloca la configuracion inicial, arranca la IA y el logueo, y en cada
    // frame drena las colas de eventos para avisarle a la Vista que algo
    // cambio. NO decide nada de negocio (eso ya vive en el Modelo) — solo
    // "pasa informacion" entre el Modelo y la Vista, tal como pidio la
    // profesora.
    public class GameController : MonoBehaviour
    {
        [Header("Vista - Mundo (Pista A)")]
        [SerializeField] private MapaView mapaView;

        // TODO (cuando la Pista B suba sus scripts): agregar aqui
        // [SerializeField] private UnidadView unidadView;
        // [SerializeField] private EdificioView edificioView;

        private Mapa mapa;
        private Jugador jugadorGrecia;
        private Jugador jugadorIA;
        private Partida partida;
        private JugadorIA cerebroIA;
        private GestorArchivos archivos;
        private CancellationTokenSource cancelacionLog;

        private void Start()
        {
            InicializarPartida();
        }

        private void InicializarPartida()
        {
            // 1. El mapa unico y compartido (20x20, ya coincide con lo que
            // pintaste en el Tilemap).
            mapa = new Mapa();

            // 2. Los dos jugadores, cada uno arrancando en una esquina
            // opuesta del mapa (asi Grecia tiene que "invadir" hacia el
            // otro lado, como se definio en el diseño del juego).
            jugadorGrecia = new Jugador("Grecia", mapa);
            jugadorIA = new Jugador("El Resto", mapa);

            var posicionCentroGrecia = new Posicion(2, 2);
            var posicionCentroIA = new Posicion(17, 17);

            var centroGrecia = new TownCenter(posicionCentroGrecia);
            var centroIA = new TownCenter(posicionCentroIA);

            jugadorGrecia.AgregarEdificio(centroGrecia);
            mapa.ColocarEdificio(posicionCentroGrecia, centroGrecia);

            jugadorIA.AgregarEdificio(centroIA);
            mapa.ColocarEdificio(posicionCentroIA, centroIA);

            // TODO (Pista B): cuando exista EdificioView, llamar aqui algo
            // como edificioView.RenderizarEdificio(centroGrecia) y
            // edificioView.RenderizarEdificio(centroIA) para que se vean
            // los dos Centros Urbanos en pantalla.

            // 3. Esparcir algunos depositos de recurso por el mapa (numeros
            // de ejemplo, se pueden ajustar facil mas adelante).
            ColocarDeposito(TipoRecurso.Oro, new Posicion(5, 10), 300);
            ColocarDeposito(TipoRecurso.Madera, new Posicion(8, 4), 400);
            ColocarDeposito(TipoRecurso.Piedra, new Posicion(12, 15), 250);
            ColocarDeposito(TipoRecurso.Metal, new Posicion(15, 6), 200);

            // 4. La partida que orquesta a los dos jugadores.
            partida = new Partida(jugadorGrecia, jugadorIA);

            // 5. Le pedimos a la Vista (tu MapaView) que dibuje los
            // depositos de recurso que acabamos de colocar en el Modelo.
            mapaView.RenderizarRecursos(mapa);

            // 6. Archivos: configuracion.txt de una vez, y arrancamos el
            // hilo que va a ir escribiendo log_partida.txt solo.
            archivos = new GestorArchivos();
            archivos.GuardarConfiguracionInicial(jugadorGrecia, jugadorIA);
            cancelacionLog = archivos.IniciarEscuchaDeEventos(partida);

            // 7. Arrancamos el "cerebro" de la maquina — desde este momento
            // el Jugador "El Resto" empieza a tomar decisiones solo, en su
            // propio hilo, cada 4 segundos.
            cerebroIA = new JugadorIA(partida, jugadorIA, jugadorGrecia);
            cerebroIA.Iniciar();
        }

        // Pequeño helper para no repetir 3 lineas por cada deposito.
        private void ColocarDeposito(TipoRecurso tipo, Posicion posicion, int cantidad)
        {
            var recurso = new Recurso(tipo, cantidad);
            mapa.ColocarRecurso(posicion, recurso);
        }

        private void Update()
        {
            // Cada frame, revisamos si algun recurso se agoto (para que
            // MapaView destruya su GameObject visual). RenderizarRecursos
            // ya esta incluido dentro de ActualizarRecursos, asi que esto
            // tambien capta depositos nuevos si en algun momento se agregan
            // en caliente durante la partida.
            mapaView.ActualizarRecursos(mapa);

            // TODO (Pista B): aqui va el drenado de Jugador.Eventos de
            // ambos jugadores para avisarle a UnidadView/EdificioView
            // cuando aparezca una tropa nueva, un edificio termine de
            // construirse, alguien reciba daño, etc. Por ahora, mientras
            // no exista esa Vista, no hacemos nada mas con esos eventos
            // aqui (igual se siguen registrando solos en log_partida.txt
            // gracias a GestorArchivos, que ya corre en su propio hilo).

            // Revisa si la partida ya termino, para guardar el resultado
            // final UNA sola vez.
            if (partida.Estado == EstadoPartida.Finalizada && cancelacionLog != null)
            {
                archivos.GuardarResultadoFinal(partida);
                cancelacionLog.Cancel();
                cancelacionLog = null; // evita que se vuelva a guardar en el siguiente frame
            }
        }

        private void OnDestroy()
        {
            // Buena practica: si el objeto se destruye (se cierra el juego,
            // se cambia de escena), detenemos los hilos en vez de dejarlos
            // corriendo en el vacio.
            cerebroIA?.Detener();
            cancelacionLog?.Cancel();
        }
    }
}