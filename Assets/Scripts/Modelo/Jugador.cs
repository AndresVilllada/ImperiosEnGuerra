using System;
using System.Collections.Generic;
using System.Collections.Concurrent; // ConcurrentDictionary, ConcurrentQueue: colecciones thread-safe
using System.Threading;               // CancellationTokenSource: detener los hilos de forma ordenada
using System.Threading.Tasks;         // Task.Run / Task.Delay, para lanzar cada hilo y esperar sin bloquearlo

namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // EVENTOJUEGO: un mensaje simple que el Modelo deposita cuando algo
    // relevante paso en un hilo secundario (termino de recolectar, termino
    // de construir, etc). El Controlador la va vaciando (TryDequeue) y
    // decide que mostrar en la Vista o que escribir en el log — el Modelo
    // no sabe nada de la Vista ni de archivos, solo "avisa que algo paso".
    // ---------------------------------------------------------------------
    public class EventoJuego
    {
        public string Tipo { get; }     // categoria del evento: "Recoleccion", "Construccion", "Entrenamiento", "Ataque", etc.
        public string Mensaje { get; }  // texto legible para mostrar/loguear

        public EventoJuego(string tipo, string mensaje)
        {
            Tipo = tipo;
            Mensaje = mensaje;
        }
    }

    // ---------------------------------------------------------------------
    // TIPOS QUE EL JUGADOR PUEDE PEDIR: el Controlador solo dice "quiero una
    // Casa" o "quiero un Espadachin" con estos enums; QUE clase concreta se
    // crea (y con que parametros) lo decide el Modelo, en FabricaDeEntidades.
    // Asi el Controlador ya no hace "new House(...)" ni "new Espadachin(...)".
    // ---------------------------------------------------------------------
    public enum TipoEdificio { Casa, Taller, Torre }
    public enum TipoTropa { Espadachin, Piquero, Arquero }

    public static class FabricaDeEntidades
    {
        public static Edificio CrearEdificio(TipoEdificio tipo, Posicion posicion)
        {
            switch (tipo)
            {
                case TipoEdificio.Casa: return new House(posicion);
                case TipoEdificio.Taller: return new Taller(posicion);
                case TipoEdificio.Torre: return new Defensa(posicion);
                default: throw new ArgumentOutOfRangeException(nameof(tipo));
            }
        }

        public static Tropa CrearTropa(TipoTropa tipo, Posicion posicion)
        {
            switch (tipo)
            {
                case TipoTropa.Espadachin: return new Espadachin(posicion);
                case TipoTropa.Piquero: return new Piquero(posicion);
                case TipoTropa.Arquero: return new Arquero(posicion);
                default: throw new ArgumentOutOfRangeException(nameof(tipo));
            }
        }
    }

    // ---------------------------------------------------------------------
    // JUGADOR: agrupa todo lo que le pertenece a un jugador (humano o el que
    // controla JugadorIA) — su mapa, sus recursos, sus unidades, sus
    // edificios — y expone los METODOS que representan las acciones del
    // juego (recolectar, construir, entrenar). Toda la logica de "cuando"
    // y "que" hacer vive AFUERA de esta clase (en el Controlador si es
    // humano, o en JugadorIA si es la maquina); Jugador solo sabe COMO
    // ejecutar cada accion de forma segura entre hilos. Por eso esta clase
    // NO sabe nada de IA — eso es responsabilidad exclusiva de JugadorIA.cs.
    //
    // CONCURRENCIA: cada actividad de larga duracion (recolectar, construir,
    // entrenar, mover, torres...) corre en su propia Task. Todas esperan con
    // "await Task.Delay(...)" en vez de Thread.Sleep: mientras esperan NO
    // ocupan un hilo del pool (antes, cada Sleep dejaba un hilo bloqueado sin
    // hacer nada), y todas comparten un CancellationToken (ver DetenerHilos)
    // para terminar de forma ordenada cuando acaba la partida.
    // ---------------------------------------------------------------------
    public class Jugador
    {
        public string Nombre { get; private set; }
        public Mapa Mapa { get; private set; } // referencia al mapa UNICO y compartido con el rival

        // ConcurrentDictionary en vez de Dictionary normal: varios hilos
        // (varios aldeanos recolectando, varias tropas entrenandose) pueden
        // leer/escribir esta coleccion "al mismo tiempo" sin corromperla.
        public ConcurrentDictionary<TipoRecurso, int> Recursos { get; private set; }
        public ConcurrentDictionary<Guid, Unidad> Unidades { get; private set; }
        public ConcurrentDictionary<Guid, Edificio> Edificios { get; private set; }

        // Cola thread-safe: cualquier hilo puede Enqueue() sin pisar a otro
        // hilo que este haciendo lo mismo al mismo tiempo.
        public ConcurrentQueue<EventoJuego> Eventos { get; private set; }

        // Candado dedicado SOLO para proteger las operaciones de "pagar"
        // recursos (ver PagarCosto). Es distinto de cualquier lock que
        // tengan Unidad/Edificio/Recurso — cada clase protege lo suyo.
        private readonly object candadoRecursos = new object();

        // Tiempo minimo entre dos entrenamientos de tropa de este jugador
        // (sin importar cual Taller use). Es una REGLA DEL JUEGO, asi que se
        // decide en el Modelo (ConfiguradorPartida la fija: 8 s para Grecia,
        // 10 s para la IA) y no en el Controlador. Si no se asigna, 10 s.
        public TimeSpan CooldownEntrenamiento { get; set; } = TimeSpan.FromSeconds(10);

        // Candado dedicado a la hora del proximo entrenamiento permitido:
        // la IA (hilo de economia) y el input del humano (hilo principal de
        // Unity) consultan y actualizan este valor desde hilos distintos.
        private readonly object candadoEntrenamiento = new object();
        private DateTime proximoEntrenamientoPermitido = DateTime.MinValue;

        // UNA ORDEN ACTIVA POR UNIDAD: cada vez que se le da una orden nueva a
        // una unidad (mover, recolectar), se cancela la anterior y se crea un
        // token nuevo, ligado al token general del jugador (asi DetenerHilos
        // sigue deteniendolo todo). Sin esto, un aldeano podria estar
        // caminando hacia un deposito y hacia una celda a la vez.
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> ordenesActivas =
            new ConcurrentDictionary<Guid, CancellationTokenSource>();

        // Ritmo de los aldeanos: cuanto tarda en dar un paso y cada cuanto
        // extrae del deposito (son reglas de balance, se ajustan aqui).
        private const int IntervaloPasoAldeanoMs = 400;
        private const int IntervaloRecoleccionMs = 1000;

        // System.Random no es thread-safe y ahora lo usan varios aldeanos a
        // la vez (cada uno en su hilo): siempre se accede por Aleatorio().
        private readonly Random random = new Random();

        // Fuente del token de cancelacion que comparten TODOS los hilos de
        // este jugador. Cancelarla (DetenerHilos) hace que cada Task.Delay
        // pendiente lance OperationCanceledException y la tarea termine.
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        // El otro Jugador de la partida. Lo necesitan las Torres y las tropas
        // para saber a QUIEN disparar. Lo asigna ConfiguradorPartida una vez,
        // al armar la partida (antes el Controlador se lo iba pasando a los
        // metodos que lo necesitaban).
        public Jugador Rival { get; private set; }

        public void DefinirRival(Jugador rival)
        {
            Rival = rival;
        }

        public Jugador(string nombre, Mapa mapa)
        {
            Nombre = nombre;
            Mapa = mapa;

            // Arranca con 100 de cada recurso. Se inicializan explicitamente
            // los 4 tipos para que Recursos nunca este "incompleto" (evita
            // tener que chequear ContainsKey en todos lados despues).
            Recursos = new ConcurrentDictionary<TipoRecurso, int>();
            Recursos[TipoRecurso.Oro] = 100;
            Recursos[TipoRecurso.Madera] = 100;
            Recursos[TipoRecurso.Piedra] = 100;
            Recursos[TipoRecurso.Metal] = 100;

            Unidades = new ConcurrentDictionary<Guid, Unidad>();
            Edificios = new ConcurrentDictionary<Guid, Edificio>();
            Eventos = new ConcurrentQueue<EventoJuego>();
        }

        // Pide que TODOS los hilos de este jugador terminen (recoleccion,
        // construccion, entrenamiento, movimiento, torres y defensa
        // automatica). Se llama cuando acaba la partida o se cierra el juego;
        // asi ningun hilo queda corriendo "en el vacio". Es seguro llamarlo
        // varias veces.
        public void DetenerHilos() => cts.Cancel();

        // Lanza una actividad en su propio hilo (Task.Run) con manejo comun:
        //  - Si cancelan (DetenerHilos), la OperationCanceledException es lo
        //    ESPERADO: la tarea simplemente termina, sin ruido.
        //  - Cualquier otra excepcion se deja en el log. Dentro de un Task.Run
        //    una excepcion no se ve en ningun lado y mataria el hilo en
        //    silencio.
        // El nombre solo sirve para identificar el hilo en ese mensaje.
        private void Lanzar(string nombreHilo, Func<Task> trabajo)
        {
            Task.Run(async () =>
            {
                try
                {
                    await trabajo();
                }
                catch (OperationCanceledException)
                {
                    // cancelacion normal: nada que reportar
                }
                catch (Exception ex)
                {
                    Eventos.Enqueue(new EventoJuego("Error", $"Fallo en el hilo de {nombreHilo}: {ex.Message}"));
                }
            });
        }

        // Suma "cantidad" al tipo de recurso indicado, de forma ATOMICA.
        // AddOrUpdate es un metodo propio de ConcurrentDictionary: si la
        // llave ya existe, aplica la funcion (sumar) de forma segura aunque
        // dos hilos llamen esto "al mismo tiempo"; si no existe, la crea
        // con el valor inicial dado (aqui nunca pasa, porque el constructor
        // ya puso los 4 tipos, pero es buena practica dejarlo resuelto igual).
        public void AgregarRecurso(TipoRecurso tipo, int cantidad)
        {
            Recursos.AddOrUpdate(tipo, cantidad, (clave, valorActual) => valorActual + cantidad);
        }

        // Revisa si el jugador tiene AL MENOS lo que pide "costo" de cada
        // tipo de recurso. Solo LEE, no modifica nada — por eso no necesita
        // lock propio (leer un ConcurrentDictionary ya es seguro de por si).
        public bool TieneRecursosSuficientes(IReadOnlyDictionary<TipoRecurso, int> costo)
        {
            foreach (var par in costo)
            {
                if (!Recursos.ContainsKey(par.Key) || Recursos[par.Key] < par.Value)
                    return false;
            }
            return true;
        }

        // Verifica Y descuenta el costo como UNA SOLA operacion atomica
        // (protegida con lock). Esto es clave: sin el lock, dos hilos
        // podrian ambos "ver" que si alcanza el oro, y los dos descontar,
        // dejando el saldo en negativo (condicion de carrera clasica).
        // Devuelve false sin tocar nada si no alcanza.
        public bool PagarCosto(IReadOnlyDictionary<TipoRecurso, int> costo)
        {
            lock (candadoRecursos)
            {
                if (!TieneRecursosSuficientes(costo)) return false;

                foreach (var par in costo)
                {
                    Recursos[par.Key] -= par.Value;
                }
                return true;
            }
        }

        // Altas/bajas simples sobre las colecciones. Como son
        // ConcurrentDictionary, el indexador "[]" y TryRemove ya son
        // atomicos por si solos, sin necesitar lock adicional aqui.
        public void AgregarUnidad(Unidad unidad) => Unidades[unidad.Id] = unidad;
        public void RemoverUnidad(Unidad unidad) => Unidades.TryRemove(unidad.Id, out _);
        public void AgregarEdificio(Edificio edificio) => Edificios[edificio.Id] = edificio;

        // Regla de pertenencia: ¿este edificio/unidad es de ESTE jugador? La
        // consulta el Controlador para saber si un clic es "seleccionar lo
        // mio" o "atacar al rival", pero la respuesta la da el Modelo.
        public bool EsPropio(Edificio edificio) => edificio != null && Edificios.ContainsKey(edificio.Id);
        public bool EsPropio(Unidad unidad) => unidad != null && Unidades.ContainsKey(unidad.Id);

        // Recorre los edificios buscando el TownCenter (Centro Urbano) y
        // devuelve si esta destruido. Si el jugador nunca tuvo uno (caso
        // raro, no deberia pasar en la practica), se considera derrotado
        // por seguridad (return true al final del foreach sin encontrarlo).
        public bool CentroUrbanoDestruido()
        {
            foreach (var edificio in Edificios.Values)
            {
                if (edificio is TownCenter) return edificio.EstaDestruido;
            }
            return true;
        }

        // Revisa si TODAS las unidades de tipo Tropa (militares) que alguna
        // vez tuvo el jugador ya estan muertas. "tuvoAlgunaTropa" evita que
        // un jugador que JAMAS entreno ninguna tropa (por ejemplo, recien
        // empezando la partida) se considere "derrotado" por default.
        public bool TodasLasUnidadesMilitaresDestruidas()
        {
            bool tuvoAlgunaTropa = false;
            foreach (var unidad in Unidades.Values)
            {
                if (unidad is Tropa) // "is" chequea el tipo real del objeto, aunque este guardado como Unidad
                {
                    tuvoAlgunaTropa = true;
                    if (unidad.EstaViva) return false; // con que UNA siga viva, ya no estan "todas destruidas"
                }
            }
            return tuvoAlgunaTropa;
        }

        // -------------------------------------------------------------
        // ENTRENAMIENTO EN PARALELO: lanza "cantidad" hilos INDEPENDIENTES
        // (uno por tropa), cada uno pagando su propio costo y esperando su
        // propio tiempo de entrenamiento. "fabricaTropa" es una funcion que
        // recibe una Posicion y devuelve la tropa concreta ya construida
        // (Espadachin, Piquero, Arquero) — asi este metodo generico no
        // necesita saber cual de las 3 tropas se esta entrenando.
        // -------------------------------------------------------------
        public void EntrenarLote(Taller taller, Func<Posicion, Tropa> fabricaTropa, Posicion posicionSpawn, int cantidad = 5)
        {
            if (taller == null || !taller.EstaConstruido)
            {
                Eventos.Enqueue(new EventoJuego("Entrenamiento", "El Taller no está listo para entrenar."));
                return;
            }

            for (int i = 0; i < cantidad; i++)
            {
                // Cada iteracion lanza SU PROPIO hilo — las "cantidad" tropas
                // se entrenan "al mismo tiempo", no una despues de otra.
                Lanzar("entrenamiento", async () =>
                {
                    var tropa = fabricaTropa(posicionSpawn);

                    // PagarCosto ya esta protegido con lock: si el oro no
                    // alcanza para todas, exactamente las que si alcanzaron
                    // tendran exito, sin dejar saldo negativo.
                    if (!PagarCosto(tropa.Costo))
                    {
                        Eventos.Enqueue(new EventoJuego("Entrenamiento", $"No hay recursos suficientes para entrenar {tropa.Nombre}."));
                        return;
                    }

                    // Tiempo de entrenamiento simulado. Task.Delay espera
                    // SIN bloquear un hilo del pool (antes Thread.Sleep). Si
                    // cancelan durante la espera (fin de la partida), la
                    // tarea termina aqui y no se despliega la tropa.
                    await Task.Delay(3000, cts.Token);

                    // Cada tropa ocupa SU PROPIA celda libre cerca del punto
                    // de spawn (antes las 5 iban a la misma celda y quedaban
                    // apiladas). Se coloca primero en el mapa, que ademas fija
                    // su posicion, y solo despues se agrega al jugador, para
                    // que la Vista nunca la dibuje en una posicion vieja.
                    if (!Mapa.TryColocarUnidadCerca(posicionSpawn, tropa, out var posicionFinal))
                    {
                        // No hubo espacio: se devuelve lo pagado.
                        foreach (var par in tropa.Costo)
                        {
                            AgregarRecurso(par.Key, par.Value);
                        }
                        Eventos.Enqueue(new EventoJuego("Entrenamiento", $"No hay espacio para desplegar {tropa.Nombre}; se devolvieron los recursos."));
                        return;
                    }

                    AgregarUnidad(tropa);

                    Eventos.Enqueue(new EventoJuego("Entrenamiento", $"{tropa.Nombre} entrenado y desplegado en {posicionFinal}."));
                });
            }
        }

        // ¿Ya se puede entrenar otra tropa? Es la regla del cooldown y la
        // decide el MODELO: la Vista solo muestra la respuesta (antes la
        // Vista la deducia con un umbral "segundos <= 0.05", que era una
        // regla del juego escrita en la interfaz).
        public bool EntrenamientoDisponible
        {
            get
            {
                lock (candadoEntrenamiento)
                {
                    return DateTime.UtcNow >= proximoEntrenamientoPermitido;
                }
            }
        }

        // Segundos que faltan para poder entrenar otra tropa (0 = ya se
        // puede). La consulta la IA antes de decidir, y la Vista para
        // mostrarle el cooldown al jugador.
        public double SegundosParaPoderEntrenar
        {
            get
            {
                lock (candadoEntrenamiento)
                {
                    return Math.Max(0, (proximoEntrenamientoPermitido - DateTime.UtcNow).TotalSeconds);
                }
            }
        }

        // -------------------------------------------------------------
        // ENTRENAR UNA TROPA CON COOLDOWN: la forma "normal" de entrenar
        // (la usan el jugador humano y la IA, con el mismo criterio). Aplica
        // la regla de "una tropa cada CooldownEntrenamiento segundos" y
        // delega el trabajo real (pagar, esperar, desplegar) en EntrenarLote
        // con cantidad 1. Devuelve true si la orden se acepto.
        //
        // Se rechaza SIN gastar el cooldown si el Taller no esta listo o si
        // no alcanzan los recursos: no tiene sentido hacer esperar al
        // jugador por una orden que no se ejecuto.
        // -------------------------------------------------------------
        public bool EntrenarTropa(Taller taller, Func<Posicion, Tropa> fabricaTropa, Posicion posicionSpawn)
        {
            if (taller == null || !taller.EstaConstruido || taller.EstaDestruido)
            {
                Eventos.Enqueue(new EventoJuego("Entrenamiento", "El Taller no está listo para entrenar."));
                return false;
            }

            lock (candadoEntrenamiento)
            {
                var ahora = DateTime.UtcNow;
                if (ahora < proximoEntrenamientoPermitido)
                {
                    double faltan = (proximoEntrenamientoPermitido - ahora).TotalSeconds;
                    Eventos.Enqueue(new EventoJuego("Entrenamiento", $"Entrenamiento en enfriamiento: faltan {faltan:0.0} s."));
                    return false;
                }

                // Se crea una tropa "de muestra" solo para conocer su costo
                // (EntrenarLote crea la definitiva dentro de su hilo).
                var muestra = fabricaTropa(posicionSpawn);
                if (!TieneRecursosSuficientes(muestra.Costo))
                {
                    Eventos.Enqueue(new EventoJuego("Entrenamiento", $"No hay recursos suficientes para entrenar {muestra.Nombre}."));
                    return false;
                }

                // La orden se acepta: arranca el cooldown.
                proximoEntrenamientoPermitido = ahora + CooldownEntrenamiento;
            }

            EntrenarLote(taller, fabricaTropa, posicionSpawn, 1);
            return true;
        }

        // -------------------------------------------------------------
        // ORDENES DE ALTO NIVEL: las que da el jugador humano por medio del
        // Controlador. El Controlador solo dice QUE quiere ("una Casa junto a
        // mi Centro Urbano"); DONDE se pone, si esta permitido y como se
        // ejecuta lo decide el Modelo, con las mismas reglas que usa la IA.
        // -------------------------------------------------------------

        // Construye un edificio del tipo pedido en la celda libre mas cercana
        // a "constructor". Solo un Centro Urbano PROPIO puede construir.
        public void ConstruirCerca(Edificio constructor, TipoEdificio tipo)
        {
            if (!(constructor is TownCenter) || !EsPropio(constructor)) return;

            var posicion = Mapa.BuscarCeldaLibreCercana(constructor.Posicion);
            ConstruirEdificio(pos => FabricaDeEntidades.CrearEdificio(tipo, pos), posicion);
        }

        // Entrena UNA tropa del tipo pedido, desplegada en la celda libre mas
        // cercana al Taller (aplica el cooldown, ver EntrenarTropa). Solo un
        // Taller PROPIO puede entrenar. Devuelve true si la orden se acepto.
        public bool EntrenarTropaCerca(Taller taller, TipoTropa tipo)
        {
            if (taller == null || !EsPropio(taller)) return false;

            var posicionSpawn = Mapa.BuscarCeldaLibreCercana(taller.Posicion);
            return EntrenarTropa(taller, pos => FabricaDeEntidades.CrearTropa(tipo, pos), posicionSpawn);
        }

        // -------------------------------------------------------------
        // MOVIMIENTO: ordena a una unidad PROPIA ir hacia "destino".
        //  - Valida que la unidad este viva y que el destino este dentro del
        //    mapa (antes lo validaba el Controlador).
        //  - Si es un ALDEANO y en el destino hay un deposito de recurso, la
        //    orden significa "ve a recolectar ahi" (ver OrdenarRecoleccion).
        //  - En cualquier otro caso camina hasta la celda (Unidad.MoverHacia).
        // Toda orden nueva CANCELA la anterior de esa unidad (NuevaOrden).
        // -------------------------------------------------------------
        public void OrdenarMovimiento(Unidad unidad, Posicion destino)
        {
            if (!EsPropio(unidad) || !unidad.EstaViva) return;
            if (!Mapa.EstaDentroDelMapa(destino)) return;

            if (unidad is Villager aldeano && Mapa.ObtenerRecurso(destino) != null)
            {
                OrdenarRecoleccion(aldeano, destino);
                return;
            }

            var token = NuevaOrden(unidad);
            unidad.MoverHacia(Mapa, destino, Eventos, token: token);
        }

        // Cancela la orden anterior de la unidad (si tenia una) y devuelve el
        // token de la nueva. Los hilos de la orden anterior terminan solos al
        // notar la cancelacion (su proximo Task.Delay lanza la excepcion).
        private CancellationToken NuevaOrden(Unidad unidad)
        {
            var nueva = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

            ordenesActivas.AddOrUpdate(
                unidad.Id,
                nueva,
                (id, anterior) =>
                {
                    anterior.Cancel();
                    return nueva;
                });

            return nueva.Token;
        }

        // Acceso thread-safe a Random (dos hilos llamando Next() a la vez
        // pueden corromper su estado interno y empezar a devolver siempre 0).
        private int Aleatorio(int maximoExclusivo)
        {
            lock (random)
            {
                return random.Next(maximoExclusivo);
            }
        }

        // -------------------------------------------------------------
        // ATAQUE MANUAL: una Tropa PROPIA golpea a un objetivo. Tropa.Atacar
        // valida rango y vida. El resultado queda como evento en la cola (y
        // por tanto en log_partida.txt); antes ese evento lo fabricaba el
        // Controlador. Devuelve true si el golpe se aplico.
        // -------------------------------------------------------------
        public bool OrdenarAtaque(Tropa tropa, IObjetivoAtacable objetivo)
        {
            if (!EsPropio(tropa) || objetivo == null) return false;

            bool golpeo = tropa.Atacar(objetivo);

            string resultado;
            if (!golpeo) resultado = $"{tropa.Nombre} no pudo atacar (fuera de rango).";
            else if (objetivo.EstaDestruido) resultado = $"{tropa.Nombre} destruyó {NombreObjetivo(objetivo)} enemigo.";
            else resultado = $"{tropa.Nombre} atacó a {NombreObjetivo(objetivo)}.";

            Eventos.Enqueue(new EventoJuego("Ataque", resultado));
            return golpeo;
        }

        // -------------------------------------------------------------
        // CONSTRUCCION CON PROGRESO: UN solo hilo por edificio, que paga el
        // costo, reserva la celda de inmediato, y va llamando
        // AvanzarConstruccion() repetidamente (con una pausa entre cada
        // llamada) hasta llegar a 100%, emitiendo un evento en cada paso
        // para que la Vista pueda mostrar una barra de progreso real.
        // -------------------------------------------------------------
        public void ConstruirEdificio(Func<Posicion, Edificio> fabricaEdificio, Posicion posicion, int incrementoPorTick = 10, int intervaloMs = 500)
        {
            if (!Mapa.CeldaLibre(posicion))
            {
                Eventos.Enqueue(new EventoJuego("Construccion", "La celda de destino no está libre."));
                return;
            }

            Lanzar("construccion", async () =>
            {
                var edificio = fabricaEdificio(posicion);

                if (!PagarCosto(edificio.Costo))
                {
                    Eventos.Enqueue(new EventoJuego("Construccion", $"No hay recursos suficientes para construir {edificio.Nombre}."));
                    return;
                }

                // Se reserva la celda apenas se paga (no al terminar), para
                // que ningun otro hilo intente construir algo distinto ahi
                // mientras este edificio sigue en progreso.
                Mapa.ColocarEdificio(posicion, edificio);
                AgregarEdificio(edificio);

                // Una Torre empieza a vigilar en cuanto se coloca (su hilo
                // espera a que termine de construirse antes de disparar).
                // Antes esto lo disparaba el Controlador cuando le aparecia
                // la vista de la Torre: era una regla del juego colgada de
                // que la interfaz "la viera".
                if (edificio is Defensa torre)
                {
                    IniciarDefensaAutomatica(torre);
                }

                Eventos.Enqueue(new EventoJuego("Construccion", $"Inició la construcción de {edificio.Nombre} en {posicion}."));

                // Bucle de progreso: cada intervaloMs suma incrementoPorTick%
                // hasta llegar a 100 (EstaConstruido pasa a true dentro de
                // AvanzarConstruccion cuando eso ocurre).
                while (!edificio.EstaConstruido)
                {
                    await Task.Delay(intervaloMs, cts.Token);
                    edificio.AvanzarConstruccion(incrementoPorTick);
                    Eventos.Enqueue(new EventoJuego("Construccion", $"{edificio.Nombre} construcción {edificio.ProgresoConstruccion}%"));
                }

                Eventos.Enqueue(new EventoJuego("Construccion", $"{edificio.Nombre} completado en {posicion}."));
            });
        }

        // -------------------------------------------------------------
        // RECOLECCION: UN hilo por aldeano. El aldeano trabaja en ciclos:
        //   1. elige un deposito (el que le ordenaron mientras siga
        //      disponible; si no, el mas cercano de su tipo de recurso),
        //   2. CAMINA hasta quedar junto a el (un paso cada 400 ms),
        //   3. recolecta cada segundo mientras el deposito tenga recurso,
        //   4. cuando se agota, vuelve al paso 1 con el siguiente deposito.
        // Termina si el aldeano muere, si le dan otra orden, si ya no quedan
        // depositos de ese tipo o si acaba la partida.
        //
        // AQUI SE VE LA COMPETENCIA ENTRE LOS DOS BANDOS: los aldeanos de
        // Grecia y de la IA corren en hilos distintos y pueden elegir el
        // MISMO deposito. Recurso.Extraer tiene su propio lock, asi que
        // cada unidad se la lleva UNA sola vez: lo que uno extrae, el otro ya
        // no lo tiene. Y Mapa.MoverUnidad protege las celdas por las que
        // caminan. El que pierde la carrera se redirige al siguiente deposito.
        // -------------------------------------------------------------

        // Pone a un aldeano PROPIO a recolectar el TIPO de recurso indicado
        // (siempre el deposito mas cercano). Lo usa ConfiguradorPartida para
        // dar el trabajo inicial de cada aldeano.
        public void IniciarRecoleccion(Villager aldeano, TipoRecurso tipo)
        {
            if (aldeano == null || !EsPropio(aldeano)) return;

            var token = NuevaOrden(aldeano);
            Lanzar("recoleccion", () => CicloRecoleccionAsync(aldeano, tipo, null, token));
        }

        // Orden del jugador: "aldeano, ve a recolectar ESE deposito". Cuando
        // se agote, sigue solo con el mas cercano del mismo tipo. Devuelve
        // false si no hay deposito en esa celda o ya esta agotado.
        public bool OrdenarRecoleccion(Villager aldeano, Posicion posicionDeposito)
        {
            if (aldeano == null || !EsPropio(aldeano) || !aldeano.EstaViva) return false;

            var deposito = Mapa.ObtenerRecurso(posicionDeposito);
            if (deposito == null || deposito.EstaAgotado()) return false;

            var token = NuevaOrden(aldeano);
            Lanzar("recoleccion", () => CicloRecoleccionAsync(aldeano, deposito.Tipo, posicionDeposito, token));
            return true;
        }

        private async Task CicloRecoleccionAsync(Villager aldeano, TipoRecurso tipo, Posicion? depositoOrdenado, CancellationToken token)
        {
            Posicion? preferido = depositoOrdenado;

            while (aldeano.EstaViva)
            {
                // ---- 1. Elegir deposito ----
                Posicion posicion = default;
                Recurso deposito = null;

                if (preferido.HasValue)
                {
                    var candidato = Mapa.ObtenerRecurso(preferido.Value);
                    if (candidato != null && !candidato.EstaAgotado())
                    {
                        posicion = preferido.Value;
                        deposito = candidato;
                    }
                }

                if (deposito == null && !Mapa.TryBuscarRecursoCercano(tipo, aldeano.Posicion, out posicion, out deposito))
                {
                    Eventos.Enqueue(new EventoJuego("Recoleccion", $"{aldeano.Nombre} no encontró más depósitos de {tipo}; se queda sin trabajo."));
                    return;
                }

                preferido = posicion; // mientras siga disponible, se sigue con el mismo

                // ---- 2. Caminar hasta quedar junto al deposito ----
                if (aldeano.Posicion.DistanciaManhattanHasta(posicion) > 1)
                {
                    Eventos.Enqueue(new EventoJuego("Recoleccion", $"{aldeano.Nombre} se dirige a recolectar {tipo} en {posicion}."));
                }

                while (aldeano.EstaViva
                       && !deposito.EstaAgotado()
                       && aldeano.Posicion.DistanciaManhattanHasta(posicion) > 1)
                {
                    Mapa.IntentarPasoHacia(aldeano, posicion, Aleatorio); // si esta bloqueado, reintenta en el proximo turno
                    await Task.Delay(IntervaloPasoAldeanoMs, token);
                }

                // ---- 3. Recolectar ----
                while (aldeano.EstaViva && !deposito.EstaAgotado())
                {
                    await Task.Delay(IntervaloRecoleccionMs, token); // lo que tarda un ciclo de recoleccion

                    // Extraer() tiene su propio lock interno: si dos aldeanos
                    // (incluso de bandos distintos) sacan del MISMO deposito a
                    // la vez, no se van a "robar" cantidad entre si.
                    int extraido = deposito.Extraer(aldeano.VelocidadRecoleccion);

                    if (extraido <= 0)
                    {
                        // Otro aldeano se llevo lo ultimo justo antes que este.
                        Eventos.Enqueue(new EventoJuego("Recoleccion", $"{aldeano.Nombre} llegó tarde: el depósito de {tipo} en {posicion} ya lo agotó otro aldeano."));
                        break;
                    }

                    AgregarRecurso(deposito.Tipo, extraido);

                    Eventos.Enqueue(new EventoJuego(
                        "Recoleccion",
                        $"{aldeano.Nombre} recolectó {extraido} de {deposito.Tipo} (quedan {deposito.Cantidad})."
                    ));
                }

                // ---- 4. Deposito agotado: se retira del mapa (una sola vez) ----
                // Si dos aldeanos lo agotan a la vez, TryRetirarRecursoAgotado
                // devuelve true solo para uno: ese es el que lo anuncia.
                if (deposito.EstaAgotado() && Mapa.TryRetirarRecursoAgotado(posicion, deposito))
                {
                    Eventos.Enqueue(new EventoJuego("Recoleccion", $"El depósito de {deposito.Tipo} en {posicion} se agotó."));
                }
                // y el ciclo vuelve al paso 1: siguiente deposito mas cercano
            }
        }

        // -------------------------------------------------------------
        // DEFENSA AUTOMATICA: UN hilo dedicado por cada Torre/Defensa, que
        // revisa periodicamente si hay algo del rival dentro de su rango y
        // dispara usando el propio Defensa.Atacar() (que ya valida rango
        // con DistanciaManhattanHasta, consistente con el resto del juego).
        // El hilo sigue vivo mientras la torre no este destruida; si aun no
        // termino de construirse, simplemente no dispara todavia. Se activa
        // sola desde ConstruirEdificio; dispara contra el Rival del jugador.
        // -------------------------------------------------------------
        private void IniciarDefensaAutomatica(Defensa torre, int intervaloMs = 2000)
        {
            var rival = Rival;
            if (torre == null || rival == null) return;

            Lanzar("defensa de torre", async () =>
            {
                while (!torre.EstaDestruido)
                {
                    await Task.Delay(intervaloMs, cts.Token);

                    if (!torre.EstaConstruido) continue; // sigue en construccion, todavia no dispara

                    var objetivo = BuscarObjetivoEnRango(torre.Posicion, torre.RangoAtaque, rival);
                    if (objetivo != null && torre.Atacar(objetivo))
                    {
                        Eventos.Enqueue(new EventoJuego("Ataque", $"{torre.Nombre} disparó y causó {torre.DanioAtaque} de daño."));
                    }
                }
            });
        }

        // -------------------------------------------------------------
        // DEFENSA AUTOMATICA DE TROPAS: UN hilo que, cada "intervaloMs",
        // hace que cada Tropa viva de este jugador ataque al enemigo mas
        // cercano que tenga DENTRO de su rango (no se mueve: solo dispara
        // a lo que ya tiene cerca). Es la "postura defensiva" clasica de un
        // RTS: sin esto el jugador humano tendria que dar una orden por
        // CADA golpe de CADA tropa, y no podria frenar a la IA. Mover a las
        // tropas sigue siendo manual.
        //
        // Dispara contra el Rival del jugador. Se activa desde
        // ConfiguradorPartida (regla de la partida, no del Controlador) y se
        // detiene con DetenerHilos (mismo token que el resto).
        // -------------------------------------------------------------
        public void IniciarDefensaAutomaticaDeTropas(int intervaloMs = 1000)
        {
            var rival = Rival;
            if (rival == null) return;

            Lanzar("defensa automatica de tropas", async () =>
            {
                while (true)
                {
                    // Al cancelar, el Delay lanza la excepcion y Lanzar()
                    // termina el hilo limpiamente.
                    await Task.Delay(intervaloMs, cts.Token);

                    try
                    {
                        foreach (var unidad in Unidades.Values)
                        {
                            if (!(unidad is Tropa tropa) || !tropa.EstaViva) continue;

                            var objetivo = BuscarObjetivoEnRango(tropa.Posicion, tropa.RangoAtaque, rival);

                            // Solo se loguea cuando el golpe DESTRUYE al
                            // objetivo (si se registrara cada golpe, el log
                            // crece a miles de lineas).
                            if (objetivo != null && tropa.Atacar(objetivo) && objetivo.EstaDestruido)
                            {
                                Eventos.Enqueue(new EventoJuego("Ataque", $"{tropa.Nombre} destruyó {NombreObjetivo(objetivo)} enemigo."));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Un fallo puntual no debe matar el hilo: se deja en
                        // el log y el ciclo sigue.
                        Eventos.Enqueue(new EventoJuego("Error", $"Fallo en la defensa automatica de tropas: {ex.Message}"));
                    }
                }
            });
        }

        // Busca, entre las unidades y edificios del rival, el mas cercano a
        // "origen" que este dentro de "rango" (usando la misma distancia
        // Manhattan que usa el resto del Modelo, no distancia euclidiana
        // como se hacia antes en la Vista). Antes solo servia para Torres
        // (recibia un Defensa); ahora recibe posicion y rango, asi lo usan
        // tambien las tropas.
        private IObjetivoAtacable BuscarObjetivoEnRango(Posicion origen, int rango, Jugador rival)
        {
            IObjetivoAtacable masCercano = null;
            int menorDistancia = int.MaxValue;

            foreach (var unidad in rival.Unidades.Values)
            {
                if (unidad.EstaDestruido) continue;
                int distancia = origen.DistanciaManhattanHasta(unidad.Posicion);
                if (distancia <= rango && distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    masCercano = unidad;
                }
            }

            foreach (var edificio in rival.Edificios.Values)
            {
                if (edificio.EstaDestruido) continue;
                int distancia = origen.DistanciaManhattanHasta(edificio.Posicion);
                if (distancia <= rango && distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    masCercano = edificio;
                }
            }

            return masCercano;
        }

        // Nombre legible del objetivo para el log (IObjetivoAtacable no
        // tiene Nombre, asi que se revisa si es Unidad o Edificio).
        private static string NombreObjetivo(IObjetivoAtacable objetivo)
        {
            if (objetivo is Unidad unidad) return unidad.Nombre;
            if (objetivo is Edificio edificio) return edificio.Nombre;
            return "objetivo";
        }
    }
}