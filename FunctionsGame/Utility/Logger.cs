#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif
#if AZURE_FUNCTIONS
using Microsoft.Extensions.Logging;
#endif
using System;

namespace Kalkatos
{
	public class Logger
	{
#if UNITY_5_3_OR_NEWER
		private static UnityLogger log = new UnityLogger();
#elif AZURE_FUNCTIONS
		private static FunctionsLogger log = new FunctionsLogger();

		public static void Setup (ILogger logger)
		{
			log.logger = logger;
		}
#else
		private static BaseLogger log = new BaseLogger();
#endif

		public static void Log (string msg)
		{
			log.Log(msg);
		}

		public static void LogWarning (string msg)
		{
			log.LogWarning(msg);
		}

		public static void LogError (string msg)
		{
			log.LogError(msg);
		}
	}

	public class BaseLogger
	{
		public virtual void Log (string msg)
		{
			Console.WriteLine(msg);
		}

		public virtual void LogWarning (string msg)
		{
			Console.WriteLine($"[Warning] {msg}");
		}

		public virtual void LogError (string msg)
		{
			Console.WriteLine($"[Error] {msg}");
		}
	}

#if AZURE_FUNCTIONS
	public class FunctionsLogger : BaseLogger
	{
		internal ILogger logger;

		public override void Log (string msg)
		{
			logger?.LogInformation(msg);
		}

		public override void LogWarning (string msg)
		{
			logger?.LogWarning(msg);
		}

		public override void LogError (string msg)
		{
			logger?.LogError(msg);
		}
	}
#endif

#if UNITY_5_3_OR_NEWER
	public class UnityLogger : BaseLogger
	{
		public override void Log (string msg)
		{
			Debug.Log(msg);
		}

		public override void LogWarning (string msg)
		{
			Debug.LogWarning(msg);
		}

		public override void LogError (string msg)
		{
			Debug.LogError(msg);
		}
	}
#endif
}
