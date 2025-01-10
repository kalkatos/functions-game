using Kalkatos.Network.Model;

namespace Kalkatos.Network
{
    internal class TempAsyncGame : IAsyncGame
    {
        public bool IsCorrectRequest (AddAsyncObjectRequest request)
        {
			return true;
        }
    }
}