using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImperiosEnGuerra.Modelo
{
    // Maneja los 3 archivos que pide la guía: configuracion.txt (una vez, al
    // iniciar), log_partida.txt (cada evento relevante) y resultado_final.txt
    // (una vez, al terminar).
    //
    // Vive en el Modelo (no en Controlador) porque, siguiendo la definicion
    // clasica de MVC, el acceso a datos/persistencia es responsabilidad del
    // Modelo — el Controlador no debe tener logica propia (ni siquiera la
    // logica de "cuando y como escribir un archivo"), solo debe transportar
    // informacion entre Modelo y Vista, tal como indico la profesora.
    //
    // La parte interesante: en vez de que CADA método de Jugador/Unidad tenga
    // que acordarse de escribir al log, esta clase lanza SU PROPIO hilo que
    // va sacando eventos de las colas (Jugador1.Eventos, Jugador2.Eventos,
    // Partida.Eventos) — que ya existen y ya son thread-safe (ConcurrentQueue)
    // — y los escribe solo. Un método nuevo que agregue un evento a la cola
    // queda logueado automáticamente, sin tocar GestorArchivos para nada.
    public class GestorArchivos
    {
        private const string ArchivoConfiguracion = "configuracion.txt";
        private const string ArchivoLog = "log_partida.txt";
        private const string ArchivoResultado = "resultado_final.txt";

        // Protege la escritura al log: aunque solo este hilo debería escribir
        // ahí, dejamos el lock por si en algún momento alguien más llama
        // RegistrarEvento directamente desde otro lado.
        private readonly object candadoLog = new object();

        public void GuardarConfiguracionInicial(Jugador jugador1, Jugador jugador2)
        {
            // IMPORTANTE: log_partida.txt se escribe con AppendAllText (ver
            // RegistrarEvento), es decir que NUNCA se borra solo — si no lo
            // limpiamos aquí, cada vez que le des Play se le van sumando los
            // eventos de la partida anterior, y en un rato el archivo queda
            // con miles de líneas mezclando varias partidas distintas.
            // GuardarConfiguracionInicial es el punto donde arranca SIEMPRE
            // una partida nueva, así que es el lugar correcto para dejar el
            // log en blanco antes de que JugadorIA/Jugador empiecen a
            // encolar eventos.
            if (File.Exists(ArchivoLog))
            {
                File.Delete(ArchivoLog);
            }

            // Mismo caso para resultado_final.txt: si la partida anterior
            // terminó con un ganador, ese archivo se queda ahí para
            // siempre (solo se reescribe cuando ALGUIEN gana). Si no lo
            // borramos, alguien podría abrir el archivo a mitad de la
            // partida nueva y pensar que ya hay un resultado, cuando en
            // realidad es el de la sesión pasada.
            if (File.Exists(ArchivoResultado))
            {
                File.Delete(ArchivoResultado);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Jugador 1: {jugador1.Nombre} - Mapa {jugador1.Mapa.Ancho}x{jugador1.Mapa.Alto}");
            sb.AppendLine($"Jugador 2: {jugador2.Nombre} - Mapa {jugador2.Mapa.Ancho}x{jugador2.Mapa.Alto}");
            sb.AppendLine($"Fecha: {DateTime.Now}");
            File.WriteAllText(ArchivoConfiguracion, sb.ToString());
        }

        // Formato exacto que pide la guía:
        //   Turno: Jugador 1
        //   Acción: Ataque
        //   Resultado: Impacto - Unidad enemiga destruida
        public void RegistrarEvento(string jugador, string accion, string resultado)
        {
            string linea = $"Turno: {jugador}{Environment.NewLine}" +
                            $"Acción: {accion}{Environment.NewLine}" +
                            $"Resultado: {resultado}{Environment.NewLine}{Environment.NewLine}";

            lock (candadoLog)
            {
                File.AppendAllText(ArchivoLog, linea);
            }
        }

        public void GuardarResultadoFinal(Partida partida)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ganador: {partida.Ganador ?? "Sin definir"}");
            sb.AppendLine($"Fecha de finalización: {DateTime.Now}");
            File.WriteAllText(ArchivoResultado, sb.ToString());
        }

        // Lanza el hilo que drena las 3 colas de eventos cada 300ms mientras
        // la partida siga en curso. Llamar esto UNA vez, justo después de
        // crear la Partida (ver comentario de ejemplo al final del archivo).
        public CancellationTokenSource IniciarEscuchaDeEventos(Partida partida)
        {
            var cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    DrenarCola(partida.Jugador1.Nombre, partida.Jugador1.Eventos);
                    DrenarCola(partida.Jugador2.Nombre, partida.Jugador2.Eventos);
                    DrenarCola("Sistema", partida.Eventos);

                    Thread.Sleep(300);
                }
            }, cts.Token);

            return cts; // guárdenlo para poder llamar cts.Cancel() cuando termine la partida
        }

        private void DrenarCola(string nombreJugador, ConcurrentQueue<EventoJuego> cola)
        {
            while (cola.TryDequeue(out var evento))
            {
                RegistrarEvento(nombreJugador, evento.Tipo, evento.Mensaje);
            }
        }
    }

    // -------------------------------------------------------------------
    // EJEMPLO DE USO (borrar este comentario, es solo referencia):
    //
    //   var mapa = new Mapa();
    //   var jugador1 = new Jugador("Grecia", mapa);
    //   var jugador2 = new Jugador("Máquina", mapa);
    //   var partida = new Partida(jugador1, jugador2);
    //
    //   var archivos = new GestorArchivos();
    //   archivos.GuardarConfiguracionInicial(jugador1, jugador2);
    //   var cancelacionLog = archivos.IniciarEscuchaDeEventos(partida);
    //
    //   // ... el juego corre, los eventos se van logueando solos ...
    //
    //   if (partida.VerificarGanador())
    //   {
    //       archivos.GuardarResultadoFinal(partida);
    //       cancelacionLog.Cancel();
    //   }
    // -------------------------------------------------------------------
}