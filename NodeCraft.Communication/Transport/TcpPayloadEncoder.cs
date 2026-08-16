using System;
using System.Text;

namespace NodeCraft.Communication.Transport
{
    internal static class TcpPayloadEncoder
    {
        public static byte[] Encode(object value, string inputId)
        {
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"TCP input '{inputId}' cannot send a null payload.");
            }

            if (value is byte[] bytes)
            {
                return bytes;
            }

            var text = value as string ?? value.ToString();
            if (text == null)
            {
                throw new InvalidOperationException(
                    $"TCP input '{inputId}' produced a null text payload.");
            }

            return Encoding.UTF8.GetBytes(text);
        }
    }
}
