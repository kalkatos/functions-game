using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame
{
    internal class TempAsyncGame : IAsyncGame
    {
        public bool IsCorrectRequest (AddAsyncObjectRequest request)
        {
			return true;
        }
    }
}