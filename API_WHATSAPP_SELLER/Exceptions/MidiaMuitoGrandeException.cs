namespace API_WHATSAPP_SELLER.Exceptions
{
    public class MidiaMuitoGrandeException : Exception
    {
        public MidiaMuitoGrandeException(long limiteBytes)
            : base($"Mídia excede o limite de {limiteBytes} bytes.")
        {
        }
    }
}
