using System;

namespace DokanNet
{
    public class DokanExeption : Exception
    {
        public int StatusCode { get; private set; }

        internal DokanExeption(int status, string message) : base(message)
        {
            StatusCode = status;
        }
    }
}