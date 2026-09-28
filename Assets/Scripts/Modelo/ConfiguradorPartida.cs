using System;

namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // CONFIGURADORPARTIDA: arma el ESTADO INICIAL del juego — el mapa, los
    // dos jugadores, los Centros Urbanos, los depositos de recurso, los
    // aldeanos que arrancan trabajando, y la Partida que los une.
    //
    // Vive en el Modelo (no en el Controlador) porque decidir DONDE arranca
    // cada bando, CUANTOS recursos hay y CUANTOS aldeanos tiene cada uno son
    // reglas del juego, no "transporte de informacion". El Controlador solo
    // le pide a esta clase que configure la partida y despues lee el
    // resultado (Mapa, JugadorGrecia, JugadorRival, Partida).
    //
    // No depende de UnityEngine (es un POCO igual que el resto del Modelo).
    // ---------------------------------------------------------------------
    public class ConfiguradorPartida
    {
        // Resultado de Configurar(): lo que el Controlador necesita para
        // arrancar la partida. Solo lectura desde afuera.
        public Mapa Mapa { get; private set; }
        public Jugador JugadorGrecia { get; private set; }
        public Jugador JugadorRival { get; private set; } // el que controla JugadorIA (no confundir con la clase JugadorIA)
        public Partida Partida { get; private set; }

        // -------------------------------------------------------------
        // DEPOSITOS DE RECURSO: 22 en total (8 arboles de Madera, 6 vetas de
        // Oro, 4 piedras y 4 vetas de hierro/Metal). Estan repartidos a
        // proposito para que haya que DISPUTARLOS:
        //   - cerca de cada base (Grecia abajo-izquierda, la IA arriba-
        //     derecha): pocos, de modo que se agotan y hay que salir a buscar,
        //   - en las bandas laterales y en el CENTRO del mapa: son los que
        //     ambos bandos quieren, y donde sus aldeanos se cruzan.
        // Cada deposito es chico (150-250) para que se agoten y los
        // aldeanos tengan que ir relevandose al siguiente mas cercano.
        // Las posiciones son casi simetricas respecto al centro del mapa
        // ((x,y) <-> (19-x,19-y)) para que ningun bando parta con ventaja.
        // -------------------------------------------------------------
        private static readonly (TipoRecurso Tipo, int X, int Y, int Cantidad)[] Depositos =
        {
            // Madera (arboles): 3 cerca de cada base + 2 en el centro
            (TipoRecurso.Madera, 5, 4, 200), (TipoRecurso.Madera, 4, 7, 200), (TipoRecurso.Madera, 7, 6, 200),
            (TipoRecurso.Madera, 14, 15, 200), (TipoRecurso.Madera, 15, 12, 200), (TipoRecurso.Madera, 12, 13, 200),
            (TipoRecurso.Madera, 9, 10, 200), (TipoRecurso.Madera, 10, 9, 200),

            // Oro: 1 cerca de cada base, 1 en cada banda y 2 en el centro
            (TipoRecurso.Oro, 8, 3, 250), (TipoRecurso.Oro, 11, 16, 250),
            (TipoRecurso.Oro, 5, 11, 250), (TipoRecurso.Oro, 14, 8, 250),
            (TipoRecurso.Oro, 9, 9, 250), (TipoRecurso.Oro, 10, 10, 250),

            // Piedra: 2 por lado
            (TipoRecurso.Piedra, 6, 9, 150), (TipoRecurso.Piedra, 13, 10, 150),
            (TipoRecurso.Piedra, 4, 14, 150), (TipoRecurso.Piedra, 15, 5, 150),

            // Metal (hierro): 2 por lado
            (TipoRecurso.Metal, 12, 4, 150), (TipoRecurso.Metal, 7, 15, 150),
            (TipoRecurso.Metal, 4, 12, 150), (TipoRecurso.Metal, 15, 7, 150),
        };

        public void Configurar()
        {
            // 1. El mapa unico y compartido (20x20, ya coincide con lo que
            // pintaste en el Tilemap).
            var mapa = new Mapa();

            // 2. Los dos jugadores, cada uno arrancando en una esquina
            // opuesta del mapa (asi Grecia tiene que "invadir" hacia el
            // otro lado, como se definio en el diseño del juego).
            var jugadorGrecia = new Jugador("Grecia", mapa);
            var jugadorRival = new Jugador("El Resto", mapa);

            // Cada jugador necesita conocer a su rival: las Torres y las
            // tropas disparan contra el. Se asigna aqui, una sola vez.
            jugadorGrecia.DefinirRival(jugadorRival);
            jugadorRival.DefinirRival(jugadorGrecia);

            // Ritmo de entrenamiento: una tropa cada N segundos por jugador.
            // Es una regla de balance (vive aqui, en el Modelo). El humano
            // tiene 8 s y la IA 10 s: como el humano debe dar cada orden a
            // mano, se le da algo de ventaja.
            jugadorGrecia.CooldownEntrenamiento = TimeSpan.FromSeconds(8);
            jugadorRival.CooldownEntrenamiento = TimeSpan.FromSeconds(10);

            var posicionCentroGrecia = new Posicion(2, 2);
            var posicionCentroRival = new Posicion(17, 17);

            var centroGrecia = new TownCenter(posicionCentroGrecia);
            var centroRival = new TownCenter(posicionCentroRival);

            jugadorGrecia.AgregarEdificio(centroGrecia);
            mapa.ColocarEdificio(posicionCentroGrecia, centroGrecia);

            jugadorRival.AgregarEdificio(centroRival);
            mapa.ColocarEdificio(posicionCentroRival, centroRival);

            // Los dos Centros Urbanos ya quedan en Jugador.Edificios, asi que
            // el Controlador los detecta en el primer frame y les crea su
            // vista solo (via SincronizacionVistaController) — no hace falta
            // instanciarlos aqui a mano.

            // 3. Esparcir los depositos de recurso por el mapa (ver la tabla
            // Depositos, arriba).
            foreach (var (tipo, x, y, cantidad) in Depositos)
            {
                mapa.ColocarRecurso(new Posicion(x, y), new Recurso(tipo, cantidad));
            }

            // 4. La partida que orquesta a los dos jugadores.
            var partida = new Partida(jugadorGrecia, jugadorRival);

            // 5. Los aldeanos iniciales: 3 por bando, cada uno con un TIPO de
            // recurso asignado. Ya no recolectan "desde su casilla" (antes
            // sacaban de un deposito fijo sin importar la distancia): cada
            // uno CAMINA hasta el deposito mas cercano de su tipo, recolecta,
            // y cuando se agota va por el siguiente. Los dos bandos comparten
            // los depositos, asi que sus aldeanos compiten por ellos.
            //
            // Grecia: Madera (para el Taller y las Casas), Oro (para las
            // tropas) y Piedra (para las Torres). El Metal tambien se usa en
            // las Torres, pero se parte con 100: para conseguir mas hay que
            // mandar un aldeano a una veta de hierro (clic en el aldeano y
            // luego en el deposito).
            CrearAldeano(mapa, jugadorGrecia, new Posicion(3, 2), TipoRecurso.Madera);
            CrearAldeano(mapa, jugadorGrecia, new Posicion(2, 3), TipoRecurso.Oro);
            CrearAldeano(mapa, jugadorGrecia, new Posicion(3, 3), TipoRecurso.Piedra);

            // El Resto (IA): Madera (para su Taller y Casas) y Oro (las
            // tropas cuestan Oro), mas un tercer aldeano en Madera para que
            // tenga la misma cantidad de trabajadores que Grecia.
            CrearAldeano(mapa, jugadorRival, new Posicion(16, 17), TipoRecurso.Madera);
            CrearAldeano(mapa, jugadorRival, new Posicion(17, 16), TipoRecurso.Oro);
            CrearAldeano(mapa, jugadorRival, new Posicion(16, 16), TipoRecurso.Madera);

            // Postura defensiva de las tropas de Grecia: atacan solas lo que
            // tengan dentro de su rango (sin moverse). Es una regla de la
            // partida, asi que se activa aqui, en el Modelo. Sin esto el
            // jugador humano tendria que dar una orden por CADA golpe de CADA
            // tropa.
            jugadorGrecia.IniciarDefensaAutomaticaDeTropas();

            // Guardamos el resultado para que el Controlador lo lea.
            Mapa = mapa;
            JugadorGrecia = jugadorGrecia;
            JugadorRival = jugadorRival;
            Partida = partida;
        }

        // Crea un Villager, lo registra en el jugador y en el mapa, y lo
        // pone a trabajar en el tipo de recurso indicado (IniciarRecoleccion
        // lanza su propio hilo, asi que esto no bloquea nada).
        private Villager CrearAldeano(Mapa mapa, Jugador jugador, Posicion posicionInicial, TipoRecurso tipo)
        {
            var aldeano = new Villager(posicionInicial);
            jugador.AgregarUnidad(aldeano);
            mapa.ColocarUnidad(posicionInicial, aldeano);
            jugador.IniciarRecoleccion(aldeano, tipo);
            return aldeano;
        }
    }
}
