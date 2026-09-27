using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using ImperiosEnGuerra.Modelo;
using ImperiosEnGuerra.Vista;

namespace ImperiosEnGuerra.Controlador
{
    // Orquesta toda la partida: crea el Modelo (Mapa, Jugadores, Partida),
    // coloca la configuracion inicial, arranca la IA y el logueo, y en cada
    // frame sincroniza la Vista (unidades, edificios, recursos, HUD) contra
    // lo que diga el Modelo. NO decide nada de negocio (eso ya vive en el
    // Modelo) — solo "pasa informacion" entre el Modelo y la Vista, tal
    // como pidio la profesora.
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

        // Recuerdan que GameObject visual le corresponde a cada Unidad/
        // Edificio del Modelo (por su Id), para no crear duplicados y para
        // poder actualizarlos frame a frame.
        private readonly Dictionary<System.Guid, GameObject> vistasUnidades = new Dictionary<System.Guid, GameObject>();
        private readonly Dictionary<System.Guid, GameObject> vistasEdificios = new Dictionary<System.Guid, GameObject>();

        // -------------------------------------------------------------
        // SISTEMA DE INPUT DEL JUGADOR HUMANO (Grecia): guarda que hay
        // seleccionado en este momento. Solo uno de los dos puede estar
        // activo a la vez — seleccionar algo nuevo limpia el otro.
        // -------------------------------------------------------------
        private Edificio edificioSeleccionado;
        private Unidad unidadSeleccionada;

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

            // Los dos Centros Urbanos ya quedan en Jugador.Edificios, asi que
            // el propio Update() los va a detectar en el primer frame y les
            // va a crear su vista solo (via ActualizarEdificiosDe) — no hace
            // falta instanciarlos aqui a mano.

            // 3. Esparcir algunos depositos de recurso por el mapa (numeros
            // de ejemplo, se pueden ajustar facil mas adelante).
            // ColocarDeposito ahora DEVUELVE el Recurso creado, porque lo
            // necesitamos mas abajo para mandar a los aldeanos a recolectar
            // de un deposito especifico (antes se creaba y se perdia la
            // referencia, ya que solo hacia falta para pintar el mapa).
            var depositoOro = ColocarDeposito(TipoRecurso.Oro, new Posicion(5, 10), 300);
            var depositoMadera = ColocarDeposito(TipoRecurso.Madera, new Posicion(8, 4), 400);
            var depositoPiedra = ColocarDeposito(TipoRecurso.Piedra, new Posicion(12, 15), 250);
            var depositoMetal = ColocarDeposito(TipoRecurso.Metal, new Posicion(15, 6), 200);

            // 4. La partida que orquesta a los dos jugadores.
            partida = new Partida(jugadorGrecia, jugadorIA);

            // 5. Le pedimos a la Vista (tu MapaView) que dibuje los
            // depositos de recurso que acabamos de colocar en el Modelo.
            mapaView.RenderizarRecursos(mapa);

            // 6. SOLUCION DEFINITIVA al problema de "nunca hay recursos
            // suficientes": antes ningun jugador recolectaba nada, asi que
            // se quedaban fijos en los 100 iniciales para siempre y jamas
            // alcanzaba para el Taller (120 Madera). Ahora cada jugador
            // arranca con UN Villager ya recolectando del deposito de
            // Madera — asi la economia avanza sola con el tiempo, tal como
            // pide la guia, y los costos de construccion empiezan a tener
            // sentido real.
            //
            // Grecia: el deposito de Madera (8,4) es el mas cercano a su
            // Centro Urbano en (2,2). Lo ubicamos justo al lado del
            // Centro Urbano, en una celda libre.
            var posicionAldeanoGrecia = new Posicion(3, 2);
            var aldeanoGrecia = new Villager(posicionAldeanoGrecia);
            jugadorGrecia.AgregarUnidad(aldeanoGrecia);
            mapa.ColocarUnidad(posicionAldeanoGrecia, aldeanoGrecia);
            jugadorGrecia.IniciarRecoleccion(aldeanoGrecia, depositoMadera, new Posicion(8, 4));

            // El Resto (IA): usamos el mismo deposito de Madera, porque es
            // justo el recurso que JugadorIA necesita primero para poder
            // construir su Taller (ver JugadorIA.DecidirYEjecutarAccion).
            // Queda mas lejos de su Centro Urbano (17,17), pero el
            // Villager igual llega recolectando con el tiempo — no hace
            // falta que este pegado al deposito, IniciarRecoleccion no
            // valida distancia.
            var posicionAldeanoIA = new Posicion(16, 17);
            var aldeanoIA = new Villager(posicionAldeanoIA);
            jugadorIA.AgregarUnidad(aldeanoIA);
            mapa.ColocarUnidad(posicionAldeanoIA, aldeanoIA);
            jugadorIA.IniciarRecoleccion(aldeanoIA, depositoMadera, new Posicion(8, 4));

            // 7. Archivos: configuracion.txt de una vez, y arrancamos el
            // hilo que va a ir escribiendo log_partida.txt solo.
            archivos = new GestorArchivos();
            archivos.GuardarConfiguracionInicial(jugadorGrecia, jugadorIA);
            cancelacionLog = archivos.IniciarEscuchaDeEventos(partida);

