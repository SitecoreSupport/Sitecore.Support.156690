
namespace Sitecore.Support.Pipelines.Loader
{
    using System;
    using Diagnostics;
    using Eventing;
    using Events;
    using Publishing;
    using Sitecore.Data.Managers;
    using Sitecore.Data.Proxies;
    using Sitecore.Data.Serialization;
    using Sitecore.Pipelines;
    using Sitecore.Search;

    public class InitializeManagers
    {
        /// <summary>The pipeline name</summary>
        public const string PipelineName = "initializeManagers";

        /// <summary>Initializes static manager classes.</summary>
        /// <param name="args">The arguments.</param>
        /// <exception cref="T:Sitecore.Exceptions.ConfigurationException"></exception>
        public void Process(PipelineArgs args)
        {
            CorePipeline pipeline = CorePipelineFactory.GetPipeline("initializeManagers", string.Empty);
            if (pipeline != null)
            {
                pipeline.Run(args);
            }
            else
            {
                Log.SingleError(string.Format("The '{0}' pipeline is not defined in the Sitecore configuration file.", (object)"initializeManagers"), (object)typeof(InitializeManagers));
                this.InitManagersInternally();
            }
        }

        [Obsolete("This method is fallback functionality for CMS 8.1 upgrade procedure. It will be removed in latest version.")]
        private void InitManagersInternally()
        {
            Event.Initialize();
            ItemManager.Initialize();
            ProxyManager.Initialize();
            HistoryManager.Initialize();
            IndexingManager.Initialize();
            LanguageManager.Initialize();
            PublishManager.Initialize();
            SearchManager.Initialize();
            Manager.Initialize();
            Support.Data.Managers.NotificationManager.Initialize();
            EventManager.Initialize();
        }
    }
}