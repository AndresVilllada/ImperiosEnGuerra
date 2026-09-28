using UnityEngine;
using System.Threading;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // Orquesta toda la partida: le pide al Modelo que arme el estado inicial
    // (ConfiguradorPartida), arranca la IA y el logueo, y en cada frame
    // delega en dos controladores especializados:
    //   - SincronizacionVistaController: pasa el estado del Modelo a la Vista.
    //   - InputController: traduce el input del jugador en ordenes al Modelo.
    // NO decide nada de negocio (eso ya vive en el Modelo) — solo "pasa
    // informacion" entre el Modelo y la Vista, tal como pidio la profesora.
    //
    // Es el UNICO MonoBehaviour del Controlador: aqui viven las referencias
    // que se asignan en el Inspector (Vista, Prefabs y HUD). Los nombres de
    // los campos [SerializeField] NO cambiaron, asi que las referencias ya
    // asignadas en la escena se conservan.
    public class GameController : MonoBehaviour
    {
        [Header("Vista - Mundo (Pista A)")]
        [SerializeField] private MapaView mapaView;

        [Header("Vista - Fin de Partida (Tus Paneles)")]
        [SerializeField] private FinPartidaView finPartidaView;

        // Un Prefab especifico por cada tipo de unidad (en vez de uno solo
        // generico), porque asi armo su Vista tu compañero: cada Prefab ya
        // trae su propio sprite y su componente UnidadView configurado.
        [Header("Vista - Prefabs de Unidades (uno por tipo)")]
        [SerializeField] private GameObject prefabUnidadAldeano;
        [SerializeField] private GameObject prefabUnidadEspadachin;
        [SerializeField] private GameObject prefabUnidadPiquero;
        [SerializeField] private GameObject prefabUnidadArquero;

        // Mismo patron para edificios: un Prefab por tipo, cada uno con su
        // componente EdificioView ya configurado.
        [Header("Vista - Prefabs de Edificios (uno por tipo)")]
        [SerializeField] private GameObject prefabEdificioTownCenter; // Edificio_Castillo
        [SerializeField] private GameObject prefabEdificioCasa;        // Edificio_Casa
        [SerializeField] private GameObject prefabEdificioTaller;      // Edificio_Cuartel
        [SerializeField] private GameObject prefabEdificioDefensa;     // Edificio_Torre

        // El Canvas_HUD que arma tu compañero (con su componente HUDController),
        // ya colocado en la escena (no se instancia en tiempo de ejecucion,
        // solo se arrastra la referencia desde la Hierarchy).
        [Header("Vista - HUD")]
        [SerializeField] private HUDController hud;

        private Mapa mapa;
        private Jugador jugadorGrecia;
        private Jugador jugadorIA;
        private Partida partida;
        private JugadorIA cerebroIA;
        private GestorArchivos archivos;
        private CancellationTokenSource cancelacionLog;

        // true cuando ya se mostro el panel de victoria/derrota: a partir de
        // ahi Update() no hace nada mas.
        private bool finDePartidaMostrado;

        // Los dos controladores especializados (se crean en InicializarPartida).
        private SincronizacionVistaController sincronizacionVista;
        private InputController inputController;

        private void Start()
        {
            InicializarPartida();
        }

        private void InicializarPartida()
        {
            // 1. El Modelo arma TODO el estado inicial (mapa, jugadores,
            // Centros Urbanos, depositos, aldeanos recolectando y Partida).
            // Ver ConfiguradorPartida: alli estan las posiciones y
            // cantidades, porque son reglas del juego, no del Controlador.
            var configurador = new ConfiguradorPartida();
            configurador.Configurar();

            mapa = configurador.Mapa;
            jugadorGrecia = configurador.JugadorGrecia;
            jugadorIA = configurador.JugadorRival;
            partida = configurador.Partida;

            // 2. Le pedimos a la Vista (tu MapaView) que dibuje los
            // depositos de recurso que el Modelo acaba de colocar.
            mapaView.RenderizarRecursos(mapa);

            // 3. Los dos controladores especializados. A SincronizacionVista
            // le pasamos las funciones que traducen "tipo del Modelo" ->
            // "Prefab" (viven aqui porque los Prefabs se asignan en este
            // Inspector). Los Centros Urbanos ya estan en Jugador.Edificios,
            // asi que el primer Sincronizar() les crea su vista solo.
            sincronizacionVista = new SincronizacionVistaController(
                mapaView, hud, transform,
                ElegirPrefabUnidad, ElegirPrefabEdificio,
                mapa, jugadorGrecia, jugadorIA);

            inputController = new InputController(jugadorGrecia);

            // 4. Archivos: configuracion.txt de una vez, y arrancamos el
            // hilo que va a ir escribiendo log_partida.txt solo.
            archivos = new GestorArchivos();
            archivos.GuardarConfiguracionInicial(jugadorGrecia, jugadorIA);
            cancelacionLog = archivos.IniciarEscuchaDeEventos(partida);

            // 5. Arrancamos el "cerebro" de la maquina — desde este momento
            // el Jugador "El Resto" empieza a tomar decisiones solo, en su
            // propio hilo, cada 4 segundos.
            cerebroIA = new JugadorIA(partida, jugadorIA, jugadorGrecia);
            cerebroIA.Iniciar();

            // (La postura defensiva de las tropas de Grecia y el disparo de
            // las Torres ya no se arrancan aqui: son reglas del juego y las
            // activa el Modelo — ver ConfiguradorPartida y
            // Jugador.ConstruirEdificio.)

            // 6. Indicador en pantalla del cooldown de entrenamiento. Se crea
            // por codigo (no hace falta agregar nada en la escena ni en el
            // Inspector) y solo LEE del Modelo, mediante una funcion.
            var indicadorCooldown = gameObject.AddComponent<EntrenamientoCooldownView>();
            indicadorCooldown.Configurar(() => jugadorGrecia.EntrenamientoDisponible, () => jugadorGrecia.SegundosParaPoderEntrenar);
        }

        private void Update()
        {
            // La partida ya termino y el resultado ya se mostro: no se
            // sincroniza nada mas ni se lee input (asi, con el panel de
            // victoria/derrota a la vista, ya no se pueden dar ordenes).
            if (finDePartidaMostrado) return;

            // Le preguntamos a la Partida (Modelo) si ya alguien gano.
            bool yaAcabo = partida.VerificarGanador();

            if (yaAcabo)
            {
                // Ultima sincronizacion: que la Vista refleje el estado final
                // (por ejemplo, el Centro Urbano destruido empieza su
                // desvanecimiento; esa animacion sigue sola despues).
                sincronizacionVista.Sincronizar();

                // AQUI CONECTAMOS CON TUS PANELES (FinPartidaView). Quien
                // gano lo responde la Partida (Modelo), no se compara aqui.
                if (finPartidaView != null)
                {
                    if (partida.GanoJugador(jugadorGrecia))
                    {
                        finPartidaView.MostrarVictoria();
                    }
                    else
                    {
                        finPartidaView.MostrarDerrota();
                    }
                }

                // La partida termino: se detienen TODOS los hilos del juego
                // (ver DetenerHilos). resultado_final.txt lo guarda solo
                // GestorArchivos al ver la Partida finalizada.
                DetenerHilos();

                finDePartidaMostrado = true;
                return;
            }

            // Modelo -> Vista: recursos, unidades, edificios y HUD.
            sincronizacionVista.Sincronizar();

            // Jugador -> Modelo: seleccionar edificios/unidades propias con
            // clic, elegir accion con teclas, mover/atacar con un segundo
            // clic.
            inputController.ManejarInput();
        }

        // Traduce el tipo real de la Unidad (Modelo) al Prefab visual que le
        // corresponde (Vista) — mismo patron que ElegirPrefab en MapaView.
        private GameObject ElegirPrefabUnidad(Unidad unidad) => unidad switch
        {
            Villager => prefabUnidadAldeano,
            Espadachin => prefabUnidadEspadachin,
            Piquero => prefabUnidadPiquero,
            Arquero => prefabUnidadArquero,
            _ => null,
        };

        // Mismo patron que ElegirPrefabUnidad, pero para Edificios.
        private GameObject ElegirPrefabEdificio(Edificio edificio) => edificio switch
        {
            TownCenter => prefabEdificioTownCenter,
            House => prefabEdificioCasa,
            Taller => prefabEdificioTaller,
            Defensa => prefabEdificioDefensa,
            _ => null,
        };

        private void OnDestroy()
        {
            // Buena practica: si el objeto se destruye (se cierra el juego,
            // se cambia de escena, se sale de Play en el Editor), detenemos
            // los hilos en vez de dejarlos corriendo en el vacio.
            DetenerHilos();
        }

        // Detiene, de forma ordenada, TODOS los hilos de la partida:
        //  - los dos ciclos de la IA (economia y militar),
        //  - los hilos de cada Jugador (recoleccion, construccion,
        //    entrenamiento, movimiento, torres y defensa automatica),
        //  - el hilo que escribe el log.
        // Todos terminan al cancelarse su CancellationToken (sus Task.Delay
        // lanzan OperationCanceledException). Es seguro llamarlo varias veces.
        private void DetenerHilos()
        {
            cerebroIA?.Detener();
            jugadorGrecia?.DetenerHilos();
            jugadorIA?.DetenerHilos();
            cancelacionLog?.Cancel();
        }
    }
}