            // 8. Arrancamos el "cerebro" de la maquina — desde este momento
            // el Jugador "El Resto" empieza a tomar decisiones solo, en su
            // propio hilo, cada 4 segundos.
            cerebroIA = new JugadorIA(partida, jugadorIA, jugadorGrecia);
            cerebroIA.Iniciar();
        }

        // Pequeño helper para no repetir 3 lineas por cada deposito. Ahora
        // devuelve el Recurso creado (antes no devolvia nada) porque lo
        // necesitamos para mandar aldeanos a recolectar de un deposito
        // puntual, no solo para pintarlo en el mapa.
        private Recurso ColocarDeposito(TipoRecurso tipo, Posicion posicion, int cantidad)
        {
            var recurso = new Recurso(tipo, cantidad);
            mapa.ColocarRecurso(posicion, recurso);
            return recurso;
        }

        private void Update()
        {
            // OJO: Le preguntamos a la Partida (Modelo) si ya alguien ganó
            // antes de dibujar recursos o mover aldeanos.
            bool yaAcabo = partida.VerificarGanador();

            // Revisa si la partida ya termino, para guardar el resultado
            // final UNA sola vez y mostrar el panel de victoria/derrota.
            if (yaAcabo && cancelacionLog != null)
            {
                archivos.GuardarResultadoFinal(partida);
                cancelacionLog.Cancel();
                cancelacionLog = null; // evita que se vuelva a guardar en el siguiente frame

                // AQUI CONECTAMOS CON TUS PANELES (FinPartidaView)
                if (finPartidaView != null)
                {
                    if (partida.Ganador == jugadorGrecia.Nombre)
                    {
                        finPartidaView.MostrarVictoria();
                    }
                    else
                    {
                        finPartidaView.MostrarDerrota();
                    }
                }

                return; // Cortamos el Update aquí para que no siga dibujando cosas si el juego ya acabó
            }

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

            // HUD: recursos actuales de Grecia (el jugador humano; el HUD no
            // muestra los recursos de la IA, solo los del jugador real).
            if (hud != null)
            {
                hud.ActualizarOro(jugadorGrecia.Recursos[TipoRecurso.Oro]);
                hud.ActualizarMadera(jugadorGrecia.Recursos[TipoRecurso.Madera]);
                hud.ActualizarPiedra(jugadorGrecia.Recursos[TipoRecurso.Piedra]);
                hud.ActualizarMetal(jugadorGrecia.Recursos[TipoRecurso.Metal]);
            }

            // Input del jugador humano: seleccionar edificios/unidades
            // propias con clic, elegir accion con teclas, mover/atacar
            // con un segundo clic. Va al final del Update porque ya
            // cortamos arriba con "return" si la partida termino, asi
            // que si llegamos hasta aqui es porque el juego sigue en curso.
            ManejarInput();
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

                    var prefab = ElegirPrefabUnidad(unidad);
                    if (prefab == null) continue; // tipo de unidad sin prefab asignado todavia

                    var instancia = Instantiate(prefab, transform);
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

                    var prefab = ElegirPrefabEdificio(edificio);
                    if (prefab == null) continue;

                    var instancia = Instantiate(prefab, transform);
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

        // ---------------------------------------------------------------
        // INPUT DEL JUGADOR: revisa cada frame si hubo clic o tecla
        // relevante. El Controlador NO decide si una accion es valida
        // (eso lo valida el Modelo, como siempre) — solo traduce la
        // intencion del jugador en la llamada correcta.
        // ---------------------------------------------------------------
        private void ManejarInput()
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
        // mundo, y revisa que collider 2D hay justo ahi (los edificios ya
        // tienen Box Collider 2D; las unidades necesitan uno tambien para
        // poder seleccionarse con clic).
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
        // posicion, para simplificar la interaccion). Jugador.ConstruirEdificio
        // ya valida costo y celda libre, exactamente igual que con la IA.
        private void OrdenarConstruir(Func<Posicion, Edificio> fabricaEdificio)
        {
            var posicionLibre = PosicionLibreCercana(edificioSeleccionado.Posicion);
            jugadorGrecia.ConstruirEdificio(fabricaEdificio, posicionLibre);
            DeseleccionarTodo();
        }

        // Mismo patron que OrdenarConstruir, pero entrena UNA sola tropa
        // (no un lote de 5 como hace JugadorIA) porque aqui es una accion
        // puntual decidida por el jugador, no una decision automatica.
        private void OrdenarEntrenar(Taller taller, Func<Posicion, Tropa> fabricaTropa)
        {
            var posicionLibre = PosicionLibreCercana(taller.Posicion);
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
            if (destino.X < 0 || destino.X >= mapa.Ancho || destino.Y < 0 || destino.Y >= mapa.Alto) return;

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

        // Busca la celda libre mas cercana a "origen", revisando anillos
        // cada vez mas grandes alrededor (radio 1, luego 2, etc.) hasta
        // encontrar una que Mapa.CeldaLibre() acepte.
        private Posicion PosicionLibreCercana(Posicion origen, int radioMaximo = 5)
        {
            for (int radio = 1; radio <= radioMaximo; radio++)
            {
                for (int dx = -radio; dx <= radio; dx++)
                {
                    for (int dy = -radio; dy <= radio; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radio) continue; // solo el borde del anillo

                        var candidata = new Posicion(origen.X + dx, origen.Y + dy);
                        if (candidata.X < 0 || candidata.X >= mapa.Ancho || candidata.Y < 0 || candidata.Y >= mapa.Alto) continue;
                        if (mapa.CeldaLibre(candidata)) return candidata;
                    }
                }
            }
            return origen; // fallback improbable: no habia ninguna celda libre cerca
        }

        private void DeseleccionarTodo()
        {
            edificioSeleccionado = null;
            unidadSeleccionada = null;
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