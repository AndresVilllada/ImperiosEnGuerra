using System;
using System.Collections.Generic;
using System.Linq;              // para los metodos .Any(), .OfType(), .FirstOrDefault(), .OrderBy() sobre las colecciones
using System.Threading;
using System.Threading.Tasks;

namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // JUGADORIA: el "cerebro" de la maquina. No es un Jugador en si mismo
    // (no hereda de Jugador ni lo reemplaza) — es una clase APARTE que
    // CONTROLA a un Jugador ya existente, tomando decisiones por el y
    // llamando a sus mismos metodos publicos (ConstruirEdificio,
    // EntrenarLote, etc.) que usaria el jugador humano.
    //
    // Vive en el Modelo (no en Controlador) porque asi lo indico la
    // profesora: toda la logica de decision debe estar aqui, y el
    // Controlador solo debe "pasar informacion", nunca decidir nada.
    //
    // CONCURRENCIA: la IA corre en DOS ciclos paralelos e independientes,
    // cada uno en su propio hilo (Task.Run):
    //   - Ciclo de ECONOMIA (cada 2 s): construye y entrena (una tropa cada 10 s).
    //   - Ciclo MILITAR (cada 1 s): mueve y ataca con las tropas ya entrenadas.
    // Ambos comparten el estado del juego (Jugador, Mapa), que ya es
    // thread-safe (ConcurrentDictionary, ConcurrentQueue y locks).
    // ---------------------------------------------------------------------
    public class JugadorIA
    {
        // ---- Parametros de comportamiento (se pueden ajustar aqui mismo) ----
        private const int IntervaloEconomiaMs = 2000;   // cada cuanto piensa en construir/entrenar (antes 4 s: con cooldown de 10 s entrenaba cada 12 s)
        private const int IntervaloMilitarMs = 1000;    // cada cuanto se mueve/ataca cada tropa (1 celda o 1 golpe por turno)
        private const int MaxTropas = 6;                // tope de tropas vivas (antes 15, luego 8: seguian siendo demasiadas)
        private const int TropasMinimasParaAtacar = 4;  // no ataca hasta juntar una oleada de este tamaño (antes 3, luego 5)
        private const int AtaqueCadaNTicks = 2;         // una tropa golpea cada N turnos militares (2 = un golpe cada 2 s; antes 1 cada 1 s)
        private const int MaxCasas = 4;                 // tope de Casas (antes construia una cada 4 s sin limite)
        private const int RadioConstruccion = 6;        // hasta donde se aleja de su Centro Urbano al construir

        private readonly Partida partida;       // para poder revisar/actualizar el estado global tras cada turno
        private readonly Jugador jugadorIA;     // el Jugador que esta IA controla (el rival de Grecia)
        private readonly Jugador jugadorRival;  // el otro Jugador (Grecia, el humano) — a quien la IA va a atacar

        // System.Random NO es thread-safe y ahora lo usan DOS hilos (economia
        // y militar), asi que siempre se accede a traves de Aleatorio(), que
        // lo protege con lock.
        private readonly Random random = new Random();

        // Permite "apagar" los hilos de la IA de forma segura desde afuera
        // (por ejemplo, cuando la partida termina) sin tener que matarlos
        // a la fuerza — se les pide "amablemente" que se detengan. El MISMO
        // token detiene los dos ciclos.
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        // Indica si la oleada de ataque ya arranco. Solo lo lee y escribe el
        // ciclo militar (un unico hilo), asi que no necesita lock.
        private bool ataqueEnCurso;

        // Turnos que le faltan a cada tropa (por Id) para poder volver a
        // golpear. Tambien lo usa solo el ciclo militar (un unico hilo), asi
        // que un Dictionary normal alcanza, sin lock.
        private readonly Dictionary<Guid, int> enfriamiento = new Dictionary<Guid, int>();

        public JugadorIA(Partida partida, Jugador jugadorIA, Jugador jugadorRival)
        {
            this.partida = partida;
            this.jugadorIA = jugadorIA;
            this.jugadorRival = jugadorRival;
        }

        // Arranca los DOS hilos de la IA. Se llama UNA vez, apenas se crea la
        // Partida (ver ejemplo de uso mas abajo en el proyecto).
        public void Iniciar()
        {
            Task.Run(() => CicloDeDecisionAsync(cts.Token));
            Task.Run(() => CicloMilitarAsync(cts.Token));
        }

        // Pide que los hilos se detengan en su proxima oportunidad (cuando
        // termine el Task.Delay actual). Llamar esto cuando la partida acabe.
        public void Detener() => cts.Cancel();

        // -------------------------------------------------------------
        // CICLO DE ECONOMIA: aqui vive la CONCURRENCIA de la IA — es un
        // hilo separado que corre en paralelo a todo lo demas del juego
        // (recoleccion, construccion, entrenamiento, movimiento), tomando
        // UNA decision cada 2 segundos, indefinidamente, hasta que la
        // partida termine o alguien llame Detener().
        // -------------------------------------------------------------
        private async Task CicloDeDecisionAsync(CancellationToken token)
        {
            // Sigue "pensando" mientras: no le hayan pedido detenerse Y
            // la partida siga en curso (si ya hay ganador, no tiene sentido
            // que la IA siga jugando).
            while (!token.IsCancellationRequested && partida.Estado == EstadoPartida.EnCurso)
            {
                try
                {
                    // Espera 2 segundos ANTES de decidir (simula "tiempo de
                    // reaccion" de la maquina, y evita que decida infinitas
                    // veces por segundo saturando el juego).
                    await Task.Delay(IntervaloEconomiaMs, token);
                }
                catch (TaskCanceledException)
                {
                    // Si Detener() se llamo mientras estaba esperando, el
                    // Delay se cancela y lanza esta excepcion — simplemente
                    // salimos del metodo, terminando el hilo limpiamente.
                    return;
                }

                // Una excepcion dentro de un Task.Run no se ve en ningun
                // lado y mataria el ciclo en silencio: se captura y se deja
                // en el log para poder diagnosticarla.
                try
                {
                    DecidirYEjecutarAccion();
                }
                catch (Exception ex)
                {
                    jugadorIA.Eventos.Enqueue(new EventoJuego("Error", $"Fallo en el ciclo de economia de la IA: {ex.Message}"));
                }

                // Tras cada turno de la IA (construya o entrene), se revisa
                // si con esa accion ya se definio un ganador.
                partida.VerificarGanador();
            }
        }

        // -------------------------------------------------------------
        // HEURISTICA DE ECONOMIA: reglas simples "si esto, entonces
        // aquello", evaluadas en orden de prioridad. A proposito no es una
        // IA sofisticada (sin machine learning ni prediccion) porque lo
        // que la guia evalua es el uso de CONCURRENCIA, no que tan lista
        // sea la maquina.
        //
        //   1. Si no tiene Taller (ni construido ni en obra) -> construir uno
        //   2. Si el Taller aun se construye                  -> esperar
        //   3. Si hay lugar y paso el cooldown de entrenamiento -> entrenar UNA tropa (al azar, entre las 3)
        //   4. Si nada de lo anterior aplica                  -> construir una Casa (hasta MaxCasas)
        //
        // El ATAQUE ya no se decide aqui: lo maneja el ciclo militar.
        // -------------------------------------------------------------
        private void DecidirYEjecutarAccion()
        {
            // Pregunta 1: ¿ya tengo un Taller, aunque este todavia en
            // construccion? Antes solo se miraba el Taller LISTO, y durante
            // los ~5 s de obra la IA empezaba OTRO Taller cada 4 s (gastando
            // 120 Madera de mas cada vez).
            bool tieneTaller = jugadorIA.Edificios.Values.Any(e => e is Taller && !e.EstaDestruido);

            if (!tieneTaller)
            {
                // No tiene Taller: prioridad maxima es construir uno.
                // Llama al mismo metodo que usaria un jugador humano desde
                // el Controlador — la IA no tiene "atajos" especiales.
                jugadorIA.ConstruirEdificio(pos => new Taller(pos), PosicionParaConstruir());
                return; // una sola accion por turno, no hace mas nada esta vez
            }

            // Pregunta 2: ¿el Taller ya esta LISTO (no solo en construccion)?
            // .OfType<Taller>() filtra los edificios quedandose solo con los
            // Taller; se descartan los destruidos.
            var taller = jugadorIA.Edificios.Values.OfType<Taller>().FirstOrDefault(t => t.EstaConstruido && !t.EstaDestruido);
            if (taller == null) return; // sigue en obra: esperar al proximo turno

            // Pregunta 3: ¿hay lugar en el ejercito y ya paso el enfriamiento
            // de entrenamiento? El ritmo lo fija Jugador.CooldownEntrenamiento
            // (10 s para la IA, ver ConfiguradorPartida): una tropa cada 10 s,
            // igual que la regla que aplica al jugador humano. Antes entrenaba
            // lotes de 5 cuando tenia 150 de Oro, y las oleadas eran enormes.
            bool hayLugar = ContarTropasVivas() < MaxTropas;
            if (hayLugar && jugadorIA.SegundosParaPoderEntrenar <= 0)
            {
                // Elige AL AZAR cual de las 3 tropas entrenar. Aleatorio(3)
                // devuelve 0, 1 o 2 — el "_" del switch es el caso por
                // defecto (equivalente a "default" en Java), aqui para Arquero.
                Func<Posicion, Tropa> fabricaTropa = Aleatorio(3) switch
                {
                    0 => pos => new Espadachin(pos),
                    1 => pos => new Piquero(pos),
                    _ => pos => new Arquero(pos),
                };

                // EntrenarTropa aplica el cooldown y valida los recursos (si
                // no alcanzan, NO gasta el cooldown y lo reintenta en el
                // proximo turno). La tropa sale AL LADO DEL TALLER.
                if (jugadorIA.EntrenarTropa(taller, fabricaTropa, taller.Posicion))
                {
                    return;
                }
            }

            // Si no aplico ninguna de las anteriores (tiene Taller y no
            // entrena): construye una Casa, pero solo hasta MaxCasas para
            // no llenar el mapa de Casas.
            int casas = jugadorIA.Edificios.Values.Count(e => e is House && !e.EstaDestruido);
            if (casas < MaxCasas)
            {
                jugadorIA.ConstruirEdificio(pos => new House(pos), PosicionParaConstruir());
            }
        }

        // -------------------------------------------------------------
        // CICLO MILITAR: segundo hilo de la IA. Cada segundo, cada tropa
        // viva da UN paso hacia el objetivo mas cercano del rival, o lo
        // ataca si ya lo tiene en rango. Corre en paralelo al ciclo de
        // economia y a todo lo demas.
        // -------------------------------------------------------------
        private async Task CicloMilitarAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && partida.Estado == EstadoPartida.EnCurso)
            {
                try
                {
                    await Task.Delay(IntervaloMilitarMs, token);
                }
                catch (TaskCanceledException)
                {
                    return; // Detener() se llamo mientras esperaba: terminar limpiamente
                }

                try
                {
                    EjecutarTurnoMilitar();
                }
                catch (Exception ex)
                {
                    jugadorIA.Eventos.Enqueue(new EventoJuego("Error", $"Fallo en el ciclo militar de la IA: {ex.Message}"));
                }

                // Revisa si algun golpe de este turno definio un ganador.
                partida.VerificarGanador();
            }
        }

        // Un turno militar: decide si la oleada arranca y le da una orden a
        // cada tropa viva.
        private void EjecutarTurnoMilitar()
        {
            // Copia (snapshot) de las tropas vivas: la coleccion puede
            // cambiar mientras se recorre (otro hilo entrenando una tropa).
            var tropas = jugadorIA.Unidades.Values.OfType<Tropa>().Where(t => t.EstaViva).ToList();

            // Sin tropas: la oleada termino (o nunca empezo).
            if (tropas.Count == 0)
            {
                ataqueEnCurso = false;
                enfriamiento.Clear(); // no dejar entradas de tropas muertas
                return;
            }

            // Espera a juntar una oleada minima antes de salir a atacar.
            // Una vez que arranca, sigue aunque bajen de ese numero (si no,
            // las sobrevivientes se quedarian paradas a mitad de camino).
            if (!ataqueEnCurso && tropas.Count >= TropasMinimasParaAtacar)
            {
                ataqueEnCurso = true;
                jugadorIA.Eventos.Enqueue(new EventoJuego("Ataque", $"La IA lanza una oleada con {tropas.Count} tropas."));
            }

            if (!ataqueEnCurso) return;

            foreach (var tropa in tropas)
            {
                ActuarConTropa(tropa);
            }
        }

        // Una tropa: busca su objetivo mas cercano; si esta en rango lo
        // ataca, y si no, da un paso hacia el.
        private void ActuarConTropa(Tropa tropa)
        {
            var objetivo = BuscarObjetivoMasCercano(tropa);
            if (objetivo == null) return; // el rival ya no tiene nada en pie

            int distancia = tropa.Posicion.DistanciaManhattanHasta(objetivo.Posicion);

            if (distancia <= tropa.RangoAtaque)
            {
                // Enfriamiento: despues de un golpe la tropa espera
                // AtaqueCadaNTicks - 1 turnos antes de golpear de nuevo. Esto
                // baja el daño por segundo de la IA sin frenar su avance.
                enfriamiento.TryGetValue(tropa.Id, out int espera);
                if (espera > 0)
                {
                    enfriamiento[tropa.Id] = espera - 1;
                    return;
                }

                // Tropa.Atacar ya valida rango y vida por su cuenta. Solo se
                // deja constancia en el log cuando el golpe DESTRUYE al
                // objetivo (si se registrara cada golpe, con muchas tropas
                // el log crece a miles de lineas).
                if (tropa.Atacar(objetivo))
                {
                    enfriamiento[tropa.Id] = AtaqueCadaNTicks - 1;

                    if (objetivo.EstaDestruido)
                    {
                        jugadorIA.Eventos.Enqueue(new EventoJuego("Ataque", $"{tropa.Nombre} (IA) destruyó {NombreDe(objetivo)} enemigo."));
                    }
                }
                return;
            }

            AvanzarHacia(tropa, objetivo.Posicion);
        }

        // Entre TODAS las unidades vivas y edificios en pie del rival,
        // devuelve el mas cercano a la tropa (distancia Manhattan, la misma
        // que usa el resto del Modelo).
        private IObjetivoAtacable BuscarObjetivoMasCercano(Tropa tropa)
        {
            IObjetivoAtacable masCercano = null;
            int menorDistancia = int.MaxValue;

            foreach (var unidad in jugadorRival.Unidades.Values)
            {
                if (!unidad.EstaViva) continue;
                int distancia = tropa.Posicion.DistanciaManhattanHasta(unidad.Posicion);
                if (distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    masCercano = unidad;
                }
            }

            foreach (var edificio in jugadorRival.Edificios.Values)
            {
                if (edificio.EstaDestruido) continue;
                int distancia = tropa.Posicion.DistanciaManhattanHasta(edificio.Posicion);
                if (distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    masCercano = edificio;
                }
            }

            return masCercano;
        }

        // Da UN paso hacia "destino". La logica de elegir la celda vecina
        // (y rodear obstaculos) vive ahora en Mapa.IntentarPasoHacia, porque
        // tambien la usan los aldeanos que caminan hacia los depositos.
        private void AvanzarHacia(Tropa tropa, Posicion destino)
        {
            jugadorIA.Mapa.IntentarPasoHacia(tropa, destino, Aleatorio);
        }

        // ---------- Utilidades ----------

        private int ContarTropasVivas() => jugadorIA.Unidades.Values.OfType<Tropa>().Count(t => t.EstaViva);

        // Celda libre cercana al Centro Urbano de la IA: asi construye
        // compacto, alrededor de su base. Antes usaba una celda al azar de
        // TODO el mapa (y llego a construir un Taller pegado al castillo
        // del jugador).
        private Posicion PosicionParaConstruir()
        {
            var centro = jugadorIA.Edificios.Values.OfType<TownCenter>().FirstOrDefault();
            var origen = centro != null
                ? centro.Posicion
                : new Posicion(jugadorIA.Mapa.Ancho / 2, jugadorIA.Mapa.Alto / 2);

            return jugadorIA.Mapa.BuscarCeldaLibreCercana(origen, RadioConstruccion);
        }

        // Nombre legible del objetivo para el log (IObjetivoAtacable no
        // tiene Nombre, asi que se revisa si es Unidad o Edificio).
        private static string NombreDe(IObjetivoAtacable objetivo)
        {
            if (objetivo is Unidad unidad) return unidad.Nombre;
            if (objetivo is Edificio edificio) return edificio.Nombre;
            return "objetivo";
        }

        // Acceso thread-safe a Random: dos hilos llamando Next() a la vez
        // pueden corromper su estado interno (y empezar a devolver siempre 0).
        private int Aleatorio(int maximoExclusivo)
        {
            lock (random)
            {
                return random.Next(maximoExclusivo);
            }
        }
    }
}