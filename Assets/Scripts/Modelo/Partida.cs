using System.Collections.Concurrent;

namespace ImperiosEnGuerra.Modelo
{
    public enum EstadoPartida
    {
        EnCurso,
        Finalizada
    }

    // ---------------------------------------------------------------------
    // PARTIDA: orquesta el juego completo — los dos Jugador (Grecia y la IA
    // rival), el estado global (en curso / finalizada), y quien gano. No
    // conoce nada de Unity ni de archivos: solo sabe evaluar la condicion
    // de victoria usando lo que ya expone Jugador.
    // ---------------------------------------------------------------------
    public class Partida
    {
        public Jugador Jugador1 { get; private set; } // Grecia (el humano)
        public Jugador Jugador2 { get; private set; } // la IA rival
        public EstadoPartida Estado { get; private set; }
        public string Ganador { get; private set; }

        // Cola propia de la Partida, para eventos que no pertenecen a
        // ningun Jugador en particular (por ejemplo, "FinDePartida") —
        // distinta de Jugador.Eventos, que es para eventos de CADA jugador.
        public ConcurrentQueue<EventoJuego> Eventos { get; private set; }

        // Protege Estado y Ganador: VerificarGanador() se va a llamar
        // despues de CADA ataque, y como puede haber varios ataques
        // ocurriendo en paralelo (distintas Tropa.Atacar() en distintos
        // hilos, incluyendo el de JugadorIA), sin este lock dos hilos
        // podrian evaluar la condicion de victoria "a medio camino" y
        // dejar Ganador en un estado inconsistente.
        private readonly object candado = new object();

        public Partida(Jugador jugador1, Jugador jugador2)
        {
            Jugador1 = jugador1;
            Jugador2 = jugador2;
            Estado = EstadoPartida.EnCurso;
            Eventos = new ConcurrentQueue<EventoJuego>();
        }

        // Revisa si algun jugador ya perdio. Se debe llamar despues de
        // cualquier evento que pueda cambiar el resultado (un ataque
        // exitoso, un edificio destruido, etc). Devuelve true si la partida
        // YA esta decidida (ya sea porque termino ahora mismo, o porque ya
        // habia terminado antes).
        //
        // Es "idempotente" a proposito: si Estado ya es Finalizada, sale
        // de inmediato sin volver a evaluar nada ni pisar el Ganador ya
        // guardado. Esto evita que, por ejemplo, un ataque que llega un
        // instante despues de que ya se anuncio un ganador, sobreescriba
        // el resultado con otra cosa.
        public bool VerificarGanador()
        {
            lock (candado)
            {
                if (Estado == EstadoPartida.Finalizada) return true;

                bool jugador1Derrotado = EstaDerrotado(Jugador1);
                bool jugador2Derrotado = EstaDerrotado(Jugador2);

                if (jugador1Derrotado && jugador2Derrotado)
                {
                    // Caso extremo pero posible con hilos en paralelo: dos
                    // ataques distintos (en dos hilos distintos) destruyen
                    // el Centro Urbano de AMBOS jugadores casi al mismo
                    // instante, antes de que VerificarGanador() alcance a
                    // ejecutarse para ninguno de los dos por separado.
                    Ganador = "Empate";
                }
                else if (jugador1Derrotado)
                {
                    Ganador = Jugador2.Nombre;
                }
                else if (jugador2Derrotado)
                {
                    Ganador = Jugador1.Nombre;
                }
                else
                {
                    return false; // nadie ha perdido todavia, la partida sigue
                }

                Estado = EstadoPartida.Finalizada;
                Eventos.Enqueue(new EventoJuego("FinDePartida", $"¡{Ganador} ha ganado la partida!"));
                return true;
            }
        }

        // Un jugador esta derrotado si CUALQUIERA de las dos condiciones
        // que pide la guia se cumple (el "y/o" del enunciado): su Centro
        // Urbano fue destruido, O ya no le queda ninguna unidad militar
        // viva. Ambos chequeos ya estan resueltos dentro de Jugador.
        private bool EstaDerrotado(Jugador jugador)
        {
            return jugador.CentroUrbanoDestruido() || jugador.TodasLasUnidadesMilitaresDestruidas();
        }
    }
}