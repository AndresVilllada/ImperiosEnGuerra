namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // CONFIGURADORPARTIDA: arma el ESTADO INICIAL del juego — el mapa, los
    // dos jugadores, los Centros Urbanos, los depositos de recurso, los
    // aldeanos que arrancan recolectando, y la Partida que los une.
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

            // 3. Esparcir algunos depositos de recurso por el mapa (numeros
            // de ejemplo, se pueden ajustar facil mas adelante).
            // ColocarDeposito DEVUELVE el Recurso creado, porque lo
            // necesitamos mas abajo para mandar a los aldeanos a recolectar
            // de un deposito especifico.
            // Cantidades subidas de 300/400/250/200: con dos aldeanos por
            // bando recolectando a 5 por segundo, los depositos originales
            // se agotaban en menos de un minuto y despues nadie tenia
            // ingresos (ni siquiera la IA, que nunca llegaba a entrenar).
            var posicionOro = new Posicion(5, 10);
            var posicionMadera = new Posicion(8, 4);
            var depositoOro = ColocarDeposito(mapa, TipoRecurso.Oro, posicionOro, 1500);
            var depositoMadera = ColocarDeposito(mapa, TipoRecurso.Madera, posicionMadera, 1500);
            ColocarDeposito(mapa, TipoRecurso.Piedra, new Posicion(12, 15), 500);
            ColocarDeposito(mapa, TipoRecurso.Metal, new Posicion(15, 6), 500);

            // 4. La partida que orquesta a los dos jugadores.
            var partida = new Partida(jugadorGrecia, jugadorRival);

            // 5. SOLUCION DEFINITIVA al problema de "nunca hay recursos
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
            CrearAldeanoRecolector(mapa, jugadorGrecia, new Posicion(3, 2), depositoMadera, posicionMadera);

            // Segundo aldeano de Grecia, dedicado al Oro: las tropas cuestan
            // Oro y el primer aldeano solo recolecta Madera, asi que sin
            // este el jugador se quedaria sin poder entrenar despues de
            // unas 3 tropas (los 100 de Oro iniciales no se reponen).
            CrearAldeanoRecolector(mapa, jugadorGrecia, new Posicion(2, 3), depositoOro, posicionOro);

            // El Resto (IA): usamos el mismo deposito de Madera, porque es
            // justo el recurso que JugadorIA necesita primero para poder
            // construir su Taller (ver JugadorIA.DecidirYEjecutarAccion).
            // Queda mas lejos de su Centro Urbano (17,17), pero el
            // Villager igual llega recolectando con el tiempo — no hace
            // falta que este pegado al deposito, IniciarRecoleccion no
            // valida distancia.
            CrearAldeanoRecolector(mapa, jugadorRival, new Posicion(16, 17), depositoMadera, posicionMadera);

            // Segundo aldeano de la IA, dedicado al Oro. Este es el que
            // resuelve que la IA nunca entrenara tropas: JugadorIA solo
            // entrena cuando tiene 150 o mas de Oro, y con un unico
            // aldeano en Madera su Oro se quedaba fijo en 100 para siempre
            // (asi que solo construia Casas cada 4 segundos).
            CrearAldeanoRecolector(mapa, jugadorRival, new Posicion(17, 16), depositoOro, posicionOro);

            // Guardamos el resultado para que el Controlador lo lea.
            Mapa = mapa;
            JugadorGrecia = jugadorGrecia;
            JugadorRival = jugadorRival;
            Partida = partida;
        }

        // Pequeño helper para no repetir 3 lineas por cada deposito. Devuelve
        // el Recurso creado porque lo necesitamos para mandar aldeanos a
        // recolectar de un deposito puntual, no solo para pintarlo en el mapa.
        private Recurso ColocarDeposito(Mapa mapa, TipoRecurso tipo, Posicion posicion, int cantidad)
        {
            var recurso = new Recurso(tipo, cantidad);
            mapa.ColocarRecurso(posicion, recurso);
            return recurso;
        }

        // Crea un Villager, lo registra en el jugador y en el mapa, y lo
        // pone a recolectar del deposito indicado (IniciarRecoleccion lanza
        // su propio hilo, asi que esto no bloquea nada).
        private Villager CrearAldeanoRecolector(Mapa mapa, Jugador jugador, Posicion posicionInicial, Recurso deposito, Posicion posicionDeposito)
        {
            var aldeano = new Villager(posicionInicial);
            jugador.AgregarUnidad(aldeano);
            mapa.ColocarUnidad(posicionInicial, aldeano);
            jugador.IniciarRecoleccion(aldeano, deposito, posicionDeposito);
            return aldeano;
        }
    }
}
