using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kalkatos.FunctionsGame
{
	internal static class Helper
	{
		internal static string ReadBytes (Stream stream, Encoding encoding)
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
			return encoding.GetString(bytes);
		}

		internal static int GetHash (params Dictionary<string, string>[] dicts)
        {
            unchecked
            {
                int hash = 23;
                foreach (var dict in dicts)
                {
                    foreach (var item in dict)
                    {
                        foreach (char c in item.Key)
                            hash = hash * 31 + c;
                        foreach (char c in item.Value)
                            hash = hash * 31 + c;
                    }
                }
                return hash;
            }
        }
    }
}