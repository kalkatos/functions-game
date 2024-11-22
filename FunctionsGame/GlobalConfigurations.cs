using System;
using System.Linq;
using System.Reflection;

namespace Kalkatos.FunctionsGame
{
    public static class GlobalConfigurations
    {
        public static Configurations LoadedConfigurations = null;

        public static ILoginService LoginService { get { CheckConfigurations(); return LoadedConfigurations.LoginService; } }
        public static IGame Game { get { CheckConfigurations(); return LoadedConfigurations.Game; } }
        public static IAsyncGame AsyncGame { get { CheckConfigurations(); return LoadedConfigurations.AsyncGame; } }
        public static IMatchService MatchService { get { CheckConfigurations(); return LoadedConfigurations.MatchService; } }
        public static ILeaderboardService LeaderboardService { get { CheckConfigurations(); return LoadedConfigurations.LeaderboardService; } }
        public static IDataService DataService { get { CheckConfigurations(); return LoadedConfigurations.DataService; } }
        public static IAsyncService AsyncService { get { CheckConfigurations(); return LoadedConfigurations.AsyncService; } }
        public static IAnalyticsService AnalyticsService { get { CheckConfigurations(); return LoadedConfigurations.AnalyticsService; } }

        private static void CheckConfigurations ()
        {
            if (LoadedConfigurations != null)
                return;
            var myAssembly = Assembly.GetAssembly(typeof(Configurations));
            var classes = myAssembly.GetTypes().Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(Configurations)));
            foreach (Type type in classes)
            {
                if (type == typeof(DefaultConfigurations))
                    continue;
                LoadedConfigurations = (Configurations)Activator.CreateInstance(type);
                Logger.Log($"Using configurations: {type}");
                return;
            }
            LoadedConfigurations = new DefaultConfigurations();
        }

        private class DefaultConfigurations : Configurations
        {
            public DefaultConfigurations ()
            {
                LoginService = new Azure.AzureService();
                Game = new Rps.RpsGame();
                AsyncGame = new TempAsyncGame();
                MatchService = LoginService as IMatchService;
                LeaderboardService = LoginService as ILeaderboardService;
                DataService = LoginService as IDataService;
                AnalyticsService = LoginService as IAnalyticsService;
                AsyncService = LoginService as IAsyncService;
                AnalyticsService = LoginService as IAnalyticsService;
            }
        }
    }

    public abstract class Configurations
    {
        public ILoginService LoginService { get; protected set; }
        public IGame Game { get; protected set; }
        public IAsyncGame AsyncGame { get; protected set; }
        public IMatchService MatchService { get; protected set; }
        public ILeaderboardService LeaderboardService { get; protected set; }
        public IDataService DataService { get; protected set; }
        public IAsyncService AsyncService { get; protected set; }
        public IAnalyticsService AnalyticsService { get; protected set; }
    }

}
