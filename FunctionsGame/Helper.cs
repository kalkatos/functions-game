using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;

namespace Kalkatos.FunctionsGame
{
	internal static class Helper
	{
		internal static string ReadBytes (Stream stream)
		{
			byte[] bytes = new byte[stream.Length];
			int numBytesToRead = (int)stream.Length;
			int numBytesRead = 0;
			while (numBytesToRead > 0)
			{
				// Read may return anything from 0 to numBytesToRead.
				int n = stream.Read(bytes, numBytesRead, numBytesToRead);

				// Break when the end of the file is reached.
				if (n == 0)
					break;
				numBytesRead += n;
				numBytesToRead -= n;
			}
			return Encoding.UTF8.GetString(bytes);
		}

		internal static bool VerifyNullParameter (string parameter, ILogger log)
		{
			bool isNullId = string.IsNullOrEmpty(parameter);
			if (isNullId)
				parameter = "<empty>";
			log.LogInformation("Request with identifier: " + (string.IsNullOrEmpty(parameter) ? "<empty>" : parameter));
			return isNullId;
		}
	}
}