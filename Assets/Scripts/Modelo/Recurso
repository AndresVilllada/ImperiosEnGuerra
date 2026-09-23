namespace ImperiosEnGuerra.Modelo
{
    // ---------------------------------------------------------------------
    // RECURSO: el DEPOSITO FISICO real que existe en una celda del Mapa
    // (por ejemplo, "un deposito de Oro en la celda (3,4) con 250 unidades
    // restantes"). No confundir con TipoRecurso, que es solo la categoria
    // (Oro/Madera/Piedra/Metal) — Recurso USA TipoRecurso como uno de sus
    // atributos, ademas de tener Cantidad y comportamiento propio.
    // ---------------------------------------------------------------------
    public class Recurso
    {
        public TipoRecurso Tipo { get; private set; }
        public int Cantidad { get; private set; }

        // Candado dedicado SOLO para esta instancia de Recurso. Se usa un
        // objeto aparte (en vez de "lock(this)") porque bloquear sobre el
        // propio objeto publico es mala practica: codigo externo tambien
        // podria intentar hacer lock sobre esta misma instancia y generar
        // interbloqueos dificiles de detectar.
        private readonly object candado = new object();

        public Recurso(TipoRecurso tipo, int cantidadInicial)
        {
            Tipo = tipo;
            Cantidad = cantidadInicial;
        }

        // Extrae HASTA "cantidadSolicitada" del deposito, y devuelve cuanto
        // se pudo extraer REALMENTE (puede ser menos, si ya casi se agota).
        // Quien llama a este metodo (por ejemplo, Jugador.IniciarRecoleccion)
        // debe usar el valor devuelto para sumar al banco del jugador — NUNCA
        // asumir que se extrajo la cantidad solicitada completa.
        //
        // El lock es CRITICO aqui: si dos aldeanos (dos hilos distintos)
        // llaman Extraer() sobre el MISMO Recurso al mismo tiempo, sin este
        // lock podrian ambos leer "quedan 10" y ambos restar 8, dejando el
        // deposito en -6 en vez de en 0 (condicion de carrera clasica de
        // "leer, modificar, escribir").
        public int Extraer(int cantidadSolicitada)
        {
            lock (candado)
            {
                int extraido = cantidadSolicitada > Cantidad ? Cantidad : cantidadSolicitada;
                Cantidad -= extraido;
                return extraido;
            }
        }

        // Indica si el deposito ya no tiene nada mas para dar. Tambien
        // protegido con lock, para leer Cantidad de forma consistente aunque
        // otro hilo este extrayendo justo en este instante.
        public bool EstaAgotado()
        {
            lock (candado)
            {
                return Cantidad <= 0;
            }
        }
    }
}